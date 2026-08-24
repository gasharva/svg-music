namespace MusicStructure;

internal static class StemAssignmentMap
{
    private const double MaxUnattachedStemDistance = 1.5;

    public static IReadOnlyDictionary<string, RecognizedStemInput> Build(MusicMeasureInput input)
    {
        var result = new Dictionary<string, RecognizedStemInput>();

        // Explicit low-level attachment is the strongest signal. If the resolver attached several
        // stems to one head, prefer the most specific stem first: a stem attached only to this head
        // is a better explanation than a nearby stem conservatively attached to several heads.
        // X distance is only the tie-breaker, never the primary replacement for resolver identity.
        foreach (var note in input.Notes)
        {
            var explicitStems = input.Stems
                .Where(stem => stem.AttachedNoteKeys.Contains(note.Key))
                .OrderBy(stem => stem.AttachedNoteKeys.Distinct().Count())
                .ThenBy(stem => Distance(stem.LogicalX, note.LogicalX))
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
