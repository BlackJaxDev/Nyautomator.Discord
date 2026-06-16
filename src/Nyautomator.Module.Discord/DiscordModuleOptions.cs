using System.Text.Json.Serialization;
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
    public string? GuildName { get; set; }
    public bool? GuildBotInstalled { get; set; }
    public string? GuildIconUrl { get; set; }
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

    public DiscordModuleOptions MergeFallback(DiscordModuleOptions fallback)
    {
        ClientId = First(ClientId, fallback.ClientId);
        ClientSecret = First(ClientSecret, fallback.ClientSecret);
        RedirectUri = First(RedirectUri, fallback.RedirectUri, DiscordOAuth.DefaultRedirectUri);
        BotToken = First(BotToken, fallback.BotToken);
        GuildId = First(GuildId, fallback.GuildId);
        GuildName = First(GuildName, fallback.GuildName);
        GuildIconUrl = First(GuildIconUrl, fallback.GuildIconUrl);
        GuildBotInstalled ??= fallback.GuildBotInstalled;
        AuthBrowserPath = First(AuthBrowserPath, fallback.AuthBrowserPath);

        if (Scopes is null || Scopes.Count == 0 || Scopes.All(static scope => string.IsNullOrWhiteSpace(scope)))
            Scopes = fallback.Scopes is { Count: > 0 } ? [.. fallback.Scopes] : ["identify"];

        return this;
    }

    private static string? First(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
