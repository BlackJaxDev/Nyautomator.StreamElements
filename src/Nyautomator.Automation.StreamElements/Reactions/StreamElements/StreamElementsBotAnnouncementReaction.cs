using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Sends a bot announcement message using StreamElements, optionally targeting a specific platform and enabling TTS.
/// </summary>
public sealed class StreamElementsBotAnnouncementReaction : StreamElementsReactionBase
{
    public override string Id => "StreamElements.Reaction.Announcement";
    public override string DisplayName => "StreamElements: Bot Announcement";
    public override string Description => "Sends a chat announcement through the StreamElements bot.";

    [Description("Announcement message resolved from the automation input. Overrides JSON when supplied.")]
    public object? Message { get; set; }

    [Description("Fallback JSON/string value used when no automation message is supplied.")]
    public string? MessageJson { get; set; }

    [Description("Optional platform override (e.g. twitch, youtube). Defaults to the channel associated with the OAuth token.")]
    public string? Platform { get; set; }

    [Description("Enable text-to-speech support when the platform integration allows it.")]
    public bool? UseTts { get; set; }

    public override async Task ExecuteAsync()
    {
        var resolvedMessage = ResolvePayload(Message, MessageJson);
        var messageText = resolvedMessage switch
        {
            null => null,
            string s => s,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element => element.GetRawText(),
            _ => resolvedMessage.ToString()
        };

        if (string.IsNullOrWhiteSpace(messageText))
        {
            LogInfo("StreamElements bot announcement skipped: message is empty.");
            return;
        }

        try
        {
            await StreamElementsApiClient.SendBotAnnouncementAsync(messageText!, Platform, UseTts, CancellationToken.None).ConfigureAwait(false);
        }
        catch (StreamElementsApiException ex)
        {
            LogError("Bot announcement", ex);
        }
        catch (Exception ex)
        {
            LogError("Bot announcement", ex);
        }
    }
}
