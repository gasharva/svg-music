namespace SvgToMusicXmlPoc.Configuration;

public sealed class RecognitionConfig
{
    public string DefaultClef { get; init; } = "G";
    public int DefaultClefLine { get; init; } = 2;
    public int Divisions { get; init; } = 16;
    public int Beats { get; init; } = 4;
    public int BeatType { get; init; } = 4;
    public double StaffTolerance { get; init; } = 0.25;
    public double MaxSymbolDistanceInSpaces { get; init; } = 5.0;
    public double MaxAttachmentDistanceInSpaces { get; init; } = 2.5;
    public double MinClassificationScore { get; init; } = 0.42;

    /// <summary>
    /// Maximum number of glyph geometries classified concurrently. Set to 1 to disable parallel classification.
    /// </summary>
    public int ClassificationParallelism { get; init; } = 8;
}
