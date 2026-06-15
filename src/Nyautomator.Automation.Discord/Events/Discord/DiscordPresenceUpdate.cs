using System;
using Nyautomator.Discord.Events;

namespace NyautomatorUI.Server.Automation.Events.Discord;

public sealed class DiscordPresenceUpdate : DiscordEventAction, ITestPayloadProvider
{
    public override string EventType => "PRESENCE_UPDATE";
    protected override string FriendlyName => "Presence Update";
    public override string Description => "Fired when a user's status or activity changes in Discord.";

    public override Type? GetEventType() => typeof(PresenceUpdateEvent);

    public object? CreateTestPayload()
    {
        return new PresenceUpdateEvent
        {
            User = new DiscordUser
            {
                Id = "111222333444555666",
                Username = "StreamerFriend",
                GlobalName = "Streamer Friend"
            },
            GuildId = "9876543210987654321",
            Status = "online",
            Activities = new[]
            {
                new DiscordActivity
                {
                    Name = "Visual Studio Code",
                    Type = 0, // Playing
                    Details = "Editing main.cs",
                    State = "Workspace: MyProject"
                }
            }
        };
    }
}
