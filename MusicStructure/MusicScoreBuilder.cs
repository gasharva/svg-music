namespace MusicStructure;

public sealed class MusicScoreBuilder
{
    private readonly MeasureBuilder _measureBuilder;

    public MusicScoreBuilder()
        : this(new MeasureBuilder())
    {
    }

    internal MusicScoreBuilder(MeasureBuilder measureBuilder)
    {
        _measureBuilder = measureBuilder;
    }

    public MusicScore Build(MusicStructureInput input)
    {
        var measureNumbers = input.MeasureNumbers
            .Concat(input.Notes.Select(x => x.Measure))
            .Concat(input.Stems.Select(x => x.Measure))
            .Concat(input.Beams.Select(x => x.Measure))
            .Concat(input.Flags.Select(x => x.Measure))
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var measures = measureNumbers
            .Select(number => _measureBuilder.Build(new MusicMeasureInput(
                number,
                input.SystemStartMeasures.Contains(number),
                input.StaffCount,
                input.Notes.Where(x => x.Measure == number).ToArray(),
                input.Stems.Where(x => x.Measure == number).ToArray(),
                input.Beams.Where(x => x.Measure == number).ToArray(),
                input.Flags.Where(x => x.Measure == number).ToArray())))
            .ToArray();

        return new MusicScore(input.StaffCount, measures);
    }
}
