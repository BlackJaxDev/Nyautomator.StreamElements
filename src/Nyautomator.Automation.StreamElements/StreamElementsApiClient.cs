using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Lightweight helper that wraps the StreamElements REST API using either OAuth access tokens or the legacy JWT as a fallback.
/// </summary>
public static class StreamElementsApiClient
{
    /// <summary>
    /// Drops the cached channel/auth context so the next call re-resolves credentials.
    /// </summary>
    public static void ResetContext() => _context = null;

    private static readonly Uri BaseUri = new("https://api.streamelements.com/kappa/v2/");
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly SemaphoreSlim ContextGate = new(1, 1);
    private static StreamElementsAuthContext? _context;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        try
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Nyautomator/1.0 (+https://github.com/BlackJaxDev/Nyautomator)");
        }
        catch
        {
            // Ignore user-agent formatting issues – the request will still succeed without it.
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// Fires a StreamElements alert immediately (or queues it) using the configured channel.
    /// </summary>
    public static async Task TriggerAlertAsync(object? body, CancellationToken cancellationToken)
    {
        var context = await EnsureContextAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        await SendAuthorizedAsync(HttpMethod.Post, $"alerts/{context.ChannelId}", body ?? new { }, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adjusts StreamElements loyalty points for a specific viewer.
    /// </summary>
    public static async Task AdjustLoyaltyAsync(string userIdentifier, string mode, int amount, string? reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userIdentifier))
            throw new StreamElementsApiException("StreamElements loyalty adjustments require a user identifier (login or ID).");

        if (string.IsNullOrWhiteSpace(mode))
            throw new StreamElementsApiException("StreamElements loyalty adjustment mode is required.");

        var normalizedAmount = Math.Abs(amount);
        if (normalizedAmount <= 0 && !string.Equals(mode, "set", StringComparison.OrdinalIgnoreCase))
            throw new StreamElementsApiException("StreamElements loyalty adjustments require a non-zero amount for add/remove operations.");

        var context = await EnsureContextAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var path = $"points/{context.ChannelId}/{Uri.EscapeDataString(userIdentifier.Trim())}";

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = mode.Trim().ToLowerInvariant()
        };

        if (string.Equals(payload["mode"] as string, "set", StringComparison.OrdinalIgnoreCase))
        {
            payload["points"] = amount;
        }
        else
        {
            payload["amount"] = normalizedAmount;
        }

        if (!string.IsNullOrWhiteSpace(reason))
            payload["reason"] = reason.Trim();

        await SendAuthorizedAsync(HttpMethod.Put, path, payload, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates StreamElements replacement variables (overlay data layer). By default the request merges values.
    /// </summary>
    public static async Task UpdateReplacementsAsync(IDictionary<string, object?> replacements, bool merge, CancellationToken cancellationToken)
    {
        if (replacements is null || replacements.Count == 0)
            throw new StreamElementsApiException("Specify at least one replacement variable when updating StreamElements overlay data.");

        var context = await EnsureContextAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var method = merge ? new HttpMethod("PATCH") : HttpMethod.Put;
        await SendAuthorizedAsync(method, $"replacements/{context.ChannelId}", replacements, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a StreamElements replacement variable from the channel scope.
    /// </summary>
    public static async Task DeleteReplacementAsync(string variable, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(variable))
            throw new StreamElementsApiException("Provide the replacement variable name to remove.");

        var context = await EnsureContextAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var path = $"replacements/{context.ChannelId}/{Uri.EscapeDataString(variable.Trim())}";
        await SendAuthorizedAsync(HttpMethod.Delete, path, null, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a chat announcement through the StreamElements bot (where supported).
    /// </summary>
    public static async Task SendBotAnnouncementAsync(string message, string? platform, bool? useTts, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new StreamElementsApiException("StreamElements bot announcements require a message.");

        var context = await EnsureContextAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["message"] = message.Trim()
        };

        if (!string.IsNullOrWhiteSpace(platform))
            payload["platform"] = platform.Trim();

        if (useTts.HasValue)
            payload["tts"] = useTts.Value;

        await SendAuthorizedAsync(HttpMethod.Post, $"bot/announce/{context.ChannelId}", payload, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StreamElementsAuthContext> EnsureContextAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _context is not null)
            return _context;

        await ContextGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _context is not null)
                return _context;

            var (accessToken, usesOAuth) = await StreamElementsAutomationHost
                .RequireAccessTokenAsync("using StreamElements reactions", cancellationToken)
                .ConfigureAwait(false);

            var channel = await FetchChannelAsync(accessToken!, cancellationToken).ConfigureAwait(false);
            var context = new StreamElementsAuthContext(accessToken!, usesOAuth, channel.Id, channel.Username, channel.DisplayName);
            _context = context;
            return context;
        }
        finally
        {
            ContextGate.Release();
        }
    }

    private static async Task<StreamElementsChannelInfo> FetchChannelAsync(string accessToken, CancellationToken cancellationToken)
    {
        var response = await SendCoreAsync(HttpMethod.Get, "channels/me", null, accessToken, cancellationToken).ConfigureAwait(false);
        try
        {
            return await HandleChannelResponseAsync(response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string relativePath, object? payload, string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (payload is not null)
            request.Content = CreateJsonContent(payload);

        return await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static HttpContent CreateJsonContent(object payload)
    {
        if (payload is HttpContent content)
            return content;

        if (payload is string raw)
        {
            raw = raw.Trim();
            if (raw.Length == 0)
                raw = "{}";
            return new StringContent(raw, Encoding.UTF8, "application/json");
        }

        if (payload is JsonDocument doc)
            return new StringContent(doc.RootElement.GetRawText(), Encoding.UTF8, "application/json");

        if (payload is JsonElement element)
            return new StringContent(element.GetRawText(), Encoding.UTF8, "application/json");

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task<JsonDocument?> SendAuthorizedAsync(HttpMethod method, string relativePath, object? payload, StreamElementsAuthContext context, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await SendCoreAsync(method, relativePath, payload, context.AccessToken, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && context.UsesOAuth)
            {
                response.Dispose();
                response = await SendCoreAsync(method, relativePath, payload, (await EnsureContextAsync(true, cancellationToken).ConfigureAwait(false)).AccessToken, cancellationToken).ConfigureAwait(false);
            }

            return await HandleResponseAsync(response, method, relativePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static async Task<JsonDocument?> HandleResponseAsync(HttpResponseMessage response, HttpMethod method, string relativePath, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            if (response.Content is null)
                return null;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            if (stream is null || stream.Length == 0)
                return null;

            try
            {
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        var payload = response.Content is null ? null : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = $"StreamElements API {method.Method} {relativePath} failed with {(int)response.StatusCode} {response.ReasonPhrase}";
        if (!string.IsNullOrWhiteSpace(payload))
            message += $". Response: {TrimForLog(payload)}";

        throw new StreamElementsApiException(message);
    }

    private static async Task<StreamElementsChannelInfo> HandleChannelResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var payload = response.Content is null ? null : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new StreamElementsApiException($"Failed to resolve StreamElements channel information ({(int)response.StatusCode}). {TrimForLog(payload)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        if (!root.TryGetProperty("_id", out var idElement) || string.IsNullOrWhiteSpace(idElement.GetString()))
            throw new StreamElementsApiException("StreamElements channel response did not include an _id field.");

        var id = idElement.GetString()!;
        var username = root.TryGetProperty("username", out var userElement) ? userElement.GetString() : id;
        var display = root.TryGetProperty("displayName", out var displayElement) ? displayElement.GetString() : username;

        return new StreamElementsChannelInfo(id, username ?? id, display ?? username ?? id);
    }

    private static string TrimForLog(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = text.Trim();
        const int max = 256;
        return text.Length <= max ? text : text[..max] + "…";
    }

    private sealed record StreamElementsAuthContext(string AccessToken, bool UsesOAuth, string ChannelId, string ChannelUsername, string? ChannelDisplayName);

    private sealed record StreamElementsChannelInfo(string Id, string Username, string? DisplayName);
}
