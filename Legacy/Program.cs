using System.Text.Json;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Services;

if (args.Length < 2)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();
var inputPath = args[1];
var inputIsFile = File.Exists(inputPath);
var inputIsDirectory = Directory.Exists(inputPath);
if (!inputIsFile && !inputIsDirectory)
{
    Console.Error.WriteLine($"SVG или папка не найдены: {inputPath}");
    return 2;
}

switch (command)
{
    case "symbols":
    {
        if (!inputIsFile)
        {
            Console.Error.WriteLine("Команда symbols принимает один SVG-файл.");
            return 1;
        }

        var parser = new SvgParser();
        var document = parser.Load(inputPath);
        foreach (var pair in parser.CountSymbols(document))
            Console.WriteLine($"{pair.Key,-8} {pair.Value,4}");
        return 0;
    }

    case "classify":
    {
        if (!inputIsFile || args.Length < 4)
        {
            PrintUsage();
            return 1;
        }

        var parser = new SvgParser();
        var document = parser.Load(inputPath);
        var staves = parser.DetectStaves(document);
        var result = new SymbolClassifier().Classify(inputPath, staves, args[2]);
        WriteJson(args[3], result);
        PrintClassifications(result);
        Console.WriteLine($"Классифицировано символов: {result.Symbols.Count}; создано: {args[3]}");
        return 0;
    }

    case "analyze":
    {
        if (!inputIsFile || args.Length < 4)
        {
            PrintUsage();
            return 1;
        }

        var catalogPath = args[2];
        var output = args[3];
        if (!File.Exists(catalogPath))
        {
            Console.Error.WriteLine($"Каталог эталонов не найден: {catalogPath}");
            return 3;
        }

        var pipelineResult = new ConversionPipeline().Analyze(inputPath, catalogPath, new RecognitionConfig());
        WriteJson(output, pipelineResult.Analysis);
        WriteJson(Path.ChangeExtension(output, ".classification.json"), pipelineResult.Classification);
        WriteJson(Path.ChangeExtension(output, ".performance.json"), pipelineResult.Performance);
        PrintAnalysisSummary(pipelineResult, output);
        return 0;
    }

    case "convert":
    {
        if (args.Length < 3)
        {
            PrintUsage();
            return 1;
        }

        var catalogPath = args[2];
        if (!File.Exists(catalogPath))
        {
            Console.Error.WriteLine($"Каталог эталонов не найден: {catalogPath}");
            return 3;
        }

        if (inputIsDirectory)
        {
            var output = args.Length >= 4 ? args[3] : null;
            try
            {
                var result = new MultiSvgConversionService().ConvertDirectory(
                    inputPath,
                    catalogPath,
                    output,
                    new RecognitionConfig());
                Console.WriteLine($"SVG-страниц: {result.SvgFiles.Count}");
                foreach (var svg in result.SvgFiles)
                    Console.WriteLine($"  {Path.GetFileName(svg)}");
                Console.WriteLine($"Диагностика страниц: {result.PageDiagnosticsDirectory}");
                Console.WriteLine($"Создано: {result.MusicXmlPath}");
                return 0;
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or InvalidOperationException)
            {
                Console.Error.WriteLine(ex.Message);
                return 4;
            }
        }

        if (args.Length < 4)
        {
            Console.Error.WriteLine("Для одного SVG-файла нужно указать выходной MusicXML.");
            PrintUsage();
            return 1;
        }

        var fileOutput = args[3];
        var conversion = new ConversionPipeline().Convert(
            inputPath,
            catalogPath,
            fileOutput,
            new RecognitionConfig(),
            writeDiagnostics: true);
        var pipelineResult = new AnalysisPipelineResult(
            conversion.Analysis,
            conversion.Classification,
            conversion.Performance);
        PrintAnalysisSummary(pipelineResult, fileOutput);
        return 0;
    }

    default:
        PrintUsage();
        return 1;
}

static void WriteJson<T>(string path, T value) =>
    File.WriteAllText(path, JsonSerializer.Serialize(value,
        new JsonSerializerOptions { WriteIndented = true }));

static void PrintClassifications(SvgToMusicXmlPoc.Models.ClassificationResult result)
{
    foreach (var item in result.Symbols.OrderByDescending(x => x.Score))
        Console.WriteLine($"{item.SymbolId,-8} {item.Kind,-24} score={item.Score:F3} shape={item.ShapeScore:F3} size={item.SizeScore:F3}");
}

static void PrintAnalysisSummary(AnalysisPipelineResult pipelineResult, string output)
{
    var analysis = pipelineResult.Analysis;
    var notes = analysis.Events.Count(x => x.Step is not null);
    var rests = analysis.Events.Count(x => x.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase));
    var dots = analysis.Events.Count(x => x.Dotted);
    Console.WriteLine(
        $"Станов: {analysis.Staves.Count}; use: {analysis.Uses.Count}; " +
        $"path: {analysis.DirectPaths.Count}; lines: {analysis.LineSegments.Count}; " +
        $"нот: {notes}; пауз: {rests}; точек: {dots}");
    Console.WriteLine($"Предупреждений: {analysis.Warnings.Count}");
    Console.WriteLine($"Время pipeline: {pipelineResult.Performance.TotalMs:F1} ms");
    Console.WriteLine($"Создано: {output}");
}

static void PrintUsage()
{
    Console.WriteLine("""
SVG → MusicXML PoC

Команды:
  dotnet run -- symbols  <score.svg>
  dotnet run -- classify <score.svg> <References/catalog.json> <classification.json>
  dotnet run -- analyze  <score.svg> <References/catalog.json> <analysis.json>
  dotnet run -- convert  <score.svg> <References/catalog.json> <score.musicxml>
  dotnet run -- convert  <folder>    <References/catalog.json> [score.musicxml]

Если convert получает папку, все *.svg верхнего уровня обрабатываются в естественном
порядке имён (page2.svg перед page10.svg) и объединяются в один MusicXML.
Если выходной файл не указан, создаётся <folder>/<folder-name>.musicxml.
Для каждой SVG-страницы рядом с итоговым MusicXML создаётся папка <score>.pages
с промежуточным MusicXML, analysis/classification/performance JSON.

analyze и convert одного файла используют один и тот же ConversionPipeline.
convert одного файла дополнительно пишет *.analysis.json, *.classification.json
и *.performance.json.
""");
}
