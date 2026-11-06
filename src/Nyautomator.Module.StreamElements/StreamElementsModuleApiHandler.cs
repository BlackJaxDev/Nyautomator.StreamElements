using Nyautomator;
using Nyautomator.Module.Abstractions;
using Nyautomator.StreamElements;
using NyautomatorUI.Server.Automation;
using static Nyautomator.Modules.StreamElements.ModuleHttp;

namespace Nyautomator.Modules.StreamElements;

/// <summary>
/// Serves <c>/modules/streamelements/api/*</c>: OAuth status/authorize/disconnect and realtime socket control.
/// </summary>
public sealed class StreamElementsModuleApiHandler : IModuleApiHandler
{
    public const string ModuleIdValue = "streamelements";

    private readonly StreamElementsCredentials _credentials;
    private readonly StreamElementsRealtimeService _realtime;
    private readonly Func<StreamElementsOAuthOptions, CancellationToken, Task<StreamElementsOAuthResult>> _authorize;
    private readonly Func<IntegrationToken?> _getToken;
    private readonly Action _clearToken;

    public StreamElementsModuleApiHandler(StreamElementsCredentials credentials, StreamElementsRealtimeService realtime)
        : this(credentials, realtime, StreamElementsOAuth.AuthorizeAsync, StreamElementsOAuth.GetToken, StreamElementsOAuth.ClearToken)
    {
    }

    internal StreamElementsModuleApiHandler(
        StreamElementsCredentials credentials,
        StreamElementsRealtimeService realtime,
        Func<StreamElementsOAuthOptions, CancellationToken, Task<StreamElementsOAuthResult>> authorize,
        Func<IntegrationToken?> getToken,
        Action clearToken)
    {
        _credentials = credentials;
        _realtime = realtime;
        _authorize = authorize;
        _getToken = getToken;
        _clearToken = clearToken;
    }

    public string ModuleId => ModuleIdValue;

    public async Task<ModuleApiResponse> HandleAsync(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        var path = NormalizePath(request.Path);
        try
        {
            return path switch
            {
                "status" when IsMethod(request, "GET") => Ok(BuildStatus()),
                "authorize" when IsMethod(request, "POST") => await AuthorizeAsync(cancellationToken).ConfigureAwait(false),
                "token" when IsMethod(request, "DELETE") => await DisconnectAsync(cancellationToken).ConfigureAwait(false),
                "disconnect" when IsMethod(request, "POST") => await DisconnectAsync(cancellationToken).ConfigureAwait(false),
                "connect" or "realtime/connect" when IsMethod(request, "POST") => await ConnectRealtimeAsync(cancellationToken).ConfigureAwait(false),
                "realtime/disconnect" when IsMethod(request, "POST") => await DisconnectRealtimeAsync(cancellationToken).ConfigureAwait(false),
                _ => NotFound("StreamElements", path)
            };
        }
        catch (OperationCanceledException)
        {
            return Error("StreamElements request cancelled or timed out.", 504);
        }
        catch (Exception ex)
        {
            return Error($"StreamElements request failed: {ex.Message}", 500);
        }
    }

    internal object BuildStatus()
    {
        var token = _getToken();
        var options = _credentials.Options;
        var connected = !string.IsNullOrWhiteSpace(token?.AccessToken);
        var scope = ResolveScope(token);

        return new
        {
            success = true,
            connected,
            configured = options.HasClientCredentials || options.EffectiveJwt is not null,
            hasLegacyJwt = options.EffectiveJwt is not null,
            displayName = token?.AccountDisplayName,
            accountLogin = token?.AccountLogin,
            account = new
            {
                id = token?.AccountId,
                login = token?.AccountLogin,
                displayName = token?.AccountDisplayName
            },
            tokenType = token?.TokenType,
            scopes = ParseScopes(scope),
            scope = token?.Scope,
            expiresAtUtc = token?.ExpiresAtUtc,
            updatedAtUtc = token?.UpdatedAtUtc,
            hasRefreshToken = !string.IsNullOrWhiteSpace(token?.RefreshToken),
            realtime = _realtime.GetStatus(),
            message = connected
                ? null
                : options.EffectiveJwt is not null
                    ? "Using the legacy StreamElements JWT."
                    : options.HasClientCredentials
                        ? "StreamElements credentials saved; account not linked."
                        : "Add StreamElements OAuth credentials or a JWT in Settings."
        };
    }

    private async Task<ModuleApiResponse> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var options = _credentials.Options;
        if (!options.HasClientCredentials)
            return Ok(new { success = false, message = "Configure StreamElements client ID and client secret before starting OAuth." });

        var result = await _authorize(options.ToOAuthOptions(), cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            StreamElementsApiClient.ResetContext();
            if (options.EffectiveRealtimeEnabled && _realtime.Running)
                await _realtime.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        return Ok(new
        {
            success = result.Success,
            message = result.Message,
            token = result.Token is null
                ? null
                : new
                {
                    result.Token.TokenType,
                    result.Token.Scope,
                    scopes = ParseScopes(result.Token.Scope),
                    result.Token.AccountId,
                    result.Token.AccountLogin,
                    result.Token.AccountDisplayName,
                    result.Token.ExpiresAtUtc,
                    result.Token.UpdatedAtUtc
                }
        });
    }

    private async Task<ModuleApiResponse> DisconnectAsync(CancellationToken cancellationToken)
    {
        _clearToken();
        StreamElementsApiClient.ResetContext();
        if (_credentials.Options.EffectiveJwt is null)
            await _realtime.StopAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new { success = true, message = "StreamElements tokens cleared." });
    }

    private async Task<ModuleApiResponse> ConnectRealtimeAsync(CancellationToken cancellationToken)
    {
        var started = await _realtime.StartAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new
        {
            success = started,
            message = started ? "StreamElements realtime socket connecting." : _realtime.LastError,
            realtime = _realtime.GetStatus()
        });
    }

    private async Task<ModuleApiResponse> DisconnectRealtimeAsync(CancellationToken cancellationToken)
    {
        await _realtime.StopAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new { success = true, message = "StreamElements realtime socket disconnected.", realtime = _realtime.GetStatus() });
    }

    private static string? ResolveScope(IntegrationToken? token)
    {
        if (token is null)
            return null;

        if (!string.IsNullOrWhiteSpace(token.Scope))
            return token.Scope;

        return token.Metadata is not null && token.Metadata.TryGetValue("scopes", out var stored) && !string.IsNullOrWhiteSpace(stored)
            ? stored
            : null;
    }

    internal static string[] ParseScopes(string? scope)
        => string.IsNullOrWhiteSpace(scope)
            ? []
            : scope
                .Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => value.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
}
