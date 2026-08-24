namespace MusicStructure;

public sealed class ChordRule : IMusicNoteRule
{
    private const double MaxLogicalXDistance = 2.0;

    private readonly IReadOnlyDictionary<string, ChordInfo> _chords;

    public ChordRule(MusicMeasureInput input)
    {
        var stemByNote = StemAssignmentMap.Build(input);
        var result = new Dictionary<string, ChordInfo>();
        var order = input.Notes.Select((note, index) => (note.Key, index)).ToDictionary(x => x.Key, x => x.index);
        var chordIndex = 0;

        foreach (var bucket in input.Notes
                     .Where(x => x.LogicalX.HasValue)
                     .GroupBy(x => (x.Staff, x.IsFilled)))
        {
            var remaining = bucket.OrderBy(x => x.LogicalX).ToList();
            while (remaining.Count > 0)
            {
                var seed = remaining[0];
                remaining.RemoveAt(0);
                var group = new List<RecognizedNoteInput> { seed };

                foreach (var candidate in remaining.ToArray())
                {
                    if (group.All(existing =>
                            Math.Abs(candidate.LogicalX!.Value - existing.LogicalX!.Value) <= MaxLogicalXDistance &&
                            StemsCompatible(existing.Key, candidate.Key, stemByNote)))
                    {
                        group.Add(candidate);
                        remaining.Remove(candidate);
                    }
                }

                if (group.Count < 2)
                    continue;

                var stems = group
                    .Where(x => stemByNote.ContainsKey(x.Key))
                    .Select(x => stemByNote[x.Key])
                    .DistinctBy(x => x.Key)
                    .ToArray();

                // A chord may have one shared stem or no stem. If two different stems are present,
                // these horizontally close heads are different voices, not one chord.
                if (stems.Length > 1)
                    continue;

                var inheritedStem = stems.SingleOrDefault();
                var sharedDotCount = group.Max(x => x.DotCount);
                var ordered = group.OrderBy(x => order[x.Key]).ToArray();
                var chordKey = $"m{input.Number}:s{bucket.Key.Staff}:chord:{++chordIndex}";
                for (var i = 0; i < ordered.Length; i++)
                    result[ordered[i].Key] = new ChordInfo(chordKey, i > 0, inheritedStem, sharedDotCount);
            }
        }

        _chords = result;
    }

    public MusicNoteDraft Apply(MusicNoteDraft note)
    {
        if (!_chords.TryGetValue(note.Source.Key, out var chord))
            return note;

        var updated = note with
        {
            IsChordTone = chord.IsChordTone,
            ChordGroupKey = chord.GroupKey,
            DotCount = Math.Max(note.DotCount, chord.SharedDotCount)
        };
        if (updated.Stem is null && chord.SharedStem is not null)
            updated = updated with { Stem = chord.SharedStem.Direction, StemKey = chord.SharedStem.Key };
        return updated;
    }

    private static bool StemsCompatible(
        string leftKey,
        string rightKey,
        IReadOnlyDictionary<string, RecognizedStemInput> stemsByNote)
    {
        var hasLeft = stemsByNote.TryGetValue(leftKey, out var left);
        var hasRight = stemsByNote.TryGetValue(rightKey, out var right);
        if (!hasLeft || !hasRight)
            return true;
        return left!.Key == right!.Key;
    }

    private sealed record ChordInfo(
        string GroupKey,
        bool IsChordTone,
        RecognizedStemInput? SharedStem,
        int SharedDotCount);
}
