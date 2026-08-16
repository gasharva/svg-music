using System.Globalization;
using System.Numerics;
using System.Text;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Step-2 diagnostic export. Writes every P+M primitive as a standalone SVG.
/// Contours originating from the same SVG &lt;use&gt; are exported once as one complete glyph.
/// </summary>
public sealed class PrimitiveSvgExporter
{
    public const string DirectoryName = "primitives";
    public const string GalleryFileName = "primitives.html";

    public PrimitiveExportResult Export(PrimitiveResolution resolution, string itemDirectory)
    {
        var outputDirectory = Path.Combine(itemDirectory, DirectoryName);
        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);

        var exports = BuildExports(resolution.PartMeasurePrimitives);
        var counters = new Dictionary<(int Part, int Measure), int>();
        var items = new List<PrimitiveExportItem>();

        foreach (var export in exports
                     .OrderBy(x => x.PartNumber)
                     .ThenBy(x => x.MeasureNumber)
                     .ThenBy(x => x.Bounds.Left)
                     .ThenBy(x => x.Bounds.Top))
        {
            var key = (export.PartNumber, export.MeasureNumber);
            counters.TryGetValue(key, out var index);
            index++;
            counters[key] = index;

            var fileName = $"part{export.PartNumber}-measure{export.MeasureNumber}-{index}.svg";
            WriteSvg(Path.Combine(outputDirectory, fileName), export.Contours);
            items.Add(new PrimitiveExportItem(
                fileName,
                export.PartNumber,
                export.MeasureNumber,
                index,
                export.SourceUseKey is not null,
                export.Contours.Count,
                export.Bounds));
        }

        var galleryPath = Path.Combine(itemDirectory, GalleryFileName);
        WriteGallery(galleryPath, items);
        return new PrimitiveExportResult(outputDirectory, galleryPath, items);
    }

    private static IReadOnlyList<ExportCandidate> BuildExports(
        IReadOnlyList<ResolvedPrimitive> primitives)
    {
        var result = new List<ExportCandidate>();
        var emittedUses = new HashSet<string>(StringComparer.Ordinal);

        foreach (var primitive in primitives
                     .Where(x => x.PartNumber is not null && x.MeasureNumber is not null)
                     .OrderBy(x => x.Id))
        {
            if (!string.IsNullOrWhiteSpace(primitive.SourceUseKey))
            {
                if (!emittedUses.Add(primitive.SourceUseKey!))
                    continue;

                var contours = primitive.SourceUseContours is { Count: > 0 }
                    ? primitive.SourceUseContours
                    : new[] { primitive.Contour };
                result.Add(new ExportCandidate(
                    primitive.PartNumber!.Value,
                    primitive.MeasureNumber!.Value,
                    primitive.SourceUseKey,
                    contours,
                    Bounds(contours)));
                continue;
            }

            result.Add(new ExportCandidate(
                primitive.PartNumber!.Value,
                primitive.MeasureNumber!.Value,
                null,
                new[] { primitive.Contour },
                primitive.PhysicalBounds));
        }

        return result;
    }

    private static void WriteSvg(string path, IReadOnlyList<PrimitiveContour> contours)
    {
        var bounds = Bounds(contours);
        var extent = Math.Max(bounds.Width, bounds.Height);
        var pad = Math.Max(extent * 0.08, 0.5);
        var width = Math.Max(bounds.Width + 2 * pad, 1);
        var height = Math.Max(bounds.Height + 2 * pad, 1);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(bounds.Left - pad)} {F(bounds.Top - pad)} {F(width)} {F(height)}\">");
        sb.Append("  <path fill=\"black\" fill-rule=\"evenodd\" d=\"");

        foreach (var contour in contours.Where(x => x.Points.Count >= 2))
        {
            var points = contour.Points;
            sb.Append($"M {F(points[0].X)} {F(points[0].Y)} ");
            foreach (var point in points.Skip(1))
                sb.Append($"L {F(point.X)} {F(point.Y)} ");
            sb.Append("Z ");
        }

        sb.AppendLine("\"/>");
        sb.AppendLine("</svg>");
        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteGallery(string path, IReadOnlyList<PrimitiveExportItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"><title>Step 2 primitives</title>");
        sb.AppendLine("<style>body{font-family:system-ui,Arial,sans-serif;margin:24px;background:#fafafa;color:#222}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:14px}.card{background:white;border:1px solid #ddd;border-radius:8px;padding:10px}.shape{height:130px;display:flex;align-items:center;justify-content:center;background:#f6f6f6}.shape img{max-width:100%;max-height:120px}.name{font:12px ui-monospace,Consolas,monospace;margin-top:8px;word-break:break-all}.meta{font-size:12px;color:#666;margin-top:4px}</style></head><body>");
        sb.AppendLine($"<h1>Step 2 primitives</h1><p>{items.Count} exported P+M primitives. SVG &lt;use&gt; instances are emitted once with all contours.</p><div class=\"grid\">");

        foreach (var item in items)
        {
            var src = $"{DirectoryName}/{item.FileName}";
            sb.AppendLine("<div class=\"card\">");
            sb.AppendLine($"<a class=\"shape\" href=\"{src}\"><img src=\"{src}\" loading=\"lazy\"></a>");
            sb.AppendLine($"<div class=\"name\">{item.FileName}</div>");
            sb.AppendLine($"<div class=\"meta\">P{item.PartNumber}-M{item.MeasureNumber} · contours={item.ContourCount}{(item.IsUse ? " · &lt;use&gt;" : "")}</div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></body></html>");
        File.WriteAllText(path, sb.ToString());
    }

    private static RectD Bounds(IReadOnlyList<PrimitiveContour> contours)
    {
        var points = contours.SelectMany(x => x.Points).ToArray();
        if (points.Length == 0)
            return new RectD(0, 0, 1, 1);
        return new RectD(
            points.Min(x => x.X),
            points.Min(x => x.Y),
            points.Max(x => x.X),
            points.Max(x => x.Y));
    }

    private static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed record ExportCandidate(
        int PartNumber,
        int MeasureNumber,
        string? SourceUseKey,
        IReadOnlyList<PrimitiveContour> Contours,
        RectD Bounds);
}

public sealed record PrimitiveExportItem(
    string FileName,
    int PartNumber,
    int MeasureNumber,
    int Index,
    bool IsUse,
    int ContourCount,
    RectD Bounds);

public sealed record PrimitiveExportResult(
    string OutputDirectory,
    string GalleryPath,
    IReadOnlyList<PrimitiveExportItem> Items);
