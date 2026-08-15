using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Completes the logical coordinate system after meters are known.
/// Meter changes propagate independently inside each part until another meter overrides them.
/// </summary>
public sealed class LogicalGridResolver
{
    private readonly int _subdivisionsPerBeat;

    public LogicalGridResolver(int subdivisionsPerBeat = 8)
    {
        if (subdivisionsPerBeat <= 0)
            throw new ArgumentOutOfRangeException(nameof(subdivisionsPerBeat));
        _subdivisionsPerBeat = subdivisionsPerBeat;
    }

    public LogicalGridResolution Resolve(
        PartMeasureResolution structure,
        IReadOnlyList<MeterResolution> meters)
    {
        var meterByBlock = meters
            .GroupBy(x => (x.PartNumber, x.MeasureNumber))
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(m => m.Confidence).First());

        var result = new List<LogicalGridBlock>();

        foreach (var part in structure.Parts.OrderBy(x => x.Number))
        {
            int? currentBeatNumber = null;
            int? currentBeatValue = null;

            foreach (var block in structure.Map.Blocks
                         .Where(x => x.PartNumber == part.Number)
                         .OrderBy(x => x.MeasureNumber))
            {
                if (meterByBlock.TryGetValue((part.Number, block.MeasureNumber), out var meter))
                {
                    currentBeatNumber = meter.BeatNumber;
                    currentBeatValue = meter.BeatValue;
                }

                result.Add(new LogicalGridBlock(
                    block.PartNumber,
                    block.MeasureNumber,
                    currentBeatNumber,
                    currentBeatValue,
                    _subdivisionsPerBeat,
                    block.PhysicalBounds));
            }
        }

        return new LogicalGridResolution(result);
    }
}
