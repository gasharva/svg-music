namespace MusicStructure;

public sealed class BeamRule : IMusicNoteRule
{
    private readonly MusicMeasureInput _input;
    private readonly IReadOnlyDictionary<string, double?> _stemX;

    public BeamRule(MusicMeasureInput input)
    {
        _input = input;
        var notesByKey = input.Notes.ToDictionary(x => x.Key);
        _stemX = input.Stems.ToDictionary(
            x => x.Key,
            x => x.AttachedNoteKeys
                .Where(notesByKey.ContainsKey)
                .Select(k => notesByKey[k].LogicalX)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .DefaultIfEmpty(double.NaN)
                .Average() is var avg && !double.IsNaN(avg) ? avg : (double?)null);
    }

    public MusicNoteDraft Apply(MusicNoteDraft note)
    {
        if (note.StemKey is null)
            return note with { Beams = Array.Empty<MusicBeam>() };

        var beams = _input.Beams
            .Where(x => x.StemKeys.Contains(note.StemKey))
            .OrderBy(x => x.Level)
            .Select(x => new MusicBeam(x.Level, Position(x, note.StemKey)))
            .ToArray();

        return note with { Beams = beams };
    }

    private MusicBeamPosition Position(RecognizedBeamInput beam, string stemKey)
    {
        var ordered = beam.StemKeys
            .Distinct()
            .OrderBy(x => _stemX.TryGetValue(x, out var value) ? value ?? double.MaxValue : double.MaxValue)
            .ToArray();

        if (ordered.Length <= 1)
            return MusicBeamPosition.Begin;

        var index = Array.IndexOf(ordered, stemKey);
        return index <= 0
            ? MusicBeamPosition.Begin
            : index == ordered.Length - 1
                ? MusicBeamPosition.End
                : MusicBeamPosition.Continue;
    }
}
