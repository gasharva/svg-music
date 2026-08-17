using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using GlyphPcaGallery.Models;
using GlyphPcaGallery.Services;

var options = Args.Parse(args);

if (!File.Exists(options.Model)) { Console.Error.WriteLine($"Model not found: {options.Model}"); return 2; }
if (!Directory.Exists(options.Input)) { Console.Error.WriteLine($"Input folder not found: {options.Input}"); return 2; }

var json = await File.ReadAllTextAsync(options.Model);
var model = JsonSerializer.Deserialize<GlyphModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidDataException("Could not deserialize glyph model.");

var svgFiles = Directory.EnumerateFiles(options.Input, "*.svg", SearchOption.AllDirectories)
    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

Console.WriteLine($"Model: {model.Pca.ComponentsCount}D PCA, {model.References.Count} references, {model.References.Select(x => x.Class).Distinct().Count()} classes");
Console.WriteLine($"SVG files: {svgFiles.Length}");
Console.WriteLine($"Parallelism: {options.Parallelism}");

var analyzer = new GlyphFingerprintAnalyzer(model);
var results = new ConcurrentBag<GlyphAnalysis>();
var sw = Stopwatch.StartNew();
var counter = 0;

Parallel.ForEach(svgFiles, new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism }, file =>
{
    var id = Interlocked.Increment(ref counter);
    var asset = $"{id:D5}-{Sanitize(Path.GetFileName(file))}";
    var res = analyzer.Analyze(file, asset);
    if (res == null) return;
    results.Add(res);
    if (id % 50 == 0 || id == svgFiles.Length) Console.WriteLine($"{id}/{svgFiles.Length}");
});

sw.Stop();
var ordered = results.OrderBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray();
GalleryBuilder.Build(options.Output, ordered, sw.Elapsed, options.Parallelism);

var errors = ordered.Count(x => x.Error is not null);
Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F2}s; errors={errors}");
Console.WriteLine($"Gallery: {Path.GetFullPath(Path.Combine(options.Output, "index.html"))}");
return errors == 0 ? 0 : 1;

static string Sanitize(string name)
{
    foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
    return name;
}

file sealed record Args(string Model, string Input, string Output, int Parallelism)
{
    public static Args Parse(string[] args)
    {
        string? model = null, input = null, output = null;
        var parallelism = Math.Max(1, Environment.ProcessorCount);
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--model": model = args[++i]; break;
                case "--input": input = args[++i]; break;
                case "--output": output = args[++i]; break;
                case "--parallelism": parallelism = int.Parse(args[++i]); break;
                case "-h": case "--help": PrintHelp(); Environment.Exit(0); break;
            }
        }
        if (model is null || input is null) { PrintHelp(); Environment.Exit(2); }
        output ??= Path.Combine(Environment.CurrentDirectory, "glyph-pca-gallery");
        return new Args(model!, input!, output, Math.Max(1, parallelism));
    }

    private static void PrintHelp() => Console.WriteLine("dotnet run --project GlyphPcaGallery -- --model glyph-model.json --input <svg-folder> --output glyph-pca-gallery --parallelism 8");
}
