using SvgSymbolScaler;

try
{
    var options = CliOptions.Parse(args);
    var scaler = new CompactSvgScaler(options.Scale, options.MaxSize, options.MaxAspectRatio);
    var postProcessor = new SvgPrintPostProcessor(options.ProtectAbove, options.CropPadding);

    var input = Path.GetFullPath(options.Input);
    var sourceDirectory = File.Exists(input)
        ? Path.GetDirectoryName(input)!
        : Directory.Exists(input)
            ? input
            : throw new FileNotFoundException($"Input does not exist: {options.Input}");

    var outputDirectory = Path.Combine(sourceDirectory, "scaled");
    Directory.CreateDirectory(outputDirectory);

    var files = File.Exists(input)
        ? [input]
        : Directory.EnumerateFiles(input, "*.svg", options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(file => !IsInside(file, outputDirectory))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    var outputs = new List<string>();
    foreach (var file in files)
    {
        var relative = File.Exists(input) ? Path.GetFileName(file) : Path.GetRelativePath(input, file);
        var output = Path.Combine(outputDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var scaleResult = scaler.ProcessFile(file, output);
        var postResult = postProcessor.Process(output);
        outputs.Add(output);
        Console.WriteLine($"{relative}: scaled {scaleResult.Scaled}, protected {postResult.Protected}, skipped {scaleResult.Skipped}; crop={postResult.CropWidth:F1}x{postResult.CropHeight:F1}");
    }

    var pdfPath = Path.Combine(outputDirectory, "combined.pdf");
    new SvgPdfWriter(options.MarginMm).Write(outputs, pdfPath);
    Console.WriteLine($"Processed {outputs.Count} SVG file(s) -> {outputDirectory}");
    Console.WriteLine($"Combined A4 PDF ({options.MarginMm:0.##} mm margins) -> {pdfPath}");
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

internal sealed record CliOptions(
    string Input,
    double Scale,
    double MaxSize,
    double MaxAspectRatio,
    double ProtectAbove,
    double CropPadding,
    double MarginMm,
    bool Recursive)
{
    public const string Usage = """
Usage:
  dotnet run --project Tools/SvgSymbolScaler -- <input.svg-or-folder> [options]

Output is always written to a `scaled` subfolder next to the input.
All generated SVG files are combined into an A4 `scaled/combined.pdf`.
The visible notation is enlarged to fill the printer-safe area.

Options:
  --scale <number>          Scale factor, default 1.2
  --max-size <number>       Maximum compact-object width and height, default 120
  --max-aspect <number>     Maximum aspect ratio before treating an object as a line, default 2
  --protect-above <number>  Do not scale objects this far above the first staff, default 80
  --crop-padding <number>   Padding around final SVG content bbox, default 2
  --margin-mm <number>      Physical PDF margin on every side, default 5 mm
  --recursive               Process SVG files recursively
""";

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h")) throw new ArgumentException(Usage);
        string? input = null;
        double scale = 1.2, maxSize = 120, maxAspect = 2, protectAbove = 80, cropPadding = 2, marginMm = 5;
        var recursive = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scale": scale = ReadDouble(args, ref i, "--scale"); break;
                case "--max-size": maxSize = ReadDouble(args, ref i, "--max-size"); break;
                case "--max-aspect": maxAspect = ReadDouble(args, ref i, "--max-aspect"); break;
                case "--protect-above": protectAbove = ReadDouble(args, ref i, "--protect-above"); break;
                case "--crop-padding": cropPadding = ReadDouble(args, ref i, "--crop-padding"); break;
                case "--margin-mm": marginMm = ReadDouble(args, ref i, "--margin-mm"); break;
                case "--recursive": recursive = true; break;
                default:
                    if (args[i].StartsWith('-')) throw new ArgumentException($"Unknown option: {args[i]}");
                    if (input is null) input = args[i]; else throw new ArgumentException($"Unexpected argument: {args[i]}");
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("Input path is required.");
        if (scale <= 0 || maxSize <= 0 || maxAspect <= 1 || protectAbove < 0 || cropPadding < 0 || marginMm < 0 || marginMm >= 50)
            throw new ArgumentOutOfRangeException("Numeric options are outside their valid range.");
        return new CliOptions(input, scale, maxSize, maxAspect, protectAbove, cropPadding, marginMm, recursive);
    }

    private static double ReadDouble(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || !double.TryParse(args[index], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"{option} requires a number.");
        return value;
    }
}
