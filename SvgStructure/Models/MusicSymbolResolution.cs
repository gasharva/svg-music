namespace SvgStructure.Models;

/// <summary>
/// Candidate visual music symbol produced after PrimitiveResolver. Primitive geometry is used only
/// to decide grouping and logical placement; recognition should use SmoothPaths reconstructed from
/// Svg.Skia's retained scene graph whenever possible.
/// ParentCandidateId is set for optional ink-connected sub-candidates derived from a broader bbox
/// candidate. If the parent is later recognized confidently, consumers may ignore its children.
/// </summary>
public sealed record MusicSymbolCandidate(
    int Id,
    PrimitiveLogicalScope Scope,
    int? PartNumber,
    int MeasureNumber,
    RectD PhysicalBounds,
    IReadOnlyList<int> PrimitiveIds,
    IReadOnlyList<RectD> PrimitiveBounds,
    IReadOnlyList<PrimitiveSourceRef> Sources,
    IReadOnlyList<SmoothSvgPath> SmoothPaths,
    int? ParentCandidateId = null)
{
    public string LogicalLabel => PartNumber is null
        ? $"M{MeasureNumber}"
        : $"P{PartNumber}-M{MeasureNumber}";

    public bool IsDerived => ParentCandidateId is not null;
}

/// <summary>
/// Original smooth SVG path data plus the exact total transform supplied by Svg.Skia's retained
/// scene graph. Keeping the matrix as SVG transform text preserves Bezier curves without flattening.
/// </summary>
public sealed record SmoothSvgPath(
    string SourceAddress,
    string PathData,
    string? Transform);

public sealed record MusicSymbolResolution(
    PrimitiveResolution Primitives,
    IReadOnlyList<MusicSymbolCandidate> Candidates);
