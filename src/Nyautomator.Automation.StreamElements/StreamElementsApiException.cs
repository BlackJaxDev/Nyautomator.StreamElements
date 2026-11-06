namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Represents recoverable errors from StreamElements API calls.
/// Shared between the Automation engine and the Server's StreamElements client.
/// </summary>
public sealed class StreamElementsApiException : Exception
{
    public StreamElementsApiException(string message) : base(message) { }
    public StreamElementsApiException(string message, Exception innerException) : base(message, innerException) { }
}
