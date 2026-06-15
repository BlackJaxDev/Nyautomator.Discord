using System;
using Nyautomator.Discord.Events;

namespace NyautomatorUI.Server.Automation.Events.Discord;

public sealed class DiscordMessageReactionAdd : DiscordEventAction, ITestPayloadProvider
{
    public override string EventType => "MESSAGE_REACTION_ADD";
    protected override string FriendlyName => "Reaction Added";
    public override string Description => "Fired when a user adds a reaction to a message.";

    public override Type? GetEventType() => typeof(MessageReactionAddEvent);

    public object? CreateTestPayload()
    {
        return new MessageReactionAddEvent
        {
            UserId = "111222333444555666",
            ChannelId = "1234567890123456789",
            MessageId = "9876543210987654321",
            GuildId = "5555666677778888999",
            Member = new DiscordGuildMember
            {
                User = new DiscordUser
                {
                    Id = "111222333444555666",
                    Username = "ReactUser",
                    GlobalName = "React User",
                    Discriminator = "0"
                },
                Roles = Array.Empty<string>(),
                JoinedAt = DateTime.UtcNow.AddDays(-30).ToString("o")
            },
            Emoji = new DiscordEmoji
            {
                Name = "\u2764\ufe0f",
                Animated = false
            }
        };
    }
}
