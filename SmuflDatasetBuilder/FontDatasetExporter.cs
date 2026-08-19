using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SkiaSharp;

internal static class FontDatasetExporter
{
    // First engraved-font pool. We intentionally avoid handwritten fonts here.
    // GitHub paths are discovered at runtime, so repository reorganizations do not
    // require hard-coded raw-file URLs.
    private static readonly FontSource[] Sources =
    [
        new("Bravura", "steinbergmedia/bravura", "Bravura.otf"),
        new("Leland", "MuseScoreFonts/Leland", "Leland.otf"),
        new("Sebastian", "fkretlow/sebastian", "Sebastian.otf"),
        new("Gootville", "musescore/MuseScore", "Gootville.otf"),
        new("FinaleMaestro", "musescore/MuseScore", "FinaleMaestro.otf")
    ];

    public static async Task ExportAsync(
        HttpClient http,
        IReadOnlyList<SmuflGlyph> glyphs,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var fontsDirectory = Path.Combine(outputDirectory, "fonts");
        var datasetDirectory = Path.Combine(outputDirectory, "dataset");
        Directory.CreateDirectory(fontsDirectory);
        Directory.CreateDirectory(datasetDirectory);

        var manifest = new List<ExportRow>();

        foreach (var source in Sources)
        {
            Console.WriteLine();
            Console.WriteLine($"[{source.Name}] locating font...");

            try
            {
                var fontPath = await DownloadFontAsync(http, source, fontsDirectory, cancellationToken);
                if (fontPath is null)
                    continue;

                var count = ExportFont(source.Name, fontPath, glyphs, datasetDirectory, manifest);
                Console.WriteLine($"[{source.Name}] exported {count}/{glyphs.Count} selected glyphs");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[{source.Name}] skipped: {ex.Message}");
                Console.ResetColor();
            }
        }

        WriteManifest(Path.Combine(outputDirectory, "dataset-manifest.csv"), manifest);
        WriteGallery(Path.Combine(outputDirectory, "dataset-gallery.html"), glyphs, Sources, manifest);

        Console.WriteLine();
        Console.WriteLine($"Dataset:  {datasetDirectory}");
        Console.WriteLine($"Manifest: {Path.Combine(outputDirectory, "dataset-manifest.csv")}");
        Console.WriteLine($"Gallery:  {Path.Combine(outputDirectory, "dataset-gallery.html")}");
    }

