namespace MusicStructure;

internal static class StemAssignmentMap
{
    private const double MaxUnattachedStemDistance = 1.5;

    public static IReadOnlyDictionary<string, RecognizedStemInput> Build(MusicMeasureInput input)
    {
        var result = new Dictionary<string, RecognizedStemInput>();
        var notesByKey = input.Notes.ToDictionary(x => x.Key);

        foreach (var note in input.Notes)
        {
            var explicitStems = input.Stems
                .Where(stem => stem.AttachedNoteKeys.Contains(note.Key))
                .ToArray();

            if (explicitStems.Length == 0)
                continue;

            var directional = ResolveOppositeStemAmbiguity(note, explicitStems, notesByKey);
            if (directional is not null)
            {
                result[note.Key] = directional;
                continue;
            }

            result[note.Key] = explicitStems
                .OrderBy(stem => stem.AttachedNoteKeys.Distinct().Count())
                .ThenBy(stem => Distance(stem.LogicalX, note.LogicalX))
                .ThenBy(stem => stem.Key, StringComparer.Ordinal)
                .First();
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

    private static RecognizedStemInput? ResolveOppositeStemAmbiguity(
        RecognizedNoteInput note,
        IReadOnlyList<RecognizedStemInput> stems,
        IReadOnlyDictionary<string, RecognizedNoteInput> notesByKey)
    {
        var up = stems.Where(x => x.Direction == MusicStemDirection.Up).ToArray();
        var down = stems.Where(x => x.Direction == MusicStemDirection.Down).ToArray();
        if (up.Length == 0 || down.Length == 0)
            return null;

        var related = stems
            .SelectMany(x => x.AttachedNoteKeys)
            .Distinct()
            .Where(notesByKey.ContainsKey)
            .Select(x => notesByKey[x])
            .Where(x => x.Staff == note.Staff)
            .Select(x => new { Note = x, Rank = PitchRank(x.Pitch) })
            .Where(x => x.Rank.HasValue)
            .ToArray();

        var noteRank = PitchRank(note.Pitch);
        if (!noteRank.HasValue || related.Length < 2)
            return null;

        var min = related.Min(x => x.Rank!.Value);
        var max = related.Max(x => x.Rank!.Value);
        if (min == max)
            return null;

        // When the low-level detector says the same close note cluster touches both an up- and a
        // down-stem, X is almost identical and cannot disambiguate voices. Musical convention gives
        // the upper note the up-stem and the lower note the down-stem.
        if (noteRank.Value == max)
            return up.OrderBy(x => Distance(x.LogicalX, note.LogicalX)).First();
        if (noteRank.Value == min)
            return down.OrderBy(x => Distance(x.LogicalX, note.LogicalX)).First();

        return null;
    }

    private static int? PitchRank(string pitch)
    {
        if (string.IsNullOrWhiteSpace(pitch))
            return null;

        var step = char.ToUpperInvariant(pitch[0]);
        var stepRank = step switch
        {
            'C' => 0, 'D' => 1, 'E' => 2, 'F' => 3, 'G' => 4, 'A' => 5, 'B' => 6,
            _ => -1
        };
        if (stepRank < 0)
            return null;

        var octaveText = new string(pitch.Skip(1).Where(c => char.IsDigit(c) || c == '-').ToArray());
        return int.TryParse(octaveText, out var octave) ? octave * 7 + stepRank : null;
    }

    private static double Distance(double? stemX, double? noteX) =>
        stemX.HasValue && noteX.HasValue ? Math.Abs(stemX.Value - noteX.Value) : double.MaxValue;
}
