namespace MusicStructure;

public sealed class StemRule : IMusicNoteRule
{
    private readonly IReadOnlyDictionary<string, RecognizedStemInput> _stemsByNote;

    public StemRule(MusicMeasureInput input)
    {
        _stemsByNote = input.Stems
            .SelectMany(stem => stem.AttachedNoteKeys.Select(noteKey => (noteKey, stem)))
            .GroupBy(x => x.noteKey)
            .ToDictionary(g => g.Key, g => g.Select(x => x.stem).First());
    }

    public MusicNoteDraft Apply(MusicNoteDraft note)
    {
        return _stemsByNote.TryGetValue(note.Source.Key, out var stem)
            ? note with { Stem = stem.Direction, StemKey = stem.Key }
            : note;
    }
}
