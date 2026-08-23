namespace MusicStructure;

internal static class StemAssignmentMap
{
    private const double MaxUnattachedStemDistance = 1.5;

    public static IReadOnlyDictionary<string, RecognizedStemInput> Build(MusicMeasureInput input)
    {
        var result = new Dictionary<string, RecognizedStemInput>();

        // Low-level stem recognition can conservatively attach more than one nearby stem to the
        // same note head. Do not depend on resolver enumeration order: when several explicit
        // attachments exist, the stem whose logical X is closest to the head wins.
        foreach (var note in input.Notes)
        {
            var explicitStems = input.Stems
                .Where(stem => stem.AttachedNoteKeys.Contains(note.Key))
                .OrderBy(stem => Distance(stem.LogicalX, note.LogicalX))
                .ThenBy(stem => stem.Key, StringComparer.Ordinal)
                .ToArray();

            if (explicitStems.Length > 0)
                result[note.Key] = explicitStems[0];
        }

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

            // If two unattached stems are genuinely equally plausible, leave the note unresolved;
            // a later, smarter rule can use more context. Otherwise preserve the obvious nearest one.
            if (nearby.Length > 1 && Math.Abs(nearby[0].Distance - nearby[1].Distance) < 0.10)
                continue;

            result[note.Key] = nearby[0].Stem;
        }

        return result;
    }

    private static double Distance(double? stemX, double? noteX) =>
        stemX.HasValue && noteX.HasValue
            ? Math.Abs(stemX.Value - noteX.Value)
            : double.MaxValue;
}
