using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Adjusts StreamElements loyalty points for a viewer.
/// </summary>
public sealed class StreamElementsAdjustLoyaltyReaction : StreamElementsReactionBase
{
    public override string Id => "StreamElements.Reaction.Loyalty";
    public override string DisplayName => "StreamElements: Adjust Loyalty";
    public override string Description => "Adds, removes, or sets StreamElements loyalty points for a user.";

    [Description("Viewer identifier (login or user ID) to adjust.")]
    public string User { get; set; } = string.Empty;

    [Description("Adjustment mode: Add, Subtract, or Set.")]
    public StreamElementsLoyaltyMode Mode { get; set; } = StreamElementsLoyaltyMode.Add;

    [Description("Point amount used for the selected mode. Negative values are normalized.")]
    public int Amount { get; set; } = 10;

    [Description("Optional note recorded alongside the loyalty change.")]
    public string? Reason { get; set; }

    public override async Task ExecuteAsync()
    {
        var target = User?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            LogInfo("StreamElements loyalty adjustment skipped: user is not set.");
            return;
        }

        try
        {
            var mode = Mode switch
            {
                StreamElementsLoyaltyMode.Add => "add",
                StreamElementsLoyaltyMode.Subtract => "subtract",
                StreamElementsLoyaltyMode.Set => "set",
                _ => "add"
            };

            var amount = Mode == StreamElementsLoyaltyMode.Set ? Amount : Math.Abs(Amount);
            if (amount <= 0 && Mode != StreamElementsLoyaltyMode.Set)
            {
                LogInfo("StreamElements loyalty adjustment skipped: amount must be non-zero for add/remove operations.");
                return;
            }

            await StreamElementsApiClient.AdjustLoyaltyAsync(target, mode, amount, Reason, CancellationToken.None).ConfigureAwait(false);
        }
        catch (StreamElementsApiException ex)
        {
            LogError("Adjust loyalty", ex);
        }
        catch (Exception ex)
        {
            LogError("Adjust loyalty", ex);
        }
    }
}

public enum StreamElementsLoyaltyMode
{
    Add,
    Subtract,
    Set
}
