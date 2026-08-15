namespace SvgStructure.Models;

public enum MeterSide
{
    Left,
    Right
}

/// <summary>Time-signature resolved inside one logical part/measure block.</summary>
public sealed record MeterResolution(
    int PartNumber,
    int MeasureNumber,
    int BeatNumber,
    int BeatValue,
    MeterSide Side,
    double Confidence,
    RectD PhysicalBounds,
    RectD NumeratorBounds,
    RectD DenominatorBounds)
{
    public string Label => $"{BeatNumber}-{BeatValue}";
    public string LogicalLabel => $"P{PartNumber}-M{MeasureNumber}";
}
