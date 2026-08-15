using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Cheap logical-size sanity gate before invoking the expensive clef recognizer.
/// X/Y are expressed in the logical P+M grid, so these limits are independent of SVG scale
/// and of where inside the measure the clef happens to be drawn.
/// </summary>
public sealed class ClefCandidateSanity
{
    public double MinimumLogicalHeight { get; init; } = 8.0;
    public double MaximumLogicalHeight { get; init; } = 22.0;
    public double MinimumLogicalWidth { get; init; } = 1.10;
    public double MaximumLogicalWidth { get; init; } = 4.50;

    public bool Accept(LogicalRectD bounds)
    {
        if (bounds.Left is null || bounds.Right is null)
            return false;

        var width = bounds.Right.Value - bounds.Left.Value;
        var height = bounds.Bottom - bounds.Top;

        return height >= MinimumLogicalHeight &&
               height <= MaximumLogicalHeight &&
               width >= MinimumLogicalWidth &&
               width <= MaximumLogicalWidth;
    }
}
