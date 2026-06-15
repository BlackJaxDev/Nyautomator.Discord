using System.Text.Json.Serialization;

namespace Nyautomator.Discord.Events;

/// <summary>
/// Fired when a user adds a reaction to a message.
/// Corresponds to Discord Gateway MESSAGE_REACTION_ADD event.
/// </summary>
[DiscordEventData("MESSAGE_REACTION_ADD", 1, "bot")]
public class MessageReactionAddEvent : DiscordEventDataBase
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; set; }

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }

    [JsonPropertyName("member")]
    public DiscordGuildMember? Member { get; set; }

    [JsonPropertyName("emoji")]
    public DiscordEmoji? Emoji { get; set; }

    public override string ToString() => $"Discord Reaction: {Member?.DisplayName} reacted with {Emoji?.Name} on message {MessageId}";
}
