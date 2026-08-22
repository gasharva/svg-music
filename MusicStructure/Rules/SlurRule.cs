namespace MusicStructure;

/// <summary>
/// Converts already recognized arc relationships into slur endpoints.
/// The left and right sides are resolved independently so an over-attached stem cannot drag an
/// earlier note into the slur merely because it shares that stem's recognition context.
/// </summary>
public sealed class SlurRule : IMusicNoteRule
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<MusicSlur>> _slursByNote;

    public SlurRule(MusicMeasureInput input)
    {
        var notesByKey = input.Notes.ToDictionary(x => x.Key);
        var stemsByKey = input.Stems.ToDictionary(x => x.Key);
        var result = new Dictionary<string, List<MusicSlur>>();
        var number = 0;

        foreach (var arc in input.Arcs)
        {
            var endpoints = ResolveEndpoints(arc, notesByKey, stemsByKey);
            if (endpoints is null || endpoints.Value.Start == endpoints.Value.Stop)
                continue;

            var slurNumber = ++number;
            Add(result, endpoints.Value.Start, new MusicSlur(slurNumber, MusicSlurType.Start));
            Add(result, endpoints.Value.Stop, new MusicSlur(slurNumber, MusicSlurType.Stop));
        }

        _slursByNote = result.ToDictionary(x => x.Key, x => (IReadOnlyList<MusicSlur>)x.Value.ToArray());
    }

    public MusicNoteDraft Apply(MusicNoteDraft note) =>
        _slursByNote.TryGetValue(note.Source.Key, out var slurs)
            ? note with { Slurs = slurs }
            : note;

    private static (string Start, string Stop)? ResolveEndpoints(
        RecognizedArcInput arc,
        IReadOnlyDictionary<string, RecognizedNoteInput> notesByKey,
        IReadOnlyDictionary<string, RecognizedStemInput> stemsByKey)
    {
        // ArcResolver preserves left/right order in these arrays.
        if (arc.NoteKeys.Count >= 2)
        {
            var left = arc.NoteKeys.FirstOrDefault(notesByKey.ContainsKey);
            var right = arc.NoteKeys.Reverse().FirstOrDefault(notesByKey.ContainsKey);
            if (left is not null && right is not null)
                return OrderByX(left, right, notesByKey);
        }

        if (arc.StemKeys.Count >= 2)
        {
            var left = ResolveStemNote(arc.StemKeys[0], notesByKey, stemsByKey);
            var right = ResolveStemNote(arc.StemKeys[^1], notesByKey, stemsByKey);
            if (left is not null && right is not null)
                return OrderByX(left, right, notesByKey);
        }

        return null;
    }

    private static string? ResolveStemNote(
        string stemKey,
        IReadOnlyDictionary<string, RecognizedNoteInput> notesByKey,
        IReadOnlyDictionary<string, RecognizedStemInput> stemsByKey)
    {
        if (!stemsByKey.TryGetValue(stemKey, out var stem))
            return null;

        var candidates = stem.AttachedNoteKeys
            .Where(notesByKey.ContainsKey)
            .Select(key => notesByKey[key])
            .ToArray();
        if (candidates.Length == 0)
            return null;

        // A stem recognizer may conservatively attach several nearby heads. For a slur endpoint,
        // choose the head horizontally closest to the recognized stem instead of expanding the
        // whole attachment set and then taking the leftmost/rightmost note of the measure.
        return candidates
            .OrderBy(note => stem.LogicalX.HasValue && note.LogicalX.HasValue
                ? Math.Abs(note.LogicalX.Value - stem.LogicalX.Value)
                : double.MaxValue)
            .ThenBy(note => note.LogicalX ?? double.MaxValue)
            .First().Key;
    }

    private static (string Start, string Stop) OrderByX(
        string left,
        string right,
        IReadOnlyDictionary<string, RecognizedNoteInput> notesByKey)
    {
        var lx = notesByKey[left].LogicalX ?? double.MaxValue;
        var rx = notesByKey[right].LogicalX ?? double.MaxValue;
        return lx <= rx ? (left, right) : (right, left);
    }

    private static void Add(IDictionary<string, List<MusicSlur>> map, string noteKey, MusicSlur slur)
    {
        if (!map.TryGetValue(noteKey, out var list))
        {
            list = new List<MusicSlur>();
            map[noteKey] = list;
        }
        list.Add(slur);
    }
}
