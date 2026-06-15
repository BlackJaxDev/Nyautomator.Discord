using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyautomator.Discord;

public abstract class DiscordEventDataBase
{
    private static readonly Lazy<ReadOnlyDictionary<string, Type>> _eventTypes = new(DiscoverEventTypes);
    public static ReadOnlyDictionary<string, Type> EventTypes => _eventTypes.Value;

    public DiscordEventDataAttribute? Attribute => GetType().GetCustomAttribute<DiscordEventDataAttribute>();
    public string EventType => Attribute?.EventType ?? string.Empty;
    public int Version => Attribute?.Version ?? 1;
    public string[] RequiredScopesAny => Attribute?.RequiredScopesAny ?? Array.Empty<string>();
    public string FullyQualifiedEventType => $"{EventType}_{Version}";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    private static ReadOnlyDictionary<string, Type> DiscoverEventTypes()
    {
        var result = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        var assemblies = new[] { typeof(DiscordEventDataBase).Assembly };

        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (!type.IsClass || type.IsAbstract) continue;
                    if (!typeof(DiscordEventDataBase).IsAssignableFrom(type)) continue;
                    var attr = type.GetCustomAttribute<DiscordEventDataAttribute>();
                    if (attr is null) continue;
                    var key = attr.FullyQualifiedEventType;
                    if (!result.ContainsKey(key)) result[key] = type;
                }
            }
            catch { }
        }
        return new ReadOnlyDictionary<string, Type>(result);
    }

    public static DiscordEventDataBase? Deserialize(string eventType, int version, JsonElement eventData)
    {
        var key = $"{eventType}_{version}";
        if (!EventTypes.TryGetValue(key, out var type)) return null;
        try { return eventData.Deserialize(type) as DiscordEventDataBase; } catch { return null; }
    }

    public static IEnumerable<(string EventType, int Version, Type PayloadType, string[] RequiredScopes)> GetAllEventTypes()
    {
        foreach (var kvp in EventTypes)
        {
            var attr = kvp.Value.GetCustomAttribute<DiscordEventDataAttribute>();
            if (attr is not null)
                yield return (attr.EventType, attr.Version, kvp.Value, attr.RequiredScopesAny);
        }
    }
}
