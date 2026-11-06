using StreamElementsRealtime = Nyautomator.StreamElements.StreamElements;

namespace Nyautomator.Modules.StreamElements;

/// <summary>
/// Owns the StreamElements realtime socket lifecycle for the module.
/// </summary>
public sealed class StreamElementsRealtimeService(StreamElementsCredentials credentials)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StreamElementsCredentials _credentials = credentials;

    public bool Running { get; private set; }
    public string? LastError { get; private set; }

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var options = _credentials.Options;
            var useOAuth = _credentials.HasOAuthToken;
            var jwt = options.EffectiveJwt;
            if (!useOAuth && jwt is null)
            {
                LastError = "Link a StreamElements account or provide a JWT before connecting the realtime socket.";
                await StopCoreAsync().ConfigureAwait(false);
                return false;
            }

            await StreamElementsRealtime.StartWebsocketServer(new StreamElementsRealtime.StreamElementsConnectionOptions
            {
                Jwt = jwt,
                OAuthAccessTokenProvider = useOAuth ? _credentials.GetOAuthAccessTokenAsync : null,
                AllowJwtFallback = jwt is not null
            }).ConfigureAwait(false);

            Running = true;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Console.WriteLine($"[StreamElements] Realtime socket failed to start: {ex.Message}");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public object GetStatus()
        => new
        {
            running = Running,
            connected = StreamElementsRealtime.IsConnected,
            authenticated = StreamElementsRealtime.IsAuthenticated,
            channelId = StreamElementsRealtime.ChannelId,
            authMethod = StreamElementsRealtime.CurrentAuthMethod,
            lastEventUtc = StreamElementsRealtime.LastEventUtc,
            lastError = LastError ?? StreamElementsRealtime.LastError
        };

    private async Task StopCoreAsync()
    {
        Running = false;
        await StreamElementsRealtime.StopWebsocketServer().ConfigureAwait(false);
    }
}
