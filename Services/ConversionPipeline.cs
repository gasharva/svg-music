using System.Diagnostics;
using System.Text.Json;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class ConversionPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConversionResult Convert(string svgPath, string catalogPath, string musicXmlPath,
        RecognitionConfig? config = null, bool writeDiagnostics = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svgPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(musicXmlPath);
        if (!File.Exists(svgPath)) throw new FileNotFoundException("SVG not found.", svgPath);
        if (!File.Exists(catalogPath)) throw new FileNotFoundException("Reference catalog not found.", catalogPath);

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
        performance.RecognizeSemanticsMs = watch.Elapsed.TotalMilliseconds;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(musicXmlPath))!);
        watch.Restart();
        new MusicXmlWriter().Write(musicXmlPath, analysis, config);
        performance.WriteMusicXmlMs = watch.Elapsed.TotalMilliseconds;
        performance.TotalMs = total.Elapsed.TotalMilliseconds;

        string? analysisPath = null;
        string? classificationPath = null;
        if (writeDiagnostics)
        {
            analysisPath = Path.ChangeExtension(musicXmlPath, ".analysis.json");
            classificationPath = Path.ChangeExtension(musicXmlPath, ".classification.json");
            File.WriteAllText(analysisPath, JsonSerializer.Serialize(analysis, JsonOptions));
            File.WriteAllText(classificationPath, JsonSerializer.Serialize(classification, JsonOptions));
            File.WriteAllText(Path.ChangeExtension(musicXmlPath, ".performance.json"), JsonSerializer.Serialize(performance, JsonOptions));
        }

        return new ConversionResult(musicXmlPath, analysisPath, classificationPath, analysis, classification, performance);
    }
}

public sealed record ConversionResult(string MusicXmlPath, string? AnalysisPath, string? ClassificationPath,
    AnalysisResult Analysis, ClassificationResult Classification, ConversionPerformance Performance);
