namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Delegates the StreamElements module wires up so automation nodes can authenticate against the
/// StreamElements REST API without reading host configuration.
/// </summary>
public static class StreamElementsAutomationHost
{
    /// <summary>
    /// Returns a usable StreamElements access token (refreshed OAuth token, or the legacy JWT) and
    /// whether it came from OAuth. Returns a null token when StreamElements is not configured.
    /// </summary>
    public static Func<CancellationToken, Task<(string? AccessToken, bool UsesOAuth)>>? GetAccessToken { get; set; }

    internal static async Task<(string AccessToken, bool UsesOAuth)> RequireAccessTokenAsync(string purpose, CancellationToken cancellationToken)
    {
        var provider = GetAccessToken
            ?? throw new StreamElementsApiException("StreamElements module is not loaded.");
        var (token, usesOAuth) = await provider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
            throw new StreamElementsApiException($"StreamElements OAuth or JWT authentication must be configured before {purpose}.");

        return (token.Trim(), usesOAuth);
    }
}
