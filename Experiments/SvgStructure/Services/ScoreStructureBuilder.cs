using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed class ScoreStructureBuilder
{
    public ScoreStructure Build(IReadOnlyList<StaffSystem> systems)
    {
        if (systems.Count == 0)
            throw new InvalidOperationException("No staff systems were detected.");

        var staffCounts = systems.Select(x => x.StaffCount).Distinct().ToList();
        if (staffCounts.Count != 1)
            throw new InvalidOperationException(
                $"Systems have inconsistent staff counts: {string.Join(", ", staffCounts)}.");

        var measures = new List<MeasureStructure>();
        var number = 1;

        foreach (var system in systems)
        {
            for (var i = 0; i < system.BarXs.Count - 1; i++)
            {
                measures.Add(new MeasureStructure(
                    number++,
                    system.BarXs[i + 1] - system.BarXs[i]));
            }
        }

        var parts = Enumerable.Range(1, staffCounts[0])
            .Select(partNumber => new PartStructure(
                $"P{partNumber}",
                measures.Select(x => x with { }).ToList()))
            .ToList();

        return new ScoreStructure(parts);
    }
}
