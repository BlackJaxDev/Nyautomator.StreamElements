using Nyautomator;
using Nyautomator.StreamElements;

namespace Nyautomator.Modules.StreamElements;

/// <summary>
/// Resolves the StreamElements credential to use: a refreshed OAuth access token when an account is
/// linked, otherwise the legacy JWT from module configuration.
/// </summary>
public sealed class StreamElementsCredentials(
    Func<StreamElementsModuleOptions> options,
    Func<IntegrationToken?>? getToken = null,
    Func<StreamElementsOAuthOptions, CancellationToken, Task<string?>>? ensureAccessToken = null)
{
    private readonly Func<StreamElementsModuleOptions> _options = options;
    private readonly Func<IntegrationToken?> _getToken = getToken ?? StreamElementsOAuth.GetToken;
    private readonly Func<StreamElementsOAuthOptions, CancellationToken, Task<string?>> _ensureAccessToken
        = ensureAccessToken ?? StreamElementsOAuth.EnsureAccessTokenAsync;

    public StreamElementsModuleOptions Options => _options();

    public bool HasOAuthToken => !string.IsNullOrWhiteSpace(_getToken()?.AccessToken);

    public bool HasAnyCredential => HasOAuthToken || Options.EffectiveJwt is not null;

    public async Task<(string? AccessToken, bool UsesOAuth)> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var options = Options;
        if (_getToken() is not null)
        {
            try
            {
                var token = await _ensureAccessToken(options.ToOAuthOptions(), cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(token))
                    return (token, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StreamElements] Failed to refresh OAuth token: {ex.Message}");
            }
        }

        return (options.EffectiveJwt, false);
    }

    public Task<string?> GetOAuthAccessTokenAsync(CancellationToken cancellationToken)
        => _ensureAccessToken(Options.ToOAuthOptions(), cancellationToken);
}
