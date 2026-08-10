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

        new MusicGeometryRelationResolver().Resolve(analysis, config);
        // Long sloped beams can have a tall axis-aligned bounding box even though the painted
        // strip itself is thin. Recover them by strip thickness and stem/path intersection.
        new SlopedBeamRhythmResolver().Resolve(analysis, config);
        // Exact path slices can miss edge stems or tiny exporter gaps. Once a long thin beam
        // has been identified, fit its centreline and complete the whole stem group against it.
        new SlopedBeamCoverageResolver().Resolve(analysis, config);
        // Reattach written accidentals after staff ownership/pitch/chords are final. In close
        // intervals noteheads are displaced horizontally, so staff-position (Y) must outrank X.
        new AccidentalGeometryResolver().Resolve(analysis, config);
        // Augmentation dots belong to the whole rhythmic chord. A single SVG dot can be
        // associated with only one notehead by the symbol recognizer, so normalize the
        // shared-stem chord before any MusicXML is written.
        new ChordRhythmNormalizer().Normalize(analysis);
        // A 16th-note hook is much shorter than an ordinary beam and was intentionally
        // filtered out by the general beam detector. Detect that second beam level separately
        // and also restore dotted duration after beam-derived note types are assigned.
        new BeamHookRhythmResolver().Resolve(analysis, config);
        // Standalone flags are compact SMuFL glyphs attached to a free stem end rather than
        // beams. Reuse the normal classifier and attach flag8th/flag16th/... geometrically.
        new StandaloneFlagRhythmResolver().Resolve(analysis, config);

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
        new MusicXmlStemPostProcessor().Apply(musicXmlPath, result.Analysis);
        // Preserve the SVG's horizontal engraving coordinates before voice reordering.
        // The following voice-layout pass reuses the same note elements, so default-x survives.
        new MusicXmlSvgLayoutPostProcessor().Apply(musicXmlPath, result.Analysis);
        // Render each detected voice as a complete lane. Do not serialize one simultaneous
        // onset at a time: that can move continuation notes behind a long parallel chord.
        new MusicXmlVoiceLayoutPostProcessor().Apply(musicXmlPath, result.Analysis);
        // If the first voice pass left opposite-stem notes and a rest in one voice, use occupied
        // onset geometry to recover the missing parallel voice before later notation passes.
        new MusicXmlRestVoiceConflictPostProcessor().Apply(musicXmlPath);
        // MusicXmlWriter emits clef attributes at the first measure of every detected SVG staff
        // system. Preserve those source line breaks so MuseScore does not repack measures.
        new MusicXmlSystemBreakPostProcessor().Apply(musicXmlPath);
        // MusicXmlWriter emits the primary beam. Add beam level 2 after voice ordering, so
        // isolated secondary hooks become forward/backward hooks and adjacent 16ths connect.
        new MusicXmlSecondaryBeamPostProcessor().Apply(musicXmlPath);
        // A written accidental changes pitch spelling for later equal pitches on the same staff
        // until the barline, but the accidental glyph itself is not repeated.
        new MusicXmlAccidentalStatePostProcessor().Apply(musicXmlPath);
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
