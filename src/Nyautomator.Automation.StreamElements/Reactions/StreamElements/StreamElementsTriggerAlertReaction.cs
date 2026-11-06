using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Reaction node that triggers a StreamElements alert.
/// </summary>
public sealed class StreamElementsTriggerAlertReaction : StreamElementsReactionBase
{
    public override string Id => "StreamElements.Reaction.Alert";
    public override string DisplayName => "StreamElements: Trigger Alert";
    public override string Description => "Triggers an alert on the configured StreamElements channel.";

    [OptionsProvider(typeof(StreamElementsAlertTypeOptionsProvider))]
    [Description("Alert type to fire (matches StreamElements event identifiers). Common values include tip, cheer, follower, subscriber, raid, host, merch, custom.")]
    public string AlertType { get; set; } = "custom";

    [Description("Optional message text shown by the alert when provided.")]
    public string? Message { get; set; }

    [Description("Optional custom image URL to override the alert artwork.")]
    public string? ImageUrl { get; set; }

    [Description("Optional custom sound URL to override the alert audio.")]
    public string? SoundUrl { get; set; }

    [Description("Optional duration override in milliseconds. Leave zero to use the overlay default.")]
    public int DurationMs { get; set; }

    [Description("Skip the alert queue and display immediately.")]
    public bool SkipQueue { get; set; }

    [Description("Optional automation payload supplying additional data fields for the alert. Overrides JSON when provided.")]
    public object? Data { get; set; }

    [Description("Fallback JSON payload used when no automation value is provided. Supports comments and trailing commas.")]
    public string? DataJson { get; set; }

    public override async Task ExecuteAsync()
    {
        try
        {
            var payload = BuildAlertPayload();
            await StreamElementsApiClient.TriggerAlertAsync(payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (StreamElementsApiException ex)
        {
            LogError("Trigger alert", ex);
        }
        catch (Exception ex)
        {
            LogError("Trigger alert", ex);
        }
    }

    private IDictionary<string, object?> BuildAlertPayload()
    {
        var body = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = string.IsNullOrWhiteSpace(AlertType) ? "custom" : AlertType.Trim()
        };

        if (!string.IsNullOrWhiteSpace(Message))
            body["message"] = Message.Trim();

        if (!string.IsNullOrWhiteSpace(ImageUrl))
            body["image"] = ImageUrl.Trim();

        if (!string.IsNullOrWhiteSpace(SoundUrl))
            body["sound"] = SoundUrl.Trim();

        if (DurationMs > 0)
            body["duration"] = DurationMs;

        if (SkipQueue)
            body["skipQueue"] = true;

        var data = ResolvePayload(Data, DataJson);
        if (data is not null)
            body["data"] = data;

        return body;
    }
}

internal sealed class StreamElementsAlertTypeOptionsProvider : IStringOptionsProvider
{
    private static readonly string[] DefaultTypes =
    {
        "custom",
        "tip",
        "cheer",
        "follower",
        "subscriber",
        "raid",
        "host",
        "merch",
        "redemption",
        "hype_train",
        "goal"
    };

    public IEnumerable<string> GetOptions() => DefaultTypes;
}
