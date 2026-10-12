using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Supplies Discord servers that can be used by automation destination pickers.
/// Option format: displayName :: guildId
/// </summary>
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
                .Select(ReadOptionValue)
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

    private static string? ReadOptionValue(JsonElement guild)
    {
        if (guild.ValueKind != JsonValueKind.Object
            || !guild.TryGetProperty("id", out var idProperty)
            || idProperty.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idProperty.GetString()))
        {
            return null;
        }

        if (guild.TryGetProperty("botInstallKnown", out var installKnownProperty)
            && installKnownProperty.ValueKind == JsonValueKind.True
            && guild.TryGetProperty("botInstalled", out var installedProperty)
            && installedProperty.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        var id = idProperty.GetString()!.Trim();
        var displayName = guild.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : null;
        return string.IsNullOrWhiteSpace(displayName) ? id : $"{displayName!.Trim()} :: {id}";
    }
}
