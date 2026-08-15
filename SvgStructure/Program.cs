using SvgStructure.Services;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var inputFolder = args.Length > 0
    ? ResolvePath(repositoryRoot, args[0])
    : Path.Combine(repositoryRoot, "Samples", "step-by-step");

if (!Directory.Exists(inputFolder))
{
    Console.Error.WriteLine($"Input folder does not exist: {inputFolder}");
    Environment.ExitCode = 1;
    return;
}

var runner = new StepByStepBatchRunner();
var result = runner.Run(inputFolder);

Console.WriteLine($"input:     {result.InputFolder}");
Console.WriteLine($"artifacts: {result.ArtifactsFolder}");
Console.WriteLine($"svg files: {result.Items.Count}");
Console.WriteLine($"success:   {result.Items.Count(x => x.Error is null)}");
Console.WriteLine($"failed:    {result.Items.Count(x => x.Error is not null)}");
Console.WriteLine($"report:    {result.HtmlReportPath}");
Console.WriteLine();

foreach (var item in result.Items)
{
    if (item.Error is not null)
    {
        Console.WriteLine($"[FAIL] {item.FileName}: {item.Error}");
        continue;
    }

    Console.WriteLine(
        $"[ OK ] {item.FileName}: lines={item.LineCount}, systems={item.SystemCount}, " +
        $"parts={item.PartCount}, measures={item.MeasureCount}");
}

if (result.Items.Any(x => x.Error is not null))
    Environment.ExitCode = 2;

static string ResolvePath(string root, string path) =>
    Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

static string FindRepositoryRoot(string start)
{
    var current = new DirectoryInfo(start);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "SvgToMusicXmlPoc.sln")))
            return current.FullName;

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not find SvgToMusicXmlPoc.sln above the application directory.");
}
