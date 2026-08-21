namespace MusicStructure;

public enum MusicAccidental { Flat, Sharp, Natural, DoubleSharp, DoubleFlat }
public enum MusicStemDirection { Up, Down }
public enum MusicBeamPosition { Begin, Continue, End }

public sealed record MusicPitch(string Step, int Octave, int Alter = 0)
{
    public override string ToString() => $"{Step}{(Alter == 0 ? "" : Alter > 0 ? $"+{Alter}" : Alter.ToString())}{Octave}";
}

public sealed record MusicBeam(int Level, MusicBeamPosition Position);

public sealed record MusicNote(
    int Staff,
    int Measure,
    double? LogicalX,
    MusicPitch Pitch,
    string Type,
    MusicStemDirection? Stem,
    MusicAccidental? Accidental,
    int DotCount,
    IReadOnlyList<MusicBeam> Beams,
    bool IsChordTone = false);

public sealed record MusicMeasure(int Number, bool StartsNewSystem, IReadOnlyList<MusicNote> Notes);
public sealed record MusicScore(int StaffCount, IReadOnlyList<MusicMeasure> Measures)
{
    public IReadOnlyList<MusicNote> Notes => Measures.SelectMany(x => x.Notes).ToArray();
}

/// <summary>
/// SVG-free recognition contract. Coordinates are already logical musical coordinates;
/// no physical boxes, contours, primitive ids, SVG nodes or XML types are allowed here.
/// </summary>
public sealed record RecognizedNoteInput(
    int Staff,
    int Measure,
    double? LogicalX,
    string Pitch,
    bool IsFilled,
    MusicStemDirection? Stem,
    MusicAccidental? Accidental,
    int DotCount,
    int? FlagDenominator,
    IReadOnlyList<MusicBeam> Beams);

public sealed record MusicStructureInput(
    int StaffCount,
    IReadOnlyList<int> MeasureNumbers,
    IReadOnlySet<int> SystemStartMeasures,
    IReadOnlyList<RecognizedNoteInput> Notes);
