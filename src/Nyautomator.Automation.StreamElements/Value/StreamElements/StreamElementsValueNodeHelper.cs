using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NyautomatorUI.Server.Automation;

internal static class StreamElementsValueNodeHelper
{
    private static readonly Uri BaseUri = new("https://api.streamelements.com/kappa/v2/");
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly object ContextSync = new();
    private static StreamElementsContext? _context;
    private static readonly ConcurrentDictionary<string, PointsCacheEntry> PointsCache = new(StringComparer.OrdinalIgnoreCase);

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
            // Ignore user-agent issues.
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static PointsSnapshot GetPointsSnapshot(string identifier, double cacheMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return PointsSnapshot.Failure(string.Empty, "Viewer identifier is required.");

        var cacheKey = NormalizeCacheKey(identifier);
        var cacheWindow = TimeSpan.FromMilliseconds(Math.Max(0d, cacheMilliseconds));
        var now = DateTimeOffset.UtcNow;

        if (cacheWindow > TimeSpan.Zero && PointsCache.TryGetValue(cacheKey, out var cached))
        {
            if (now - cached.Snapshot.FetchedAt <= cacheWindow)
                return cached.Snapshot;
        }

        var snapshot = FetchPoints(identifier);
        PointsCache[cacheKey] = new PointsCacheEntry(snapshot);
        return snapshot;
    }

    private static PointsSnapshot FetchPoints(string identifier)
    {
        if (!TryEnsureContext(false, out var context, out var contextError) || context is null)
            return PointsSnapshot.Failure(identifier, contextError ?? "StreamElements authentication unavailable.");

        var ctx = context.Value;
        var path = $"points/{ctx.ChannelId}/{Uri.EscapeDataString(identifier)}";
        var snapshot = RequestPoints(identifier, ctx, path, allowRetry: true);
        return snapshot;
    }

    private static PointsSnapshot RequestPoints(string identifier, StreamElementsContext context, string path, bool allowRetry)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = SendAuthorized(HttpMethod.Get, path, context.AccessToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && allowRetry && context.UsesOAuth)
            {
                response.Dispose();
                InvalidateContext();
                if (!TryEnsureContext(true, out var refreshed, out var refreshError) || refreshed is null)
                    return PointsSnapshot.Failure(identifier, refreshError ?? "StreamElements OAuth token refresh failed.");

                return RequestPoints(identifier, refreshed.Value, path, allowRetry: false);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return PointsSnapshot.Missing(identifier, DateTimeOffset.UtcNow);

            if (!response.IsSuccessStatusCode)
            {
                var payload = response.Content is null ? null : SafeReadContent(response.Content);
                return PointsSnapshot.Failure(identifier, $"StreamElements points request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(payload)}");
            }

            if (response.Content is null)
                return PointsSnapshot.Failure(identifier, "StreamElements points response did not include content.");

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var fetchedAt = DateTimeOffset.UtcNow;

            var points = TryGetLong(root, "points");
            var total = TryGetLong(root, "totalPoints")
                ?? TryGetLong(root, "lifetime")
                ?? TryGetLong(root, "lifetimePoints");
            var rank = TryGetLong(root, "rank");
            var username = TryGetString(root, "username");
            var display = TryGetString(root, "displayName") ?? username;
            var raw = root.GetRawText();

            var found = points.HasValue || total.HasValue || rank.HasValue || !string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(display);
            if (!found)
                return PointsSnapshot.Missing(identifier, fetchedAt, raw);

            return PointsSnapshot.CreateSuccess(identifier, fetchedAt, points, total, rank, username, display, raw);
        }
        catch (StreamElementsApiException ex)
        {
            return PointsSnapshot.Failure(identifier, ex.Message);
        }
        catch (Exception ex)
        {
            return PointsSnapshot.Failure(identifier, $"StreamElements points request error: {ex.Message}");
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static HttpResponseMessage SendAuthorized(HttpMethod method, string relativePath, string accessToken)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
    }

