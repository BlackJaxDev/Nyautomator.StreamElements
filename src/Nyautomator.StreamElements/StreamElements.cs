using Newtonsoft.Json;
using SocketIOClient;
using SocketIOClient.Common;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading;

namespace Nyautomator.StreamElements
{
    public static class StreamElements
    {
        public delegate void StreamElementsEventHandler(StreamElementsEvent eventData, TimestampData time);
        public static event StreamElementsEventHandler? EventReceived;

        public sealed class StreamElementsConnectionOptions
        {
            public string? Jwt { get; init; }
            public Func<CancellationToken, Task<string?>>? OAuthAccessTokenProvider { get; init; }
            public bool AllowJwtFallback { get; init; } = true;
        }

        private const string Uri = "https://realtime.streamelements.com";

        private const string AuthenticatedEventName = "authenticated";
        private const string UnauthorizedEventName = "unauthorized";
        private const string EventTestEventName = "event:test";
        private const string EventEventName = "event";
        private const string EventUpdateEventName = "event:update";
        private const string EventResetEventName = "event:reset";

    private const string AuthMethodJwt = "jwt";
    private const string AuthMethodOAuth = "oauth2";

        private static SocketIOClient.SocketIO? _socket;
    private static Func<CancellationToken, Task<string?>>? _oauthAccessTokenProvider;
    private static string? _currentAuthToken;
    private static string _preferredAuthMethod = AuthMethodJwt;
    private static string _currentAuthMethod = AuthMethodJwt;
    private static bool _allowJwtFallback;
        private static string? _jwt;
        private static string? _channelId;
        private static volatile bool _stopping;
        private static volatile bool _authenticated;
        private static DateTimeOffset? _lastEventUtc;
        private static string? _lastError;

        private static volatile bool _connected;

        public static bool IsConnected => _connected;
        public static bool IsAuthenticated => _authenticated;
        public static string? ChannelId => _channelId;
        public static string CurrentAuthMethod => _currentAuthMethod;
        public static DateTimeOffset? LastEventUtc => _lastEventUtc;
        public static string? LastError => _lastError;

        /// <summary>
        /// Disconnects the realtime socket and stops automatic reconnection.
        /// </summary>
        public static async Task StopWebsocketServer()
        {
            _stopping = true;
            var socket = _socket;
            _socket = null;
            _connected = false;
            _authenticated = false;
            _channelId = null;
            if (socket is null)
                return;

            socket.OnConnected -= SocketConnected;
            socket.OnDisconnected -= SocketDisconnected;
            try
            {
                await socket.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ SE ] Failed to disconnect websocket: {ex.Message}");
            }
            finally
            {
                (socket as IDisposable)?.Dispose();
            }
        }

        public static Task StartWebsocketServer(string jwt)
            => StartWebsocketServer(new StreamElementsConnectionOptions
            {
                Jwt = jwt,
                AllowJwtFallback = false
            });

        public static async Task StartWebsocketServer(StreamElementsConnectionOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            _jwt = string.IsNullOrWhiteSpace(options.Jwt) ? null : options.Jwt;
            _oauthAccessTokenProvider = options.OAuthAccessTokenProvider;
            _allowJwtFallback = options.AllowJwtFallback && !string.IsNullOrWhiteSpace(_jwt);
            _preferredAuthMethod = _oauthAccessTokenProvider is not null ? AuthMethodOAuth : AuthMethodJwt;
            _currentAuthMethod = _preferredAuthMethod;
            _currentAuthToken = null;

            if (_preferredAuthMethod == AuthMethodOAuth && _oauthAccessTokenProvider is null)
                throw new InvalidOperationException("StreamElements OAuth mode requires an access token provider.");

            if (_preferredAuthMethod == AuthMethodJwt && string.IsNullOrWhiteSpace(_jwt))
                throw new InvalidOperationException("StreamElements requires a JWT token when OAuth is not configured.");

            await StopWebsocketServer().ConfigureAwait(false);
            _stopping = false;
            _lastError = null;
            _socket = new(new Uri(Uri), new SocketIOOptions { Transport = TransportProtocol.WebSocket });

            _socket.OnConnected += SocketConnected;
            _socket.OnDisconnected += SocketDisconnected;

            _socket.On(AuthenticatedEventName, ctx => { SocketAuthenticated(ctx); return Task.CompletedTask; });
            _socket.On(UnauthorizedEventName, ctx => { SocketUnauthorized(ctx); return Task.CompletedTask; });
            _socket.On(EventTestEventName, ctx => { TestEvent(ctx); return Task.CompletedTask; });
            _socket.On(EventEventName, ctx => { Event(ctx); return Task.CompletedTask; });
            _socket.On(EventUpdateEventName, ctx => { EventUpdate(ctx); return Task.CompletedTask; });
            _socket.On(EventResetEventName, ctx => { EventReset(ctx); return Task.CompletedTask; });

            Trace.WriteLine("[ SE ] Connecting to the StreamElements websocket...");

            await _socket.ConnectAsync();
        }

