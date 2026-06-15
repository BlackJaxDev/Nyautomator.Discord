using System;
using Nyautomator.Discord.Events;

namespace NyautomatorUI.Server.Automation.Events.Discord;

public sealed class DiscordGuildMemberRemove : DiscordEventAction, ITestPayloadProvider
{
    public override string EventType => "GUILD_MEMBER_REMOVE";
    protected override string FriendlyName => "Member Left";
    public override string Description => "Fired when a member leaves or is removed from a Discord server.";

    public override Type? GetEventType() => typeof(GuildMemberRemoveEvent);

    public object? CreateTestPayload()
    {
        return new GuildMemberRemoveEvent
        {
            GuildId = "9876543210987654321",
            User = new DiscordUser
            {
                Id = "111222333444555666",
                Username = "FormerMember",
                GlobalName = "Former Member",
                Discriminator = "0",
                Bot = false
            }
        };
    }
}
