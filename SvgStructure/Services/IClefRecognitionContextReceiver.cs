using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Optional hook for recognizer decorators that need the logical context of the next clef candidate.
/// The resolver only publishes context; diagnostics decide whether to consume it.
/// </summary>
public interface IClefRecognitionContextReceiver
{
    void SetNextContext(ClefRecognitionContext context);
}

public sealed record ClefRecognitionContext(
    int PartNumber,
    int MeasureNumber,
    LogicalRectD LogicalBounds);
