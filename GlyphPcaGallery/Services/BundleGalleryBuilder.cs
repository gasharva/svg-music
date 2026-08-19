using System.Net;
using System.Text;
using GlyphPcaGallery.Models;

namespace GlyphPcaGallery.Services;

public static class BundleGalleryBuilder
{
    public static void Build(
        string outputDirectory,
        string bundlePath,
        GlyphModelBundle bundle,
        IReadOnlyDictionary<string, BundleModelRunSummary> summaries)
    {
        Directory.CreateDirectory(outputDirectory);

        var html = new StringBuilder();
        html.Append("""
<!doctype html><html><head><meta charset="utf-8"><title>Glyph PCA model bundle</title>
<style>
:root{font-family:system-ui,sans-serif;color:#222;background:#f5f5f5}body{margin:24px;max-width:1200px}.card{background:#fff;border:1px solid #ddd;border-radius:12px;padding:16px;margin:12px 0}.family{font-size:22px;font-weight:700}.meta{color:#555;font-size:13px;line-height:1.6}.classes{margin-top:8px;font-family:Consolas,monospace;font-size:12px;overflow-wrap:anywhere}a{color:#065fd4;text-decoration:none}a:hover{text-decoration:underline}.badge{display:inline-block;background:#eee;border-radius:999px;padding:3px 8px;margin-right:6px;font-size:12px}
</style></head><body>
""");
        html.Append($"<h1>Glyph PCA model bundle</h1><p><b>{H(Path.GetFileName(bundlePath))}</b> · {bundle.Models.Count} models</p>");

        foreach (var family in bundle.Models.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var model = bundle.Models[family];
            summaries.TryGetValue(family, out var summary);
            var classes = model.References.Select(x => x.Class).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            var href = Uri.EscapeDataString(family) + "/index.html";

            html.Append("<div class=\"card\">");
            html.Append($"<div class=\"family\"><a href=\"{href}\">{H(family)}</a></div>");
            html.Append("<div class=\"meta\">");
            html.Append($"<span class=\"badge\">PCA {model.Pca.ComponentsCount}D</span>");
            html.Append($"<span class=\"badge\">{model.References.Count} refs</span>");
            html.Append($"<span class=\"badge\">{classes.Length} classes</span>");
            if (summary is not null)
                html.Append($"<span class=\"badge\">{summary.Successful}/{summary.Total} analyzed</span><span class=\"badge\">{summary.Elapsed.TotalSeconds:F2}s</span>");
            html.Append($"<br>ZIP entry: {H(bundle.Entries[family])}</div>");
            html.Append($"<div class=\"classes\">{H(string.Join(", ", classes))}</div>");
            html.Append("</div>");
        }

        html.Append("</body></html>");
        File.WriteAllText(Path.Combine(outputDirectory, "index.html"), html.ToString(), Encoding.UTF8);
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
}

public sealed record BundleModelRunSummary(int Total, int Successful, TimeSpan Elapsed);
