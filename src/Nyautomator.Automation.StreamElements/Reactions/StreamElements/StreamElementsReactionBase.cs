using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Base class for StreamElements reactions providing common helpers.
/// </summary>
public abstract class StreamElementsReactionBase : ReactionType, IAsyncReaction
{
    private static readonly JsonSerializerOptions PayloadDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public sealed override void Execute()
        => ExecuteAsync().GetAwaiter().GetResult();

    public abstract Task ExecuteAsync();

    /// <summary>
    /// Resolves a payload from a dynamic automation value or a fallback JSON string.
    /// </summary>
    protected static object? ResolvePayload(object? provided, string? fallbackJson)
    {
        if (provided is not null)
            return provided;

        if (string.IsNullOrWhiteSpace(fallbackJson))
            return null;

        var text = fallbackJson.Trim();
        if (text.Length == 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize<object>(text, PayloadDeserializeOptions);
        }
        catch
        {
            return text;
        }
    }

    protected static void LogInfo(string message)
        => Console.WriteLine($"[Automation][StreamElements] {message}");

    protected static void LogError(string action, Exception ex)
        => Console.WriteLine($"[Automation][StreamElements] {action} failed: {ex.Message}");
}
