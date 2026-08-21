namespace MusicStructure;

public sealed class ChordRule : IMusicNoteRule
{
    private const double MaxLogicalXDistance = 2.0;

    private readonly IReadOnlyDictionary<string, ChordInfo> _chords;

    public ChordRule(MusicMeasureInput input)
    {
        var directStemByNote = input.Stems
            .SelectMany(stem => stem.AttachedNoteKeys.Select(noteKey => (noteKey, stem)))
            .GroupBy(x => x.noteKey)
            .ToDictionary(g => g.Key, g => g.Select(x => x.stem).ToArray());

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
                            StemsCompatible(existing.Key, candidate.Key, directStemByNote)))
                    {
                        group.Add(candidate);
                        remaining.Remove(candidate);
                    }
                }

                if (group.Count < 2)
                    continue;

                var stems = group
                    .SelectMany(x => directStemByNote.TryGetValue(x.Key, out var s) ? s : Array.Empty<RecognizedStemInput>())
                    .DistinctBy(x => x.Key)
                    .ToArray();

                var inheritedStem = stems.Length == 1 ? stems[0] : null;
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
        IReadOnlyDictionary<string, RecognizedStemInput[]> stemsByNote)
    {
        var left = stemsByNote.TryGetValue(leftKey, out var ls) ? ls : Array.Empty<RecognizedStemInput>();
        var right = stemsByNote.TryGetValue(rightKey, out var rs) ? rs : Array.Empty<RecognizedStemInput>();
        if (left.Length == 0 || right.Length == 0)
            return true;
        return left.Any(l => right.Any(r => l.Key == r.Key));
    }

    private sealed record ChordInfo(
        string GroupKey,
        bool IsChordTone,
        RecognizedStemInput? SharedStem,
        int SharedDotCount);
}
