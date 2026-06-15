using System;
using Nyautomator.Discord.Events;

namespace NyautomatorUI.Server.Automation.Events.Discord;

public sealed class DiscordInteractionCreate : DiscordEventAction, ITestPayloadProvider
{
    public override string EventType => "INTERACTION_CREATE";
    protected override string FriendlyName => "Interaction";
    public override string Description => "Fired when a user invokes a slash command, button, or other interaction.";

    public override Type? GetEventType() => typeof(InteractionCreateEvent);

    public object? CreateTestPayload()
    {
        return new InteractionCreateEvent
        {
            InteractionId = $"{new Random().NextInt64(100000000000000000, 999999999999999999)}",
            ApplicationId = "000111222333444555",
            Type = 2, // ApplicationCommand
            Data = new DiscordInteractionData
            {
                Id = "999888777666555444",
                Name = "hello",
                Type = 1 // ChatInput
            },
            GuildId = "9876543210987654321",
            ChannelId = "1234567890123456789",
            Member = new DiscordGuildMember
            {
                User = new DiscordUser
                {
                    Id = "111222333444555666",
                    Username = "SlashUser",
                    GlobalName = "Slash User",
                    Discriminator = "0"
                },
                Roles = Array.Empty<string>(),
                JoinedAt = DateTime.UtcNow.AddDays(-90).ToString("o")
            },
            Token = "test-interaction-token-placeholder"
        };
    }
}
