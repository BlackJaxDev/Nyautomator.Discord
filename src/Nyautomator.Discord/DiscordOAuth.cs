using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.OAuth;

namespace Nyautomator.Discord;

public static class DiscordOAuth
{
    public const string TokenKey = "discord";
    public const string DefaultRedirectUri = "http://localhost:8086/discord/callback";
    private const string ApiBase = "https://discord.com/api/v10";
    private static readonly Uri AuthorizeEndpoint = new("https://discord.com/oauth2/authorize");
    private static readonly Uri TokenEndpoint = new($"{ApiBase}/oauth2/token");
    private static readonly Uri RevokeEndpoint = new($"{ApiBase}/oauth2/token/revoke");
    private static readonly Uri UserEndpoint = new($"{ApiBase}/users/@me");
    private static readonly Uri UserGuildsEndpoint = new($"{ApiBase}/users/@me/guilds");
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(5);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        try
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Nyautomator/1.0 (+https://github.com/BlackJaxDev/Nyautomator)");
        }
        catch
        {
            // Ignore user-agent parse errors.
        }
        return client;
    }

    public static IntegrationToken? GetToken() => IntegrationTokenStore.Get(TokenKey);

    public static void ClearToken() => IntegrationTokenStore.Clear(TokenKey);

    public static async Task<DiscordOAuthResult> AuthorizeAsync(AppConfiguration config, CancellationToken cancellationToken = default)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));

        var options = config.Discord ?? new AppConfiguration.DiscordOptions();
        var clientId = options.ClientId?.Trim();
        var clientSecret = options.ClientSecret?.Trim();
        var redirect = string.IsNullOrWhiteSpace(options.RedirectUri)
            ? DefaultRedirectUri
            : options.RedirectUri.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return DiscordOAuthResult.CreateFailure("Configure Discord client ID and client secret before starting OAuth.");

        if (!Uri.TryCreate(redirect, UriKind.Absolute, out var redirectUri))
            return DiscordOAuthResult.CreateFailure("Discord redirect URI must be an absolute URI.");

        var scopeList = NormalizeScopes(options.Scopes);
        var scopeParam = scopeList.Length > 0 ? string.Join(' ', scopeList) : "identify";
        var state = Guid.NewGuid().ToString("n");

        var authUrl = BuildAuthorizeUrl(clientId, redirectUri, scopeParam, state);
        LaunchBrowser(authUrl);

        var callback = await OAuthCallbackListener
            .WaitForCodeAsync(redirectUri, state, AuthorizationTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (callback.TimedOut)
            return DiscordOAuthResult.CreateFailure("Discord authorization timed out. Try again.");

        if (!string.IsNullOrEmpty(callback.Error))
        {
            var message = string.IsNullOrEmpty(callback.ErrorDescription)
                ? callback.Error
                : callback.ErrorDescription;
            return DiscordOAuthResult.CreateFailure($"Discord authorization failed: {message}.");
        }

        if (string.IsNullOrEmpty(callback.Code))
            return DiscordOAuthResult.CreateFailure("Discord did not return an authorization code.");

        var token = await ExchangeCodeAsync(clientId, clientSecret, redirectUri.ToString(), callback.Code, scopeParam, cancellationToken)
            .ConfigureAwait(false);
        if (token is null)
            return DiscordOAuthResult.CreateFailure("Unable to exchange the authorization code for Discord tokens.");

        await PopulateUserMetadataAsync(token, cancellationToken).ConfigureAwait(false);
        IntegrationTokenStore.Set(TokenKey, token);

        return DiscordOAuthResult.CreateSuccess(token.Clone());
    }

    public static async Task<bool> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = IntegrationTokenStore.Get(TokenKey);
        if (token is null || string.IsNullOrWhiteSpace(token.RefreshToken))
            return false;

        var config = ConfigurationProvider.GetOrLoadAppConfig();
        var options = config?.Discord ?? new AppConfiguration.DiscordOptions();
        var clientId = options.ClientId?.Trim();
        var clientSecret = options.ClientSecret?.Trim();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return false;

        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = token.RefreshToken
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(payload)
        };

        // Discord requires HTTP Basic auth with client_id:client_secret
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var refreshed = await JsonSerializer.DeserializeAsync<TokenResponse>(content, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (refreshed?.AccessToken is null)
                return false;

            token.AccessToken = refreshed.AccessToken;
            token.TokenType = string.IsNullOrWhiteSpace(refreshed.TokenType) ? "Bearer" : refreshed.TokenType;
            if (refreshed.ExpiresIn.HasValue)
                token.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(30, refreshed.ExpiresIn.Value));
            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                token.RefreshToken = refreshed.RefreshToken;
            if (!string.IsNullOrWhiteSpace(refreshed.Scope))
                token.Scope = refreshed.Scope;

            IntegrationTokenStore.Set(TokenKey, token);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Discord token refresh failed: {ex.Message}");
            return false;
        }
    }

    public static async Task<IntegrationToken?> GetValidTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = IntegrationTokenStore.Get(TokenKey);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return null;

        if (ShouldRefreshToken(token))
        {
            var refreshed = await RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (refreshed)
                token = IntegrationTokenStore.Get(TokenKey) ?? token;
        }

        return string.IsNullOrWhiteSpace(token.AccessToken) ? null : token;
    }

    public static async Task<IReadOnlyList<DiscordGuildSummary>> GetUserGuildsAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetValidTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return Array.Empty<DiscordGuildSummary>();

        if (!HasScope(token.Scope, "guilds"))
            return Array.Empty<DiscordGuildSummary>();

        using var request = new HttpRequestMessage(HttpMethod.Get, UserGuildsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<DiscordGuildSummary>();

            var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var guilds = await JsonSerializer.DeserializeAsync<List<DiscordGuildSummary>>(content, cancellationToken: cancellationToken).ConfigureAwait(false);
            return guilds is null ? Array.Empty<DiscordGuildSummary>() : guilds;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to load Discord guild list: {ex.Message}");
            return Array.Empty<DiscordGuildSummary>();
        }
    }

    private static string BuildAuthorizeUrl(string clientId, Uri redirectUri, string scope, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["response_type"] = "code",
            ["scope"] = scope,
            ["state"] = state,
            ["prompt"] = "consent"
        };

        var queryString = QueryStringBuilder.Build(query);
        return new UriBuilder(AuthorizeEndpoint)
        {
            Query = queryString
        }.Uri.ToString();
    }

    private static async Task<IntegrationToken?> ExchangeCodeAsync(string clientId, string clientSecret, string redirectUri, string code, string requestedScope, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(payload)
        };

        // Discord requires HTTP Basic auth with client_id:client_secret
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Trace.WriteLine($"Discord token exchange failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }

        try
        {
            var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var token = await JsonSerializer.DeserializeAsync<TokenResponse>(content, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (token?.AccessToken is null)
                return null;

            var expiresAt = token.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn.Value))
                : (DateTime?)null;

            return new IntegrationToken
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                TokenType = string.IsNullOrWhiteSpace(token.TokenType) ? "Bearer" : token.TokenType,
                Scope = string.IsNullOrWhiteSpace(token.Scope) ? requestedScope : token.Scope,
                ExpiresAtUtc = expiresAt,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["redirectUri"] = redirectUri
                }
            };
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to parse Discord token response: {ex.Message}");
            return null;
        }
    }

    private static async Task PopulateUserMetadataAsync(IntegrationToken token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            return;

        using var request = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            token.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var metadata = token.Metadata;

            if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                token.AccountId = idProp.GetString();
                SetMetadataValue(metadata, "userId", token.AccountId);
            }

            if (root.TryGetProperty("username", out var usernameProp) && usernameProp.ValueKind == JsonValueKind.String)
            {
                token.AccountLogin = usernameProp.GetString();
                SetMetadataValue(metadata, "username", token.AccountLogin);
            }

            if (root.TryGetProperty("global_name", out var globalNameProp) && globalNameProp.ValueKind == JsonValueKind.String)
            {
                token.AccountDisplayName = globalNameProp.GetString();
                SetMetadataValue(metadata, "globalName", token.AccountDisplayName);
            }
            else if (root.TryGetProperty("username", out var displayProp) && displayProp.ValueKind == JsonValueKind.String)
            {
                token.AccountDisplayName = displayProp.GetString();
                SetMetadataValue(metadata, "globalName", token.AccountDisplayName);
            }

            if (root.TryGetProperty("discriminator", out var discriminatorProp) && discriminatorProp.ValueKind == JsonValueKind.String)
                SetMetadataValue(metadata, "discriminator", discriminatorProp.GetString());

            if (root.TryGetProperty("avatar", out var avatarProp) && avatarProp.ValueKind == JsonValueKind.String)
            {
                var avatarHash = avatarProp.GetString();
                SetMetadataValue(metadata, "avatar", avatarHash);

                if (!string.IsNullOrWhiteSpace(token.AccountId) && !string.IsNullOrWhiteSpace(avatarHash))
                    SetMetadataValue(metadata, "avatarUrl", BuildAvatarUrl(token.AccountId!, avatarHash!));
            }
            else
            {
                metadata.Remove("avatar");
                metadata.Remove("avatarUrl");
            }

            if (root.TryGetProperty("email", out var emailProp) && emailProp.ValueKind == JsonValueKind.String)
                SetMetadataValue(metadata, "email", emailProp.GetString());

            if (root.TryGetProperty("verified", out var verifiedProp) &&
                (verifiedProp.ValueKind == JsonValueKind.True || verifiedProp.ValueKind == JsonValueKind.False))
            {
                SetMetadataValue(metadata, "verified", verifiedProp.GetBoolean().ToString());
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to load Discord user profile: {ex.Message}");
        }
    }

    private static bool ShouldRefreshToken(IntegrationToken token)
        => token.ExpiresAtUtc.HasValue && token.ExpiresAtUtc.Value <= DateTime.UtcNow.AddMinutes(1);

    private static bool HasScope(string? tokenScope, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(tokenScope) || string.IsNullOrWhiteSpace(requiredScope))
            return false;

        var required = requiredScope.Trim();
        foreach (var scope in tokenScope.Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(scope.Trim(), required, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void SetMetadataValue(Dictionary<string, string> metadata, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (string.IsNullOrWhiteSpace(value))
        {
            metadata.Remove(key);
            return;
        }

        metadata[key] = value.Trim();
    }

    private static string BuildAvatarUrl(string userId, string avatarHash)
    {
        var extension = avatarHash.StartsWith("a_", StringComparison.OrdinalIgnoreCase)
            ? "gif"
            : "png";
        return $"https://cdn.discordapp.com/avatars/{Uri.EscapeDataString(userId)}/{Uri.EscapeDataString(avatarHash)}.{extension}?size=128";
    }

    private static string[] NormalizeScopes(IEnumerable<string>? scopes)
    {
        if (scopes is null)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var scope in scopes)
        {
            var trimmed = scope?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (!list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                list.Add(trimmed);
        }
        return list.ToArray();
    }

    private static void LaunchBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to launch browser for Discord OAuth: {ex.Message}");
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope
    );

    public sealed class DiscordGuildSummary
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("icon")]
        public string? Icon { get; set; }

        [JsonPropertyName("owner")]
        public bool? Owner { get; set; }

        [JsonPropertyName("permissions")]
        public string? Permissions { get; set; }
    }
}

public sealed class DiscordOAuthResult
{
    private DiscordOAuthResult(bool success, string? message, IntegrationToken? token)
    {
        Success = success;
        Message = message;
        Token = token;
    }

    public bool Success { get; }
    public string? Message { get; }
    public IntegrationToken? Token { get; }

    public static DiscordOAuthResult CreateSuccess(IntegrationToken token)
        => new DiscordOAuthResult(true, null, token);

    public static DiscordOAuthResult CreateFailure(string message)
        => new DiscordOAuthResult(false, string.IsNullOrWhiteSpace(message) ? "Discord authorization failed." : message.Trim(), null);
}
