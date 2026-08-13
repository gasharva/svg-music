using System.Text.RegularExpressions;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Configuration;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Converts a directory of SVG pages into one MusicXML score.
/// Every SVG is processed through the normal ConversionPipeline first; the resulting
/// post-processed measures are then concatenated in natural filename order.
/// </summary>
public sealed class MultiSvgConversionService
{
    public MultiSvgConversionResult ConvertDirectory(
        string directoryPath,
        string catalogPath,
        string? musicXmlPath = null,
        RecognitionConfig? config = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        var directory = new DirectoryInfo(directoryPath);
        if (!directory.Exists)
            throw new DirectoryNotFoundException($"Папка SVG не найдена: {directory.FullName}");
        if (!File.Exists(catalogPath))
            throw new FileNotFoundException("Каталог эталонов не найден.", catalogPath);

        var svgFiles = directory
            .EnumerateFiles("*.svg", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x.Name, NaturalFileNameComparer.Instance)
            .ToList();

        if (svgFiles.Count == 0)
            throw new InvalidOperationException($"В папке нет SVG-файлов: {directory.FullName}");

        musicXmlPath ??= Path.Combine(directory.FullName, directory.Name + ".musicxml");
        musicXmlPath = Path.GetFullPath(musicXmlPath);
        Directory.CreateDirectory(Path.GetDirectoryName(musicXmlPath)!);

        config ??= new RecognitionConfig();
        var pipeline = new ConversionPipeline();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "svg-music-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            XDocument? combined = null;
            XElement? combinedPart = null;
            var nextMeasureNumber = 1;

            for (var pageIndex = 0; pageIndex < svgFiles.Count; pageIndex++)
            {
                var pageOutput = Path.Combine(tempDirectory, $"page-{pageIndex + 1:D4}.musicxml");
                pipeline.Convert(svgFiles[pageIndex].FullName, catalogPath, pageOutput, config, writeDiagnostics: false);

                var pageDocument = XDocument.Load(pageOutput, LoadOptions.PreserveWhitespace);
                var pagePart = pageDocument.Root?.Element("part")
                    ?? throw new InvalidOperationException($"В сгенерированном MusicXML нет <part>: {svgFiles[pageIndex].Name}");

                if (combined is null)
                {
                    combined = pageDocument;
                    combinedPart = combined.Root!.Element("part")!;
                    foreach (var measure in combinedPart.Elements("measure"))
                        measure.SetAttributeValue("number", nextMeasureNumber++);
                    continue;
                }

                var pageMeasures = pagePart.Elements("measure").Select(x => new XElement(x)).ToList();
                if (pageMeasures.Count == 0) continue;

                // Preserve the fact that each input SVG is a separate source page.
                var firstMeasure = pageMeasures[0];
                var print = firstMeasure.Element("print");
                if (print is null)
                    firstMeasure.AddFirst(new XElement("print", new XAttribute("new-page", "yes")));
                else
                    print.SetAttributeValue("new-page", "yes");

                foreach (var measure in pageMeasures)
                {
                    measure.SetAttributeValue("number", nextMeasureNumber++);
                    combinedPart!.Add(measure);
                }
            }

            combined!.Save(musicXmlPath);
            return new MultiSvgConversionResult(musicXmlPath, svgFiles.Select(x => x.FullName).ToArray());
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); }
            catch { /* Temp cleanup must not hide a successful conversion. */ }
        }
    }

    private sealed class NaturalFileNameComparer : IComparer<string>
    {
        public static readonly NaturalFileNameComparer Instance = new();
        private static readonly Regex Chunks = new("(\\d+)|(\\D+)", RegexOptions.Compiled);

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var a = Chunks.Matches(x);
            var b = Chunks.Matches(y);
            var count = Math.Min(a.Count, b.Count);
            for (var i = 0; i < count; i++)
            {
                var ac = a[i].Value;
                var bc = b[i].Value;
                int result;
                if (char.IsDigit(ac[0]) && char.IsDigit(bc[0]) &&
                    long.TryParse(ac, out var an) && long.TryParse(bc, out var bn))
                    result = an.CompareTo(bn);
                else
                    result = StringComparer.OrdinalIgnoreCase.Compare(ac, bc);

                if (result != 0) return result;
            }

            return a.Count != b.Count
                ? a.Count.CompareTo(b.Count)
                : StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }
}

public sealed record MultiSvgConversionResult(string MusicXmlPath, IReadOnlyList<string> SvgFiles);