    private static bool TryEnsureContext(bool forceRefresh, out StreamElementsContext? context, out string? error)
    {
        lock (ContextSync)
        {
            error = null;
            if (!forceRefresh && _context.HasValue && !IsContextExpired(_context.Value))
            {
                context = _context.Value;
                return true;
            }

            try
            {
                var created = CreateContext();
                _context = created;
                PointsCache.Clear();
                context = created;
                return true;
            }
            catch (StreamElementsApiException ex)
            {
                _context = null;
                error = ex.Message;
            }
            catch (Exception ex)
            {
                _context = null;
                error = ex.Message;
            }
        }

        context = null;
        return false;
    }

    private static void InvalidateContext()
    {
        lock (ContextSync)
        {
            _context = null;
            PointsCache.Clear();
        }
    }

    private static bool IsContextExpired(StreamElementsContext context)
    {
        var now = DateTimeOffset.UtcNow;
        return now - context.RetrievedAt >= TimeSpan.FromMinutes(5);
    }

    private static StreamElementsContext CreateContext()
    {
        var (accessToken, usesOAuth) = StreamElementsAutomationHost
            .RequireAccessTokenAsync("retrieving loyalty points", CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var channel = FetchChannel(accessToken!);
        return new StreamElementsContext(accessToken!, usesOAuth, channel.Id, channel.Username, channel.DisplayName, DateTimeOffset.UtcNow);
    }

    private static StreamElementsChannel FetchChannel(string accessToken)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = SendAuthorized(HttpMethod.Get, "channels/me", accessToken);
            if (!response.IsSuccessStatusCode)
            {
                var payload = response.Content is null ? null : SafeReadContent(response.Content);
                throw new StreamElementsApiException($"Failed to resolve StreamElements channel: {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(payload)}");
            }

            if (response.Content is null)
                throw new StreamElementsApiException("StreamElements channel response did not include content.");

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            var id = TryGetString(root, "_id");
            if (string.IsNullOrWhiteSpace(id))
                throw new StreamElementsApiException("StreamElements channel response did not include an _id field.");

            var username = TryGetString(root, "username") ?? id;
            var display = TryGetString(root, "displayName") ?? username;

            return new StreamElementsChannel(id!, username, display);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static string SafeReadContent(HttpContent content)
    {
        try
        {
            return content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeCacheKey(string identifier)
        => identifier.Trim().ToLowerInvariant();

    private static string Trim(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return string.Empty;

        payload = payload.Trim();
        const int max = 160;
        return payload.Length <= max ? payload : payload[..max] + "…";
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static long? TryGetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    private readonly record struct StreamElementsContext(string AccessToken, bool UsesOAuth, string ChannelId, string ChannelUsername, string? ChannelDisplayName, DateTimeOffset RetrievedAt);

    private readonly record struct StreamElementsChannel(string Id, string Username, string? DisplayName);

    private readonly record struct PointsCacheEntry(PointsSnapshot Snapshot);

    internal readonly record struct PointsSnapshot(
        bool Success,
        bool Found,
        string Identifier,
        long? Points,
        long? TotalPoints,
        long? Rank,
        string? Username,
        string? DisplayName,
        DateTimeOffset FetchedAt,
        string? RawJson,
        string? Error)
    {
        public static PointsSnapshot CreateSuccess(string identifier, DateTimeOffset fetchedAt, long? points, long? total, long? rank, string? username, string? displayName, string? raw)
            => new(true, true, identifier, points, total, rank, username, displayName, fetchedAt, raw, null);

        public static PointsSnapshot Missing(string identifier, DateTimeOffset fetchedAt, string? raw = null)
            => new(true, false, identifier, null, null, null, null, null, fetchedAt, raw, null);

        public static PointsSnapshot Failure(string identifier, string error)
            => new(false, false, identifier, null, null, null, null, null, DateTimeOffset.UtcNow, null, string.IsNullOrWhiteSpace(error) ? "StreamElements request failed." : error.Trim());
    }
}
