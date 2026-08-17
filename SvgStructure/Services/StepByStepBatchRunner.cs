using System.Text.Json;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

public sealed record StepByStepBatchResult(
    string InputFolder,
    string ArtifactsFolder,
    string HtmlReportPath,
    string MarkdownReportPath,
    IReadOnlyList<StepByStepItemResult> Items);

public sealed record StepByStepItemResult(
    string FileName,
    string ArtifactDirectoryName,
    int LineCount,
    int SystemCount,
    int PartCount,
    int MeasureCount,
    int PartMeasurePrimitiveCount = 0,
    int MeasurePrimitiveCount = 0,
    int PhysicalOnlyPrimitiveCount = 0,
    int MusicSymbolCount = 0,
    int MeterCount = 0,
    int ClefCount = 0,
    int ExportedPrimitiveCount = 0,
    int SourceElementCount = 0,
    int SourceUseCount = 0,
    string? Error = null);

public sealed class StepByStepBatchRunner
{
    public const string ArtifactsDirectoryName = "_artifacts";
    public const int DefaultSubdivisionsPerBeat = 8;

    private readonly PartMeasureResolver _partMeasureResolver = new();
    private readonly PrimitiveResolver _primitiveResolver = new(0.25);
    private readonly PrimitiveSvgExporter _primitiveSvgExporter = new();
    private readonly MusicSymbolResolver _musicSymbolResolver = new();
    private readonly MusicSymbolSvgExporter _musicSymbolSvgExporter = new();
    private readonly SvgSourceModelDumper _sourceModelDumper = new();
    private readonly LogicalGridResolver _logicalGridResolver = new(DefaultSubdivisionsPerBeat);
    private readonly PartMeasureOverlayRenderer _partMeasureOverlayRenderer = new();
    private readonly PrimitiveOverlayRenderer _primitiveOverlayRenderer = new();
    private readonly MeterOverlayRenderer _meterOverlayRenderer = new();
    private readonly StepByStepReportBuilder _reportBuilder = new();

    public StepByStepBatchResult Run(string inputFolder)
    {
        inputFolder = Path.GetFullPath(inputFolder);
        var artifactsFolder = Path.Combine(inputFolder, ArtifactsDirectoryName);

        if (Directory.Exists(artifactsFolder))
            Directory.Delete(artifactsFolder, recursive: true);
        Directory.CreateDirectory(artifactsFolder);

        var repositoryRoot = FindRepositoryRoot(inputFolder);
        var recognizerWork = Path.Combine(Path.GetTempPath(), $"svg-music-recognizers-{Guid.NewGuid():N}");
        var glyphs = Path.Combine(repositoryRoot, "References", "glyphs");
        var glyphPcaModel = Path.Combine(repositoryRoot, "GlyphPcaGallery", "glyph-model.json");

        var baseNumberRecognizer = new GlyphPcaNumberRecognizer(
            glyphPcaModel,
            Path.Combine(recognizerWork, "meter-pca"),
            minimumConfidence: 0.20);
        var diagnosticNumberRecognizer = new DiagnosticNumberRecognizer(baseNumberRecognizer);
        var meterResolver = new MeterResolver(diagnosticNumberRecognizer);

        var baseClefRecognizer = new GlyphPcaClefRecognizer(
            glyphPcaModel,
            Path.Combine(recognizerWork, "clef-pca"));
        var legacyIoUClefAnalyzer = new LegacyIoUClefAnalyzer(glyphs);
        var diagnosticClefRecognizer = new DiagnosticClefRecognizer(baseClefRecognizer, legacyIoUClefAnalyzer);
        var clefResolver = new ClefResolver(
            diagnosticClefRecognizer,
            minimumConfidence: 0.70);

        try
        {
            var svgFiles = Directory
                .EnumerateFiles(inputFolder, "*.svg", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var items = new List<StepByStepItemResult>();
            foreach (var svgPath in svgFiles)
                items.Add(Process(
                    svgPath,
                    artifactsFolder,
                    meterResolver,
                    clefResolver,
                    diagnosticNumberRecognizer,
                    diagnosticClefRecognizer));

            var htmlReportPath = Path.Combine(artifactsFolder, "index.html");
            var markdownReportPath = Path.Combine(artifactsFolder, "README.md");
            _reportBuilder.WriteHtml(htmlReportPath, items);
            _reportBuilder.WriteMarkdown(markdownReportPath, items);

            return new StepByStepBatchResult(
                inputFolder,
                artifactsFolder,
                htmlReportPath,
                markdownReportPath,
                items);
        }
        finally
        {
            try { if (Directory.Exists(recognizerWork)) Directory.Delete(recognizerWork, recursive: true); }
            catch { }
        }
    }

    private StepByStepItemResult Process(
        string svgPath,
        string artifactsFolder,
        MeterResolver meterResolver,
        ClefResolver clefResolver,
        DiagnosticNumberRecognizer diagnosticNumberRecognizer,
        DiagnosticClefRecognizer diagnosticClefRecognizer)
    {
        var fileName = Path.GetFileName(svgPath);
        var stem = Path.GetFileNameWithoutExtension(svgPath);
        var itemDirectory = Path.Combine(artifactsFolder, stem);
        Directory.CreateDirectory(itemDirectory);

        try
        {
            File.Copy(svgPath, Path.Combine(itemDirectory, "source.svg"), overwrite: true);

            var sourceModel = _sourceModelDumper.Dump(svgPath, itemDirectory);

            var structure = _partMeasureResolver.Resolve(svgPath);
            var primitives = _primitiveResolver.Resolve(structure);
            var primitiveExport = _primitiveSvgExporter.Export(primitives, itemDirectory);

            var musicSymbols = _musicSymbolResolver.Resolve(primitives);
            _musicSymbolSvgExporter.Export(musicSymbols, itemDirectory);

            diagnosticNumberRecognizer.BeginDocument(Path.Combine(itemDirectory, "meter-inputs"));

            var meters = structure.Map.Blocks
                .Select(block => meterResolver.Resolve(block, musicSymbols))
                .Where(x => x is not null)
                .Select(x => x!)
                .ToArray();

            var logicalGrid = _logicalGridResolver.Resolve(structure, meters);

            diagnosticClefRecognizer.BeginDocument(Path.Combine(itemDirectory, "clef-inputs"));
            var clefs = structure.Map.Blocks
                .SelectMany(block => clefResolver.Resolve(block, musicSymbols, logicalGrid))
                .ToArray();

            _partMeasureOverlayRenderer.Render(
                structure,
                Path.Combine(itemDirectory, "measures.png"));
            _primitiveOverlayRenderer.Render(
                primitives,
                Path.Combine(itemDirectory, "classified.png"));
            _meterOverlayRenderer.Render(
                structure,
                meters,
                clefs,
                Path.Combine(itemDirectory, "meters.png"));

            WriteResolutionJson(
                Path.Combine(itemDirectory, "structure.json"),
                fileName,
                structure,
                primitives,
                musicSymbols,
                meters,
                logicalGrid,
                clefs);

            return new StepByStepItemResult(
                fileName,
                stem,
                structure.LineCount,
                structure.SystemCount,
                structure.Parts.Count,
                structure.Measures.Count,
                primitives.PartMeasurePrimitives.Count,
                primitives.MeasurePrimitives.Count,
                primitives.PhysicalOnlyPrimitives.Count,
                musicSymbols.Candidates.Count,
                meters.Length,
                clefs.Length,
                primitiveExport.Items.Count,
                sourceModel.ElementCount,
                sourceModel.UseCount);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(itemDirectory, "error.txt"), ex.ToString());
            return new StepByStepItemResult(
                fileName, stem, 0, 0, 0, 0,
                Error: ex.Message);
        }
    }