        private static async Task<(string Method, string? Token)> AcquireAuthPayloadAsync(CancellationToken cancellationToken)
        {
            if (_preferredAuthMethod == AuthMethodOAuth && _oauthAccessTokenProvider is not null)
            {
                try
                {
                    var token = await _oauthAccessTokenProvider(cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        _currentAuthMethod = AuthMethodOAuth;
                        _currentAuthToken = token;
                        return (_currentAuthMethod, token);
                    }

                    Trace.WriteLine("[ SE ] OAuth token provider returned an empty token.");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[ SE ] OAuth token provider failed: {ex.Message}");
                }

                if (_allowJwtFallback && !string.IsNullOrWhiteSpace(_jwt))
                {
                    Trace.WriteLine("[ SE ] Falling back to StreamElements JWT authentication.");
                    _currentAuthMethod = AuthMethodJwt;
                    return (_currentAuthMethod, _jwt);
                }

                return (AuthMethodOAuth, null);
            }

            if (!string.IsNullOrWhiteSpace(_jwt))
            {
                _currentAuthMethod = AuthMethodJwt;
                return (_currentAuthMethod, _jwt);
            }

            if (!string.IsNullOrWhiteSpace(_currentAuthToken))
                return (_currentAuthMethod, _currentAuthToken);

            return (_preferredAuthMethod, null);
        }

        #region Connection
        private static async void SocketConnected(object? sender, EventArgs e)
        {
            if (_socket is null)
            {
                Trace.WriteLine("[ SE ] Socket is null, unable to connect");
                return;
            }

            _connected = true;
            Trace.WriteLine("[ SE ] Successfully connected to the websocket");
            var auth = await AcquireAuthPayloadAsync(CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(auth.Token))
            {
                Trace.WriteLine("[ SE ] No authentication token available; connection will remain unauthenticated.");
                return;
            }

            try
            {
                await _socket.EmitAsync("authenticate", [new { method = auth.Method, token = auth.Token }]);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ SE ] Failed to emit authenticate payload: {ex.Message}");
            }
        }
        private static async void SocketDisconnected(object? sender, string e)
        {
            Trace.WriteLine($"[ SE ] Disconnected from the websocket: {e}");

            //TODO: verify if this works or if we need a reconnection loop for testing the connection

            if (_socket is null)
            {
                Trace.WriteLine("[ SE ] Socket is null, unable to reconnect");
                return;
            }

            _connected = false;
            _authenticated = false;
            if (_stopping)
                return;

            // Attempt to reconnect
            try
            {
                await _socket.ConnectAsync();
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                Trace.WriteLine($"[ SE ] Reconnect failed: {ex.Message}");
            }
        }
        #endregion

        #region Authentication
        private static void SocketAuthenticated(IEventContext response)
        {
            var payload = TryGetValue<AuthenticatedPayload>(response, 0);
            if (payload is null)
            {
                Console.WriteLine("[ SE ] Failed to parse authenticated payload");
                return;
            }

            _channelId = payload.ChannelId;
            _authenticated = true;
            _lastError = null;
            Console.WriteLine($"[ SE ] Authenticated: {payload.ClientId} - {_channelId} - {payload.Project?? "Unknown"} - {payload.Message ?? "No message"}");
        }
        public class AuthenticatedPayload
        {
            [JsonPropertyName("clientId")]
            [JsonProperty("clientId")]
            public string? ClientId { get; set; }

            [JsonPropertyName("channelId")]
            [JsonProperty("channelId")]
            public string? ChannelId { get; set; }

            [JsonPropertyName("project")]
            [JsonProperty("project")]
            public string? Project { get; set; }

            [JsonPropertyName("message")]
            [JsonProperty("message")]
            public string? Message { get; set; }
        }
        public class TimestampData
        {
            [JsonPropertyName("ts")]
            [JsonProperty("ts")]
            public long Timestamp { get; set; }
            
            [JsonPropertyName("nonce")]
            [JsonProperty("nonce")]
            public string? Nonce { get; set; }
        }
        private static void SocketUnauthorized(IEventContext response)
        {
            _lastError = TryGetValue<string>(response, 0) ?? response.RawText;
            Console.WriteLine($"[ SE ] Unauthorized: {_lastError}");
            _channelId = null;
            _authenticated = false;
        }
        #endregion

        #region Events
        private static void EventReset(IEventContext response)
        {
            Trace.WriteLine("[ SE ] event:reset");
            HandleEvent(response);
        }
        private static void EventUpdate(IEventContext response)
        {
            Trace.WriteLine("[ SE ] event:update");
            HandleEvent(response);
        }
        private static void Event(IEventContext response)
        {
            Trace.WriteLine("[ SE ] event");
            HandleEvent(response);
        }
        private static void TestEvent(IEventContext response)
        {
            Trace.WriteLine("[ SE ] event:test");
            HandleEvent(response);
        }
        private static void HandleEvent(IEventContext response)
        {
            var evObj = TryGetValue<StreamElementsEvent>(response, 0);
            var tsObj = TryGetValue<TimestampData>(response, 1);
            if (evObj is null || tsObj is null)
            {
                Trace.WriteLine("[ SE ] Failed to parse event payload");
                return;
            }

            Trace.WriteLine($"[ SE ] Received event: {evObj.Name} at {tsObj.Timestamp} (Nonce: {tsObj.Nonce})");
            _lastEventUtc = DateTimeOffset.UtcNow;

            EventReceived?.Invoke(evObj, tsObj);
        }

        private static T? TryGetValue<T>(IEventContext response, int index)
        {
            try
            {
                return response.GetValue<T>(index);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ SE ] Failed to read websocket payload value {index}: {ex.Message}");
                return default;
            }
        }
        #endregion
    }
}
