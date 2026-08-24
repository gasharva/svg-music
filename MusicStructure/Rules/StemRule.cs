namespace MusicStructure;

public sealed class StemRule : IMusicNoteRule
{
    private readonly IReadOnlyDictionary<string, RecognizedStemInput> _stemsByNote;

    public StemRule(MusicMeasureInput input)
    {
        _stemsByNote = StemAssignmentMap.Build(input);
    }

    public MusicNoteDraft Apply(MusicNoteDraft note)
    {
        return _stemsByNote.TryGetValue(note.Source.Key, out var stem)
            ? note with { Stem = stem.Direction, StemKey = stem.Key }
            : note;
    }
}
