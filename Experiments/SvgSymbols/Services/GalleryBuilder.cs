using System.Globalization;
using System.Net;
using System.Text;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

public sealed class GalleryBuilder
{
    private readonly FourierDescriptorAnalyzer _fourier = new();
    private readonly FourierDescriptorComparer _comparer = new();
    private readonly DigitTopologyAnalyzer _digitTopology = new();

    public async Task<string> BuildAsync(
        string rootDirectory,
        IReadOnlyList<SymbolSource> treble,
        IReadOnlyList<SymbolSource> bass,
        IReadOnlyList<SymbolSource> rhythm,
        IReadOnlyList<SymbolSource> other,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(rootDirectory, "gallery.html");
        var all = AnalyzeAll(rootDirectory, treble, bass, rhythm, other);
        var rhythmCorpus = rhythm
            .Select(source => (Source: source, Path: GetLocalPath(rootDirectory, "Rhythm", source.FileName)))
            .ToArray();
        var html = new StringBuilder();

        html.AppendLine("<!doctype html>");
        html.AppendLine("<html><head><meta charset=\"utf-8\">");
        html.AppendLine("<title>SvgSymbols corpus</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f5f5f5;color:#222}");
        html.AppendLine("h1,h2{margin:0 0 16px} h2{margin-top:32px}");
        html.AppendLine(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(330px,1fr));gap:14px}");
        html.AppendLine(".card{background:white;border:1px solid #ccc;border-radius:8px;padding:10px;min-height:480px;display:flex;flex-direction:column}");
        html.AppendLine(".preview{height:180px;display:flex;align-items:center;justify-content:center;background:#fafafa;border:1px solid #eee;margin-bottom:8px}");
        html.AppendLine("img{max-width:100%;max-height:170px}.name{font-weight:600;word-break:break-word}.meta{font-size:12px;color:#666;margin-top:5px;word-break:break-word}");
        html.AppendLine(".fourier,.nearest,.digit-verdict{margin-top:8px;padding:7px 8px;background:#f1f3f5;border-radius:5px;font-family:Consolas,monospace;font-size:12px;line-height:1.45}");
        html.AppendLine(".fourier .label,.nearest .label,.digit-verdict .label{font-family:Segoe UI,Arial,sans-serif;font-weight:600;color:#444;margin-bottom:2px}");
        html.AppendLine(".digit-verdict{background:#fff6d8;border:1px solid #eed58a}.digit-main{font-family:Segoe UI,Arial,sans-serif;font-size:18px;font-weight:700}.digit-row{display:grid;grid-template-columns:38px 1fr;gap:5px}.digit-candidates{color:#666}");
        html.AppendLine(".nearest.complex{background:#eef7ee}.nearest.magnitude{background:#f5f1fa}.neighbor{display:grid;grid-template-columns:52px 42px 1fr;gap:4px}.kind{font-weight:700}.error{color:#a00}.muted{color:#777}label{margin-top:auto;font-size:13px}.bad{accent-color:#c00}");
        html.AppendLine("</style></head><body>");
        html.AppendLine($"<h1>SvgSymbols corpus — {all.Count} SVG</h1>");
        html.AppendLine("<p>Vector-only experiment. Fourier/scanline nearest-neighbour remains as before. A separate DigitTopologyAnalyzer now tries to split time-signature numbers into individual vector digit groups and recognizes each group from the single-digit corpus using scanlines, Fourier and simple topology together.</p>");

        // Put the current target of the experiment first.
        AppendSection(html, all, "Time-signature numbers", "Rhythm", rhythm, "Wikimedia source", true, rootDirectory, rhythmCorpus);
        AppendSection(html, all, "Treble / G clef", "Treble", treble, "Wikimedia source", true, rootDirectory, rhythmCorpus);
        AppendSection(html, all, "Bass / F clef", "Bass", bass, "Wikimedia source", true, rootDirectory, rhythmCorpus);
        AppendSection(html, all, "Other musical symbols (negative/control corpus)", "Other", other, "Reference glyph", false, rootDirectory, rhythmCorpus);

        html.AppendLine("<script>");
        html.AppendLine("document.querySelectorAll('input[type=checkbox]').forEach(x=>{const k='svgsymbols:'+x.dataset.id;x.checked=localStorage.getItem(k)==='1';x.onchange=()=>localStorage.setItem(k,x.checked?'1':'0');});");
        html.AppendLine("</script></body></html>");

