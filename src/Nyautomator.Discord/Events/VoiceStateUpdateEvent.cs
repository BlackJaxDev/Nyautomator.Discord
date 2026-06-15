using System.Text.Json.Serialization;

namespace Nyautomator.Discord.Events;

/// <summary>
/// Fired when a user's voice state changes (join/leave/mute/deafen voice channel).
/// Corresponds to Discord Gateway VOICE_STATE_UPDATE event.
/// </summary>
[DiscordEventData("VOICE_STATE_UPDATE", 1, "bot")]
public class VoiceStateUpdateEvent : DiscordEventDataBase
{
    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }

    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("member")]
    public DiscordGuildMember? Member { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("deaf")]
    public bool Deaf { get; set; }

    [JsonPropertyName("mute")]
    public bool Mute { get; set; }

    [JsonPropertyName("self_deaf")]
    public bool SelfDeaf { get; set; }

    [JsonPropertyName("self_mute")]
    public bool SelfMute { get; set; }

    [JsonPropertyName("self_stream")]
    public bool? SelfStream { get; set; }

    [JsonPropertyName("self_video")]
    public bool SelfVideo { get; set; }

    [JsonPropertyName("suppress")]
    public bool Suppress { get; set; }

    public override string ToString() => $"Discord Voice: {Member?.DisplayName} in channel {ChannelId}";
}
