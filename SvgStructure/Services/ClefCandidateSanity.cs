using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Cheap size sanity gate before invoking the expensive clef recognizer.
/// Vertical size is expressed in logical Y (half staff-spaces), which is stable across the score.
/// Horizontal size must NOT use logical X: logical X is measure-relative and the same clef becomes
/// numerically narrower in a physically wide measure. Instead we use width relative to staff height.
/// </summary>
public sealed class ClefCandidateSanity
{
    // F clefs are substantially shorter than G clefs. In our real samples an F clef is about
    // 6.6 logical Y units high, while a G clef is around 14.8.
    public double MinimumLogicalHeight { get; init; } = 5.5;
    public double MaximumLogicalHeight { get; init; } = 22.0;

    // Width is normalized by physical staff height, not by logical X.
    // This rejects thin arpeggiation waves while remaining independent of measure width/meter.
    public double MinimumWidthPerStaffHeight { get; init; } = 0.28;
    public double MaximumWidthPerStaffHeight { get; init; } = 1.80;

    public bool Accept(LogicalRectD logicalBounds, RectD physicalBounds, double staffHeight)
    {
        var logicalHeight = logicalBounds.Bottom - logicalBounds.Top;
        var normalizedWidth = physicalBounds.Width / Math.Max(1e-9, staffHeight);

        return logicalHeight >= MinimumLogicalHeight &&
               logicalHeight <= MaximumLogicalHeight &&
               normalizedWidth >= MinimumWidthPerStaffHeight &&
               normalizedWidth <= MaximumWidthPerStaffHeight;
    }
}
