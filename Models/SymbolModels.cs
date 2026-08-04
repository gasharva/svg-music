namespace SvgToMusicXmlPoc.Models;

public sealed record PointD(double X, double Y);
public sealed record SymbolGeometry(string Id, IReadOnlyList<IReadOnlyList<PointD>> Contours);

public sealed record ShapeDescriptor(
    double Width,
    double Height,
    double AspectRatio,
    double SignedArea,
    double FillRatio,
    double Perimeter,
    int ClosedContourCount,
    IReadOnlyList<PointD> NormalizedPoints);

public sealed class ReferenceCatalog
{
    public List<ReferenceSymbol> Symbols { get; init; } = [];
}

public sealed class ReferenceSymbol
{
    public string Id { get; init; } = "";
    public string SvgPath { get; init; } = "";
    public string Kind { get; init; } = "unknown";
    public string? MusicXmlElement { get; init; }
    public string? MusicXmlValue { get; init; }
    public double? ExpectedWidthInSpaces { get; init; }
    public double? ExpectedHeightInSpaces { get; init; }
    public double SizeTolerance { get; init; } = 0.65;
}

public sealed record SymbolClassification(
    string SymbolId,
    string Kind,
    string ReferenceId,
    double Score,
    double ShapeScore,
    double SizeScore,
    double WidthInSpaces,
    double HeightInSpaces,
    string? MusicXmlElement,
    string? MusicXmlValue);

public sealed class ClassificationResult
{
    public List<SymbolClassification> Symbols { get; init; } = [];
}
