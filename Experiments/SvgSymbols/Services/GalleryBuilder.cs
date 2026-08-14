using System.Net;
using System.Text;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed class GalleryBuilder
{
    public async Task<string> BuildAsync(
        string rootDirectory,
        IReadOnlyList<SymbolSource> treble,
        IReadOnlyList<SymbolSource> bass,
        IReadOnlyList<SymbolSource> other,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(rootDirectory, "gallery.html");
        var html = new StringBuilder();

        html.AppendLine("<!doctype html>");
        html.AppendLine("<html><head><meta charset=\"utf-8\">");
        html.AppendLine("<title>SvgSymbols corpus</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f5f5f5;color:#222}");
        html.AppendLine("h1,h2{margin:0 0 16px} h2{margin-top:32px}");
        html.AppendLine(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:14px}");
        html.AppendLine(".card{background:white;border:1px solid #ccc;border-radius:8px;padding:10px;min-height:270px;display:flex;flex-direction:column}");
        html.AppendLine(".preview{height:180px;display:flex;align-items:center;justify-content:center;background:#fafafa;border:1px solid #eee;margin-bottom:8px}");
        html.AppendLine("img{max-width:100%;max-height:170px}.name{font-weight:600;word-break:break-word}.meta{font-size:12px;color:#666;margin-top:5px;word-break:break-word}");
        html.AppendLine("label{margin-top:auto;font-size:13px}.bad{accent-color:#c00}");
        html.AppendLine("</style></head><body>");
        html.AppendLine($"<h1>SvgSymbols corpus — {treble.Count + bass.Count + other.Count} SVG</h1>");
        html.AppendLine("<p>Clefs plus a control corpus of other musical symbols.</p>");

        AppendSection(html, "Treble / G clef", "Treble", treble, "Wikimedia source", true);
        AppendSection(html, "Bass / F clef", "Bass", bass, "Wikimedia source", true);
        AppendSection(html, "Other musical symbols (negative/control corpus)", "Other", other, "Reference glyph", false);

        html.AppendLine("<script>");
        html.AppendLine("document.querySelectorAll('input[type=checkbox]').forEach(x=>{const k='svgsymbols:'+x.dataset.id;x.checked=localStorage.getItem(k)==='1';x.onchange=()=>localStorage.setItem(k,x.checked?'1':'0');});");
        html.AppendLine("</script></body></html>");

        await File.WriteAllTextAsync(path, html.ToString(), cancellationToken);
        return path;
    }

    private static void AppendSection(
        StringBuilder html,
        string title,
        string folder,
        IReadOnlyList<SymbolSource> sources,
        string sourceLabel,
        bool showReviewCheckbox)
    {
        html.AppendLine($"<h2>{WebUtility.HtmlEncode(title)} ({sources.Count})</h2><div class=\"grid\">");

        foreach (var source in sources)
        {
            var relative = $"Samples/{folder}/{Uri.EscapeDataString(source.FileName)}";
            var id = folder + ":" + source.FileName;
            html.AppendLine("<div class=\"card\">");
            html.AppendLine($"<div class=\"preview\"><img loading=\"lazy\" src=\"{relative}\"></div>");
            html.AppendLine($"<div class=\"name\">{WebUtility.HtmlEncode(source.FileName)}</div>");
            html.AppendLine($"<div class=\"meta\">Category: {WebUtility.HtmlEncode(source.Category)}</div>");
            html.AppendLine($"<div class=\"meta\">License: {WebUtility.HtmlEncode(source.License ?? "unknown")}</div>");
            html.AppendLine($"<div class=\"meta\"><a href=\"{WebUtility.HtmlEncode(source.DescriptionUrl)}\">{WebUtility.HtmlEncode(sourceLabel)}</a></div>");

            if (showReviewCheckbox)
                html.AppendLine($"<label><input class=\"bad\" type=\"checkbox\" data-id=\"{WebUtility.HtmlEncode(id)}\"> мусор / не подходит</label>");

            html.AppendLine("</div>");
        }

        html.AppendLine("</div>");
    }
}