    private static async Task<string?> DownloadFontAsync(
        HttpClient http,
        FontSource source,
        string fontsDirectory,
        CancellationToken cancellationToken)
    {
        var localPath = Path.Combine(fontsDirectory, $"{source.Name}.otf");
        if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
            return localPath;

        var repoInfoUrl = $"https://api.github.com/repos/{source.Repository}";
        var repoJson = JsonNode.Parse(await http.GetStringAsync(repoInfoUrl, cancellationToken))?.AsObject()
            ?? throw new InvalidDataException("GitHub repository response is invalid.");
        var branch = repoJson["default_branch"]?.GetValue<string>()
            ?? throw new InvalidDataException("GitHub repository has no default_branch.");

        var treeUrl = $"https://api.github.com/repos/{source.Repository}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1";
        var treeJson = JsonNode.Parse(await http.GetStringAsync(treeUrl, cancellationToken))?.AsObject()
            ?? throw new InvalidDataException("GitHub tree response is invalid.");

        var candidates = treeJson["tree"]?.AsArray()
            .Select(x => x?.AsObject())
            .Where(x => x is not null && string.Equals(x["type"]?.GetValue<string>(), "blob", StringComparison.Ordinal))
            .Select(x => x!["path"]?.GetValue<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Where(x => x.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .Where(x => string.Equals(Path.GetFileName(x), source.FileName, StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];

        if (candidates.Length == 0)
        {
            var truncated = treeJson["truncated"]?.GetValue<bool>() == true ? " (GitHub tree was truncated)" : string.Empty;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[{source.Name}] skipped: {source.FileName} not found in {source.Repository}{truncated}");
            Console.ResetColor();
            return null;
        }

        // Prefer redistributable/build output over test fixtures if a repository contains duplicates.
        var remotePath = candidates
            .OrderBy(x => x.Contains("test", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(x => x.Length)
            .First();

        var rawUrl = $"https://raw.githubusercontent.com/{source.Repository}/{branch}/{remotePath}";
        Console.WriteLine($"[{source.Name}] {remotePath}");

        var bytes = await http.GetByteArrayAsync(rawUrl, cancellationToken);
        await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);
        return localPath;
    }

    private static int ExportFont(
        string fontName,
        string fontPath,
        IReadOnlyList<SmuflGlyph> glyphs,
        string datasetDirectory,
        List<ExportRow> manifest)
    {
        using var typeface = SKTypeface.FromFile(fontPath)
            ?? throw new InvalidDataException($"SkiaSharp could not load {fontPath}");
        using var font = new SKFont(typeface, 1000f);

        var exported = 0;
        foreach (var glyph in glyphs)
        {
            if (!TryParseCodepoint(glyph.Codepoint, out var codepoint))
            {
                manifest.Add(new ExportRow(glyph.Name, fontName, glyph.Codepoint, false, "invalid codepoint", null));
                continue;
            }

            var glyphIds = font.GetGlyphs(new[] { codepoint });
            if (glyphIds.Length != 1 || glyphIds[0] == 0)
            {
                manifest.Add(new ExportRow(glyph.Name, fontName, glyph.Codepoint, false, "missing glyph", null));
                continue;
            }

            using var path = font.GetGlyphPath(glyphIds[0]);
            if (path is null || path.IsEmpty)
            {
                manifest.Add(new ExportRow(glyph.Name, fontName, glyph.Codepoint, false, "no outline", null));
                continue;
            }

            var bounds = path.TightBounds;
            var extent = Math.Max(bounds.Width, bounds.Height);
            var pad = Math.Max(extent * 0.08f, 4f);

            using var normalized = new SKPath(path);
            normalized.Offset(-bounds.Left + pad, -bounds.Top + pad);

            var width = Math.Max(bounds.Width + 2 * pad, 1f);
            var height = Math.Max(bounds.Height + 2 * pad, 1f);
            var pathData = normalized.ToSvgPathData();

            var glyphDirectory = Path.Combine(datasetDirectory, glyph.Name);
            Directory.CreateDirectory(glyphDirectory);
            var fileName = $"{fontName}.svg";
            var filePath = Path.Combine(glyphDirectory, fileName);

            var svg = new StringBuilder()
                .AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
                .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
                .Append(F(width)).Append(' ').Append(F(height)).AppendLine("\">")
                .Append("  <path fill=\"black\" fill-rule=\"nonzero\" d=\"")
                .Append(System.Net.WebUtility.HtmlEncode(pathData)).AppendLine("\"/>")
                .AppendLine("</svg>")
                .ToString();

            File.WriteAllText(filePath, svg, new UTF8Encoding(false));
            manifest.Add(new ExportRow(glyph.Name, fontName, glyph.Codepoint, true, string.Empty,
                Path.GetRelativePath(datasetDirectory, filePath).Replace('\\', '/')));
            exported++;
        }

        return exported;
    }

    private static bool TryParseCodepoint(string value, out int codepoint)
    {
        var hex = value.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codepoint);
    }

    private static void WriteManifest(string path, IReadOnlyList<ExportRow> rows)
    {
        var sb = new StringBuilder("glyph,font,codepoint,exported,note,file\n");
        foreach (var row in rows.OrderBy(x => x.Glyph).ThenBy(x => x.Font))
            sb.Append(Csv(row.Glyph)).Append(',').Append(Csv(row.Font)).Append(',').Append(Csv(row.Codepoint)).Append(',')
                .Append(row.Exported ? "1" : "0").Append(',').Append(Csv(row.Note)).Append(',').Append(Csv(row.File ?? string.Empty)).AppendLine();
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static void WriteGallery(
        string path,
        IReadOnlyList<SmuflGlyph> glyphs,
        IReadOnlyList<FontSource> sources,
        IReadOnlyList<ExportRow> rows)
    {
        var available = rows.Where(x => x.Exported && x.File is not null)
            .ToDictionary(x => (x.Glyph, x.Font), x => x.File!, new GlyphFontComparer());

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>SMuFL dataset</title>");
        sb.AppendLine("<style>body{font-family:system-ui,Arial,sans-serif;margin:24px;background:#fafafa}.glyph{margin:28px 0}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px}.card{background:white;border:1px solid #ddd;border-radius:8px;padding:10px}.shape{height:130px;display:flex;align-items:center;justify-content:center;background:#f5f5f5}.shape img{max-width:100%;max-height:120px}.font{font-size:12px;margin-top:6px}.missing{color:#999}.mono{font-family:ui-monospace,Consolas,monospace}</style></head><body>");
        sb.AppendLine("<h1>Selected SMuFL glyph dataset</h1><p>Canonical SMuFL labels; one vector sample per available font.</p>");

        foreach (var glyph in glyphs)
        {
            sb.AppendLine($"<section class=\"glyph\"><h2 class=\"mono\">{glyph.Name}</h2><div class=\"grid\">");
            foreach (var source in sources)
            {
                sb.AppendLine("<div class=\"card\">");
                if (available.TryGetValue((glyph.Name, source.Name), out var relative))
                {
                    var fromGallery = "dataset/" + relative;
                    sb.AppendLine($"<div class=\"shape\"><img src=\"{fromGallery}\"></div><div class=\"font\">{source.Name}</div>");
                }
                else
                {
                    sb.AppendLine($"<div class=\"shape missing\">missing</div><div class=\"font\">{source.Name}</div>");
                }
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div></section>");
        }

        sb.AppendLine("</body></html>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private sealed record FontSource(string Name, string Repository, string FileName);
    private sealed record ExportRow(string Glyph, string Font, string Codepoint, bool Exported, string Note, string? File);

    private sealed class GlyphFontComparer : IEqualityComparer<(string Glyph, string Font)>
    {
        public bool Equals((string Glyph, string Font) x, (string Glyph, string Font) y) =>
            string.Equals(x.Glyph, y.Glyph, StringComparison.Ordinal) && string.Equals(x.Font, y.Font, StringComparison.Ordinal);

        public int GetHashCode((string Glyph, string Font) obj) => HashCode.Combine(obj.Glyph, obj.Font);
    }
}
