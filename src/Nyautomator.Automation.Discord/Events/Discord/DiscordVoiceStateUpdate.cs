using System;
using Nyautomator.Discord.Events;

namespace NyautomatorUI.Server.Automation.Events.Discord;

public sealed class DiscordVoiceStateUpdate : DiscordEventAction, ITestPayloadProvider
{
    public override string EventType => "VOICE_STATE_UPDATE";
    protected override string FriendlyName => "Voice State Update";
    public override string Description => "Fired when a user joins, leaves, or changes state in a voice channel.";

    public override Type? GetEventType() => typeof(VoiceStateUpdateEvent);

    public object? CreateTestPayload()
    {
        return new VoiceStateUpdateEvent
        {
            GuildId = "9876543210987654321",
            ChannelId = "1111222233334444555",
            UserId = "111222333444555666",
            Member = new DiscordGuildMember
            {
                User = new DiscordUser
                {
                    Id = "111222333444555666",
                    Username = "VoiceUser",
                    GlobalName = "Voice User",
                    Discriminator = "0"
                },
                Roles = Array.Empty<string>(),
                JoinedAt = DateTime.UtcNow.AddDays(-60).ToString("o")
            },
            SessionId = $"session-{Guid.NewGuid():N}",
            Deaf = false,
            Mute = false,
            SelfDeaf = false,
            SelfMute = false,
            SelfVideo = false,
            Suppress = false
        };
    }
}
