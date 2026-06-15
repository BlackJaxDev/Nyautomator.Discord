using System;
using System.Collections.Generic;
using Nyautomator.Discord.Events;

namespace NyautomatorUI.Server.Automation.Events.Discord;

public sealed class DiscordMessageCreate : DiscordEventAction, ITestPayloadProvider
{
    public override string EventType => "MESSAGE_CREATE";
    protected override string FriendlyName => "Message Created";
    public override string Description => "Fired when a message is sent in a Discord channel.";

    public override Type? GetEventType() => typeof(MessageCreateEvent);

    public object? CreateTestPayload()
    {
        var random = new Random();
        var usernames = new[] { "DiscordUser", "StreamFan", "ChatLurker", "BotDeveloper", "NightOwl" };
        var username = usernames[random.Next(usernames.Length)];
        var messages = new[] {
            "Hey everyone! Great stream!",
            "Just joined the server, love the community!",
            "Can someone clip that?",
            "GG that was amazing!",
            "When is the next stream?"
        };

        return new MessageCreateEvent
        {
            MessageId = $"{random.NextInt64(100000000000000000, 999999999999999999)}",
            ChannelId = "1234567890123456789",
            GuildId = "9876543210987654321",
            Author = new DiscordUser
            {
                Id = $"{random.NextInt64(100000000000000000, 999999999999999999)}",
                Username = username,
                GlobalName = username,
                Discriminator = "0",
                Bot = false
            },
            Content = messages[random.Next(messages.Length)],
            Timestamp = DateTime.UtcNow.ToString("o"),
            MentionEveryone = false,
            Tts = false
        };
    }
}
