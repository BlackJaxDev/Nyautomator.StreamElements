using Nyautomator.StreamElements;
using StreamElementsRealtime = Nyautomator.StreamElements.StreamElements;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Registers StreamElements realtime socket events as an automation event source.
/// </summary>
public static class StreamElementsAutomationIntegration
{
    public const string SourceName = "StreamElements";

    private static readonly object Gate = new();
    private static bool _wired;

    public static void Register()
        => AutomationFlowEngine.RegisterExternalEventSource(SourceName, Link, Unlink);

    public static void Unregister()
    {
        AutomationFlowEngine.UnregisterExternalEventSource(SourceName);
        Unlink();
    }

    private static void Link()
    {
        lock (Gate)
        {
            if (_wired)
                return;

            StreamElementsRealtime.EventReceived += OnEventReceived;
            _wired = true;
        }
    }

    private static void Unlink()
    {
        lock (Gate)
        {
            if (!_wired)
                return;

            StreamElementsRealtime.EventReceived -= OnEventReceived;
            _wired = false;
        }
    }

    private static void OnEventReceived(StreamElementsEvent eventData, StreamElementsRealtime.TimestampData timestamp)
    {
        if (eventData is null || timestamp is null)
            return;

        AutomationFlowEngine.DispatchExternalAction(
            SourceName,
            StreamElementsEventAction.TypeId,
            new StreamElementsEventPayload(eventData, timestamp));
    }
}
