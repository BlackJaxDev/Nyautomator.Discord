using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

[AutomationOutputs("out", "success", "issuccess", "statuscode", "error", "rawjson", "membersjson", "firstmemberjson", "count")]
public sealed class DiscordGetMembers : ReactionType, IHasOutputs, IAsyncReaction
{
    public override string Id => "Discord.Guild.GetMembers";
    public override string DisplayName => "Discord: Get Members";
    public override string Description => "Retrieves Discord server members that match a filter and returns selected member information.";

    [OptionsProvider(typeof(DiscordGuildOptionsProvider))]
    [Description("Discord server ID. Leave empty to use the configured server.")]
    public string? GuildId { get; set; }

    [Description("Filter as JSON, key=value lines, or a search term. Examples: userId=123, usernameContains=mod, roles=Subscriber, rolesAny=VIP,Mod, bot=false.")]
    public string Filter { get; set; } = string.Empty;

    [Description("Comma-separated Discord member/user fields to return. Examples: id, username, globalName, nick, roles, roleIds, joinedAt, avatarUrl, banner, flags, premiumType, primaryGuild, raw, all.")]
    public string Information { get; set; } = "id, username, displayName, roles";

    [Description("Maximum members to return. Large all-member scans may require the bot's Server Members intent.")]
    public int MaxResults { get; set; } = 1000;

