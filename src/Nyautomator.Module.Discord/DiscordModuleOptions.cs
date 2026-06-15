using System.Text.Json.Serialization;
using Nyautomator;
using Nyautomator.Discord;

namespace Nyautomator.Modules.Discord;

public sealed class DiscordModuleOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RedirectUri { get; set; } = DiscordOAuth.DefaultRedirectUri;
    public List<string> Scopes { get; set; } = ["identify"];
    public string? BotToken { get; set; }
    public string? GuildId { get; set; }
    public string? AuthBrowserPath { get; set; }

    [JsonIgnore]
    public bool HasOAuthCredentials =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    public static DiscordModuleOptions CreateDefault()
        => new()
        {
            RedirectUri = DiscordOAuth.DefaultRedirectUri,
            Scopes = ["identify"]
        };

    public static DiscordModuleOptions FromLegacy(AppConfiguration.DiscordOptions? legacy)
    {
        var defaults = CreateDefault();
        if (legacy is null)
            return defaults;

        defaults.ClientId = legacy.ClientId;
        defaults.ClientSecret = legacy.ClientSecret;
        defaults.RedirectUri = string.IsNullOrWhiteSpace(legacy.RedirectUri)
            ? defaults.RedirectUri
            : legacy.RedirectUri;
        defaults.Scopes = legacy.Scopes is { Count: > 0 }
            ? legacy.Scopes.Where(static scope => !string.IsNullOrWhiteSpace(scope)).Select(static scope => scope.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : defaults.Scopes;
        defaults.BotToken = legacy.BotToken;
        defaults.GuildId = legacy.GuildId;
        return defaults;
    }

    public DiscordModuleOptions MergeFallback(DiscordModuleOptions fallback)
    {
        ClientId = First(ClientId, fallback.ClientId);
        ClientSecret = First(ClientSecret, fallback.ClientSecret);
        RedirectUri = First(RedirectUri, fallback.RedirectUri, DiscordOAuth.DefaultRedirectUri);
        BotToken = First(BotToken, fallback.BotToken);
        GuildId = First(GuildId, fallback.GuildId);
        AuthBrowserPath = First(AuthBrowserPath, fallback.AuthBrowserPath);

        if (Scopes is null || Scopes.Count == 0 || Scopes.All(static scope => string.IsNullOrWhiteSpace(scope)))
            Scopes = fallback.Scopes is { Count: > 0 } ? [.. fallback.Scopes] : ["identify"];

        return this;
    }

    private static string? First(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
