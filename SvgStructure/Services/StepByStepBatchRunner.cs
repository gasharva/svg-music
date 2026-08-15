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
    string? Error = null);

public sealed class StepByStepBatchRunner
{
    public const string ArtifactsDirectoryName = "_artifacts";

    private readonly SvgSceneGeometryReader _geometryReader = new();
    private readonly StaffSystemDetector _systemDetector = new();
    private readonly ScoreStructureBuilder _structureBuilder = new();
    private readonly MeasureOverlayRenderer _measureOverlayRenderer = new();
    private readonly PrimitiveClassificationRenderer _classificationRenderer = new(0.25);
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
            var sourceCopyPath = Path.Combine(itemDirectory, "source.svg");
            File.Copy(svgPath, sourceCopyPath, overwrite: true);

            var lines = _geometryReader.ReadLines(svgPath);
            var systems = _systemDetector.Detect(lines);
            var score = _structureBuilder.Build(systems);

            _measureOverlayRenderer.Render(
                svgPath,
                systems,
                Path.Combine(itemDirectory, "measures.png"));

            _classificationRenderer.Render(
                svgPath,
                systems,
                Path.Combine(itemDirectory, "classified.png"));

            WriteStructureJson(
                Path.Combine(itemDirectory, "structure.json"),
                fileName,
                lines.Count,
                systems.Count,
                score);

            return new StepByStepItemResult(
                fileName,
                stem,
                lines.Count,
                systems.Count,
                score.Parts.Count,
                score.Parts.FirstOrDefault()?.Measures.Count ?? 0);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(itemDirectory, "error.txt"), ex.ToString());
            return new StepByStepItemResult(fileName, stem, 0, 0, 0, 0, ex.Message);
        }
    }

    private static void WriteStructureJson(
        string path,
        string fileName,
        int lineCount,
        int systemCount,
        ScoreStructure score)
    {
        var payload = new
        {
            source = fileName,
            lineCount,
            systemCount,
            parts = score.Parts.Select(part => new
            {
                id = part.Id,
                measures = part.Measures.Select(measure => new
                {
                    number = measure.Number,
                    width = Math.Round(measure.Width, 4)
                })
            })
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
