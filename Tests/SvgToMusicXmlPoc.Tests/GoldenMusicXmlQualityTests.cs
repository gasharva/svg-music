using System.Xml.Linq;
using SvgToMusicXmlPoc.Quality;
using SvgToMusicXmlPoc.Services;

namespace SvgToMusicXmlPoc.Tests;

public sealed class GoldenMusicXmlQualityTests
{
    [Fact]
    public void YellowLeaves_SvgConversion_ProducesSemanticQualityAndPerformanceReport()
    {
        var root = FindRepositoryRoot();
        var goldenDirectory = Path.Combine(root, "Golden");
        var outputDirectory = Path.Combine(root, "TestResults", "golden-quality", "yellow-leaves-giya-kancheli");
        Directory.CreateDirectory(outputDirectory);

        var svgPath = Path.Combine(goldenDirectory, "yellow-leaves-giya-kancheli.svg");
        var expectedPath = Path.Combine(goldenDirectory, "yellow-leaves-giya-kancheli.musicxml");
        var catalogPath = Path.Combine(root, "References", "catalog.json");
        var actualPath = Path.Combine(outputDirectory, "actual.musicxml");

        Assert.True(File.Exists(svgPath));
        Assert.True(File.Exists(expectedPath));
        Assert.True(File.Exists(catalogPath));

        var conversion = new ConversionPipeline().Convert(svgPath, catalogPath, actualPath);
        var comparison = new MusicXmlSemanticComparer().Compare(expectedPath, actualPath);
        new QualityReportWriter().Write(comparison, outputDirectory, conversion.Performance);

        Assert.True(File.Exists(actualPath));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "quality-report.csv")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "quality-report.md")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "performance.csv")));
        Assert.True(comparison.Metrics.Expected > 0);

        Assert.NotEmpty(conversion.Analysis.Uses);
        Assert.All(conversion.Analysis.Uses, x => Assert.Equal("path", x.SourceKind));
        Assert.Equal(conversion.Analysis.Uses.Count, conversion.Classification.Symbols.Count);
        Assert.True(conversion.Performance.GlyphInstances > 0);
        Assert.True(conversion.Performance.UniqueGeometries > 0);
        Assert.True(conversion.Performance.UniqueGeometries <= conversion.Performance.GlyphInstances);
        Assert.True(conversion.Performance.MaskComparisons > 0);
        Assert.True(conversion.Performance.VectorComparisons <= conversion.Performance.UniqueGeometries * 5L);

        var actual = XDocument.Load(actualPath);
        Assert.Equal(5, actual.Descendants("measure").Count());
        Assert.Equal(5, actual.Descendants("staves").Count(x => x.Value == "2"));
        Assert.Contains(actual.Descendants("note"), x => x.Element("staff")?.Value == "2");
        Assert.True(actual.Descendants("note").Count(x => x.Element("type")?.Value == "eighth") > 0,
            "Beam recognition must preserve notes shorter than a quarter.");
        Assert.True(conversion.Analysis.Events.Count(x => x.Kind == "notehead-black") > 0,
            "MuseScore round black heads must not remain smufl-unknown.");
        Assert.Contains(conversion.Analysis.Events, x => x.Chord);

        Assert.Equal(comparison.Metrics.Expected,
            comparison.Metrics.Matched + comparison.Metrics.Mismatched + comparison.Metrics.Missing);
        Assert.Equal(comparison.Metrics.Actual,
            comparison.Metrics.Matched + comparison.Metrics.Mismatched + comparison.Metrics.Extra);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SvgToMusicXmlPoc.csproj")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Golden")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
