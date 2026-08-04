namespace SvgToMusicXmlPoc.Models;

public sealed record SvgUse(string SymbolId, double X, double Y);

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
    public int StaffIndex { get; init; }

    // Music semantics. Fields not applicable to a particular event stay null/default.
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
}

public sealed class AnalysisResult
{
    public List<Staff> Staves { get; init; } = [];
    public List<SvgUse> Uses { get; init; } = [];
    public List<SymbolClassification> Classifications { get; init; } = [];
    public List<RecognizedEvent> Events { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
