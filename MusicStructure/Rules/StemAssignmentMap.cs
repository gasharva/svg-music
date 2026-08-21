namespace MusicStructure;

internal static class StemAssignmentMap
{
    private const double MaxUnattachedStemDistance = 1.5;

    public static IReadOnlyDictionary<string, RecognizedStemInput> Build(MusicMeasureInput input)
    {
        var result = input.Stems
            .SelectMany(stem => stem.AttachedNoteKeys.Select(noteKey => (noteKey, stem)))
            .GroupBy(x => x.noteKey)
            .ToDictionary(g => g.Key, g => g.Select(x => x.stem).First());

        var unattached = input.Stems
            .Where(x => x.AttachedNoteKeys.Count == 0 && x.LogicalX.HasValue)
            .ToArray();

        foreach (var note in input.Notes.Where(x => !result.ContainsKey(x.Key) && x.LogicalX.HasValue))
        {
            var nearby = unattached
                .Where(x => x.Staff == note.Staff)
                .Select(x => new { Stem = x, Distance = Math.Abs(x.LogicalX!.Value - note.LogicalX!.Value) })
                .Where(x => x.Distance <= MaxUnattachedStemDistance)
                .OrderBy(x => x.Distance)
                .ToArray();

            if (nearby.Length == 0)
                continue;
            if (nearby.Length > 1 && Math.Abs(nearby[0].Distance - nearby[1].Distance) < 0.25)
                continue;

            result[note.Key] = nearby[0].Stem;
        }

        return result;
    }
}
