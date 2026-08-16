namespace SvgStructure.Models;

/// <summary>
/// Candidate visual music symbol produced after PrimitiveResolver. Primitive geometry is used only
/// to decide grouping and logical placement; recognition should use SmoothPaths reconstructed from
/// Svg.Skia's original SourceDocument whenever possible.
/// </summary>
public sealed record MusicSymbolCandidate(
    int Id,
    PrimitiveLogicalScope Scope,
    int? PartNumber,
    int MeasureNumber,
    RectD PhysicalBounds,
    IReadOnlyList<int> PrimitiveIds,
    IReadOnlyList<PrimitiveSourceRef> Sources,
    IReadOnlyList<SmoothSvgPath> SmoothPaths)
{
    public string LogicalLabel => PartNumber is null
        ? $"M{MeasureNumber}"
        : $"P{PartNumber}-M{MeasureNumber}";
}

/// <summary>
/// Original smooth SVG path data. Transform is deliberately kept as SVG transform text instead of
/// flattening Beziers into points.
/// </summary>
public sealed record SmoothSvgPath(
    string SourceAddress,
    string PathData,
    string? Transform);

public sealed record MusicSymbolResolution(
    PrimitiveResolution Primitives,
    IReadOnlyList<MusicSymbolCandidate> Candidates);
