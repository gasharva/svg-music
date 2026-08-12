namespace SvgToMusicXmlPoc.Models;

public sealed record SvgUse(string SymbolId, double X, double Y, string SourceKind = "use");

public sealed record SvgDirectPath(string SymbolId, SymbolGeometry Geometry, double X, double Y);

/// <summary>
/// One actual painted path geometry on the SVG page, regardless of whether it originated as a
/// standalone path or as a reusable symbol instantiated through use. Geometry is already in page
/// coordinates; downstream structural recognition must not need to know the SVG storage form.
/// </summary>
public sealed record SvgPageGeometry(
    string InstanceId,
    string? SourceSymbolId,
    string SourceKind,
    SymbolGeometry Geometry,
    double X,
    double Y);

public sealed record SvgLineSegment(
    double X1,
    double Y1,
    double X2,
    double Y2,
    string SourceKind,
    string? CssClass = null)
{
    public double CenterX => (X1 + X2) / 2;
    public double CenterY => (Y1 + Y2) / 2;
    public double Top => Math.Min(Y1, Y2);
    public double Bottom => Math.Max(Y1, Y2);
    public double Width => Math.Abs(X2 - X1);
    public double Height => Math.Abs(Y2 - Y1);
}

public sealed record Staff(int Index, double Left, double Right, IReadOnlyList<double> Lines)
{
    public double Space => Lines.Count > 1 ? Lines.Zip(Lines.Skip(1), (a, b) => b - a).Average() : 0;
    public double Top => Lines.Min();
    public double Bottom => Lines.Max();
    public double Center => Lines.Average();
}

public sealed class RecognizedEvent
{
    public string SourceSymbolId { get; init; } = "";
    public string Kind { get; set; } = "unknown";
    public string ReferenceId { get; init; } = "";
    public double Confidence { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public int StaffIndex { get; set; }

    public string? Step { get; set; }
    public int? Octave { get; set; }
    public int Alter { get; set; }
    public string? Type { get; set; }
    public int Duration { get; set; }
    public bool Dotted { get; set; }
    public bool Chord { get; set; }
    public string? ClefSign { get; set; }
    public int? ClefLine { get; set; }
    public string? AttachedToSymbolId { get; set; }

    // Geometry relationships. These are inferred from shape/position only; SVG CSS classes are not used.
    public double? StemX { get; set; }
    public string? StemDirection { get; set; }
    public string? BeamValue { get; set; }
    public int BeamCount { get; set; }
    public bool SlurStart { get; set; }
    public bool SlurStop { get; set; }
    public int? SlurNumber { get; set; }
    public bool TieStart { get; set; }
    public bool TieStop { get; set; }
}

public sealed class AnalysisResult
{
    public List<Staff> Staves { get; init; } = [];
    public List<SvgUse> Uses { get; init; } = [];
    public List<SvgDirectPath> DirectPaths { get; init; } = [];
    public List<SvgPageGeometry> PageGeometry { get; init; } = [];
    public List<SvgLineSegment> LineSegments { get; init; } = [];
    public List<SymbolClassification> Classifications { get; init; } = [];
    public List<RecognizedEvent> Events { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
