using System.Text.Json;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class ConversionPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConversionResult Convert(
        string svgPath,
        string catalogPath,
        string musicXmlPath,
        RecognitionConfig? config = null,
        bool writeDiagnostics = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svgPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(musicXmlPath);
        if (!File.Exists(svgPath)) throw new FileNotFoundException("SVG not found.", svgPath);
        if (!File.Exists(catalogPath)) throw new FileNotFoundException("Reference catalog not found.", catalogPath);

        config ??= new RecognitionConfig();
        var parser = new SvgParser();
        var document = parser.Load(svgPath);
        var staves = parser.DetectStaves(document, config.StaffTolerance);
        var uses = parser.ReadUses(document);
        var classification = new SymbolClassifier().Classify(svgPath, staves, catalogPath);
        var analysis = new MusicSemanticRecognizer().Recognize(uses, staves, classification, config);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(musicXmlPath))!);
        new MusicXmlWriter().Write(musicXmlPath, analysis, config);

        string? analysisPath = null;
        string? classificationPath = null;
        if (writeDiagnostics)
        {
            analysisPath = Path.ChangeExtension(musicXmlPath, ".analysis.json");
            classificationPath = Path.ChangeExtension(musicXmlPath, ".classification.json");
            File.WriteAllText(analysisPath, JsonSerializer.Serialize(analysis, JsonOptions));
            File.WriteAllText(classificationPath, JsonSerializer.Serialize(classification, JsonOptions));
        }

        return new ConversionResult(musicXmlPath, analysisPath, classificationPath, analysis, classification);
    }
}

public sealed record ConversionResult(
    string MusicXmlPath,
    string? AnalysisPath,
    string? ClassificationPath,
    AnalysisResult Analysis,
    ClassificationResult Classification);
