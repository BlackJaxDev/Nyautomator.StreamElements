using System;
using System.ComponentModel;
using System.Globalization;

namespace NyautomatorUI.Server.Automation;

[AutomationOutputs(
    "out",
    "points",
    "value",
    "success",
    "found",
    "usedefault",
    "lifetime",
    "rank",
    "identifier",
    "username",
    "displayname",
    "error",
    "raw",
    "source",
    "fetchedat",
    "age"
)]
public sealed class StreamElementsPointsNode : ValueNodeType
{
    public override string Id => "Value.StreamElements.Points";
    public override string DisplayName => "StreamElements: Points";
    public override string Description => "Retrieves StreamElements loyalty point balances for a viewer and exposes cached metadata.";

    [Description("Viewer login or identifier to query. Connect a value input to drive this dynamically.")]
    public string? User { get; set; }

    [Description("Cache duration in milliseconds before refreshing the StreamElements API response.")]
    public double CacheMilliseconds { get; set; } = 5000d;

    [Description("Optional fallback value when the viewer has no recorded points or the request fails.")]
    public object? DefaultValue { get; set; }

    public override object? ComputeValue(string? outputHandle)
    {
        var handle = NormalizeHandle(outputHandle);
        var identifier = NormalizeIdentifier(User);
        var now = DateTimeOffset.UtcNow;

        StreamElementsValueNodeHelper.PointsSnapshot snapshot;
        if (string.IsNullOrEmpty(identifier))
        {
            snapshot = StreamElementsValueNodeHelper.PointsSnapshot.Failure(string.Empty, "Viewer identifier is required.");
        }
        else
        {
            snapshot = StreamElementsValueNodeHelper.GetPointsSnapshot(identifier, CacheMilliseconds);
        }

        var result = BuildResult(snapshot, now);
        var age = snapshot.FetchedAt == DateTimeOffset.MinValue
            ? TimeSpan.Zero
            : now - snapshot.FetchedAt;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        return handle switch
        {
            "" or "out" or "points" or "value" => result.Points,
            "success" => snapshot.Success,
            "found" => snapshot.Found,
            "usedefault" => result.UsedDefault,
            "lifetime" => snapshot.TotalPoints,
            "rank" => snapshot.Rank,
            "identifier" => snapshot.Identifier,
            "username" => snapshot.Username,
            "displayname" => snapshot.DisplayName,
            "fetchedat" => snapshot.FetchedAt,
            "age" => age,
            "error" => result.Error,
            "raw" => snapshot.RawJson,
            "source" => "streamElements",
            _ => result.Points
        };
    }

    public override Type? GetOutputType(string? outputHandle)
    {
        var handle = NormalizeHandle(outputHandle);
        return ResolveOutputTypeToken(handle switch
        {
            "" or "out" or "points" or "value" => "int64",
            "success" or "found" or "usedefault" => "boolean",
            "lifetime" or "rank" => "int64",
            "identifier" or "username" or "displayname" or "error" or "raw" or "source" => "string",
            "fetchedat" => "object:System.DateTimeOffset",
            "age" => "object:System.TimeSpan",
            _ => null
        }, fallbackToObject: false);
    }

    private StreamElementsNodeResult BuildResult(StreamElementsValueNodeHelper.PointsSnapshot snapshot, DateTimeOffset evaluatedAt)
    {
        long? points = snapshot.Points;
        bool usedDefault = false;
        string? error = snapshot.Error;

        if ((!snapshot.Success || !snapshot.Found || !points.HasValue) && DefaultValue is not null)
        {
            if (TryConvertDefaultToLong(DefaultValue, out var converted))
            {
                points = converted;
                usedDefault = true;
            }
            else
            {
                error = AppendError(error, "DefaultValue could not be converted to an integer.");
            }
        }

        return new StreamElementsNodeResult(snapshot, points, usedDefault, error, evaluatedAt);
    }

    private static bool TryConvertDefaultToLong(object value, out long result)
    {
        result = 0L;
        try
        {
            switch (value)
            {
                case long l:
                    result = l;
                    return true;
                case int i:
                    result = i;
                    return true;
                case short s:
                    result = s;
                    return true;
                case byte b:
                    result = b;
                    return true;
                case ulong ul when ul <= long.MaxValue:
                    result = (long)ul;
                    return true;
                case uint ui:
                    result = ui;
                    return true;
                case double d:
                    result = (long)Math.Round(d, MidpointRounding.AwayFromZero);
                    return true;
                case float f:
                    result = (long)Math.Round(f, MidpointRounding.AwayFromZero);
                    return true;
                case decimal dec:
                    result = (long)Math.Round(dec, MidpointRounding.AwayFromZero);
                    return true;
                case string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    if (AutomationTypeTokens.TryConvertToTarget(value, typeof(long), out var converted) && converted is long convertedLong)
                    {
                        result = convertedLong;
                        return true;
                    }
                    break;
            }
        }
        catch
        {
            // ignore conversion errors and fall through to false
        }

        return false;
    }

    private static string NormalizeHandle(string? handle)
    {
        var normalized = AutomationValueHelper.NormalizeValueHandle(handle);
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized.ToLowerInvariant();
    }

    private static string NormalizeIdentifier(string? identifier)
        => string.IsNullOrWhiteSpace(identifier) ? string.Empty : identifier.Trim();

    private static string? AppendError(string? existing, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return existing;

        message = message.Trim();
        if (string.IsNullOrWhiteSpace(existing))
            return message;

        return string.Concat(existing.Trim(), " ", message);
    }

    private readonly record struct StreamElementsNodeResult(
        StreamElementsValueNodeHelper.PointsSnapshot Snapshot,
        long? Points,
        bool UsedDefault,
        string? Error,
        DateTimeOffset EvaluatedAt);
}
