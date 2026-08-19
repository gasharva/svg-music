using System.Collections.Concurrent;
using System.Diagnostics;
using GlyphPcaGallery.Models;
using GlyphPcaGallery.Services;

var options = Args.Parse(args);

if (!File.Exists(options.Models)) { Console.Error.WriteLine($"Model bundle not found: {options.Models}"); return 2; }
if (!Directory.Exists(options.Input)) { Console.Error.WriteLine($"Input folder not found: {options.Input}"); return 2; }

GlyphModelBundle bundle;
try
{
    bundle = GlyphModelBundleLoader.Load(options.Models);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not load model bundle: {ex.Message}");
    return 2;
}

var svgFiles = Directory.EnumerateFiles(options.Input, "*.svg", SearchOption.AllDirectories)
    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
    .ToArray();

Console.WriteLine($"Bundle: {Path.GetFullPath(options.Models)}");
Console.WriteLine($"Models: {bundle.Models.Count}");
Console.WriteLine($"SVG files: {svgFiles.Length}");
Console.WriteLine($"Parallelism: {options.Parallelism}");

Directory.CreateDirectory(options.Output);
var summaries = new Dictionary<string, BundleModelRunSummary>(StringComparer.OrdinalIgnoreCase);
var totalErrors = 0;

foreach (var (family, model) in bundle.Models.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
{
    var classes = model.References
        .Select(x => x.Class)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    Console.WriteLine();
    Console.WriteLine($"[{family}] {model.Pca.ComponentsCount}D PCA, {model.References.Count} references, {classes.Length} classes");
    Console.WriteLine($"[{family}] {string.Join(", ", classes)}");

    var analyzer = new GlyphFingerprintAnalyzer(model);
    var results = new ConcurrentBag<GlyphAnalysis>();
    var sw = Stopwatch.StartNew();
    var counter = 0;

    Parallel.ForEach(svgFiles, new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism }, file =>
    {
        var id = Interlocked.Increment(ref counter);
        var asset = $"{id:D5}-{Sanitize(Path.GetFileName(file))}";
        var res = analyzer.Analyze(file, asset);
        if (res == null)
            return;

        results.Add(res);
        if (id % 50 == 0 || id == svgFiles.Length)
            Console.WriteLine($"[{family}] {id}/{svgFiles.Length}");
    });

    sw.Stop();

    var ordered = results
        .OrderBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var familyOutput = Path.Combine(options.Output, family);
    GalleryBuilder.Build(familyOutput, ordered, sw.Elapsed, options.Parallelism);

    var errors = ordered.Count(x => x.Error is not null);
    totalErrors += errors;
    summaries[family] = new BundleModelRunSummary(
        ordered.Length,
        ordered.Length - errors,
        sw.Elapsed);

    Console.WriteLine($"[{family}] done in {sw.Elapsed.TotalSeconds:F2}s; errors={errors}");
    Console.WriteLine($"[{family}] gallery: {Path.GetFullPath(Path.Combine(familyOutput, "index.html"))}");
}

BundleGalleryBuilder.Build(options.Output, options.Models, bundle, summaries);

Console.WriteLine();
Console.WriteLine($"Bundle gallery: {Path.GetFullPath(Path.Combine(options.Output, "index.html"))}");
return totalErrors == 0 ? 0 : 1;

static string Sanitize(string name)
{
    foreach (var c in Path.GetInvalidFileNameChars())
        name = name.Replace(c, '_');
    return name;
}

file sealed record Args(string Models, string Input, string Output, int Parallelism)
{
    public static Args Parse(string[] args)
    {
        string? models = null, input = null, output = null;
        var parallelism = Math.Max(1, Environment.ProcessorCount);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--models": models = args[++i]; break;
                case "--input": input = args[++i]; break;
                case "--output": output = args[++i]; break;
                case "--parallelism": parallelism = int.Parse(args[++i]); break;
                case "-h":
                case "--help":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        if (models is null || input is null)
        {
            PrintHelp();
            Environment.Exit(2);
        }

        output ??= Path.Combine(Environment.CurrentDirectory, "glyph-pca-gallery");
        return new Args(models!, input!, output, Math.Max(1, parallelism));
    }

    private static void PrintHelp() => Console.WriteLine(
        "dotnet run --project GlyphPcaGallery -- --models glyph-models.zip --input <svg-folder> --output glyph-pca-gallery --parallelism 8");
}
