using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Updates StreamElements replacement variables that can be consumed by overlays and custom widgets.
/// </summary>
public sealed class StreamElementsUpdateReplacementReaction : StreamElementsReactionBase
{
    public override string Id => "StreamElements.Reaction.Replacement";
    public override string DisplayName => "StreamElements: Update Replacement";
    public override string Description => "Sets or removes a StreamElements replacement variable.";

    [Description("Replacement variable name to update (case-sensitive in overlays).")]
    public string Variable { get; set; } = string.Empty;

    [Description("Value supplied via automation wiring. Overrides JSON when provided.")]
    public object? Value { get; set; }

    [Description("Fallback JSON value used when no automation value is supplied. Supports comments and trailing commas.")]
    public string? ValueJson { get; set; }

    [Description("When enabled, merge with existing replacements (PATCH). When disabled the request replaces the value using PUT.")]
    public bool Merge { get; set; } = true;

    [Description("Remove the replacement when the value resolves to null or empty.")]
    public bool RemoveWhenNull { get; set; } = true;

    public override async Task ExecuteAsync()
    {
        var key = Variable?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            LogInfo("StreamElements replacement update skipped: variable name is empty.");
            return;
        }

        var resolved = ResolvePayload(Value, ValueJson);
        if (IsNullOrEmpty(resolved) && RemoveWhenNull)
        {
            try
            {
                await StreamElementsApiClient.DeleteReplacementAsync(key, CancellationToken.None).ConfigureAwait(false);
            }
            catch (StreamElementsApiException ex)
            {
                LogError("Delete replacement", ex);
            }
            catch (Exception ex)
            {
                LogError("Delete replacement", ex);
            }
            return;
        }

        try
        {
            var replacements = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [key] = resolved
            };

            await StreamElementsApiClient.UpdateReplacementsAsync(replacements, Merge, CancellationToken.None).ConfigureAwait(false);
        }
        catch (StreamElementsApiException ex)
        {
            LogError("Update replacement", ex);
        }
        catch (Exception ex)
        {
            LogError("Update replacement", ex);
        }
    }

    private static bool IsNullOrEmpty(object? value)
    {
        if (value is null)
            return true;

        if (value is string s)
            return string.IsNullOrWhiteSpace(s);

        if (value is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Null
                || (element.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(element.GetString()))
                || (element.ValueKind == JsonValueKind.Undefined);
        }

        return false;
    }
}
