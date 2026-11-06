using Microsoft.Extensions.DependencyInjection;
using Nyautomator.Module.Abstractions;
using Nyautomator.Runtime.Abstractions;
using NyautomatorUI.Server.Automation;

namespace Nyautomator.Modules.StreamElements;

public sealed class StreamElementsModule : INyautomatorModule
{
    private static readonly object Gate = new();
    private static Services? _services;

    public NyautomatorModuleDescriptor Descriptor { get; } = new(
        StreamElementsModuleApiHandler.ModuleIdValue,
        "StreamElements",
        "StreamElements OAuth/JWT auth, realtime events, loyalty, alerts, and automation nodes.",
        "0.1.0");

    public void ConfigureRuntime(NyautomatorModuleRuntimeContext context)
    {
        var services = GetOrCreateServices(context.Services);
        StreamElementsAutomationHost.GetAccessToken = services.Credentials.GetAccessTokenAsync;
        StreamElementsAutomationIntegration.Register();
        context.ApiHandlers.Register(new StreamElementsModuleApiHandler(services.Credentials, services.Realtime));
    }

    internal static Services GetOrCreateServices(IServiceProvider provider)
    {
        lock (Gate)
        {
            if (_services is not null)
                return _services;

            var options = provider.GetService<IModuleOptionsProvider>();
            var credentials = new StreamElementsCredentials(() =>
                options?.Get(StreamElementsModuleApiHandler.ModuleIdValue, StreamElementsModuleOptions.CreateDefault())
                ?? StreamElementsModuleOptions.CreateDefault());
            _services = new Services(credentials, new StreamElementsRealtimeService(credentials));
            return _services;
        }
    }

    internal sealed record Services(StreamElementsCredentials Credentials, StreamElementsRealtimeService Realtime);
}
