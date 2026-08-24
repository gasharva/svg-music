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
        // MusicXML puts beam elements on the first note of a chord; chord tones inherit the visual
        // beam through <chord/> and should not duplicate beam tags of their own.
        if (note.IsChordTone || note.StemKey is null)
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
        var ordered = OrderStems(beam.StemKeys);
        if (ordered.Length > 1)
        {
            var index = Array.IndexOf(ordered, stemKey);
            return index <= 0
                ? MusicBeamPosition.Begin
                : index == ordered.Length - 1
                    ? MusicBeamPosition.End
                    : MusicBeamPosition.Continue;
        }

        // A secondary/tertiary beam touching only one stem is a hook, not a one-note "begin" beam.
        // Use the nearest lower beam containing this stem to decide which way the hook points.
        var outer = _input.Beams
            .Where(x => x.Level < beam.Level && x.StemKeys.Contains(stemKey))
            .OrderByDescending(x => x.Level)
            .FirstOrDefault(x => x.StemKeys.Distinct().Count() > 1);

        if (outer is null)
            return MusicBeamPosition.Begin;

        var outerOrdered = OrderStems(outer.StemKeys);
        var outerIndex = Array.IndexOf(outerOrdered, stemKey);
        if (outerIndex <= 0)
            return MusicBeamPosition.ForwardHook;
        if (outerIndex == outerOrdered.Length - 1)
            return MusicBeamPosition.BackwardHook;

        // In the middle of an outer beam, prefer a backward hook when the note has a predecessor;
        // this matches the conventional short secondary beam extending toward the previous note.
        return MusicBeamPosition.BackwardHook;
    }

    private string[] OrderStems(IEnumerable<string> stemKeys) => stemKeys
        .Distinct()
        .OrderBy(x => _stemX.TryGetValue(x, out var value) ? value ?? double.MaxValue : double.MaxValue)
        .ToArray();
}
