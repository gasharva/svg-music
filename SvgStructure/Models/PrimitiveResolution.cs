namespace SvgStructure.Models;

public enum PrimitiveLogicalScope
{
    PartMeasure,
    Measure,
    PhysicalOnly
}

/// <summary>
/// A real content primitive from the SVG, enriched with logical score coordinates, physical vector
/// geometry and stable source provenance. Later steps must preserve Source so any recognition result
/// can be traced back to the original SVG scene/XML element without reopening the source SVG.
/// </summary>
public sealed record ResolvedPrimitive(
    int Id,
    RectD PhysicalBounds,
    PrimitiveContour Contour,
    PrimitiveLogicalScope Scope,
    int? PartNumber,
    int? MeasureNumber,
    PrimitiveSourceRef Source,
    IReadOnlyList<PrimitiveContour>? SourceGroupContours = null)
{
    public string LogicalLabel => Scope switch
    {
        PrimitiveLogicalScope.PartMeasure => $"P{PartNumber}-M{MeasureNumber}",
        PrimitiveLogicalScope.Measure => $"M{MeasureNumber}",
        _ => "physical-only"
    };
}

public sealed record PrimitiveResolution(
    PartMeasureResolution Structure,
    IReadOnlyList<ResolvedPrimitive> Primitives)
{
    public IReadOnlyList<ResolvedPrimitive> PartMeasurePrimitives =>
        Primitives.Where(x => x.Scope == PrimitiveLogicalScope.PartMeasure).ToArray();

    public IReadOnlyList<ResolvedPrimitive> MeasurePrimitives =>
        Primitives.Where(x => x.Scope == PrimitiveLogicalScope.Measure).ToArray();

    public IReadOnlyList<ResolvedPrimitive> PhysicalOnlyPrimitives =>
        Primitives.Where(x => x.Scope == PrimitiveLogicalScope.PhysicalOnly).ToArray();
}
