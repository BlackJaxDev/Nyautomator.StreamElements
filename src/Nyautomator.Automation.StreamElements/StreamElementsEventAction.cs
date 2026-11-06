using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Nyautomator.StreamElements;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Action node that fires when a StreamElements realtime API event matching the configured filters arrives.
/// </summary>
public sealed class StreamElementsEventAction : ActionType, IExternalPayloadAction
{
    public const string TypeId = "StreamElements.Event.Generic";

    [Description("StreamElements event name to listen for. Refer to https://dev.streamelements.com/docs/events for valid values.")]
    [OptionsProvider(typeof(StreamElementsEventOptionsProvider))]
    public string EventName { get; set; } = StreamElementsEventCatalog.DefaultEventName;

    [Description("Ignore mock events emitted from the StreamElements dashboard test tools.")]
    public bool IgnoreMockEvents { get; set; } = true;

    public override string Id => TypeId;
    public override string DisplayName => "StreamElements: Event";
    public override string Description => "Triggered when StreamElements sends the selected realtime API event. Exposes the full event payload for downstream nodes.";

    /// <summary>
    /// Advertise payload type so value nodes can inspect the incoming event.
    /// </summary>
    public override Type? GetOutputType(string? outputHandle)
        => typeof(StreamElementsEventPayload);

    public bool ShouldHandlePayload(object? payload)
        => payload is StreamElementsEventPayload streamElementsPayload && Matches(streamElementsPayload);

    internal bool Matches(StreamElementsEventPayload payload)
    {
        if (payload?.Event is null)
            return false;

        if (!string.IsNullOrWhiteSpace(EventName) &&
            !string.Equals(payload.Event.Name, EventName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (IgnoreMockEvents && payload.Event.IsMock)
            return false;

        return true;
    }
}

/// <summary>
/// Payload delivered to automation flows for StreamElements events.
/// </summary>
public sealed record StreamElementsEventPayload(StreamElementsEvent Event, StreamElements.TimestampData Timestamp)
{
    public DateTimeOffset TimestampUtc => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp.Timestamp);
}

internal sealed class StreamElementsEventOptionsProvider : IStringOptionsProvider
{
    public IEnumerable<string> GetOptions() => StreamElementsEventCatalog.Definitions.Select(d => d.Name);
}

internal static class StreamElementsEventCatalog
{
    private static readonly StreamElementsEventDefinition[] _definitions =
    [
        new("tip-latest", "Tips", "Latest Tip", "Raised when a new tip is processed."),
        new("follower-latest", "Followers", "Latest Follower", "Raised when a new follower is detected."),
        new("follower-session", "Followers", "Session Follower Count", "Emitted when the session follower count updates."),
        new("follower-week", "Followers", "Weekly Follower Count", "Emitted when the rolling weekly follower count updates."),
        new("follower-month", "Followers", "Monthly Follower Count", "Emitted when the rolling monthly follower count updates."),
        new("follower-goal", "Followers", "Follower Goal Progress", "Raised when follower goal progress changes."),
        new("follower-total", "Followers", "Total Followers", "Raised when the all-time follower count updates."),
        new("subscriber-alltime-gifter", "Subscribers", "All-Time Top Gifter", "Raised when the top all-time gift sub contributor changes."),
        new("subscriber-gifted-latest", "Subscribers", "Latest Gifted Subscription", "Raised when the most recent gifted subscription is recorded."),
        new("subscriber-gifted-session", "Subscribers", "Session Gifted Subscriptions", "Emitted when the session gifted sub total changes."),
        new("subscriber-latest", "Subscribers", "Latest Subscriber", "Raised when a new subscription is received."),
        new("subscriber-new-latest", "Subscribers", "Latest New Subscriber", "Raised when the latest brand new subscriber changes."),
        new("subscriber-new-session", "Subscribers", "Session New Subscribers", "Emitted when the number of new subscribers this session updates."),
        new("subscriber-resub-latest", "Subscribers", "Latest Resubscription", "Raised when the most recent resubscription is received."),
        new("subscriber-resub-session", "Subscribers", "Session Resubscriptions", "Emitted when the count of session resubscriptions updates."),
        new("subscriber-session", "Subscribers", "Session Subscribers", "Raised when the total subscriber count for the session updates."),
        new("subscriber-week", "Subscribers", "Weekly Subscribers", "Emitted when the weekly subscriber total updates."),
        new("subscriber-month", "Subscribers", "Monthly Subscribers", "Emitted when the monthly subscriber total updates."),
        new("subscriber-goal", "Subscribers", "Subscriber Goal Progress", "Raised when subscriber goal progress changes."),
        new("subscriber-total", "Subscribers", "Total Subscribers", "Raised when the all-time subscriber count updates."),
        new("subscriber-points", "Subscribers", "Subscriber Points", "Emitted when the loyalty point total updates."),
        new("host-latest", "Hosts", "Latest Host", "Raised when a new channel host event is processed."),
        new("raid-latest", "Raids", "Latest Raid", "Raised when an incoming raid is detected."),
        new("cheer-session", "Cheers", "Session Bits Total", "Emitted when the total bits for the current session updates."),
        new("cheer-month", "Cheers", "Monthly Bits Total", "Emitted when the monthly bits total updates."),
        new("cheer-total", "Cheers", "All-Time Bits Total", "Raised when the all-time bits total updates."),
        new("cheer-count", "Cheers", "Cheer Event Count", "Emitted when the total number of cheer events updates."),
        new("cheer-goal", "Cheers", "Bits Goal Progress", "Raised when progress toward the configured bits goal changes."),
        new("cheer-latest", "Cheers", "Latest Cheer", "Raised when a new cheer is received."),
        new("cheer-session-top-donation", "Cheers", "Session Top Bits Amount", "Emitted when the highest bits amount for the session updates."),
        new("cheer-weekly-top-donation", "Cheers", "Weekly Top Bits Amount", "Emitted when the highest bits amount for the past week updates."),
        new("cheer-monthly-top-donation", "Cheers", "Monthly Top Bits Amount", "Emitted when the highest bits amount for the past month updates."),
        new("cheer-alltime-top-donation", "Cheers", "All-Time Top Bits Amount", "Emitted when the highest bits amount overall updates."),
        new("cheer-session-top-donator", "Cheers", "Session Top Cheerer", "Raised when the top cheerer for the session changes."),
        new("cheer-weekly-top-donator", "Cheers", "Weekly Top Cheerer", "Raised when the top cheerer for the past week changes."),
        new("cheer-monthly-top-donator", "Cheers", "Monthly Top Cheerer", "Raised when the top cheerer for the past month changes."),
        new("cheer-alltime-top-donator", "Cheers", "All-Time Top Cheerer", "Raised when the top cheerer overall changes."),
        new("channel-points-latest", "Channel Points", "Latest Channel Points Redemption", "Raised when the latest channel point redemption updates."),
    ];

    private static readonly Dictionary<string, StreamElementsEventDefinition> _definitionsByName =
        _definitions.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<StreamElementsEventDefinition> Definitions => _definitions;
    public static string DefaultEventName => _definitions[0].Name;

    public static StreamElementsEventDefinition? TryGetDefinition(string? name)
        => name is not null && _definitionsByName.TryGetValue(name, out var def) ? def : null;
}

internal sealed record StreamElementsEventDefinition(string Name, string Category, string DisplayName, string Description);