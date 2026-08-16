using System.Globalization;
using System.Net;
using System.Text;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic gallery for MusicSymbolResolver candidates.</summary>
public sealed class MusicSymbolSvgExporter
{
    public const string DirectoryName = "music-symbols";
    public const string GalleryFileName = "music-symbols.html";

    public MusicSymbolExportResult Export(MusicSymbolResolution resolution, string itemDirectory)
    {
        var outputDirectory = Path.Combine(itemDirectory, DirectoryName);
        if (Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        Directory.CreateDirectory(outputDirectory);

        var counters = new Dictionary<(int? Part, int Measure), int>();
        var items = new List<MusicSymbolExportItem>();

        foreach (var candidate in resolution.Candidates)
        {
            var key = (candidate.PartNumber, candidate.MeasureNumber);
            counters.TryGetValue(key, out var index);
            index++;
            counters[key] = index;

            var prefix = candidate.PartNumber is null
                ? $"measure{candidate.MeasureNumber}"
                : $"part{candidate.PartNumber}-measure{candidate.MeasureNumber}";
            var fileName = $"{prefix}-{index}.svg";
            WriteSvg(Path.Combine(outputDirectory, fileName), candidate);
            items.Add(new MusicSymbolExportItem(fileName, candidate, index));
        }

        var galleryPath = Path.Combine(itemDirectory, GalleryFileName);
        WriteGallery(galleryPath, items);
        return new MusicSymbolExportResult(outputDirectory, galleryPath, items);
    }

    private static void WriteSvg(string path, MusicSymbolCandidate candidate)
    {
        // Include both the PrimitiveResolver scaffold and the transformed smooth geometry. The
        // scaffold determines the viewport, while a little extra padding makes transform mistakes
        // visible instead of silently clipping them at the candidate edge.
        var b = candidate.PhysicalBounds;
        var extent = Math.Max(b.Width, b.Height);
        var pad = Math.Max(extent * 0.35, 2.0);
        var width = Math.Max(b.Width + 2 * pad, 1);
        var height = Math.Max(b.Height + 2 * pad, 1);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(b.Left - pad)} {F(b.Top - pad)} {F(width)} {F(height)}\">");

        // PrimitiveResolver evidence: never used as recognition geometry, only shown for alignment.
        foreach (var box in candidate.PrimitiveBounds)
        {
            sb.AppendLine($"  <rect x=\"{F(box.Left)}\" y=\"{F(box.Top)}\" width=\"{F(box.Width)}\" height=\"{F(box.Height)}\" fill=\"none\" stroke=\"#999\" stroke-width=\"0.45\" stroke-dasharray=\"1.2 0.8\"/>");
        }

        foreach (var smooth in candidate.SmoothPaths)
        {
            var transform = string.IsNullOrWhiteSpace(smooth.Transform)
                ? string.Empty
                : $" transform=\"{H(smooth.Transform)}\"";
            sb.AppendLine($"  <path fill=\"black\" fill-rule=\"evenodd\"{transform} d=\"{H(smooth.PathData)}\"/>");
        }
        sb.AppendLine("</svg>");
        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteGallery(string path, IReadOnlyList<MusicSymbolExportItem> items)
    {
        var resolved = items.Where(x => x.Candidate.SmoothPaths.Count > 0).ToArray();
        var unresolved = items.Where(x => x.Candidate.SmoothPaths.Count == 0).ToArray();

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Music symbol candidates</title>");
        sb.AppendLine("<style>body{font-family:system-ui,Arial,sans-serif;margin:24px;background:#fafafa;color:#222}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:14px}.card{background:#fff;border:1px solid #ddd;border-radius:8px;padding:10px}.shape{height:150px;display:flex;align-items:center;justify-content:center;background:#f6f6f6}.shape img{max-width:100%;max-height:140px}.name{font:12px ui-monospace,Consolas,monospace;margin-top:8px}.meta{font-size:12px;color:#666;margin-top:4px}.src{font:10px ui-monospace,Consolas,monospace;color:#888;margin-top:5px;word-break:break-all}.bad{color:#a00}h2{margin-top:36px}</style></head><body>");
        sb.AppendLine($"<h1>MusicSymbolResolver</h1><p>{items.Count} candidates: {resolved.Length} with retained smooth geometry, {unresolved.Length} unresolved. Gray dashed boxes are the PrimitiveResolver artifacts used only for spatial grouping; black paths are original Beziers positioned by Svg.Skia's retained-scene TotalTransform.</p>");
        sb.AppendLine("<h2>Resolved smooth geometry</h2><div class=\"grid\">");
        WriteCards(sb, resolved);
        sb.AppendLine("</div>");

        if (unresolved.Length > 0)
        {
            sb.AppendLine($"<h2>Unresolved ({unresolved.Length})</h2><p class=\"bad\">Kept for diagnostics, but excluded from the main visual result.</p><div class=\"grid\">");
            WriteCards(sb, unresolved);
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteCards(StringBuilder sb, IReadOnlyList<MusicSymbolExportItem> items)
    {
        foreach (var item in items)
        {
            var c = item.Candidate;
            var src = $"{DirectoryName}/{item.FileName}";
            sb.AppendLine("<div class=\"card\">");
            sb.AppendLine($"<a class=\"shape\" href=\"{src}\"><img src=\"{src}\" loading=\"lazy\"></a>");
            sb.AppendLine($"<div class=\"name\">{H(item.FileName)}</div>");
            sb.AppendLine($"<div class=\"meta\">{H(c.LogicalLabel)} · primitives={c.PrimitiveIds.Count} · smooth paths={c.SmoothPaths.Count}</div>");
            if (c.SmoothPaths.Count == 0)
                sb.AppendLine("<div class=\"meta bad\">No retained smooth path resolved</div>");
            sb.AppendLine($"<div class=\"src\">primitive ids: {string.Join(",", c.PrimitiveIds)}<br>{string.Join("<br>", c.Sources.Select(x => H(x.ElementAddress ?? x.Anchor)))}</div>");
            sb.AppendLine("</div>");
        }
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
    private static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

public sealed record MusicSymbolExportItem(
    string FileName,
    MusicSymbolCandidate Candidate,
    int Index);

public sealed record MusicSymbolExportResult(
    string OutputDirectory,
    string GalleryPath,
    IReadOnlyList<MusicSymbolExportItem> Items);