    private static void WriteResolutionJson(
        string path,
        string fileName,
        PartMeasureResolution structure,
        PrimitiveResolution primitives,
        MusicSymbolResolution musicSymbols,
        IReadOnlyList<MeterResolution> meters,
        LogicalGridResolution logicalGrid,
        IReadOnlyList<ClefResolution> clefs)
    {
        var payload = new
        {
            source = fileName,
            structure = new
            {
                lineCount = structure.LineCount,
                systemCount = structure.SystemCount,
                pageBounds = structure.Map.PageBounds,
                parts = structure.Parts,
                measures = structure.Measures,
                blocks = structure.Map.Blocks
            },
            primitives = primitives.Primitives.Select(x => new
            {
                x.Id,
                x.Scope,
                x.PartNumber,
                x.MeasureNumber,
                x.PhysicalBounds,
                source = new
                {
                    x.Source.Anchor,
                    x.Source.GroupAnchor,
                    x.Source.ReferenceAnchor,
                    x.Source.InstanceX,
                    x.Source.InstanceY,
                    x.Source.ElementType,
                    x.Source.ElementId,
                    x.Source.ElementAddress,
                    x.Source.IsExplicitUse,
                    groupContourCount = x.SourceGroupContours?.Count
                },
                contourPointCount = x.Contour.Points.Count
            }),
            musicSymbols = musicSymbols.Candidates.Select(x => new
            {
                x.Id,
                x.ParentCandidateId,
                x.IsDerived,
                x.Scope,
                x.PartNumber,
                x.MeasureNumber,
                x.PhysicalBounds,
                x.PrimitiveIds,
                smoothPathCount = x.SmoothPaths.Count,
                sourceAddresses = x.Sources.Select(s => s.ElementAddress ?? s.Anchor).ToArray()
            }),
            meters,
            logicalGrid = new
            {
                subdivisionsPerBeat = DefaultSubdivisionsPerBeat,
                blocks = logicalGrid.Blocks.Select(x => new
                {
                    x.PartNumber,
                    x.MeasureNumber,
                    x.BeatNumber,
                    x.BeatValue,
                    x.SubdivisionsPerBeat,
                    x.HorizontalUnits,
                    x.HalfStaffSpace,
                    x.PhysicalBounds
                })
            },
            clefs
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }

    private static string FindRepositoryRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SvgToMusicXmlPoc.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root above input folder.");
    }
}
