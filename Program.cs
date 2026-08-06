using System.Text.Json;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Services;

if (args.Length < 2)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();
var svgPath = args[1];
if (!File.Exists(svgPath))
{
    Console.Error.WriteLine($"SVG не найден: {svgPath}");
    return 2;
}

switch (command)
{
    case "symbols":
    {
        var parser = new SvgParser();
        var document = parser.Load(svgPath);
        foreach (var pair in parser.CountSymbols(document))
            Console.WriteLine($"{pair.Key,-8} {pair.Value,4}");
        return 0;
    }

    case "classify":
    {
        if (args.Length < 4)
        {
            PrintUsage();
            return 1;
        }

        var parser = new SvgParser();
        var document = parser.Load(svgPath);
        var staves = parser.DetectStaves(document);
        var result = new SymbolClassifier().Classify(svgPath, staves, args[2]);
        WriteJson(args[3], result);
        PrintClassifications(result);
        Console.WriteLine($"Классифицировано символов: {result.Symbols.Count}; создано: {args[3]}");
        return 0;
    }

    case "analyze":
    case "convert":
    {
        if (args.Length < 4)
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

        var pipeline = new ConversionPipeline();
        var config = new RecognitionConfig();

        AnalysisPipelineResult pipelineResult;
        if (command == "analyze")
        {
            pipelineResult = pipeline.Analyze(svgPath, catalogPath, config);
            WriteJson(output, pipelineResult.Analysis);
            WriteJson(Path.ChangeExtension(output, ".classification.json"), pipelineResult.Classification);
            WriteJson(Path.ChangeExtension(output, ".performance.json"), pipelineResult.Performance);
        }
        else
        {
            var conversion = pipeline.Convert(svgPath, catalogPath, output, config, writeDiagnostics: true);
            pipelineResult = new AnalysisPipelineResult(
                conversion.Analysis,
                conversion.Classification,
                conversion.Performance);
        }

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

static void PrintUsage()
{
    Console.WriteLine("""
SVG → MusicXML PoC

Команды:
  dotnet run -- symbols  <score.svg>
  dotnet run -- classify <score.svg> <References/catalog.json> <classification.json>
  dotnet run -- analyze  <score.svg> <References/catalog.json> <analysis.json>
  dotnet run -- convert  <score.svg> <References/catalog.json> <score.musicxml>

analyze и convert используют один и тот же ConversionPipeline.
convert дополнительно пишет MusicXML, а рядом создаёт *.analysis.json,
*.classification.json и *.performance.json.
""");
}
