using System.Text.Json.Serialization;

namespace Nyautomator.Discord.Events;

/// <summary>
/// Fired when a new member joins a Discord guild.
/// Corresponds to Discord Gateway GUILD_MEMBER_ADD event.
/// </summary>
[DiscordEventData("GUILD_MEMBER_ADD", 1, "bot")]
public class GuildMemberAddEvent : DiscordEventDataBase
{
    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }

    [JsonPropertyName("user")]
    public DiscordUser? User { get; set; }

    [JsonPropertyName("nick")]
    public string? Nick { get; set; }

    [JsonPropertyName("roles")]
    public string[]? Roles { get; set; }

    [JsonPropertyName("joined_at")]
    public string? JoinedAt { get; set; }

    [JsonPropertyName("premium_since")]
    public string? PremiumSince { get; set; }

    public override string ToString() => $"Discord Member Add: {User?.DisplayName} joined guild {GuildId}";
}
