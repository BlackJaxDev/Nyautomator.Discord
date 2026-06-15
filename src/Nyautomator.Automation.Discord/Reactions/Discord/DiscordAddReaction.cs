using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Adds a reaction (emoji) to a Discord message.
/// </summary>
public sealed class DiscordAddReaction : ReactionType, IAsyncReaction
{
    public override string Id => "Discord.Message.AddReaction";
    public override string DisplayName => "Discord: Add Reaction";
    public override string Description => "Adds a reaction emoji to a Discord message.";

    [OptionsProvider(typeof(DiscordChannelOptionsProvider))]
    [Description("The Discord channel ID containing the message.")]
    public string? ChannelId { get; set; }

    [Description("The Discord message ID to react to.")]
    public string? MessageId { get; set; }

    [Description("The emoji to react with (e.g., '\u2764\ufe0f' or custom emoji format 'name:id').")]
    public string Emoji { get; set; } = "\u2764\ufe0f";

    public override void Execute()
        => ExecuteAsync().GetAwaiter().GetResult();

    public async Task ExecuteAsync()
    {
        var normalizedChannelId = NormalizeChannelId(ChannelId);
        if (string.IsNullOrWhiteSpace(normalizedChannelId) || string.IsNullOrWhiteSpace(MessageId))
        {
            Console.WriteLine("[Automation][Discord] Cannot add reaction: channel ID and message ID are required.");
            return;
        }

        var emoji = (Emoji ?? string.Empty).Trim();
        if (emoji.Length == 0)
        {
            Console.WriteLine("[Automation][Discord] Skipping empty emoji reaction.");
            return;
        }

        try
        {
            var sender = AutomationHost.SendAuthenticatedIntegrationRequest;
            if (sender is null)
            {
                Console.WriteLine("[Automation][Discord] Authenticated integration host delegate is not available.");
                return;
            }

            var response = await sender(new AuthenticatedIntegrationRequest(
                "Discord",
                "POST",
                "/reactions",
                null,
                new { channelId = normalizedChannelId, messageId = MessageId, emoji },
                "application/json",
                10000), CancellationToken.None).ConfigureAwait(false);
            if (!response.Success || !response.IsSuccess)
                Console.WriteLine($"[Automation][Discord] Failed to add reaction {emoji} to message {MessageId}: {response.Error ?? response.Body}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Automation][Discord] Error adding reaction: {ex.Message}");
        }
    }

    private static string? NormalizeChannelId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var pipeIndex = trimmed.IndexOf('|');
        if (pipeIndex > 0)
            trimmed = trimmed[..pipeIndex].Trim();

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
