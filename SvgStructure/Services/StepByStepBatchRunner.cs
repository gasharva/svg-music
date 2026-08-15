using System.Text.Json;
using SvgStructure.Models;

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
    string? Error = null);

public sealed class StepByStepBatchRunner
{
    public const string ArtifactsDirectoryName = "_artifacts";

    private readonly PartMeasureResolver _partMeasureResolver = new();
    private readonly PrimitiveResolver _primitiveResolver = new(0.25);
    private readonly PartMeasureOverlayRenderer _partMeasureOverlayRenderer = new();
    private readonly PrimitiveOverlayRenderer _primitiveOverlayRenderer = new();
    private readonly StepByStepReportBuilder _reportBuilder = new();

    public StepByStepBatchResult Run(string inputFolder)
    {
        inputFolder = Path.GetFullPath(inputFolder);
        var artifactsFolder = Path.Combine(inputFolder, ArtifactsDirectoryName);

        if (Directory.Exists(artifactsFolder))
            Directory.Delete(artifactsFolder, recursive: true);
        Directory.CreateDirectory(artifactsFolder);

        var svgFiles = Directory
            .EnumerateFiles(inputFolder, "*.svg", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = new List<StepByStepItemResult>();
        foreach (var svgPath in svgFiles)
            items.Add(Process(svgPath, artifactsFolder));

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

    private StepByStepItemResult Process(string svgPath, string artifactsFolder)
    {
        var fileName = Path.GetFileName(svgPath);
        var stem = Path.GetFileNameWithoutExtension(svgPath);
        var itemDirectory = Path.Combine(artifactsFolder, stem);
        Directory.CreateDirectory(itemDirectory);

        try
        {
            File.Copy(svgPath, Path.Combine(itemDirectory, "source.svg"), overwrite: true);

            var structure = _partMeasureResolver.Resolve(svgPath);
            var primitives = _primitiveResolver.Resolve(structure);

            _partMeasureOverlayRenderer.Render(
                structure,
                Path.Combine(itemDirectory, "measures.png"));
            _primitiveOverlayRenderer.Render(
                primitives,
                Path.Combine(itemDirectory, "classified.png"));

            WriteResolutionJson(
                Path.Combine(itemDirectory, "structure.json"),
                fileName,
                structure,
                primitives);

            return new StepByStepItemResult(
                fileName,
                stem,
                structure.LineCount,
                structure.SystemCount,
                structure.Parts.Count,
                structure.Measures.Count,
                primitives.PartMeasurePrimitives.Count,
                primitives.MeasurePrimitives.Count,
                primitives.PhysicalOnlyPrimitives.Count);
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
        PrimitiveResolution primitives)
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
                x.PhysicalBounds
            })
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }
}
