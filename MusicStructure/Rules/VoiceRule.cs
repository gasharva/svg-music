namespace MusicStructure;

/// <summary>
/// Initial voice heuristic. Opposite stem directions imply separate voices only when their
/// horizontal ranges overlap. Sequential material remains one voice even if stem direction flips.
/// </summary>
public sealed class VoiceRule : IMusicNoteRule
{
    private const double XOverlapTolerance = 2.0;

    private readonly IReadOnlyDictionary<string, int> _voiceByNote;

    public VoiceRule(MusicMeasureInput input)
    {
        var stems = StemAssignmentMap.Build(input);
        var result = new Dictionary<string, int>();

        foreach (var staffGroup in input.Notes.GroupBy(x => x.Staff))
        {
            var withStem = staffGroup
                .Where(x => x.LogicalX.HasValue && stems.ContainsKey(x.Key))
                .Select(x => new { Note = x, Stem = stems[x.Key] })
                .ToArray();

            var up = withStem.Where(x => x.Stem.Direction == MusicStemDirection.Up).ToArray();
            var down = withStem.Where(x => x.Stem.Direction == MusicStemDirection.Down).ToArray();
            var splitVoices = up.Length > 0 && down.Length > 0 && RangesOverlap(up, down);

            foreach (var item in withStem)
                result[item.Note.Key] = splitVoices && item.Stem.Direction == MusicStemDirection.Down ? 2 : 1;
        }

        _voiceByNote = result;
    }

    public MusicNoteDraft Apply(MusicNoteDraft note) =>
        _voiceByNote.TryGetValue(note.Source.Key, out var voice)
            ? note with { Voice = voice }
            : note;

    private static bool RangesOverlap(
        IReadOnlyList<dynamic> up,
        IReadOnlyList<dynamic> down)
    {
        var upMin = up.Min(x => (double)x.Note.LogicalX.Value);
        var upMax = up.Max(x => (double)x.Note.LogicalX.Value);
        var downMin = down.Min(x => (double)x.Note.LogicalX.Value);
        var downMax = down.Max(x => (double)x.Note.LogicalX.Value);
        return upMin <= downMax + XOverlapTolerance && downMin <= upMax + XOverlapTolerance;
    }
}
