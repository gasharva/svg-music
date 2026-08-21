namespace MusicStructure;

public sealed class DurationRule : IMusicNoteRule
{
    private readonly IReadOnlyDictionary<string, RecognizedFlagInput> _flagsByStem;

    public DurationRule(MusicMeasureInput input)
    {
        _flagsByStem = input.Flags
            .GroupBy(x => x.StemKey)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Denominator).First());
    }

    public MusicNoteDraft Apply(MusicNoteDraft note)
    {
        int? denominator = null;

        if (note.StemKey is not null && _flagsByStem.TryGetValue(note.StemKey, out var flag))
            denominator = flag.Denominator;
        else if (note.Beams is { Count: > 0 })
            denominator = 4 * (1 << note.Beams.Max(x => x.Level));

        var type = denominator is not null
            ? TypeName(denominator.Value)
            : note.Stem is null
                ? note.Source.IsFilled ? "quarter" : "whole"
                : note.Source.IsFilled ? "quarter" : "half";

        return note with { Type = type };
    }

    private static string TypeName(int denominator) => denominator switch
    {
        1 => "whole",
        2 => "half",
        4 => "quarter",
        8 => "eighth",
        16 => "16th",
        32 => "32nd",
        64 => "64th",
        _ => $"1/{denominator}"
    };
}
