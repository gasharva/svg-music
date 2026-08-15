namespace SvgStructure.Models;

public enum PrimitiveLogicalScope
{
    PartMeasure,
    Measure,
    PhysicalOnly
}

/// <summary>
/// SVG primitive enriched with logical score coordinates when they can be resolved.
/// PartNumber is null for measure-wide primitives (for example cross-staff geometry).
/// Both logical coordinates are null for primitives that cannot be attached safely.
/// </summary>
public sealed record ResolvedPrimitive(
    int Id,
    RectD PhysicalBounds,
    PrimitiveLogicalScope Scope,
    int? PartNumber,
    int? MeasureNumber)
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
