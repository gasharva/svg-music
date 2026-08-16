using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Pipeline step 1. Resolves the page into linear logical parts and measures and
/// creates the map between logical Pn-Mm blocks and physical SVG coordinates.
/// </summary>
public sealed class PartMeasureResolver
{
    private readonly SvgSceneGeometryReader _geometryReader = new();
    private readonly StaffSystemDetector _systemDetector = new();

    public PartMeasureResolution Resolve(string svgPath)
    {
        svgPath = Path.GetFullPath(svgPath);

        // Resolve the physical page first. Staff detection must be scale-independent: SVG exporters
        // are free to choose completely different coordinate systems for the same printed page.
        using var svg = SKSvg.CreateFromFile(svgPath);
        var picture = svg.Picture
            ?? throw new InvalidOperationException("Svg.Skia did not produce a renderable picture.");
        var page = picture.CullRect;
        var pageBounds = new RectD(page.Left, page.Top, page.Right, page.Bottom);

        var lines = _geometryReader.ReadLines(svgPath);
        var systems = _systemDetector.Detect(lines, pageBounds);
        if (systems.Count == 0)
            throw new InvalidOperationException("No staff systems were detected.");

        var staffCounts = systems.Select(x => x.StaffCount).Distinct().ToArray();
        if (staffCounts.Length != 1)
            throw new InvalidOperationException(
                $"Systems have inconsistent staff counts: {string.Join(", ", staffCounts)}.");

        var partCount = staffCounts[0];
        var parts = Enumerable.Range(1, partCount)
            .Select(number => new Part(number, $"P{number}"))
            .ToArray();

        var measures = new List<Measure>();
        var blocks = new List<PartMeasureBlock>();
        var measureNumber = 1;

        for (var systemIndex = 0; systemIndex < systems.Count; systemIndex++)
        {
            var system = systems[systemIndex];
            for (var measureIndex = 0; measureIndex < system.BarXs.Count - 1; measureIndex++, measureNumber++)
            {
                var left = system.BarXs[measureIndex];
                var right = system.BarXs[measureIndex + 1];
                measures.Add(new Measure(measureNumber, right - left));

                foreach (var staff in system.Staffs)
                {
                    blocks.Add(new PartMeasureBlock(
                        staff.PartIndex + 1,
                        measureNumber,
                        systemIndex,
                        new RectD(left, staff.Top, right, staff.Bottom)));
                }
            }
        }

        return new PartMeasureResolution(
            svgPath,
            parts,
            measures,
            new PartMeasureMap(blocks, pageBounds),
            lines.Count,
            systems.Count);
    }
}
