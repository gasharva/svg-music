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
        var pageGeometry = parser.ReadPageGeometry(document);
        var lineSegments = parser.ReadLineSegments(document);
        lineSegments.AddRange(new CompoundVerticalStrokeExtractor().Extract(pageGeometry, staves, lineSegments));
        performance.ReadInstancesMs = watch.Elapsed.TotalMilliseconds;

        var classifier = new SymbolClassifier();
        var classification = classifier.Classify(svgPath, staves, catalogPath);
        new SourceFontSemanticNormalizer().Normalize(svgPath, staves, classification, lineSegments);
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
        analysis.PageGeometry.AddRange(pageGeometry);

        analysis.DirectPaths.AddRange(pageGeometry.Select(x =>
            new SvgDirectPath(x.InstanceId, x.Geometry, x.X, x.Y)));
        analysis.LineSegments.AddRange(lineSegments);

        // Low-confidence clefs can still be structurally decisive: losing the upper G clef on a
        // continuation page makes every grand staff collapse into two unrelated systems.
        new StaffClefRecoveryResolver().Resolve(analysis);
        new PaintedGlyphPositionNormalizer().Normalize(analysis);
        new LongStemRelationResolver().Resolve(analysis);
        new MusicGeometryRelationResolver().Resolve(analysis, config);
        new PolyphonicSharedStemResolver().Resolve(analysis);
        new StemlessHollowFalsePositiveResolver().Resolve(analysis);
        new SlopedBeamRhythmResolver().Resolve(analysis, config);
        new SlopedBeamCoverageResolver().Resolve(analysis, config);
        new UnifiedBeamGeometryResolver().Resolve(analysis, config);
        new AccidentalGeometryResolver().Resolve(analysis, config);
        new ArcSemanticsResolver().Resolve(analysis);
        new ChordRhythmNormalizer().Normalize(analysis);
        new BeamHookRhythmResolver().Resolve(analysis, config);
        new StandaloneFlagRhythmResolver().Resolve(analysis, config);
        new DynamicsGeometryResolver().Resolve(analysis);

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
        new MusicXmlMeterRecoveryPostProcessor().Apply(musicXmlPath, result.Analysis, config);
        new MusicXmlStemPostProcessor().Apply(musicXmlPath, result.Analysis);
        new MusicXmlGraceNotePostProcessor().Apply(musicXmlPath, result.Analysis);
        new MusicXmlSvgLayoutPostProcessor().Apply(musicXmlPath, result.Analysis);
        new MusicXmlVoiceLayoutPostProcessor().Apply(musicXmlPath, result.Analysis);
        new MusicXmlRestVoiceConflictPostProcessor().Apply(musicXmlPath);
        new MusicXmlGraceVoiceTimingPostProcessor().Apply(musicXmlPath);
        new MusicXmlSystemBreakPostProcessor().Apply(musicXmlPath);
        new MusicXmlSecondaryBeamPostProcessor().Apply(musicXmlPath);
        new MusicXmlAccidentalStatePostProcessor().Apply(musicXmlPath);
        new MusicXmlDynamicsPostProcessor().Apply(musicXmlPath, result.Analysis);
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
