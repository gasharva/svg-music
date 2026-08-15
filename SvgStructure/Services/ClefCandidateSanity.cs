using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Cheap logical-size sanity gate before invoking the expensive clef recognizer.
/// Y is expressed in half staff-spaces, so these limits are independent of SVG scale
/// and of where inside the measure the clef happens to be drawn.
/// </summary>
public sealed class ClefCandidateSanity
{
    public double MinimumLogicalHeight { get; init; } = 5.0;
    public double MaximumLogicalHeight { get; init; } = 22.0;

    public bool Accept(LogicalRectD bounds)
    {
        var height = bounds.Bottom - bounds.Top;
        return height >= MinimumLogicalHeight && height <= MaximumLogicalHeight;
    }
}
