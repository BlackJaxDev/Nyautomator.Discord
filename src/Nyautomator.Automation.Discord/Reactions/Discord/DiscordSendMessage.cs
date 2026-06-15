using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Sends a message to a Discord channel using the Discord API.
/// </summary>
public sealed class DiscordSendMessage : ReactionType, IAsyncReaction
{
    public override string Id => "Discord.Channel.SendMessage";
    public override string DisplayName => "Discord: Send Message";
    public override string Description => "Sends a message to a Discord channel.";

    [Description("Message that will be sent to the Discord channel. Supports dynamic values.")]
    public string Message { get; set; } = string.Empty;

    [OptionsProvider(typeof(DiscordChannelOptionsProvider))]
    [Description("The Discord channel ID to send the message to.")]
    public string? ChannelId { get; set; }

    public override void Execute()
        => ExecuteAsync().GetAwaiter().GetResult();

    public async Task ExecuteAsync()
    {
        var text = (Message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0)
        {
            Console.WriteLine("[Automation][Discord] Skipping empty message.");
            return;
        }

        var normalizedChannelId = NormalizeChannelId(ChannelId);
        if (string.IsNullOrWhiteSpace(normalizedChannelId))
        {
            Console.WriteLine("[Automation][Discord] No channel ID specified, cannot send message.");
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
                "/messages",
                null,
                new { channelId = normalizedChannelId, content = text },
                "application/json",
                10000), CancellationToken.None).ConfigureAwait(false);
            if (!response.Success || !response.IsSuccess)
                Console.WriteLine($"[Automation][Discord] Failed to send message to channel {normalizedChannelId}: {response.Error ?? response.Body}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Automation][Discord] Error sending message: {ex.Message}");
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
