using System.Net;
using System.Text.Json;
using Nyautomator.Module.Abstractions;

namespace Nyautomator.Modules.StreamElements;

/// <summary>
/// Small request/response helpers shared by this module's API handler.
/// </summary>
internal static class ModuleHttp
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('/').ToLowerInvariant();

    public static bool IsMethod(ModuleApiRequest request, string expected)
        => string.Equals(request.Method?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    public static string? Query(ModuleApiRequest request, string name)
        => request.Query.TryGetValue(name, out var value) ? value : null;

    public static bool QueryBool(ModuleApiRequest request, string name, bool defaultValue = false)
        => bool.TryParse(Query(request, name), out var parsed) ? parsed : defaultValue;

    public static int? QueryInt(ModuleApiRequest request, string name)
        => int.TryParse(Query(request, name), out var parsed) ? parsed : null;

    public static async Task<T?> ReadBodyAsync<T>(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        var body = request.Body;
        if (body is null || !body.CanRead)
            return default;

        if (body.CanSeek && body.Length == 0)
            return default;

        using var reader = new StreamReader(body, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, JsonOptions);
    }

    public static ModuleApiResponse Ok(object? body)
        => ModuleApiResponse.Json(body);

    public static ModuleApiResponse Error(string message, int statusCode)
        => ModuleApiResponse.Json(new { ok = false, success = false, error = message, message }, statusCode);

    public static ModuleApiResponse NotFound(string moduleName, string path)
        => Error($"{moduleName} module API path '/{path}' is not available.", (int)HttpStatusCode.NotFound);
}
