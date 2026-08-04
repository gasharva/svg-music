using SvgToMusicXmlPoc.Quality;
using SvgToMusicXmlPoc.Services;

namespace SvgToMusicXmlPoc.Tests;

public sealed class GoldenMusicXmlQualityTests
{
    [Fact]
    public void YellowLeaves_SvgConversion_ProducesSemanticQualityReport()
    {
        var root = FindRepositoryRoot();
        var goldenDirectory = Path.Combine(root, "Golden");
        var outputDirectory = Path.Combine(root, "TestResults", "golden-quality", "yellow-leaves-giya-kancheli");
        Directory.CreateDirectory(outputDirectory);

        var svgPath = Path.Combine(goldenDirectory, "yellow-leaves-giya-kancheli.svg");
        var expectedPath = Path.Combine(goldenDirectory, "yellow-leaves-giya-kancheli.musicxml");
        var catalogPath = Path.Combine(root, "References", "catalog.json");
        var actualPath = Path.Combine(outputDirectory, "actual.musicxml");

        Assert.True(File.Exists(svgPath), $"Golden SVG not found: {svgPath}");
        Assert.True(File.Exists(expectedPath), $"Golden MusicXML not found: {expectedPath}");
        Assert.True(File.Exists(catalogPath), $"SMuFL catalog not found: {catalogPath}");

        var conversion = new ConversionPipeline().Convert(svgPath, catalogPath, actualPath);

        var comparison = new MusicXmlSemanticComparer().Compare(expectedPath, actualPath);
        new QualityReportWriter().Write(comparison, outputDirectory);

        Assert.True(File.Exists(actualPath));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "quality-report.csv")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "quality-report.md")));
        Assert.True(comparison.Metrics.Expected > 0, "Golden MusicXML must contain semantic events.");

        // This golden export is path-only. These assertions protect the first stage of
        // the pipeline independently of later staff and musical-semantic recognition.
        Assert.NotEmpty(conversion.Analysis.Uses);
        Assert.All(conversion.Analysis.Uses, x => Assert.Equal("path", x.SourceKind));
        Assert.Equal(conversion.Analysis.Uses.Count, conversion.Classification.Symbols.Count);

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
