namespace SvgStructure.Models;

public sealed record ScoreStructure(IReadOnlyList<PartStructure> Parts);

public sealed record PartStructure(
    string Id,
    IReadOnlyList<MeasureStructure> Measures);

public sealed record MeasureStructure(
    int Number,
    double Width);
