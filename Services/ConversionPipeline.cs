using System.Diagnostics;
using System.Text.Json;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class ConversionPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AnalysisPipelineResult Analyze(
        string svgPath,
        string catalogPath,
        RecognitionConfig? config = null)
    {
        ValidateInput(svgPath, catalogPath);
        config ??= new RecognitionConfig();

        var performance = new ConversionPerformance();
        var total = Stopwatch.StartNew();
        var watch = Stopwatch.StartNew();

        var parser = new SvgParser();
        var document = parser.Load(svgPath);
        performance.ParseSvgMs = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        var staves = parser.DetectStaves(document, config.StaffTolerance);
        performance.DetectStavesMs = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        var uses = parser.ReadUses(document);
        var directPaths = parser.ReadDirectPaths(document);
        var lineSegments = parser.ReadLineSegments(document);
        performance.ReadInstancesMs = watch.Elapsed.TotalMilliseconds;

        var classifier = new SymbolClassifier();
        var classification = classifier.Classify(svgPath, staves, catalogPath);
        var cp = classifier.LastPerformance;
        performance.LoadCatalogMs = cp.LoadCatalogMs;
        performance.ClassifyMs = cp.ClassifyMs;
        performance.GlyphInstances = cp.GlyphInstances;
        performance.UniqueGeometries = cp.UniqueGeometries;
        performance.CatalogGlyphs = cp.CatalogGlyphs;
        performance.MaskComparisons = cp.MaskComparisons;
        performance.VectorComparisons = cp.VectorComparisons;
        performance.CatalogCacheHit = cp.CatalogCacheHit;

        watch.Restart();
        var analysis = new MusicSemanticRecognizer().Recognize(uses, staves, classification, config);
        analysis.DirectPaths.AddRange(directPaths);
        analysis.LineSegments.AddRange(lineSegments);

        // Relationship reconstruction deliberately runs after symbol recognition and uses
        // only raw geometry. This keeps CLI and tests on exactly the same conversion path.
        new MusicGeometryRelationResolver().Resolve(analysis, config);

        performance.RecognizeSemanticsMs = watch.Elapsed.TotalMilliseconds;
        performance.TotalMs = total.Elapsed.TotalMilliseconds;

        return new AnalysisPipelineResult(analysis, classification, performance);
    }

    public ConversionResult Convert(
        string svgPath,
        string catalogPath,
        string musicXmlPath,
        RecognitionConfig? config = null,
        bool writeDiagnostics = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(musicXmlPath);
        config ??= new RecognitionConfig();

        var result = Analyze(svgPath, catalogPath, config);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(musicXmlPath))!);

        var watch = Stopwatch.StartNew();
        new MusicXmlWriter().Write(musicXmlPath, result.Analysis, config);
        result.Performance.WriteMusicXmlMs = watch.Elapsed.TotalMilliseconds;
        result.Performance.TotalMs += result.Performance.WriteMusicXmlMs;

        string? analysisPath = null;
        string? classificationPath = null;
        if (writeDiagnostics)
        {
            analysisPath = Path.ChangeExtension(musicXmlPath, ".analysis.json");
            classificationPath = Path.ChangeExtension(musicXmlPath, ".classification.json");
            WriteJson(analysisPath, result.Analysis);
            WriteJson(classificationPath, result.Classification);
            WriteJson(Path.ChangeExtension(musicXmlPath, ".performance.json"), result.Performance);
        }

        return new ConversionResult(
            musicXmlPath,
            analysisPath,
            classificationPath,
            result.Analysis,
            result.Classification,
            result.Performance);
    }

    private static void ValidateInput(string svgPath, string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svgPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        if (!File.Exists(svgPath)) throw new FileNotFoundException("SVG not found.", svgPath);
        if (!File.Exists(catalogPath)) throw new FileNotFoundException("Reference catalog not found.", catalogPath);
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
}

public sealed record AnalysisPipelineResult(
    AnalysisResult Analysis,
    ClassificationResult Classification,
    ConversionPerformance Performance);

public sealed record ConversionResult(
    string MusicXmlPath,
    string? AnalysisPath,
    string? ClassificationPath,
    AnalysisResult Analysis,
    ClassificationResult Classification,
    ConversionPerformance Performance);
