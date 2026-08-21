namespace MusicStructure;

public interface IMusicNoteRule
{
    MusicNoteDraft Apply(MusicNoteDraft note);
}
