namespace MusicStructure;

/// <summary>
/// Converts already recognized left/right arc attachments into musical slur endpoints.
/// Arc geometry stays below the MusicStructure boundary; this rule works only with DTO relations.
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
            var start = ResolveSide(arc.LeftNoteKey, arc.LeftStemKey, notesByKey, stemsByKey);
            var stop = ResolveSide(arc.RightNoteKey, arc.RightStemKey, notesByKey, stemsByKey);

            if (start is null || stop is null || start.Key == stop.Key)
                continue;

            var slurNumber = ++number;
            Add(result, start.Key, new MusicSlur(slurNumber, MusicSlurType.Start, arc.Placement));
            Add(result, stop.Key, new MusicSlur(slurNumber, MusicSlurType.Stop, arc.Placement));
        }

        _slursByNote = result.ToDictionary(x => x.Key, x => (IReadOnlyList<MusicSlur>)x.Value.ToArray());
    }

    public MusicNoteDraft Apply(MusicNoteDraft note) =>
        _slursByNote.TryGetValue(note.Source.Key, out var slurs)
            ? note with { Slurs = slurs }
            : note;

    private static RecognizedNoteInput? ResolveSide(
        string? directNoteKey,
        string? stemKey,
        IReadOnlyDictionary<string, RecognizedNoteInput> notesByKey,
        IReadOnlyDictionary<string, RecognizedStemInput> stemsByKey)
    {
        if (directNoteKey is not null && notesByKey.TryGetValue(directNoteKey, out var directNote))
            return directNote;

        if (stemKey is null || !stemsByKey.TryGetValue(stemKey, out var stem))
            return null;

        var attached = stem.AttachedNoteKeys
            .Where(notesByKey.ContainsKey)
            .Select(key => notesByKey[key])
            .ToArray();

        if (attached.Length == 0)
            return null;
        if (attached.Length == 1)
            return attached[0];

        if (stem.LogicalX is { } stemX)
            return attached.OrderBy(note => note.LogicalX is { } x ? Math.Abs(x - stemX) : double.MaxValue).First();

        return attached[0];
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
