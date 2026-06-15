using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nyautomator.Module.Abstractions;
using Nyautomator.Runtime.Abstractions;

namespace Nyautomator.Modules.Discord;

public sealed class DiscordModule : INyautomatorModule
{
    private static readonly object FallbackGate = new();
    private static DiscordModuleBridge? _fallbackBridge;

    public NyautomatorModuleDescriptor Descriptor { get; } = new(
        "discord",
        "Discord",
        "Discord OAuth, server directory, bot messages, reactions, and automation nodes.",
        "0.1.0");

    public void ConfigureServices(NyautomatorModuleServiceContext context, IServiceCollection services)
    {
        services.AddSingleton<DiscordModuleBridge>();
    }

    public void ConfigureRuntime(NyautomatorModuleRuntimeContext context)
    {
        var defaults = JsonSerializer.SerializeToElement(
            DiscordModuleOptions.CreateDefault(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var configuration = new ModuleConfigurationContribution(
            "options",
            Defaults: defaults,
            ModuleId: context.ModuleId);

        context.Configurations.Register(configuration);
        context.Contributions.Register(new ModuleContributionSet(context.ModuleId, Configurations: [configuration]));

        var bridge = context.Services.GetService<DiscordModuleBridge>() ?? GetFallbackBridge(context.Services);
        bridge.MigrateLegacyOptions();

        context.ApiHandlers.Register(bridge);
        context.AuthenticatedIntegrations.Register(bridge);
        context.AuthenticatedIntegrations.Register(new AuthenticatedIntegrationAliasAdapter("discord", bridge));
    }

    private static DiscordModuleBridge GetFallbackBridge(IServiceProvider services)
    {
        lock (FallbackGate)
        {
            _fallbackBridge ??= new DiscordModuleBridge(
                services.GetRequiredService<IModuleOptionsProvider>(),
                services.GetRequiredService<IIntegrationTokenStore>(),
                services.GetService<IConfigurationService>());
            return _fallbackBridge;
        }
    }

    private sealed class AuthenticatedIntegrationAliasAdapter(string id, IAuthenticatedIntegrationAdapter inner)
        : IAuthenticatedIntegrationAdapter
    {
        public string Id { get; } = id;

        public Task<AuthenticatedIntegrationResponse> SendAsync(
            AuthenticatedIntegrationRequest request,
            CancellationToken cancellationToken)
            => inner.SendAsync(request, cancellationToken);
    }
}
