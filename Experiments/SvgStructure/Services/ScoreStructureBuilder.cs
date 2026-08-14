using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed class ScoreStructureBuilder
{
    public ScoreStructure Build(IReadOnlyList<StaffSystem> systems)
    {
        if (systems.Count == 0)
            throw new InvalidOperationException("No staff systems were detected.");

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

        // For this experiment each system has two five-line staves, matching the two
        // source parts. We keep that one tiny assumption isolated here.
        return new ScoreStructure(new[]
        {
            new PartStructure("P1", measures),
            new PartStructure("P2", measures.Select(x => x with { }).ToList())
        });
    }
}
