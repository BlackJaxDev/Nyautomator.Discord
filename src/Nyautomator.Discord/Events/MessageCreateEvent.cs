using System.Text.Json.Serialization;

namespace Nyautomator.Discord.Events;

/// <summary>
/// Fired when a message is created in a Discord channel.
/// Corresponds to Discord Gateway MESSAGE_CREATE event.
/// </summary>
[DiscordEventData("MESSAGE_CREATE", 1, "bot", "messages.read")]
public class MessageCreateEvent : DiscordEventDataBase
{
    [JsonPropertyName("id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; set; }

    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }

    [JsonPropertyName("author")]
    public DiscordUser? Author { get; set; }

    [JsonPropertyName("member")]
    public DiscordGuildMember? Member { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("attachments")]
    public DiscordAttachment[]? Attachments { get; set; }

    [JsonPropertyName("embeds")]
    public DiscordEmbed[]? Embeds { get; set; }

    [JsonPropertyName("mention_everyone")]
    public bool MentionEveryone { get; set; }

    [JsonPropertyName("tts")]
    public bool Tts { get; set; }

    public override string ToString() => $"Discord Message: [{Author?.DisplayName}] {Content}";
}
