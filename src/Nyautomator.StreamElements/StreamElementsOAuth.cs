using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.OAuth;

namespace Nyautomator.StreamElements;

public static class StreamElementsOAuth
{
    public const string TokenKey = "streamelements";
    public const string DefaultRedirectUri = "http://localhost:8084/streamelements/callback";

    private static readonly Uri AuthorizeEndpoint = new("https://api.streamelements.com/oauth2/authorize");
    private static readonly Uri TokenEndpoint = new("https://api.streamelements.com/oauth2/token");
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(5);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        try
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Nyautomator/1.0 (+https://github.com/BlackJaxDev/Nyautomator)");
        }
        catch
        {
            // Ignore user-agent parse issues.
        }

        return client;
    }

    public static IntegrationToken? GetToken() => IntegrationTokenStore.Get(TokenKey);

    public static void ClearToken() => IntegrationTokenStore.Clear(TokenKey);

    public static async Task<StreamElementsOAuthResult> AuthorizeAsync(StreamElementsOAuthOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var clientId = options.ClientId?.Trim();
        var clientSecret = options.ClientSecret?.Trim();
        var redirect = string.IsNullOrWhiteSpace(options.RedirectUri)
            ? DefaultRedirectUri
            : options.RedirectUri.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return StreamElementsOAuthResult.CreateFailure("Configure StreamElements client ID and client secret before starting OAuth.");

        if (!Uri.TryCreate(redirect, UriKind.Absolute, out var redirectUri))
            return StreamElementsOAuthResult.CreateFailure("StreamElements redirect URI must be an absolute URI.");

        var scopes = NormalizeScopes(options.Scopes);
        if (scopes.Length == 0)
            scopes = new[] { "realtime:socket" };

        var state = Guid.NewGuid().ToString("n");
        var authUrl = BuildAuthorizeUrl(clientId, redirectUri, scopes, state);
        LaunchBrowser(authUrl, options.AuthBrowserPath);

        var callback = await OAuthCallbackListener
            .WaitForCodeAsync(redirectUri, state, AuthorizationTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (callback.TimedOut)
            return StreamElementsOAuthResult.CreateFailure("StreamElements authorization timed out. Try again.");

        if (!string.IsNullOrEmpty(callback.Error))
        {
            var message = string.IsNullOrEmpty(callback.ErrorDescription)
                ? callback.Error
                : callback.ErrorDescription;
            return StreamElementsOAuthResult.CreateFailure($"StreamElements authorization failed: {message}.");
        }

        if (string.IsNullOrEmpty(callback.Code))
            return StreamElementsOAuthResult.CreateFailure("StreamElements did not return an authorization code.");

        var token = await ExchangeCodeAsync(
            clientId,
            clientSecret,
            redirectUri.ToString(),
            callback.Code,
            scopes,
            cancellationToken).ConfigureAwait(false);

        if (token is null)
            return StreamElementsOAuthResult.CreateFailure("Unable to exchange the authorization code for StreamElements tokens.");

        token.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        token.Metadata["clientId"] = clientId;
        token.Metadata["clientSecret"] = clientSecret;
        token.Metadata["redirectUri"] = redirectUri.ToString();
        token.Metadata["scopes"] = string.Join(' ', scopes);

        token.UpdatedAtUtc = DateTime.UtcNow;
        IntegrationTokenStore.Set(TokenKey, token);

        return StreamElementsOAuthResult.CreateSuccess(token.Clone());
    }

    public static async Task<string?> EnsureAccessTokenAsync(StreamElementsOAuthOptions options, CancellationToken cancellationToken = default)
    {
        var token = GetToken();
        if (token is null)
            return null;

        if (!string.IsNullOrWhiteSpace(token.AccessToken) && !IsExpiring(token, TimeSpan.FromMinutes(1)))
            return token.AccessToken;

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            return token.AccessToken;

        var refreshed = await RefreshTokenAsync(options, token, cancellationToken).ConfigureAwait(false);
        if (refreshed is null)
            return token.AccessToken;

        IntegrationTokenStore.Set(TokenKey, refreshed);
        return refreshed.AccessToken;
    }

    private static bool IsExpiring(IntegrationToken token, TimeSpan window)
        => token.ExpiresAtUtc.HasValue && token.ExpiresAtUtc.Value <= DateTime.UtcNow.Add(window);

    private static async Task<IntegrationToken?> RefreshTokenAsync(StreamElementsOAuthOptions options, IntegrationToken existing, CancellationToken cancellationToken)
    {
        var refreshToken = existing.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        var clientId = options.ClientId?.Trim();
        var clientSecret = options.ClientSecret?.Trim();

        if (string.IsNullOrWhiteSpace(clientId) && existing.Metadata is not null)
            existing.Metadata.TryGetValue("clientId", out clientId);
        if (string.IsNullOrWhiteSpace(clientSecret) && existing.Metadata is not null)
            existing.Metadata.TryGetValue("clientSecret", out clientSecret);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            Console.WriteLine("[StreamElements] Cannot refresh token; client ID/secret missing.");
            return null;
        }

        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(payload)
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Trace.WriteLine($"StreamElements refresh failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var tokenResponse = await JsonSerializer.DeserializeAsync<TokenResponse>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (tokenResponse?.AccessToken is null)
                return null;

            var expiresAt = tokenResponse.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(Math.Max(30, tokenResponse.ExpiresIn.Value))
                : (DateTime?)null;

            var metadata = existing.Metadata is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(existing.Metadata, StringComparer.OrdinalIgnoreCase);

            metadata["clientId"] = clientId;
            metadata["clientSecret"] = clientSecret;
            if (!string.IsNullOrWhiteSpace(existing.Scope))
                metadata["scopes"] = existing.Scope!;

            return new IntegrationToken
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? existing.RefreshToken,
                TokenType = string.IsNullOrWhiteSpace(tokenResponse.TokenType) ? existing.TokenType : tokenResponse.TokenType,
                Scope = string.IsNullOrWhiteSpace(tokenResponse.Scope) ? existing.Scope : tokenResponse.Scope,
                ExpiresAtUtc = expiresAt,
                Metadata = metadata,
                UpdatedAtUtc = DateTime.UtcNow,
                AccountId = existing.AccountId,
                AccountLogin = existing.AccountLogin,
                AccountDisplayName = existing.AccountDisplayName
            };
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to parse StreamElements refresh response: {ex.Message}");
            return null;
        }
    }

    private static async Task<IntegrationToken?> ExchangeCodeAsync(string clientId, string clientSecret, string redirectUri, string code, IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
            ["scope"] = scopes.Count == 0 ? string.Empty : string.Join(' ', scopes)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(payload)
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Trace.WriteLine($"StreamElements token exchange failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var token = await JsonSerializer.DeserializeAsync<TokenResponse>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (token?.AccessToken is null)
                return null;

            var expiresAt = token.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn.Value))
                : (DateTime?)null;

            return new IntegrationToken
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                TokenType = string.IsNullOrWhiteSpace(token.TokenType) ? "Bearer" : token.TokenType,
                Scope = token.Scope,
                ExpiresAtUtc = expiresAt,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to parse StreamElements token response: {ex.Message}");
            return null;
        }
    }

    private static string BuildAuthorizeUrl(string clientId, Uri redirectUri, IReadOnlyList<string> scopes, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["response_type"] = "code",
            ["state"] = state,
            ["scope"] = scopes.Count == 0 ? null : string.Join(' ', scopes)
        };

        var queryString = QueryStringBuilder.Build(query);
        return new UriBuilder(AuthorizeEndpoint)
        {
            Query = queryString
        }.Uri.ToString();
    }

    private static string[] NormalizeScopes(IEnumerable<string>? scopes)
    {
        if (scopes is null)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var scope in scopes)
        {
            var trimmed = scope?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (!list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                list.Add(trimmed);
        }

        return list.ToArray();
    }

    private static void LaunchBrowser(string url, string? browserPath)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            ProcessStartInfo psi;
            if (string.IsNullOrWhiteSpace(browserPath) || string.Equals(browserPath, "__system__", StringComparison.OrdinalIgnoreCase))
            {
                psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = browserPath,
                    Arguments = url,
                    UseShellExecute = false
                };
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to launch browser for StreamElements OAuth: {ex.Message}");
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope
    );
}

/// <summary>
/// OAuth client settings supplied by the StreamElements module configuration.
/// </summary>
public sealed record StreamElementsOAuthOptions(
    string? ClientId,
    string? ClientSecret,
    string? RedirectUri,
    IReadOnlyList<string>? Scopes,
    string? AuthBrowserPath = null);

public sealed class StreamElementsOAuthResult
{
    private StreamElementsOAuthResult(bool success, string? message, IntegrationToken? token)
    {
        Success = success;
        Message = message;
        Token = token;
    }

    public bool Success { get; }
    public string? Message { get; }
    public IntegrationToken? Token { get; }

    public static StreamElementsOAuthResult CreateSuccess(IntegrationToken token)
        => new StreamElementsOAuthResult(true, null, token);

    public static StreamElementsOAuthResult CreateFailure(string message)
        => new StreamElementsOAuthResult(false, string.IsNullOrWhiteSpace(message) ? "StreamElements authorization failed." : message.Trim(), null);
}
