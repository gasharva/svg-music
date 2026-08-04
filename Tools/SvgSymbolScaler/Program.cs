using SvgSymbolScaler;

try
{
    var options = CliOptions.Parse(args);
    var scaler = new CompactSvgScaler(options.Scale, options.MaxSize, options.MaxAspectRatio);

    if (File.Exists(options.Input))
    {
        var output = options.Output ?? BuildOutputPath(options.Input);
        var result = scaler.ProcessFile(options.Input, output);
        Console.WriteLine($"{Path.GetFileName(options.Input)}: scaled {result.Scaled}, skipped {result.Skipped} -> {output}");
        return;
    }

    if (Directory.Exists(options.Input))
    {
        var inputDirectory = Path.GetFullPath(options.Input);
        var outputDirectory = Path.GetFullPath(options.Output ?? Path.Combine(options.Input, "scaled"));
        var files = Directory.EnumerateFiles(inputDirectory, "*.svg", options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(file => !IsInside(file, outputDirectory))
            .ToArray();
        Directory.CreateDirectory(outputDirectory);

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(inputDirectory, file);
            var output = Path.Combine(outputDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var result = scaler.ProcessFile(file, output);
            Console.WriteLine($"{relative}: scaled {result.Scaled}, skipped {result.Skipped}");
        }
        Console.WriteLine($"Processed {files.Length} SVG file(s) -> {outputDirectory}");
        return;
    }

    throw new FileNotFoundException($"Input does not exist: {options.Input}");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.Usage);
    Environment.ExitCode = 1;
}

static bool IsInside(string file, string directory)
{
    var relative = Path.GetRelativePath(directory, Path.GetFullPath(file));
    return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar);
}

static string BuildOutputPath(string input) =>
    Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, Path.GetFileNameWithoutExtension(input) + ".scaled.svg");

internal sealed record CliOptions(string Input, string? Output, double Scale, double MaxSize, double MaxAspectRatio, bool Recursive)
{
    public const string Usage = """
Usage:
  dotnet run --project Tools/SvgSymbolScaler -- <input.svg> [output.svg] [options]
  dotnet run --project Tools/SvgSymbolScaler -- <folder> [output-folder] [options]

Options:
  --scale <number>       Scale factor, default 1.5
  --max-size <number>    Maximum compact-object width and height in SVG units, default 120
  --max-aspect <number>  Maximum width/height ratio before treating an object as a line, default 12
  --recursive            Process SVG files recursively
""";

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            throw new ArgumentException(Usage);

        string? input = null, output = null;
        double scale = 1.2, maxSize = 120, maxAspect = 2;
        var recursive = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scale": scale = ReadDouble(args, ref i, "--scale"); break;
                case "--max-size": maxSize = ReadDouble(args, ref i, "--max-size"); break;
                case "--max-aspect": maxAspect = ReadDouble(args, ref i, "--max-aspect"); break;
                case "--recursive": recursive = true; break;
                default:
                    if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown option: {args[i]}");
                    if (input is null) input = args[i];
                    else if (output is null) output = args[i];
                    else throw new ArgumentException($"Unexpected argument: {args[i]}");
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("Input path is required.");
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
        if (maxAspect <= 1) throw new ArgumentOutOfRangeException(nameof(maxAspect));
        return new CliOptions(input, output, scale, maxSize, maxAspect, recursive);
    }

    private static double ReadDouble(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || !double.TryParse(args[index], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"{option} requires a number.");
        return value;
    }
}
