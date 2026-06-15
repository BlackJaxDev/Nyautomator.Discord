using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Supplies Discord channel picker options for the currently configured guild.
/// Option format: channelId|displayName
/// </summary>
public sealed class DiscordChannelOptionsProvider : IStringOptionsProvider
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
                "/channels",
                null,
                null,
                "application/json",
                5000), CancellationToken.None).GetAwaiter().GetResult();
            if (!response.Success || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Body))
                return [];

            using var document = JsonDocument.Parse(response.Body);
            if (!document.RootElement.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
                return [];

            return [.. channels.EnumerateArray()
                .Select(ReadOptionValue)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Automation][Discord] Failed to enumerate channel options: {ex.Message}");
            return [];
        }
    }

    private static string? ReadOptionValue(JsonElement channel)
    {
        if (channel.ValueKind != JsonValueKind.Object)
            return null;

        if (channel.TryGetProperty("optionValue", out var optionValue)
            && optionValue.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(optionValue.GetString()))
        {
            return optionValue.GetString();
        }

        if (!channel.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idProp.GetString()))
        {
            return null;
        }

        var id = idProp.GetString()!.Trim();
        var displayName = channel.TryGetProperty("displayName", out var displayProp) && displayProp.ValueKind == JsonValueKind.String
            ? displayProp.GetString()
            : null;
        return string.IsNullOrWhiteSpace(displayName) ? id : $"{id}|{displayName!.Trim()}";
    }
}
