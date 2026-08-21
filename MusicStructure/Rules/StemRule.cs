namespace MusicStructure;

public sealed class StemRule : IMusicNoteRule
{
    private const double MaxUnattachedStemDistance = 1.5;

    private readonly IReadOnlyDictionary<string, RecognizedStemInput> _stemsByNote;
    private readonly IReadOnlyList<RecognizedStemInput> _unattachedStems;

    public StemRule(MusicMeasureInput input)
    {
        _stemsByNote = input.Stems
            .SelectMany(stem => stem.AttachedNoteKeys.Select(noteKey => (noteKey, stem)))
            .GroupBy(x => x.noteKey)
            .ToDictionary(g => g.Key, g => g.Select(x => x.stem).First());

        _unattachedStems = input.Stems
            .Where(x => x.AttachedNoteKeys.Count == 0 && x.LogicalX.HasValue)
            .ToArray();
    }

    public MusicNoteDraft Apply(MusicNoteDraft note)
    {
        if (_stemsByNote.TryGetValue(note.Source.Key, out var direct))
            return note with { Stem = direct.Direction, StemKey = direct.Key };

        if (!note.Source.LogicalX.HasValue)
            return note;

        var nearby = _unattachedStems
            .Where(x => x.Staff == note.Source.Staff)
            .Select(x => new { Stem = x, Distance = Math.Abs(x.LogicalX!.Value - note.Source.LogicalX.Value) })
            .Where(x => x.Distance <= MaxUnattachedStemDistance)
            .OrderBy(x => x.Distance)
            .ToArray();

        // Only make the inference when one stem is clearly the local candidate. Ambiguous geometry
        // stays unresolved instead of being silently forced onto a note.
        if (nearby.Length == 0 || (nearby.Length > 1 && Math.Abs(nearby[0].Distance - nearby[1].Distance) < 0.25))
            return note;

        return note with { Stem = nearby[0].Stem.Direction, StemKey = nearby[0].Stem.Key };
    }
}
