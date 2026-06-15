using System.Text.Json.Serialization;

namespace Nyautomator.Discord.Events;

/// <summary>
/// Fired when a user's presence (status/activity) updates.
/// Corresponds to Discord Gateway PRESENCE_UPDATE event.
/// </summary>
[DiscordEventData("PRESENCE_UPDATE", 1, "bot")]
public class PresenceUpdateEvent : DiscordEventDataBase
{
    [JsonPropertyName("user")]
    public DiscordUser? User { get; set; }

    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }

    /// <summary>
    /// Status: "idle", "dnd", "online", "offline".
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("activities")]
    public DiscordActivity[]? Activities { get; set; }

    public override string ToString() => $"Discord Presence: {User?.DisplayName} is {Status}";
}

/// <summary>
/// Represents a Discord activity (game, streaming, etc.).
/// </summary>
public class DiscordActivity
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Activity type: 0=Playing, 1=Streaming, 2=Listening, 3=Watching, 4=Custom, 5=Competing
    /// </summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}
