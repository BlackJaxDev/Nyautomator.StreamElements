using System.Text;
using System.Text.Json;
using Nyautomator.Module.Abstractions;
using Nyautomator.Runtime.Abstractions;

namespace Nyautomator.Module.StreamElements.Tests;

internal static class TestSupport
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ModuleApiRequest Request(string moduleId, string method, string path, string? body = null, IDictionary<string, string?>? query = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (body is not null)
            headers["Content-Length"] = bytes.Length.ToString();

        return new ModuleApiRequest(
            moduleId,
            method,
            path,
            new Dictionary<string, string?>(query ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase),
            new MemoryStream(bytes),
            body is null ? null : "application/json",
            headers);
    }

    public static JsonElement ToJson(object? body)
        => JsonSerializer.SerializeToElement(body, JsonOptions);
}

internal sealed class FakeOptionsProvider(object? options) : IModuleOptionsProvider
{
    public object? Options { get; set; } = options;

    public List<(string ModuleId, object? Options)> Writes { get; } = [];

    public event Action<ModuleOptionsChangedEventArgs>? ModuleOptionsChanged;

    public T Get<T>(string moduleId) where T : new()
        => Get(moduleId, new T());

    public T Get<T>(string moduleId, T defaults)
        => Options is T typed ? typed : defaults;

    public bool TryGetRaw(string moduleId, out JsonElement raw)
    {
        raw = JsonSerializer.SerializeToElement(Options, TestSupport.JsonOptions);
        return Options is not null;
    }

    public bool Write<T>(string moduleId, T options, bool force = true)
    {
        Options = options;
        Writes.Add((moduleId, options));
        ModuleOptionsChanged?.Invoke(new ModuleOptionsChangedEventArgs(moduleId, JsonSerializer.SerializeToElement(options, TestSupport.JsonOptions)));
        return true;
    }
}
