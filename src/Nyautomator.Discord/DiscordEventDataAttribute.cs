using System;

namespace Nyautomator.Discord;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DiscordEventDataAttribute(string eventType, int version = 1, params string[] requiredScopesAny) : Attribute
{
    public string EventType { get; } = eventType ?? throw new ArgumentNullException(nameof(eventType));
    public int Version { get; } = version;
    public string[] RequiredScopesAny { get; } = requiredScopesAny ?? Array.Empty<string>();
    public string FullyQualifiedEventType => $"{EventType}_{Version}";
}
