using System.Text.Json.Serialization;

namespace Nyautomator.Discord.Events;

/// <summary>
/// Fired when a member leaves or is removed from a Discord guild.
/// Corresponds to Discord Gateway GUILD_MEMBER_REMOVE event.
/// </summary>
[DiscordEventData("GUILD_MEMBER_REMOVE", 1, "bot")]
public class GuildMemberRemoveEvent : DiscordEventDataBase
{
    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }

    [JsonPropertyName("user")]
    public DiscordUser? User { get; set; }

    public override string ToString() => $"Discord Member Remove: {User?.DisplayName} left guild {GuildId}";
}
