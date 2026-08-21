namespace MusicStructure;

/// <summary>
/// Initial voice heuristic: stem-up notes are voice 1, stem-down notes are voice 2.
/// Stemless notes remain unassigned until a smarter voice rule is introduced.
/// </summary>
public sealed class VoiceRule : IMusicNoteRule
{
    public VoiceRule(MusicMeasureInput input)
    {
        _ = input;
    }

    public MusicNoteDraft Apply(MusicNoteDraft note) => note with
    {
        Voice = note.Stem switch
        {
            MusicStemDirection.Up => 1,
            MusicStemDirection.Down => 2,
            _ => null
        }
    };
}
