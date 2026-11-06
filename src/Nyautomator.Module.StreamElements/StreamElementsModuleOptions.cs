using System.Text.Json.Serialization;
using Nyautomator.StreamElements;

namespace Nyautomator.Modules.StreamElements;

public sealed class StreamElementsModuleOptions
{
    public string? ClientId { get; set; }

    /// <summary>
    /// StreamElements OAuth client secret. Stored through module secret storage.
    /// </summary>
    public string? ClientSecret { get; set; }

    public string? RedirectUri { get; set; }

    public List<string>? Scopes { get; set; }

    public string? AuthBrowserPath { get; set; }

    /// <summary>
    /// Legacy StreamElements JWT used when OAuth is not linked. Stored through module secret storage.
    /// </summary>
    public string? Jwt { get; set; }

    /// <summary>
    /// Connect to the StreamElements realtime socket while the runtime is running.
    /// </summary>
    public bool? RealtimeEnabled { get; set; }

    [JsonIgnore]
    public bool EffectiveRealtimeEnabled => RealtimeEnabled ?? true;

    [JsonIgnore]
    public string? EffectiveJwt => string.IsNullOrWhiteSpace(Jwt) ? null : Jwt.Trim();

    [JsonIgnore]
    public bool HasClientCredentials
        => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public static StreamElementsModuleOptions CreateDefault()
        => new()
        {
            RedirectUri = StreamElementsOAuth.DefaultRedirectUri,
            Scopes = ["realtime:socket"],
            RealtimeEnabled = true
        };

    public StreamElementsOAuthOptions ToOAuthOptions()
        => new(ClientId, ClientSecret, RedirectUri, Scopes ?? [], AuthBrowserPath);
}