        await File.WriteAllTextAsync(path, html.ToString(), cancellationToken);
        return path;
    }

    private IReadOnlyList<AnalyzedSymbol> AnalyzeAll(
        string rootDirectory,
        IReadOnlyList<SymbolSource> treble,
        IReadOnlyList<SymbolSource> bass,
        IReadOnlyList<SymbolSource> rhythm,
        IReadOnlyList<SymbolSource> other)
    {
        var result = new List<AnalyzedSymbol>();
        AnalyzeGroup(result, rootDirectory, "Rhythm", rhythm);
        AnalyzeGroup(result, rootDirectory, "Treble", treble);
        AnalyzeGroup(result, rootDirectory, "Bass", bass);
        AnalyzeGroup(result, rootDirectory, "Other", other);
        return result;
    }

    private void AnalyzeGroup(
        ICollection<AnalyzedSymbol> result,
        string rootDirectory,
        string folder,
        IReadOnlyList<SymbolSource> sources)
    {
        foreach (var source in sources)
        {
            var localPath = GetLocalPath(rootDirectory, folder, source.FileName);
            try
            {
                result.Add(new AnalyzedSymbol(folder, source, _fourier.Analyze(localPath), null));
            }
            catch (Exception ex)
            {
                result.Add(new AnalyzedSymbol(folder, source, null, ex.Message));
            }
        }
    }

    private void AppendSection(
        StringBuilder html,
        IReadOnlyList<AnalyzedSymbol> all,
        string title,
        string folder,
        IReadOnlyList<SymbolSource> sources,
        string sourceLabel,
        bool showReviewCheckbox,
        string rootDirectory,
        IReadOnlyList<(SymbolSource Source, string Path)> rhythmCorpus)
    {
        html.AppendLine($"<h2>{WebUtility.HtmlEncode(title)} ({sources.Count})</h2><div class=\"grid\">");

        foreach (var source in sources)
        {
            var relative = $"Samples/{folder}/{EscapeRelativePath(source.FileName)}";
            var id = folder + ":" + source.FileName;
            var analyzed = all.First(x => x.Folder == folder && ReferenceEquals(x.Source, source));

            html.AppendLine("<div class=\"card\">");
            html.AppendLine($"<div class=\"preview\"><img loading=\"lazy\" src=\"{relative}\"></div>");

            if (folder == "Rhythm")
            {
                var localPath = GetLocalPath(rootDirectory, folder, source.FileName);
                var recognition = _digitTopology.Analyze(localPath, rhythmCorpus, source.FileName);
                AppendDigitVerdict(html, recognition);
            }

            html.AppendLine($"<div class=\"name\">{WebUtility.HtmlEncode(source.FileName)}</div>");
            html.AppendLine($"<div class=\"meta\">Category: {WebUtility.HtmlEncode(source.Category)}</div>");
            html.AppendLine($"<div class=\"meta\">License: {WebUtility.HtmlEncode(source.License ?? "unknown")}</div>");
            html.AppendLine($"<div class=\"meta\"><a href=\"{WebUtility.HtmlEncode(source.DescriptionUrl)}\">{WebUtility.HtmlEncode(sourceLabel)}</a></div>");
            AppendFourier(html, analyzed);
            AppendNearest(html, analyzed, all, complex: true);
            AppendNearest(html, analyzed, all, complex: false);

            if (showReviewCheckbox)
                html.AppendLine($"<label><input class=\"bad\" type=\"checkbox\" data-id=\"{WebUtility.HtmlEncode(id)}\"> мусор / не подходит</label>");

            html.AppendLine("</div>");
        }

        html.AppendLine("</div>");
    }

    private static void AppendDigitVerdict(StringBuilder html, NumberRecognition recognition)
    {
        html.AppendLine("<div class=\"digit-verdict\">");
        html.AppendLine("<div class=\"label\">DigitTopologyAnalyzer</div>");

        if (!string.IsNullOrWhiteSpace(recognition.Error))
        {
            html.AppendLine($"<div class=\"error\">{WebUtility.HtmlEncode(recognition.Error)}</div></div>");
            return;
        }

        var percentage = recognition.Probability * 100d;
        html.AppendLine($"<div class=\"digit-main\">→ {WebUtility.HtmlEncode(recognition.Value ?? "?")} &nbsp; {percentage:0.0}%</div>");
        html.AppendLine($"<div class=\"muted\">segments={recognition.SegmentCount}</div>");

        foreach (var digit in recognition.Digits)
        {
            var candidates = string.Join(" · ", digit.Candidates.Select(x =>
                $"{x.Digit}:{x.Probability * 100d:0.0}% d={x.Distance:0.00}"));
            html.AppendLine("<div class=\"digit-row\">" +
                $"<b>#{digit.Index + 1}</b>" +
                $"<span>{digit.Digit} ({digit.Probability * 100d:0.0}%) <span class=\"digit-candidates\">{WebUtility.HtmlEncode(candidates)}</span></span>" +
                "</div>");
        }

        html.AppendLine("</div>");
    }

    private static void AppendFourier(StringBuilder html, AnalyzedSymbol symbol)
    {
        if (symbol.Descriptor is null)
        {
            html.AppendLine($"<div class=\"fourier error\">DFT error: {WebUtility.HtmlEncode(symbol.Error ?? "unknown")}</div>");
            return;
        }

        if (symbol.Descriptor.Contours.Count == 0)
        {
            html.AppendLine("<div class=\"fourier error\">DFT: no usable contour</div>");
            return;
        }

        html.AppendLine("<div class=\"fourier\">");
        html.AppendLine("<div class=\"label\">Complex vector Fourier</div>");

        for (var i = 0; i < symbol.Descriptor.Contours.Count; i++)
        {
            var contour = symbol.Descriptor.Contours[i];
            html.AppendLine($"<div><b>C{i + 1}</b> w={Format(contour.Weight)} x={Format(contour.CenterX)} y={Format(contour.CenterY)} size={Format(contour.Width)}×{Format(contour.Height)}</div>");

            var values = contour.Coefficients
                .Take(4)
                .Select((value, index) => $"F{index + 1}={FormatSigned(value.Real)}{FormatSignedImag(value.Imag)}i")
                .ToArray();
            html.AppendLine($"<div>{WebUtility.HtmlEncode(string.Join("  ", values.Take(2)))}</div>");
            html.AppendLine($"<div>{WebUtility.HtmlEncode(string.Join("  ", values.Skip(2)))}</div>");
        }

        html.AppendLine($"<div class=\"muted\">contours raw={symbol.Descriptor.RawContourCount}, unique={symbol.Descriptor.ContourCount}, described={symbol.Descriptor.Contours.Count}</div>");
        html.AppendLine("</div>");
    }

    private void AppendNearest(
        StringBuilder html,
        AnalyzedSymbol current,
        IReadOnlyList<AnalyzedSymbol> all,
        bool complex)
    {
        if (current.Descriptor is null || current.Descriptor.Contours.Count == 0)
            return;

        var nearest = all
            .Where(x => !ReferenceEquals(x, current) && x.Descriptor is { Contours.Count: > 0 })
            .Select(x => new
            {
                Symbol = x,
                Distance = complex
                    ? _comparer.ComplexDistance(current.Descriptor, x.Descriptor!)
                    : _comparer.MagnitudeDistance(current.Descriptor, x.Descriptor!)
            })
            .OrderBy(x => x.Distance)
            .Take(5)
            .ToArray();

        var css = complex ? "complex" : "magnitude";
        var title = complex
            ? "Top 5 — complex/phase-aware"
            : "Top 5 — magnitude-only baseline";

        html.AppendLine($"<div class=\"nearest {css}\">");
        html.AppendLine($"<div class=\"label\">{title}</div>");
        foreach (var item in nearest)
        {
            html.AppendLine("<div class=\"neighbor\">" +
                $"<span>{Format(item.Distance)}</span>" +
                $"<span class=\"kind\">{WebUtility.HtmlEncode(ShortKind(item.Symbol.Folder))}</span>" +
                $"<span>{WebUtility.HtmlEncode(item.Symbol.Source.FileName)}</span>" +
                "</div>");
        }
        html.AppendLine("</div>");
    }

    private static string ShortKind(string folder) => folder switch
    {
        "Treble" => "G",
        "Bass" => "F",
        "Rhythm" => "R",
        _ => "Other"
    };

    private static string GetLocalPath(string rootDirectory, string folder, string fileName) =>
        Path.Combine(
            rootDirectory,
            "Samples",
            folder,
            fileName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

    private static string Format(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
    private static string FormatSigned(double value) => value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
    private static string FormatSignedImag(double value) => value.ToString("+0.000;-0.000;+0.000", CultureInfo.InvariantCulture);

    private static string EscapeRelativePath(string path) =>
        string.Join('/', path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

    private sealed record AnalyzedSymbol(
        string Folder,
        SymbolSource Source,
        FourierDescriptor? Descriptor,
        string? Error);
}
