using Nyautomator;
using Nyautomator.Runtime.Abstractions;
using NyautomatorUI.Server.Automation;

namespace Nyautomator.Modules.StreamElements;

/// <summary>
/// Connects the StreamElements realtime socket while the runtime is running, when enabled and credentials exist.
/// </summary>
public sealed class StreamElementsRuntimeParticipant : IRuntimeParticipant
{
    private readonly StreamElementsModule.Services _services;
    private readonly IModuleOptionsProvider? _options;
    private bool _started;

    public StreamElementsRuntimeParticipant(IServiceProvider services)
    {
        _services = StreamElementsModule.GetOrCreateServices(services);
        _options = services.GetService(typeof(IModuleOptionsProvider)) as IModuleOptionsProvider;
        if (_options is not null)
            _options.ModuleOptionsChanged += OnModuleOptionsChanged;
    }

    public string Name => "StreamElements";

    public Task ConfigureAsync(AppConfiguration config, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _started = true;
        await ApplyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _started = false;
        await _services.Realtime.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_options is not null)
            _options.ModuleOptionsChanged -= OnModuleOptionsChanged;

        await _services.Realtime.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        StreamElementsApiClient.ResetContext();
        var options = _services.Credentials.Options;
        if (_started && options.EffectiveRealtimeEnabled && _services.Credentials.HasAnyCredential)
            await _services.Realtime.StartAsync(cancellationToken).ConfigureAwait(false);
        else
            await _services.Realtime.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnModuleOptionsChanged(ModuleOptionsChangedEventArgs args)
    {
        if (!string.Equals(args.ModuleId, StreamElementsModuleApiHandler.ModuleIdValue, StringComparison.OrdinalIgnoreCase))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await ApplyAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StreamElements] Failed to apply module options: {ex.Message}");
            }
        });
    }
}
