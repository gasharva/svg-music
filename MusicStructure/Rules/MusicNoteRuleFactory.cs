namespace MusicStructure;

public sealed class MusicNoteRuleFactory
{
    public IReadOnlyList<IMusicNoteRule> Create(MusicMeasureInput input) => new IMusicNoteRule[]
    {
        new PitchRule(input),
        new StemRule(input),
        new ChordRule(input),
        new VoiceRule(input),
        new BeamRule(input),
        new DurationRule(input)
    };
}
