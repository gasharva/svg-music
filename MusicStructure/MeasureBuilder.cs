namespace MusicStructure;

public sealed class MeasureBuilder
{
    private readonly MusicNoteRuleFactory _ruleFactory;

    public MeasureBuilder()
        : this(new MusicNoteRuleFactory())
    {
    }

    internal MeasureBuilder(MusicNoteRuleFactory ruleFactory)
    {
        _ruleFactory = ruleFactory;
    }

    public MusicMeasure Build(MusicMeasureInput input)
    {
        var rules = _ruleFactory.Create(input);
        var notes = new List<MusicNote>();

        foreach (var staff in Enumerable.Range(1, Math.Max(1, input.StaffCount)))
        {
            foreach (var head in input.Notes
                         .Where(x => x.Staff == staff)
                         .OrderBy(x => x.LogicalX ?? double.MaxValue))
            {
                var draft = MusicNoteDraft.From(head);
                foreach (var rule in rules)
                    draft = rule.Apply(draft);

                notes.Add(draft.ToMusicNote());
            }
        }

        return new MusicMeasure(
            input.Number,
            input.StartsNewSystem,
            notes.OrderBy(x => x.LogicalX ?? double.MaxValue)
                .ThenBy(x => x.Staff)
                .ThenBy(x => x.Pitch.Octave)
                .ThenBy(x => x.Pitch.Step)
                .ToArray());
    }
}
