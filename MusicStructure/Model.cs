namespace MusicStructure;

public enum MusicAccidental { Flat, Sharp, Natural, DoubleSharp, DoubleFlat }
public enum MusicStemDirection { Up, Down }
public enum MusicBeamPosition { Begin, Continue, End, ForwardHook, BackwardHook }

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
/// SVG-free recognition contract. This layer contains only recognized musical facts and
/// relationships between them. No physical boxes, contours, primitive ids, SVG nodes or XML types.
/// </summary>
public sealed record RecognizedNoteInput(
    string Key,
    int Staff,
    int Measure,
    double? LogicalX,
    string Pitch,
    bool IsFilled,
    MusicAccidental? Accidental,
    int DotCount);

public sealed record RecognizedStemInput(
    string Key,
    int Staff,
    int Measure,
    MusicStemDirection Direction,
    IReadOnlyList<string> AttachedNoteKeys);

public sealed record RecognizedBeamInput(
    int Measure,
    int Level,
    IReadOnlyList<string> StemKeys);

public sealed record RecognizedFlagInput(
    int Staff,
    int Measure,
    int Denominator,
    string StemKey);

public sealed record MusicStructureInput(
    int StaffCount,
    IReadOnlyList<int> MeasureNumbers,
    IReadOnlySet<int> SystemStartMeasures,
    IReadOnlyList<RecognizedNoteInput> Notes,
    IReadOnlyList<RecognizedStemInput> Stems,
    IReadOnlyList<RecognizedBeamInput> Beams,
    IReadOnlyList<RecognizedFlagInput> Flags);

public sealed record MusicMeasureInput(
    int Number,
    bool StartsNewSystem,
    int StaffCount,
    IReadOnlyList<RecognizedNoteInput> Notes,
    IReadOnlyList<RecognizedStemInput> Stems,
    IReadOnlyList<RecognizedBeamInput> Beams,
    IReadOnlyList<RecognizedFlagInput> Flags);

public sealed record MusicNoteDraft(
    RecognizedNoteInput Source,
    MusicPitch? Pitch = null,
    MusicStemDirection? Stem = null,
    string? StemKey = null,
    MusicAccidental? Accidental = null,
    int DotCount = 0,
    IReadOnlyList<MusicBeam>? Beams = null,
    string? Type = null,
    bool IsChordTone = false)
{
    public static MusicNoteDraft From(RecognizedNoteInput source) => new(
        source,
        Accidental: source.Accidental,
        DotCount: source.DotCount,
        Beams: Array.Empty<MusicBeam>());

    public MusicNote ToMusicNote()
    {
        if (Pitch is null)
            throw new InvalidOperationException($"Pitch was not resolved for note '{Source.Key}'.");
        if (Type is null)
            throw new InvalidOperationException($"Duration was not resolved for note '{Source.Key}'.");

        return new MusicNote(
            Source.Staff,
            Source.Measure,
            Source.LogicalX,
            Pitch,
            Type,
            Stem,
            Accidental,
            DotCount,
            Beams ?? Array.Empty<MusicBeam>(),
            IsChordTone);
    }
}
