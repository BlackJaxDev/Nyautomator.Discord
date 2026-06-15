using System.Reflection;
using Nyautomator.Discord;

namespace NyautomatorUI.Server.Automation;

public abstract class DiscordEventAction : ActionType, IRequiredScopesProvider
{
    private IReadOnlyList<string>? _requiredScopesCache;

    public abstract string EventType { get; }
    public virtual int Version => 1;
    protected abstract string FriendlyName { get; }

    public override string Id => $"Discord.Event.{EventType}.{Version}";
    public override string DisplayName => $"Discord: {FriendlyName}";

    public virtual Type? GetEventType()
    {
        Console.WriteLine($"DiscordEventAction {Id} does not implement GetEventType()");
        return null;
    }

    public override Type? GetOutputType(string? outputHandle) => GetEventType();

    public virtual IReadOnlyList<string> GetRequiredScopes()
    {
        if (_requiredScopesCache is not null)
            return _requiredScopesCache;

        var eventType = GetEventType();
        if (eventType is null)
            return _requiredScopesCache = Array.Empty<string>();

        var attr = eventType.GetCustomAttribute<DiscordEventDataAttribute>(inherit: true);
        _requiredScopesCache = attr?.RequiredScopesAny is { Length: > 0 }
            ? attr.RequiredScopesAny
            : Array.Empty<string>();
        return _requiredScopesCache;
    }

    public string FullyQualifiedEventType => $"{EventType}_{Version}";
}
