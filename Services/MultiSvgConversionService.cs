using System.Text.RegularExpressions;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

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

        var outputStem = Path.Combine(
            Path.GetDirectoryName(musicXmlPath)!,
            Path.GetFileNameWithoutExtension(musicXmlPath));
        var pageDiagnosticsDirectory = outputStem + ".pages";
        if (Directory.Exists(pageDiagnosticsDirectory))
            Directory.Delete(pageDiagnosticsDirectory, recursive: true);
        Directory.CreateDirectory(pageDiagnosticsDirectory);

        var pageArtifacts = new List<MultiSvgPageArtifact>();
        XDocument? combined = null;
        XElement? combinedPart = null;
        var nextMeasureNumber = 1;

        for (var pageIndex = 0; pageIndex < svgFiles.Count; pageIndex++)
        {
            var source = svgFiles[pageIndex];
            var pageBaseName = $"page-{pageIndex + 1:D4}-{Path.GetFileNameWithoutExtension(source.Name)}";
            var pageOutput = Path.Combine(pageDiagnosticsDirectory, pageBaseName + ".musicxml");
            var conversion = pipeline.Convert(source.FullName, catalogPath, pageOutput, config, writeDiagnostics: true);

            pageArtifacts.Add(new MultiSvgPageArtifact(
                source.FullName,
                conversion.MusicXmlPath,
                conversion.AnalysisPath!,
                conversion.ClassificationPath!,
                Path.ChangeExtension(conversion.MusicXmlPath, ".performance.json")));

            var pageDocument = XDocument.Load(pageOutput, LoadOptions.PreserveWhitespace);
            var pagePart = pageDocument.Root?.Element("part")
                ?? throw new InvalidOperationException($"В сгенерированном MusicXML нет <part>: {source.Name}");

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

            // A source-page boundary must remain a SYSTEM boundary, otherwise the last system of
            // one SVG and first system of the next page can be merged into one line. It is not a
            // page-formatting command though: no new-page is emitted, so the editor may freely
            // reflow those systems onto physical pages later.
            var firstMeasure = pageMeasures[0];
            var print = firstMeasure.Element("print");
            if (print is null)
            {
                print = new XElement("print");
                firstMeasure.AddFirst(print);
            }
            print.SetAttributeValue("new-system", "yes");
            print.Attribute("new-page")?.Remove();

            // A continuation SVG often repeats clefs but omits the time signature. Its standalone
            // page MusicXML necessarily starts with the configured fallback meter; when joining
            // pages that synthetic <time> must not override the last explicitly printed meter from
            // the preceding page. In MusicXML, omitting <time> naturally carries the prior meter.
            if (!HasExplicitTimeSignatureGlyphs(conversion.Analysis))
            {
                var firstAttributes = pageMeasures[0].Element("attributes");
                firstAttributes?.Element("time")?.Remove();
            }

            foreach (var measure in pageMeasures)
            {
                measure.SetAttributeValue("number", nextMeasureNumber++);
                combinedPart!.Add(measure);
            }
        }

        combined!.Save(musicXmlPath);
        return new MultiSvgConversionResult(
            musicXmlPath,
            svgFiles.Select(x => x.FullName).ToArray(),
            pageDiagnosticsDirectory,
            pageArtifacts);
    }

    private static bool HasExplicitTimeSignatureGlyphs(AnalysisResult analysis) =>
        analysis.Classifications.Any(x =>
            x.Kind.Contains("time-signature", StringComparison.OrdinalIgnoreCase) ||
            x.Kind.Contains("timesig", StringComparison.OrdinalIgnoreCase) ||
            x.ReferenceId.Contains("timeSig", StringComparison.OrdinalIgnoreCase) ||
            x.ReferenceId.Contains("timeSignature", StringComparison.OrdinalIgnoreCase));

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

public sealed record MultiSvgPageArtifact(
    string SvgPath,
    string MusicXmlPath,
    string AnalysisPath,
    string ClassificationPath,
    string PerformancePath);

public sealed record MultiSvgConversionResult(
    string MusicXmlPath,
    IReadOnlyList<string> SvgFiles,
    string PageDiagnosticsDirectory,
    IReadOnlyList<MultiSvgPageArtifact> Pages);
