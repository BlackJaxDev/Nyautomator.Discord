using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nyautomator.Discord;
using Nyautomator.Module.Abstractions;
using Nyautomator.OAuth;
using Nyautomator.Runtime.Abstractions;
using NyautomatorUI.Server.Automation;
using ModuleAuthRequest = Nyautomator.Module.Abstractions.AuthenticatedIntegrationRequest;
using ModuleAuthResponse = Nyautomator.Module.Abstractions.AuthenticatedIntegrationResponse;

namespace Nyautomator.Modules.Discord;

public sealed class DiscordModuleBridge(
    IModuleOptionsProvider options,
    IIntegrationTokenStore tokenStore) : IModuleApiHandler, IAuthenticatedIntegrationAdapter
{
    private const string TokenKey = "discord";
    private const string BotTokenKey = "discord:bot";
    private const string SystemBrowserSentinel = "__system__";
    private const string DefaultBotInstallPermissions = "347200";
    private const string ApiBase = "https://discord.com/api/v10";
    private static readonly Uri AuthorizeEndpoint = new("https://discord.com/oauth2/authorize");
    private static readonly Uri TokenEndpoint = new($"{ApiBase}/oauth2/token");
    private static readonly Uri UserEndpoint = new($"{ApiBase}/users/@me");
    private static readonly Uri UserGuildsEndpoint = new($"{ApiBase}/users/@me/guilds");
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DirectoryCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DirectoryRefreshTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IModuleOptionsProvider _options = options;
    private readonly IIntegrationTokenStore _tokenStore = tokenStore;
    private readonly object _directoryCacheLock = new();
    private CachedDirectoryPayload? _guildsCache;
    private CachedDirectoryPayload? _channelsCache;

    public string ModuleId => "discord";
    public string Id => "Discord";

    public async Task<ModuleApiResponse> HandleAsync(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        var path = NormalizePath(request.Path);
        try
        {
            return path.ToLowerInvariant() switch
            {
                "status" when IsGet(request) => ModuleApiResponse.Json(GetStatus()),
                "authorize" when IsPost(request) => ModuleApiResponse.Json(await AuthorizeAsync(cancellationToken).ConfigureAwait(false)),
                "bot/install" when IsPost(request) => OpenBotInstall(request),
                "token" when IsDelete(request) => ClearToken(),
                "guilds" when IsGet(request) => await GetGuildsAsync(request, cancellationToken).ConfigureAwait(false),
                "channels" when IsGet(request) => await GetChannelsAsync(request, null, cancellationToken).ConfigureAwait(false),
                "scopes/auto" when IsGet(request) => ModuleApiResponse.Json(GetAutoScopes()),
                "messages" when IsPost(request) => await SendMessageAsync(await ReadBodyAsync<DiscordMessageRequest>(request.Body, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                "reactions" when IsPost(request) || IsPut(request) => await AddReactionAsync(await ReadBodyAsync<DiscordReactionRequest>(request.Body, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                _ when TryMatchLegacyChannelMessage(path, out var channelId) && IsPost(request)
                    => await SendMessageAsync(WithChannelId(await ReadBodyAsync<DiscordMessageRequest>(request.Body, cancellationToken).ConfigureAwait(false), channelId), cancellationToken).ConfigureAwait(false),
                _ when TryMatchLegacyGuildChannels(path, out var guildId) && IsGet(request)
                    => await GetChannelsAsync(request, guildId, cancellationToken).ConfigureAwait(false),
                _ => NotFound($"Discord module API path '/{path}' is not available.")
            };
        }
        catch (OperationCanceledException)
        {
            return Error("Discord request cancelled or timed out.", 504);
        }
        catch (Exception ex)
        {
            return Error($"Discord request failed: {ex.Message}", 500);
        }
    }

    public async Task<ModuleAuthResponse> SendAsync(ModuleAuthRequest request, CancellationToken cancellationToken)
    {
        var path = NormalizePath(request.Path);
        if (path.Equals("messages", StringComparison.OrdinalIgnoreCase) && IsPost(request.Method))
        {
            var payload = DeserializeBody<DiscordMessageRequest>(request.Body);
            var response = await SendMessageCoreAsync(payload, cancellationToken).ConfigureAwait(false);
            return ToAuthResponse(response);
        }

        if (path.Equals("reactions", StringComparison.OrdinalIgnoreCase) && (IsPost(request.Method) || IsPut(request.Method)))
        {
            var payload = DeserializeBody<DiscordReactionRequest>(request.Body);
            var response = await AddReactionCoreAsync(payload, cancellationToken).ConfigureAwait(false);
            return ToAuthResponse(response);
        }

        if (path.Equals("channels", StringComparison.OrdinalIgnoreCase) && IsMethod(request.Method, "GET"))
        {
            var response = await GetChannelsAsync(BuildAdapterRequest(request, path), null, cancellationToken).ConfigureAwait(false);
            return ToAuthResponse(response);
        }

        if (path.Equals("guilds", StringComparison.OrdinalIgnoreCase) && IsMethod(request.Method, "GET"))
        {
            var response = await GetGuildsAsync(BuildAdapterRequest(request, path), cancellationToken).ConfigureAwait(false);
            return ToAuthResponse(response);
        }

        return await SendRawDiscordApiAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private object GetStatus()
    {
        var token = _tokenStore.Get(TokenKey);
        var scopes = ParseScopes(token?.Scope);
        var moduleOptions = GetOptions();
        var botToken = GetBotToken(moduleOptions);

        return new
        {
            connected = !string.IsNullOrWhiteSpace(token?.AccessToken),
            configured = moduleOptions.HasOAuthCredentials,
            botConfigured = !string.IsNullOrWhiteSpace(botToken),
            configuredGuildId = NormalizeSnowflake(moduleOptions.GuildId),
            account = new
            {
                id = token?.AccountId,
                login = token?.AccountLogin,
                displayName = token?.AccountDisplayName
            },
            tokenType = token?.TokenType,
            scopes,
            scope = token?.Scope,
            expiresAtUtc = token?.ExpiresAtUtc,
            updatedAtUtc = token?.UpdatedAtUtc,
            hasRefreshToken = !string.IsNullOrWhiteSpace(token?.RefreshToken),
            metadata = token?.Metadata,
            configuredGuildName = moduleOptions.GuildName,
            configuredGuildBotInstalled = moduleOptions.GuildBotInstalled,
            configuredGuildIconUrl = moduleOptions.GuildIconUrl
        };
    }

    private async Task<object> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var moduleOptions = GetOptions();
        var clientId = moduleOptions.ClientId?.Trim();
        var clientSecret = moduleOptions.ClientSecret?.Trim();
        var redirect = string.IsNullOrWhiteSpace(moduleOptions.RedirectUri)
            ? DiscordOAuth.DefaultRedirectUri
            : moduleOptions.RedirectUri.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return new { success = false, message = "Configure Discord client ID and client secret before starting OAuth.", token = (object?)null };

        if (!Uri.TryCreate(redirect, UriKind.Absolute, out var redirectUri))
            return new { success = false, message = "Discord redirect URI must be an absolute URI.", token = (object?)null };

        var scopeList = NormalizeScopes(moduleOptions.Scopes);
        var scopeParam = scopeList.Length > 0 ? string.Join(' ', scopeList) : "identify";
        var state = Guid.NewGuid().ToString("n");

        var authUrl = BuildAuthorizeUrl(clientId, redirectUri, scopeParam, state);
        LaunchBrowser(authUrl, moduleOptions.AuthBrowserPath);

        var callback = await OAuthCallbackListener
            .WaitForCodeAsync(redirectUri, state, AuthorizationTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (callback.TimedOut)
            return new { success = false, message = "Discord authorization timed out. Try again.", token = (object?)null };

        if (!string.IsNullOrEmpty(callback.Error))
        {
            var message = string.IsNullOrEmpty(callback.ErrorDescription)
                ? callback.Error
                : callback.ErrorDescription;
            return new { success = false, message = $"Discord authorization failed: {message}.", token = (object?)null };
        }

        if (string.IsNullOrEmpty(callback.Code))
            return new { success = false, message = "Discord did not return an authorization code.", token = (object?)null };

        var token = await ExchangeCodeAsync(clientId, clientSecret, redirectUri.ToString(), callback.Code, scopeParam, cancellationToken)
            .ConfigureAwait(false);
        if (token is null)
            return new { success = false, message = "Unable to exchange the authorization code for Discord tokens.", token = (object?)null };

        await PopulateUserMetadataAsync(token, cancellationToken).ConfigureAwait(false);
        _tokenStore.Set(TokenKey, token);

        return new { success = true, message = (string?)null, token = ToTokenPayload(token) };
    }

    private ModuleApiResponse ClearToken()
    {
        _tokenStore.Clear(TokenKey);
        return ModuleApiResponse.Json(new { success = true });
    }

    private ModuleApiResponse OpenBotInstall(ModuleApiRequest request)
    {
        var moduleOptions = GetOptions();
        var dryRun = ParseBooleanQuery(request, "dryRun");
        var clientId = moduleOptions.ClientId?.Trim();
        if (dryRun && !string.IsNullOrWhiteSpace(QueryValue(request, "clientId")))
            clientId = QueryValue(request, "clientId")!.Trim();

        if (string.IsNullOrWhiteSpace(clientId))
            return Error("Configure Discord client ID before installing the bot.", 400);

        var guildId = NormalizeSnowflake(QueryValue(request, "guildId")) ?? NormalizeSnowflake(moduleOptions.GuildId);
        var installUrl = BuildBotInstallUrl(clientId, guildId);
        if (!dryRun)
            LaunchBrowser(installUrl, moduleOptions.AuthBrowserPath);

        var message = dryRun
            ? "Generated Discord bot install prompt URL."
            : string.IsNullOrWhiteSpace(guildId)
            ? "Opened Discord bot install prompt. Choose a server in Discord, then refresh servers here."
            : "Opened Discord bot install prompt for the selected server. After approving it, refresh servers here.";

        return ModuleApiResponse.Json(new
        {
            success = true,
            message,
            installUrl,
            guildId
        });
    }

    private async Task<ModuleApiResponse> GetGuildsAsync(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        var moduleOptions = GetOptions();
        var configuredGuildId = NormalizeSnowflake(moduleOptions.GuildId);
        var storedToken = _tokenStore.Get(TokenKey);
        var botToken = GetBotToken(moduleOptions);
        var cacheKey = BuildDiscordDirectoryCacheKey(
            "guilds",
            configuredGuildId,
            moduleOptions.GuildName,
            moduleOptions.GuildBotInstalled?.ToString(),
            moduleOptions.GuildIconUrl,
            botToken,
            storedToken?.Scope,
            storedToken?.UpdatedAtUtc?.ToString("O"));
        var refresh = ParseBooleanQuery(request, "refresh");

        if (!refresh && TryGetCachedDirectory(_guildsCache, cacheKey, requireFresh: true, out var cachedGuilds))
            return ModuleApiResponse.Json(cachedGuilds);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DirectoryRefreshTimeout);

        try
        {
            var token = await GetValidTokenAsync(moduleOptions, timeoutCts.Token).ConfigureAwait(false);
            var connected = !string.IsNullOrWhiteSpace(token?.AccessToken);
            var scopes = ParseScopes(token?.Scope);
            var hasGuildScope = scopes.Contains("guilds", StringComparer.OrdinalIgnoreCase);

            var userGuilds = hasGuildScope
                ? await GetUserGuildsAsync(moduleOptions, timeoutCts.Token).ConfigureAwait(false)
                : Array.Empty<DiscordGuildSummary>();

            var botGuildsResult = await GetBotGuildsAsync(botToken, timeoutCts.Token).ConfigureAwait(false);
            var map = new Dictionary<string, GuildListing>(StringComparer.OrdinalIgnoreCase);

            foreach (var guild in userGuilds)
            {
                var id = NormalizeSnowflake(guild?.Id);
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                map[id] = new GuildListing
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(guild?.Name) ? $"Server {id}" : guild!.Name!.Trim(),
                    IconUrl = BuildGuildIconUrl(id, guild?.Icon),
                    Owner = guild?.Owner ?? false,
                    Permissions = guild?.Permissions,
                    BotInstalled = false,
                    BotInstallKnown = botGuildsResult.Checked,
                    Source = "user"
                };
            }

            foreach (var guild in botGuildsResult.Guilds)
            {
                var id = NormalizeSnowflake(guild?.Id);
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (!map.TryGetValue(id, out var existing))
                {
                    existing = new GuildListing
                    {
                        Id = id,
                        Name = string.IsNullOrWhiteSpace(guild?.Name) ? $"Server {id}" : guild!.Name!.Trim(),
                        IconUrl = BuildGuildIconUrl(id, guild?.Icon),
                        Owner = guild?.Owner ?? false,
                        Permissions = guild?.Permissions,
                        BotInstallKnown = true,
                        Source = "bot"
                    };
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(existing.IconUrl))
                        existing.IconUrl = BuildGuildIconUrl(id, guild?.Icon);

                    if (string.Equals(existing.Source, "user", StringComparison.OrdinalIgnoreCase))
                        existing.Source = "both";
                }

                existing.BotInstalled = true;
                existing.BotInstallKnown = true;
                map[id] = existing;
            }

            if (!string.IsNullOrWhiteSpace(configuredGuildId) && !map.ContainsKey(configuredGuildId))
            {
                var botInstalled = botGuildsResult.Checked
                    ? false
                    : moduleOptions.GuildBotInstalled == true;

                map[configuredGuildId] = new GuildListing
                {
                    Id = configuredGuildId,
                    Name = FirstNonEmpty(moduleOptions.GuildName, $"Server {configuredGuildId}")!,
                    IconUrl = moduleOptions.GuildIconUrl,
                    BotInstalled = botInstalled,
                    BotInstallKnown = botGuildsResult.Checked || moduleOptions.GuildBotInstalled.HasValue,
                    Source = "configured"
                };
            }

            var guilds = map.Values
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    id = g.Id,
                    name = g.Name,
                    iconUrl = g.IconUrl,
                    owner = g.Owner,
                    permissions = g.Permissions,
                    botInstalled = g.BotInstalled,
                    botInstallKnown = g.BotInstallKnown,
                    source = g.Source,
                    selected = !string.IsNullOrWhiteSpace(configuredGuildId)
                               && string.Equals(configuredGuildId, g.Id, StringComparison.OrdinalIgnoreCase)
                })
                .ToArray();

            var payload = new
            {
                connected,
                hasGuildScope,
                configuredGuildId,
                configuredGuildName = moduleOptions.GuildName,
                configuredGuildBotInstalled = moduleOptions.GuildBotInstalled,
                configuredGuildIconUrl = moduleOptions.GuildIconUrl,
                botGuildsChecked = botGuildsResult.Checked,
                botGuildsError = botGuildsResult.Error,
                guilds,
                message = BuildGuildsMessage(connected, hasGuildScope, botToken, guilds.Length, botGuildsResult.Checked, botGuildsResult.Error),
                checkedAtUtc = DateTime.UtcNow,
                fromCache = false
            };

            SetCachedDirectory(ref _guildsCache, cacheKey, payload);
            return ModuleApiResponse.Json(payload);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (TryGetCachedDirectory(_guildsCache, cacheKey, requireFresh: false, out var staleGuilds))
                return ModuleApiResponse.Json(staleGuilds);

            return ModuleApiResponse.Json(new
            {
                connected = false,
                hasGuildScope = false,
                configuredGuildId,
                configuredGuildName = moduleOptions.GuildName,
                configuredGuildBotInstalled = moduleOptions.GuildBotInstalled,
                configuredGuildIconUrl = moduleOptions.GuildIconUrl,
                botGuildsChecked = false,
                botGuildsError = "Discord server list timed out.",
                guilds = Array.Empty<object>(),
                message = "Discord server list timed out. Try refresh again.",
                checkedAtUtc = DateTime.UtcNow,
                fromCache = false
            });
        }
    }

    private async Task<ModuleApiResponse> GetChannelsAsync(ModuleApiRequest request, string? guildIdOverride, CancellationToken cancellationToken)
    {
        var moduleOptions = GetOptions();
        var effectiveGuildId = NormalizeSnowflake(guildIdOverride ?? QueryValue(request, "guildId")) ?? NormalizeSnowflake(moduleOptions.GuildId);
        if (string.IsNullOrWhiteSpace(effectiveGuildId))
        {
            return ModuleApiResponse.Json(new
            {
                guildId = (string?)null,
                channels = Array.Empty<object>(),
                message = "Select a Discord server to browse channels."
            });
        }

        var botToken = GetBotToken(moduleOptions);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return ModuleApiResponse.Json(new
            {
                guildId = effectiveGuildId,
                channels = Array.Empty<object>(),
                message = "Set a Discord bot token to load channel lists."
            });
        }

        var cacheKey = BuildDiscordDirectoryCacheKey("channels", effectiveGuildId, botToken);
        var refresh = ParseBooleanQuery(request, "refresh");
        if (!refresh && TryGetCachedDirectory(_channelsCache, cacheKey, requireFresh: true, out var cachedChannels))
            return ModuleApiResponse.Json(cachedChannels);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DirectoryRefreshTimeout);

            var client = new DiscordRestClient(botToken);
            var channels = await client.GetGuildChannelsAsync(effectiveGuildId, timeoutCts.Token).ConfigureAwait(false)
                ?? new List<DiscordRestClient.DiscordChannelResponse>();

            var categories = channels
                .Where(c => c.Type == 4 && !string.IsNullOrWhiteSpace(c.Id))
                .ToDictionary(
                    c => c.Id!,
                    c => string.IsNullOrWhiteSpace(c.Name) ? "Category" : c.Name!.Trim(),
                    StringComparer.OrdinalIgnoreCase);

            var payload = channels
                .Where(c => !string.IsNullOrWhiteSpace(c.Id) && IsMessageCapableChannel(c.Type))
                .OrderBy(c => c.Position ?? int.MaxValue)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c =>
                {
                    var displayName = BuildChannelDisplayName(c, categories);
                    return new
                    {
                        id = c.Id,
                        name = c.Name,
                        type = c.Type,
                        parentId = c.ParentId,
                        displayName,
                        optionValue = $"{c.Id}|{displayName}"
                    };
                })
                .ToArray();

            var response = new
            {
                guildId = effectiveGuildId,
                channels = payload,
                message = payload.Length == 0 ? "No message-capable channels found in the selected server." : null,
                checkedAtUtc = DateTime.UtcNow,
                fromCache = false
            };

            SetCachedDirectory(ref _channelsCache, cacheKey, response);
            return ModuleApiResponse.Json(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (TryGetCachedDirectory(_channelsCache, cacheKey, requireFresh: false, out var staleChannels))
                return ModuleApiResponse.Json(staleChannels);

            return ModuleApiResponse.Json(new
            {
                guildId = effectiveGuildId,
                channels = Array.Empty<object>(),
                message = "Discord channel list timed out. Try refresh again.",
                checkedAtUtc = DateTime.UtcNow,
                fromCache = false
            });
        }
        catch (Exception ex)
        {
            return ModuleApiResponse.Json(new
            {
                guildId = effectiveGuildId,
                channels = Array.Empty<object>(),
                message = $"Unable to load channels: {ex.Message}"
            });
        }
    }

    private object GetAutoScopes()
    {
        var scopes = DiscordScopeUtility.ComputeAutoScopes(AutomationHost.GetAutomationState?.Invoke());
        return new { scopes };
    }

    private async Task<ModuleApiResponse> SendMessageAsync(DiscordMessageRequest? request, CancellationToken cancellationToken)
        => ToModuleResponse(await SendMessageCoreAsync(request, cancellationToken).ConfigureAwait(false));

    private async Task<MessageSendResult> SendMessageCoreAsync(DiscordMessageRequest? request, CancellationToken cancellationToken)
    {
        var channelId = NormalizeSnowflake(request?.ChannelId);
        var content = (request?.Content ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(channelId))
            return MessageSendResult.Fail("channelId is required.", 400);
        if (string.IsNullOrWhiteSpace(content) && (request?.Embeds is null || request.Embeds.Count == 0))
            return MessageSendResult.Fail("content or embeds is required.", 400);

        var botToken = GetBotToken(GetOptions());
        if (string.IsNullOrWhiteSpace(botToken))
            return MessageSendResult.Fail("Discord bot token is not configured.", 409);

        var client = new DiscordRestClient(botToken);
        var message = await client.SendMessageAsync(channelId, content, request?.Embeds?.ToArray(), cancellationToken).ConfigureAwait(false);
        return message is null
            ? MessageSendResult.Fail("Discord did not accept the message request.", 502)
            : MessageSendResult.Ok(new { success = true, sent = true, message });
    }

    private async Task<ModuleApiResponse> AddReactionAsync(DiscordReactionRequest? request, CancellationToken cancellationToken)
        => ToModuleResponse(await AddReactionCoreAsync(request, cancellationToken).ConfigureAwait(false));

    private async Task<MessageSendResult> AddReactionCoreAsync(DiscordReactionRequest? request, CancellationToken cancellationToken)
    {
        var channelId = NormalizeSnowflake(request?.ChannelId);
        var messageId = NormalizeSnowflake(request?.MessageId);
        var emoji = request?.Emoji?.Trim();
        if (string.IsNullOrWhiteSpace(channelId))
            return MessageSendResult.Fail("channelId is required.", 400);
        if (string.IsNullOrWhiteSpace(messageId))
            return MessageSendResult.Fail("messageId is required.", 400);
        if (string.IsNullOrWhiteSpace(emoji))
            return MessageSendResult.Fail("emoji is required.", 400);

        var botToken = GetBotToken(GetOptions());
        if (string.IsNullOrWhiteSpace(botToken))
            return MessageSendResult.Fail("Discord bot token is not configured.", 409);

        var client = new DiscordRestClient(botToken);
        var added = await client.AddReactionAsync(channelId, messageId, emoji, cancellationToken).ConfigureAwait(false);
        return added
            ? MessageSendResult.Ok(new { success = true, added = true })
            : MessageSendResult.Fail("Discord did not accept the reaction request.", 502);
    }

    private async Task<ModuleAuthResponse> SendRawDiscordApiAsync(ModuleAuthRequest request, CancellationToken cancellationToken)
    {
        var botToken = GetBotToken(GetOptions());
        if (string.IsNullOrWhiteSpace(botToken))
            return new ModuleAuthResponse(false, 409, null, "Discord bot token is not configured.", "application/json");

        try
        {
            using var cts = request.TimeoutMs > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            if (cts is not null)
                cts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1000, request.TimeoutMs)));

            using var http = CreateDiscordHttpClient(botToken);
            using var httpRequest = new HttpRequestMessage(
                ResolveHttpMethod(request.Method),
                BuildDiscordApiUri(request.Path, request.Query));
            if (RequestSupportsBody(httpRequest.Method) && request.Body is not null)
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, string.IsNullOrWhiteSpace(request.ContentType) ? "application/json" : request.ContentType);

            using var response = await http.SendAsync(httpRequest, cts?.Token ?? cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cts?.Token ?? cancellationToken).ConfigureAwait(false);
            return new ModuleAuthResponse(
                Success: response.IsSuccessStatusCode,
                StatusCode: (int)response.StatusCode,
                Body: body,
                Error: response.IsSuccessStatusCode ? null : $"Discord API request failed: {(int)response.StatusCode} {response.StatusCode}.",
                ContentType: response.Content.Headers.ContentType?.ToString() ?? "application/json",
                Headers: FormatResponseHeaders(response));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ModuleAuthResponse(false, 0, null, "Discord API request timed out.", "application/json");
        }
        catch (Exception ex)
        {
            return new ModuleAuthResponse(false, 0, null, ex.Message, "application/json");
        }
    }

    private DiscordModuleOptions GetOptions()
    {
        var moduleOptions = _options.Get(ModuleId, DiscordModuleOptions.CreateDefault());
        return moduleOptions.MergeFallback(DiscordModuleOptions.CreateDefault());
    }

    private string? GetBotToken(DiscordModuleOptions options)
    {
        var configured = options.BotToken?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return _tokenStore.Get(BotTokenKey)?.AccessToken?.Trim();
    }

    private async Task<bool> RefreshAccessTokenAsync(DiscordModuleOptions moduleOptions, CancellationToken cancellationToken)
    {
        var token = _tokenStore.Get(TokenKey);
        if (token is null || string.IsNullOrWhiteSpace(token.RefreshToken))
            return false;

        var clientId = moduleOptions.ClientId?.Trim();
        var clientSecret = moduleOptions.ClientSecret?.Trim();
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicCredentials(clientId, clientSecret));

        try
        {
            using var http = CreateDiscordHttpClient(null);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

            _tokenStore.Set(TokenKey, token);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Discord token refresh failed: {ex.Message}");
            return false;
        }
    }

    private async Task<IntegrationToken?> GetValidTokenAsync(DiscordModuleOptions moduleOptions, CancellationToken cancellationToken)
    {
        var token = _tokenStore.Get(TokenKey);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return null;

        if (ShouldRefreshToken(token))
        {
            var refreshed = await RefreshAccessTokenAsync(moduleOptions, cancellationToken).ConfigureAwait(false);
            if (refreshed)
                token = _tokenStore.Get(TokenKey) ?? token;
        }

        return string.IsNullOrWhiteSpace(token.AccessToken) ? null : token;
    }

    private async Task<IReadOnlyList<DiscordGuildSummary>> GetUserGuildsAsync(DiscordModuleOptions moduleOptions, CancellationToken cancellationToken)
    {
        var token = await GetValidTokenAsync(moduleOptions, cancellationToken).ConfigureAwait(false);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return Array.Empty<DiscordGuildSummary>();

        if (!HasScope(token.Scope, "guilds"))
            return Array.Empty<DiscordGuildSummary>();

        using var request = new HttpRequestMessage(HttpMethod.Get, UserGuildsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        try
        {
            using var http = CreateDiscordHttpClient(null);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
        var query = QueryStringBuilder.Build(new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["response_type"] = "code",
            ["scope"] = scope,
            ["state"] = state,
            ["prompt"] = "consent"
        });

        return new UriBuilder(AuthorizeEndpoint) { Query = query }.Uri.ToString();
    }

    private static string BuildBotInstallUrl(string clientId, string? guildId)
    {
        var query = QueryStringBuilder.Build(new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["scope"] = "bot applications.commands",
            ["permissions"] = DefaultBotInstallPermissions,
            ["guild_id"] = guildId,
            ["disable_guild_select"] = string.IsNullOrWhiteSpace(guildId) ? null : "true"
        });

        return new UriBuilder(AuthorizeEndpoint) { Query = query }.Uri.ToString();
    }

    private static async Task<IntegrationToken?> ExchangeCodeAsync(
        string clientId,
        string clientSecret,
        string redirectUri,
        string code,
        string requestedScope,
        CancellationToken cancellationToken)
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicCredentials(clientId, clientSecret));

        using var http = CreateDiscordHttpClient(null);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

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

    private static async Task PopulateUserMetadataAsync(IntegrationToken token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            return;

        using var request = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        try
        {
            using var http = CreateDiscordHttpClient(null);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

    private static async Task<BotGuildsResult> GetBotGuildsAsync(string? botToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            return new BotGuildsResult(Array.Empty<DiscordRestClient.DiscordGuildResponse>(), false, null);

        try
        {
            var client = new DiscordRestClient(botToken);
            var guilds = await client.GetCurrentUserGuildsAsync(cancellationToken).ConfigureAwait(false);
            return guilds is null
                ? new BotGuildsResult(Array.Empty<DiscordRestClient.DiscordGuildResponse>(), false, "Discord rejected the configured bot token or guild request.")
                : new BotGuildsResult(guilds, true, null);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to load Discord bot guilds: {ex.Message}");
            return new BotGuildsResult(Array.Empty<DiscordRestClient.DiscordGuildResponse>(), false, ex.Message);
        }
    }

    private static bool IsMessageCapableChannel(int channelType)
        => channelType == 0
           || channelType == 5
           || channelType == 10
           || channelType == 11
           || channelType == 12
           || channelType == 15;

    private static string BuildChannelDisplayName(DiscordRestClient.DiscordChannelResponse channel, IReadOnlyDictionary<string, string> categories)
    {
        var name = string.IsNullOrWhiteSpace(channel.Name)
            ? channel.Id ?? "unknown-channel"
            : channel.Name.Trim();

        var label = $"#{name}";
        return !string.IsNullOrWhiteSpace(channel.ParentId)
               && categories.TryGetValue(channel.ParentId, out var category)
               && !string.IsNullOrWhiteSpace(category)
            ? $"{category} / {label}"
            : label;
    }

    private static ModuleApiResponse ToModuleResponse(MessageSendResult result)
        => ModuleApiResponse.Json(result.Payload, result.StatusCode);

    private static ModuleAuthResponse ToAuthResponse(MessageSendResult result)
        => new(
            Success: result.StatusCode is >= 200 and <= 299,
            StatusCode: result.StatusCode,
            Body: JsonSerializer.Serialize(result.Payload, JsonOptions),
            Error: result.Error,
            ContentType: "application/json");

    private static ModuleAuthResponse ToAuthResponse(ModuleApiResponse response)
        => new(
            Success: response.StatusCode is >= 200 and <= 299,
            StatusCode: response.StatusCode,
            Body: response.Body is null ? null : JsonSerializer.Serialize(response.Body, JsonOptions),
            Error: response.StatusCode is >= 200 and <= 299 ? null : "Discord module request failed.",
            ContentType: response.ContentType ?? "application/json",
            Headers: response.Headers);

    private static ModuleApiResponse NotFound(string message)
        => ModuleApiResponse.Json(new { ok = false, success = false, error = message }, 404);

    private static ModuleApiResponse Error(string message, int statusCode)
        => ModuleApiResponse.Json(new { ok = false, success = false, error = message }, statusCode);

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('/');

    private static bool IsGet(ModuleApiRequest request)
        => IsMethod(request.Method, "GET");

    private static bool IsPost(ModuleApiRequest request)
        => IsMethod(request.Method, "POST");

    private static bool IsPut(ModuleApiRequest request)
        => IsMethod(request.Method, "PUT");

    private static bool IsDelete(ModuleApiRequest request)
        => IsMethod(request.Method, "DELETE");

    private static bool IsPost(string? method)
        => IsMethod(method, "POST");

    private static bool IsPut(string? method)
        => IsMethod(method, "PUT");

    private static bool IsMethod(string? actual, string expected)
        => string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool TryMatchLegacyChannelMessage(string path, out string? channelId)
    {
        channelId = null;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3
            && parts[0].Equals("channels", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("messages", StringComparison.OrdinalIgnoreCase))
        {
            channelId = WebUtility.UrlDecode(parts[1]);
            return true;
        }

        return false;
    }

    private static bool TryMatchLegacyGuildChannels(string path, out string? guildId)
    {
        guildId = null;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3
            && parts[0].Equals("guilds", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("channels", StringComparison.OrdinalIgnoreCase))
        {
            guildId = WebUtility.UrlDecode(parts[1]);
            return true;
        }

        return false;
    }

    private static async Task<T?> ReadBodyAsync<T>(Stream body, CancellationToken cancellationToken)
    {
        if (body is null || !body.CanRead)
            return default;

        return await JsonSerializer.DeserializeAsync<T>(body, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static T? DeserializeBody<T>(string? body)
        => string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body, JsonOptions);

    private static ModuleApiRequest BuildAdapterRequest(ModuleAuthRequest request, string path)
        => new(
            ModuleId: "discord",
            Method: request.Method,
            Path: "/" + path.TrimStart('/'),
            Query: ParseQuery(request.Query),
            Body: Stream.Null,
            ContentType: request.ContentType,
            Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, string?> ParseQuery(string? query)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return values;

        foreach (var part in query.Trim().TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = part.Split('=', 2);
            var name = WebUtility.UrlDecode(split[0]);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            values[name] = split.Length > 1 ? WebUtility.UrlDecode(split[1]) : string.Empty;
        }

        return values;
    }

    private static DiscordMessageRequest WithChannelId(DiscordMessageRequest? request, string? channelId)
        => request is null
            ? new DiscordMessageRequest(channelId, null, null)
            : request with { ChannelId = channelId };

    private static string? QueryValue(ModuleApiRequest request, string name)
        => request.Query.TryGetValue(name, out var value) ? value : null;

    private static bool ParseBooleanQuery(ModuleApiRequest request, string name)
        => bool.TryParse(QueryValue(request, name), out var value) && value;

    private static string? NormalizeSnowflake(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var pipeIndex = trimmed.IndexOf('|');
        if (pipeIndex > 0)
            trimmed = trimmed[..pipeIndex].Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string[] ParseScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return [];

        return scope
            .Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim())
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeScopes(IEnumerable<string>? scopes)
        => scopes is null
            ? []
            : scopes
                .Select(static scope => scope?.Trim())
                .Where(static scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray();

    private static bool HasScope(string? tokenScope, string requiredScope)
        => ParseScopes(tokenScope).Any(scope => string.Equals(scope, requiredScope, StringComparison.OrdinalIgnoreCase));

    private static bool ShouldRefreshToken(IntegrationToken token)
        => token.ExpiresAtUtc.HasValue && token.ExpiresAtUtc.Value <= DateTime.UtcNow.AddMinutes(1);

    private static object? ToTokenPayload(IntegrationToken? token)
        => token is null
            ? null
            : new
            {
                token.TokenType,
                token.Scope,
                scopes = ParseScopes(token.Scope),
                token.AccountId,
                token.AccountLogin,
                token.AccountDisplayName,
                token.ExpiresAtUtc,
                token.UpdatedAtUtc,
                token.Metadata
            };

    private static string? BuildGuildIconUrl(string? guildId, string? iconHash)
        => string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(iconHash)
            ? null
            : $"https://cdn.discordapp.com/icons/{Uri.EscapeDataString(guildId.Trim())}/{Uri.EscapeDataString(iconHash.Trim())}.png?size=64";

    private static string? BuildGuildsMessage(
        bool connected,
        bool hasGuildScope,
        string? botToken,
        int guildCount,
        bool botGuildsChecked,
        string? botGuildsError)
    {
        if (!connected)
            return "Connect Discord to browse account servers.";
        if (!hasGuildScope)
            return "Add the 'guilds' scope and reconnect Discord to browse account servers.";
        if (string.IsNullOrWhiteSpace(botToken))
            return "Set a Discord bot token to mark bot-enabled servers and load channels.";
        if (!botGuildsChecked)
            return string.IsNullOrWhiteSpace(botGuildsError)
                ? "Could not verify which servers already have the bot installed. Check the bot token and refresh servers."
                : $"Could not verify which servers already have the bot installed: {botGuildsError}";
        if (guildCount == 0)
            return "No Discord servers found for the connected account or configured bot token.";
        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string BuildAvatarUrl(string userId, string avatarHash)
    {
        var extension = avatarHash.StartsWith("a_", StringComparison.OrdinalIgnoreCase) ? "gif" : "png";
        return $"https://cdn.discordapp.com/avatars/{Uri.EscapeDataString(userId)}/{Uri.EscapeDataString(avatarHash)}.{extension}?size=128";
    }

    private static void SetMetadataValue(Dictionary<string, string> metadata, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            metadata.Remove(key);
        else
            metadata[key] = value.Trim();
    }

    private static string BuildBasicCredentials(string clientId, string clientSecret)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

    private static void LaunchBrowser(string url, string? browserPath)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            var normalizedBrowser = string.IsNullOrWhiteSpace(browserPath) || browserPath.Equals(SystemBrowserSentinel, StringComparison.OrdinalIgnoreCase)
                ? null
                : browserPath.Trim();

            var psi = string.IsNullOrWhiteSpace(normalizedBrowser)
                ? new ProcessStartInfo { FileName = url, UseShellExecute = true }
                : new ProcessStartInfo { FileName = normalizedBrowser, Arguments = url, UseShellExecute = false };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to launch browser for Discord OAuth: {ex.Message}");
        }
    }

    private static HttpClient CreateDiscordHttpClient(string? botToken)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrWhiteSpace(botToken))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", botToken);

        try
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Nyautomator/1.0 (+https://github.com/BlackJaxDev/Nyautomator)");
        }
        catch
        {
        }

        return client;
    }

    private static Uri BuildDiscordApiUri(string? path, string? query)
    {
        var baseUri = new Uri(ApiBase.TrimEnd('/') + "/");
        var relative = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        if (Uri.TryCreate(relative, UriKind.Absolute, out var absolute))
        {
            if (!string.Equals(absolute.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Discord module requests must target the Discord API host.");

            relative = absolute.PathAndQuery;
        }

        var split = relative.Split('?', 2);
        var pathPart = split[0].TrimStart('/');
        var queryPart = split.Length > 1 ? split[1] : string.Empty;
        var builder = new UriBuilder(new Uri(baseUri, pathPart))
        {
            Query = CombineQuery(queryPart, query)
        };
        return builder.Uri;
    }

    private static string CombineQuery(string? left, string? right)
    {
        left = (left ?? string.Empty).Trim().TrimStart('?');
        right = (right ?? string.Empty).Trim().TrimStart('?');
        if (left.Length == 0)
            return right;
        if (right.Length == 0)
            return left;
        return $"{left}&{right}";
    }

    private static HttpMethod ResolveHttpMethod(string? method)
    {
        var normalized = string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant();
        return normalized switch
        {
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            "HEAD" => HttpMethod.Head,
            "OPTIONS" => HttpMethod.Options,
            _ => HttpMethod.Get
        };
    }

    private static bool RequestSupportsBody(HttpMethod method)
        => method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Delete;

    private static IReadOnlyDictionary<string, string> FormatResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            headers[header.Key] = string.Join(", ", header.Value);
        return headers;
    }

    private string BuildDiscordDirectoryCacheKey(string kind, params string?[] parts)
        => string.Join("|", new[] { kind }.Concat(parts.Select(static part => part?.Trim() ?? string.Empty)));

    private bool TryGetCachedDirectory(CachedDirectoryPayload? cached, string key, bool requireFresh, out object payload)
    {
        lock (_directoryCacheLock)
        {
            if (cached is not null
                && string.Equals(cached.Key, key, StringComparison.Ordinal)
                && (!requireFresh || DateTime.UtcNow - cached.CreatedUtc <= DirectoryCacheDuration))
            {
                payload = cached.Payload;
                return true;
            }
        }

        payload = default!;
        return false;
    }

    private void SetCachedDirectory(ref CachedDirectoryPayload? cache, string key, object payload)
    {
        lock (_directoryCacheLock)
        {
            cache = new CachedDirectoryPayload(key, DateTime.UtcNow, payload);
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed class DiscordGuildSummary
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

    private sealed class GuildListing
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? IconUrl { get; set; }
        public bool Owner { get; set; }
        public string? Permissions { get; set; }
        public bool BotInstalled { get; set; }
        public bool BotInstallKnown { get; set; }
        public string Source { get; set; } = "user";
    }

    private sealed record BotGuildsResult(
        IReadOnlyList<DiscordRestClient.DiscordGuildResponse> Guilds,
        bool Checked,
        string? Error);

    private sealed record CachedDirectoryPayload(string Key, DateTime CreatedUtc, object Payload);

    private sealed record MessageSendResult(int StatusCode, object Payload, string? Error)
    {
        public static MessageSendResult Ok(object payload)
            => new(200, payload, null);

        public static MessageSendResult Fail(string error, int statusCode)
            => new(statusCode, new { success = false, error }, error);
    }

    private sealed record DiscordMessageRequest(
        string? ChannelId,
        string? Content,
        List<DiscordEmbedPayload>? Embeds);

    private sealed record DiscordReactionRequest(
        string? ChannelId,
        string? MessageId,
        string? Emoji);
}
