using System.Text.Json.Serialization;

namespace Nyautomator.Discord.Events;

/// <summary>
/// Fired when a user invokes a slash command, button, select menu, or other interaction.
/// Corresponds to Discord Gateway INTERACTION_CREATE event.
/// </summary>
[DiscordEventData("INTERACTION_CREATE", 1, "bot")]
public class InteractionCreateEvent : DiscordEventDataBase
{
    [JsonPropertyName("id")]
    public string? InteractionId { get; set; }

    [JsonPropertyName("application_id")]
    public string? ApplicationId { get; set; }

    /// <summary>
    /// Interaction type: 1=Ping, 2=ApplicationCommand, 3=MessageComponent, 4=ApplicationCommandAutocomplete, 5=ModalSubmit
    /// </summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("data")]
    public DiscordInteractionData? Data { get; set; }

    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }

    [JsonPropertyName("channel_id")]
    public string? ChannelId { get; set; }

    [JsonPropertyName("member")]
    public DiscordGuildMember? Member { get; set; }

    [JsonPropertyName("user")]
    public DiscordUser? User { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    public override string ToString() => $"Discord Interaction: {Data?.Name ?? "unknown"} by {Member?.DisplayName ?? User?.DisplayName}";
}
