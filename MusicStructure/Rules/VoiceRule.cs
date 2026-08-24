namespace MusicStructure;

/// <summary>
/// Initial voice heuristic. Opposite stem directions imply separate voices only when their
/// horizontal ranges overlap. Sequential material remains one voice even if stem direction flips.
/// </summary>
public sealed class VoiceRule : IMusicNoteRule
{
    private const double XOverlapTolerance = 2.0;

    private readonly IReadOnlyDictionary<string, int> _voiceByNote;
    private readonly IReadOnlyDictionary<int, bool> _splitByStaff;

    public VoiceRule(MusicMeasureInput input)
    {
        var stems = StemAssignmentMap.Build(input);
        var result = new Dictionary<string, int>();
        var splitByStaff = new Dictionary<int, bool>();

        foreach (var staffGroup in input.Notes.GroupBy(x => x.Staff))
        {
            var withStem = staffGroup
                .Where(x => x.LogicalX.HasValue && stems.ContainsKey(x.Key))
                .Select(x => (Note: x, Stem: stems[x.Key]))
                .ToArray();

            var up = withStem.Where(x => x.Stem.Direction == MusicStemDirection.Up).ToArray();
            var down = withStem.Where(x => x.Stem.Direction == MusicStemDirection.Down).ToArray();
            var splitVoices = up.Length > 0 && down.Length > 0 && RangesOverlap(up, down);
            splitByStaff[staffGroup.Key] = splitVoices;

            foreach (var item in withStem)
                result[item.Note.Key] = splitVoices && item.Stem.Direction == MusicStemDirection.Down ? 2 : 1;
        }

        _voiceByNote = result;
        _splitByStaff = splitByStaff;
    }

    public MusicNoteDraft Apply(MusicNoteDraft note)
    {
        if (_voiceByNote.TryGetValue(note.Source.Key, out var voice))
            return note with { Voice = voice };

        // ChordRule may have inherited a shared stem for a head that had no direct stem assignment.
        // Preserve the same staff-level voice decision for that inherited stem instead of leaving
        // the chord tone voice-less (which would make the MusicXML writer attach it to the wrong chord).
        if (note.Stem is not null)
        {
            var split = _splitByStaff.TryGetValue(note.Source.Staff, out var value) && value;
            var inheritedVoice = split && note.Stem == MusicStemDirection.Down ? 2 : 1;
            return note with { Voice = inheritedVoice };
        }

        return note;
    }

    private static bool RangesOverlap(
        IReadOnlyList<(RecognizedNoteInput Note, RecognizedStemInput Stem)> up,
        IReadOnlyList<(RecognizedNoteInput Note, RecognizedStemInput Stem)> down)
    {
        var upMin = up.Min(x => x.Note.LogicalX!.Value);
        var upMax = up.Max(x => x.Note.LogicalX!.Value);
        var downMin = down.Min(x => x.Note.LogicalX!.Value);
        var downMax = down.Max(x => x.Note.LogicalX!.Value);
        return upMin <= downMax + XOverlapTolerance && downMin <= upMax + XOverlapTolerance;
    }
}