    public IDictionary<string, object?> Outputs { get; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public override void Execute()
        => ExecuteAsync().GetAwaiter().GetResult();

    public async Task ExecuteAsync()
    {
        SetDefaults();

        var sender = AutomationHost.SendAuthenticatedIntegrationRequest;
        if (sender is null)
        {
            Outputs["Error"] = "Authenticated integration request host delegate is not available.";
            return;
        }

        try
        {
            var response = await sender(new AuthenticatedIntegrationRequest(
                "Discord",
                "POST",
                "/members/query",
                null,
                new
                {
                    guildId = NormalizeOptionId(GuildId),
                    filter = string.IsNullOrWhiteSpace(Filter) ? null : Filter,
                    fields = string.IsNullOrWhiteSpace(Information) ? null : Information,
                    maxResults = Math.Clamp(MaxResults, 1, 10000)
                },
                "application/json",
                30000), CancellationToken.None).ConfigureAwait(false);

            PopulateMemberResponse(response);
        }
        catch (Exception ex)
        {
            Outputs["Error"] = ex.Message;
        }
    }

    public override Type? GetOutputType(string? outputHandle)
    {
        var handle = AutomationValueHelper.NormalizeValueHandle(outputHandle).ToLowerInvariant();
        return ResolveOutputTypeToken(handle switch
        {
            "" or "out" => "object:System.Text.Json.JsonElement",
            "success" or "issuccess" => "boolean",
            "statuscode" or "count" => "int32",
            "error" or "rawjson" or "membersjson" or "firstmemberjson" => "string",
            _ => "object"
        }, fallbackToObject: false);
    }

    private void SetDefaults()
    {
        Outputs.Clear();
        Outputs["Success"] = false;
        Outputs["out"] = null;
        Outputs["IsSuccess"] = false;
        Outputs["StatusCode"] = 0;
        Outputs["Error"] = string.Empty;
        Outputs["RawJson"] = string.Empty;
        Outputs["MembersJson"] = "[]";
        Outputs["FirstMemberJson"] = string.Empty;
        Outputs["Count"] = 0;
    }

    private void PopulateMemberResponse(AuthenticatedIntegrationResponse response)
    {
        Outputs["StatusCode"] = response.StatusCode;
        Outputs["IsSuccess"] = response.IsSuccess;
        Outputs["RawJson"] = response.Body ?? string.Empty;
        Outputs["Error"] = response.Error ?? string.Empty;

        if (string.IsNullOrWhiteSpace(response.Body))
        {
            Outputs["Success"] = response.Success && response.IsSuccess;
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            var bodySuccess = root.TryGetProperty("success", out var successProp)
                              && successProp.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? successProp.GetBoolean()
                : response.Success && response.IsSuccess;

            Outputs["Success"] = response.Success && response.IsSuccess && bodySuccess;

            if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
                Outputs["Error"] = errorProp.GetString() ?? string.Empty;

            if (root.TryGetProperty("members", out var membersProp) && membersProp.ValueKind == JsonValueKind.Array)
            {
                Outputs["out"] = membersProp.Clone();
                Outputs["MembersJson"] = membersProp.GetRawText();
                Outputs["Count"] = membersProp.GetArrayLength();
                Outputs["FirstMemberJson"] = membersProp.GetArrayLength() > 0
                    ? membersProp[0].GetRawText()
                    : string.Empty;
            }
        }
        catch (JsonException ex)
        {
            Outputs["Error"] = ex.Message;
            Outputs["Success"] = false;
        }
    }

    private static string? NormalizeOptionId(string? value)
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

[AutomationOutputs("out", "success", "issuccess", "statuscode", "error", "rawjson", "changedcount", "failedcount", "userid", "displayname")]
public sealed class DiscordModifyMemberRoles : ReactionType, IHasOutputs, IAsyncReaction
{
    public override string Id => "Discord.Guild.ModifyMemberRoles";
    public override string DisplayName => "Discord: Modify Member Roles";
    public override string Description => "Adds or removes one or more Discord roles from a server member by user ID, username, nickname, or display name.";

    [OptionsProvider(typeof(DiscordGuildOptionsProvider))]
    [Description("Discord server ID. Leave empty to use the configured server.")]
    public string? GuildId { get; set; }

    [Description("User ID, mention, username, nickname, or display name. Use a user ID when names are ambiguous.")]
    public string User { get; set; } = string.Empty;

    [OptionsProvider(typeof(DiscordRoleOptionsProvider))]
    [Description("One or more role IDs or role names, separated by commas or new lines.")]
    public string Roles { get; set; } = string.Empty;

    [OptionsProvider(typeof(DiscordRoleActionOptionsProvider))]
    [Description("Whether to add or remove the roles.")]
    public string Action { get; set; } = "add";

    [Description("Optional Discord audit log reason.")]
    public string AuditLogReason { get; set; } = string.Empty;

    public IDictionary<string, object?> Outputs { get; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public override void Execute()
        => ExecuteAsync().GetAwaiter().GetResult();

    public async Task ExecuteAsync()
    {
        SetDefaults();

        var sender = AutomationHost.SendAuthenticatedIntegrationRequest;
        if (sender is null)
        {
            Outputs["Error"] = "Authenticated integration request host delegate is not available.";
            return;
        }

        if (string.IsNullOrWhiteSpace(User))
        {
            Outputs["Error"] = "User is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Roles))
        {
            Outputs["Error"] = "Roles is required.";
            return;
        }

        try
        {
            var response = await sender(new AuthenticatedIntegrationRequest(
                "Discord",
                "POST",
                "/member-roles",
                null,
                new
                {
                    guildId = NormalizeOptionId(GuildId),
                    user = User,
                    roles = Roles,
                    action = string.IsNullOrWhiteSpace(Action) ? "add" : Action,
                    auditLogReason = string.IsNullOrWhiteSpace(AuditLogReason) ? null : AuditLogReason
                },
                "application/json",
                30000), CancellationToken.None).ConfigureAwait(false);

            PopulateRoleResponse(response);
        }
        catch (Exception ex)
        {
            Outputs["Error"] = ex.Message;
        }
    }

    public override Type? GetOutputType(string? outputHandle)
    {
        var handle = AutomationValueHelper.NormalizeValueHandle(outputHandle).ToLowerInvariant();
        return ResolveOutputTypeToken(handle switch
        {
            "" or "out" or "success" or "issuccess" => "boolean",
            "statuscode" or "changedcount" or "failedcount" => "int32",
            "error" or "rawjson" or "userid" or "displayname" => "string",
            _ => "object"
        }, fallbackToObject: false);
    }

    private void SetDefaults()
    {
        Outputs.Clear();
        Outputs["Success"] = false;
        Outputs["out"] = false;
        Outputs["IsSuccess"] = false;
        Outputs["StatusCode"] = 0;
        Outputs["Error"] = string.Empty;
        Outputs["RawJson"] = string.Empty;
        Outputs["ChangedCount"] = 0;
        Outputs["FailedCount"] = 0;
        Outputs["UserId"] = string.Empty;
        Outputs["DisplayName"] = string.Empty;
    }

    private void PopulateRoleResponse(AuthenticatedIntegrationResponse response)
    {
        Outputs["StatusCode"] = response.StatusCode;
        Outputs["IsSuccess"] = response.IsSuccess;
        Outputs["RawJson"] = response.Body ?? string.Empty;
        Outputs["Error"] = response.Error ?? string.Empty;

        if (string.IsNullOrWhiteSpace(response.Body))
        {
            Outputs["Success"] = response.Success && response.IsSuccess;
            Outputs["out"] = Outputs["Success"];
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            var bodySuccess = root.TryGetProperty("success", out var successProp)
                              && successProp.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? successProp.GetBoolean()
                : response.Success && response.IsSuccess;

            Outputs["Success"] = response.Success && bodySuccess;
            Outputs["out"] = response.Success && bodySuccess;

            if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
                Outputs["Error"] = errorProp.GetString() ?? string.Empty;
            if (root.TryGetProperty("changedCount", out var changedProp) && changedProp.TryGetInt32(out var changed))
                Outputs["ChangedCount"] = changed;
            if (root.TryGetProperty("failedCount", out var failedProp) && failedProp.TryGetInt32(out var failed))
                Outputs["FailedCount"] = failed;
            if (root.TryGetProperty("userId", out var userIdProp) && userIdProp.ValueKind == JsonValueKind.String)
                Outputs["UserId"] = userIdProp.GetString() ?? string.Empty;
            if (root.TryGetProperty("displayName", out var displayNameProp) && displayNameProp.ValueKind == JsonValueKind.String)
                Outputs["DisplayName"] = displayNameProp.GetString() ?? string.Empty;
        }
        catch (JsonException ex)
        {
            Outputs["Error"] = ex.Message;
            Outputs["Success"] = false;
            Outputs["out"] = false;
        }
    }

    private static string? NormalizeOptionId(string? value)
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

public sealed class DiscordRoleActionOptionsProvider : IStringOptionsProvider
{
    public IEnumerable<string> GetOptions() => ["add", "remove"];
}

public sealed class DiscordGuildOptionsProvider : IStringOptionsProvider
{
    public IEnumerable<string> GetOptions()
    {
        try
        {
            var sender = AutomationHost.SendAuthenticatedIntegrationRequest;
            if (sender is null)
                return [];

            var response = sender(new AuthenticatedIntegrationRequest(
                "Discord",
                "GET",
                "/guilds",
                null,
                null,
                "application/json",
                5000), CancellationToken.None).GetAwaiter().GetResult();
            if (!response.Success || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Body))
                return [];

            using var document = JsonDocument.Parse(response.Body);
            if (!document.RootElement.TryGetProperty("guilds", out var guilds) || guilds.ValueKind != JsonValueKind.Array)
                return [];

            return [.. guilds.EnumerateArray()
                .Select(ReadGuildOption)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Automation][Discord] Failed to enumerate server options: {ex.Message}");
            return [];
        }
    }

    private static string? ReadGuildOption(JsonElement guild)
    {
        if (guild.ValueKind != JsonValueKind.Object)
            return null;

        if (!guild.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idProp.GetString()))
        {
            return null;
        }

        var id = idProp.GetString()!.Trim();
        var name = guild.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
            ? nameProp.GetString()
            : null;

        return string.IsNullOrWhiteSpace(name) ? id : $"{id}|{name!.Trim()}";
    }
}

public sealed class DiscordRoleOptionsProvider : IStringOptionsProvider
{
    public IEnumerable<string> GetOptions()
    {
        try
        {
            var sender = AutomationHost.SendAuthenticatedIntegrationRequest;
            if (sender is null)
                return [];

            var response = sender(new AuthenticatedIntegrationRequest(
                "Discord",
                "GET",
                "/roles",
                null,
                null,
                "application/json",
                5000), CancellationToken.None).GetAwaiter().GetResult();
            if (!response.Success || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Body))
                return [];

            using var document = JsonDocument.Parse(response.Body);
            if (!document.RootElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
                return [];

            return [.. roles.EnumerateArray()
                .Select(ReadRoleOption)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Automation][Discord] Failed to enumerate role options: {ex.Message}");
            return [];
        }
    }

    private static string? ReadRoleOption(JsonElement role)
    {
        if (role.ValueKind != JsonValueKind.Object)
            return null;

        if (!role.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idProp.GetString()))
        {
            return null;
        }

        var id = idProp.GetString()!.Trim();
        var name = role.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
            ? nameProp.GetString()
            : null;

        return string.IsNullOrWhiteSpace(name) ? id : $"{id}|@{name!.Trim()}";
    }
}
