using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NyautomatorUI.Server.Automation;

public static class DiscordScopeUtility
{
    private static readonly string[] _baselineScopes =
    [
        "identify"
    ];

    /// <summary>
    /// Computes the auto-generated scopes for Discord based on the current automation state.
    /// Scans for Discord event/action nodes and extracts their required scopes.
    /// </summary>
    public static IReadOnlyList<string> ComputeAutoScopes(AutomationState? state)
    {
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var baseline in _baselineScopes)
            scopes.Add(baseline);

        if (state?.Actions is null || state.Actions.Count == 0)
            return scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        // Build a lookup of Discord action types by their Id
        var discordActions = new Dictionary<string, DiscordEventAction>(StringComparer.OrdinalIgnoreCase);
        var asm = typeof(DiscordEventAction).Assembly;
        foreach (var type in asm.GetTypes())
        {
            if (type.IsAbstract || !typeof(DiscordEventAction).IsAssignableFrom(type)) continue;
            try
            {
                var inst = Activator.CreateInstance(type) as DiscordEventAction;
                if (inst is not null)
                    discordActions[inst.Id] = inst;
            }
            catch { }
        }

        // Scan through all automation actions looking for Discord triggers
        foreach (var action in state.Actions)
        {
            if (action is null || string.IsNullOrWhiteSpace(action.TriggerType))
                continue;

            if (!action.TriggerType.StartsWith("Discord.", StringComparison.OrdinalIgnoreCase))
                continue;

            if (discordActions.TryGetValue(action.TriggerType, out var dcAction))
            {
                foreach (var scope in dcAction.GetRequiredScopes())
                    scopes.Add(scope);
            }
        }

        return scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Gets all available Discord OAuth2 scopes with their descriptions.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetAllScopes() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["identify"] = "Allows /users/@me without email",
        ["email"] = "Enables /users/@me to return an email",
        ["guilds"] = "Allows /users/@me/guilds to return basic information about all of a user's guilds",
        ["guilds.join"] = "Allows /guilds/{guild.id}/members/{user.id} to be used for joining users to a guild",
        ["guilds.members.read"] = "Allows /users/@me/guilds/{guild.id}/member to return a user's member information in a guild",
        ["connections"] = "Allows /users/@me/connections to return linked third-party accounts",
        ["bot"] = "For OAuth2 bots, this puts the bot in the user's selected guild by default",
        ["messages.read"] = "Read messages from all client channels",
        ["webhook.incoming"] = "Generates a webhook returned in the OAuth token response for authorization code grants",
        ["applications.commands"] = "Allows your app to add commands to a guild (included by default with the bot scope)",
        ["applications.commands.update"] = "Allows your app to update its commands using a Bearer token (client credentials grant only)",
        ["applications.commands.permissions.update"] = "Allows your app to update permissions for its commands in a guild",
        ["applications.entitlements"] = "Allows your app to read entitlements for a user's applications",
        ["applications.builds.read"] = "Allows your app to read build data for a user's applications",
        ["role_connections.write"] = "Allows your app to update a user's connection and metadata for the app",
        ["voice"] = "Allows your app to connect to voice on user's behalf and see all voice members",
        ["activities.read"] = "Allows your app to fetch data from a user's Now Playing/Recently Played list",
        ["activities.write"] = "Allows your app to update a user's activity"
    };
}
