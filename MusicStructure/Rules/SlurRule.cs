namespace MusicStructure;

/// <summary>
/// Converts the already recognized arc relationships into MusicXML-style slur endpoints.
/// Arc geometry itself stays below the MusicStructure boundary: this rule only sees the note/stem
/// relationships carried by the DTO and chooses the left and right musical endpoints.
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
            var keys = arc.NoteKeys
                .Concat(arc.StemKeys
                    .Where(stemsByKey.ContainsKey)
                    .SelectMany(key => stemsByKey[key].AttachedNoteKeys))
                .Distinct()
                .Where(notesByKey.ContainsKey)
                .ToArray();

            if (keys.Length < 2)
                continue;

            var ordered = keys
                .Select(key => notesByKey[key])
                .Where(note => note.LogicalX.HasValue)
                .OrderBy(note => note.LogicalX)
                .ToArray();

            if (ordered.Length < 2)
                continue;

            var start = ordered.First();
            var stop = ordered.Last();
            if (start.Key == stop.Key)
                continue;

            var slurNumber = ++number;
            Add(result, start.Key, new MusicSlur(slurNumber, MusicSlurType.Start));
            Add(result, stop.Key, new MusicSlur(slurNumber, MusicSlurType.Stop));
        }

        _slursByNote = result.ToDictionary(x => x.Key, x => (IReadOnlyList<MusicSlur>)x.Value.ToArray());
    }

    public MusicNoteDraft Apply(MusicNoteDraft note) =>
        _slursByNote.TryGetValue(note.Source.Key, out var slurs)
            ? note with { Slurs = slurs }
            : note;

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
