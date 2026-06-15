using System;
using Nyautomator.Discord.Events;

namespace NyautomatorUI.Server.Automation.Events.Discord;

public sealed class DiscordGuildMemberAdd : DiscordEventAction, ITestPayloadProvider
{
    public override string EventType => "GUILD_MEMBER_ADD";
    protected override string FriendlyName => "Member Joined";
    public override string Description => "Fired when a new member joins a Discord server.";

    public override Type? GetEventType() => typeof(GuildMemberAddEvent);

    public object? CreateTestPayload()
    {
        return new GuildMemberAddEvent
        {
            GuildId = "9876543210987654321",
            User = new DiscordUser
            {
                Id = "111222333444555666",
                Username = "NewMember",
                GlobalName = "New Member",
                Discriminator = "0",
                Bot = false
            },
            Roles = Array.Empty<string>(),
            JoinedAt = DateTime.UtcNow.ToString("o")
        };
    }
}
