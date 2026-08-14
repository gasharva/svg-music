using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed record PrimitiveAssignment(
    int SystemIndex,
    int PartIndex,
    int MeasureIndex,
    int MeasureNumber);

public sealed class PrimitiveClassifier
{
    public PrimitiveAssignment? Classify(
        double x,
        double y,
        IReadOnlyList<StaffSystem> systems)
    {
        for (var systemIndex = 0; systemIndex < systems.Count; systemIndex++)
        {
            var system = systems[systemIndex];
            if (!IsInsideSystemArea(x, y, system))
                continue;

            var measureIndex = FindMeasure(system, x);
            if (measureIndex < 0)
                return null;

            var partIndex = FindNearestStaff(system, y);
            var measureNumber = 1
                + systems.Take(systemIndex).Sum(s => s.BarXs.Count - 1)
                + measureIndex;

            return new PrimitiveAssignment(
                systemIndex,
                partIndex,
                measureIndex,
                measureNumber);
        }

        return null;
    }

    private static bool IsInsideSystemArea(double x, double y, StaffSystem system)
    {
        if (x < system.Left - 3 || x > system.Right + 3)
            return false;

        var staffHeight = system.Staffs
            .Select(s => s.Bottom - s.Top)
            .DefaultIfEmpty(system.Bottom - system.Top)
            .Average();

        // Expressions, slurs, tuplets and stems legitimately extend outside the five staff
        // lines. Keep a generous but finite vertical halo so page titles do not become notes.
        var verticalHalo = Math.Max(12, staffHeight * 2.2);
        return y >= system.Top - verticalHalo && y <= system.Bottom + verticalHalo;
    }

    private static int FindMeasure(StaffSystem system, double x)
    {
        for (var i = 0; i < system.BarXs.Count - 1; i++)
        {
            var left = system.BarXs[i];
            var right = system.BarXs[i + 1];
            var isLast = i == system.BarXs.Count - 2;

            if (x >= left && (x < right || isLast && x <= right))
                return i;
        }

        return -1;
    }

    private static int FindNearestStaff(StaffSystem system, double y) =>
        system.Staffs
            .OrderBy(s => DistanceToBand(y, s))
            .ThenBy(s => s.PartIndex)
            .First()
            .PartIndex;

    private static double DistanceToBand(double y, StaffBand staff)
    {
        if (y < staff.Top)
            return staff.Top - y;
        if (y > staff.Bottom)
            return y - staff.Bottom;
        return 0;
    }
}
