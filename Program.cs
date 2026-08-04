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

var parser = new SvgParser();
var document = parser.Load(svgPath);

switch (command)
{
    case "symbols":
        foreach (var pair in parser.CountSymbols(document))
            Console.WriteLine($"{pair.Key,-8} {pair.Value,4}");
        return 0;

    case "classify":
    {
        if (args.Length < 4)
        {
            PrintUsage();
            return 1;
        }

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

        var config = new RecognitionConfig();
        var staves = parser.DetectStaves(document, config.StaffTolerance);
        var uses = parser.ReadUses(document);

        // The same vector classifier is now the mandatory first stage for both
        // analyze and convert. No recognition.json/manual SymbolKinds are used.
        var classification = new SymbolClassifier().Classify(svgPath, staves, catalogPath);
        var analysis = new MusicSemanticRecognizer().Recognize(uses, staves, classification, config);

        if (command == "analyze")
        {
            WriteJson(output, analysis);
        }
        else
        {
            new MusicXmlWriter().Write(output, analysis, config);
            WriteJson(Path.ChangeExtension(output, ".analysis.json"), analysis);
            WriteJson(Path.ChangeExtension(output, ".classification.json"), classification);
        }

        var notes = analysis.Events.Count(x => x.Step is not null);
        var rests = analysis.Events.Count(x => x.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase));
        var dots = analysis.Events.Count(x => x.Dotted);
        Console.WriteLine($"Станов: {staves.Count}; use: {uses.Count}; нот: {notes}; пауз: {rests}; точек: {dots}");
        Console.WriteLine($"Предупреждений: {analysis.Warnings.Count}");
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

convert теперь всегда использует векторную классификацию по Bravura/SMuFL,
затем связывает головки, паузы, точки, альтерации и ключи и пишет MusicXML.
Рядом создаются *.analysis.json и *.classification.json.
""");
}
