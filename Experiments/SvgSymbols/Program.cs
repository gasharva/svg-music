using SvgSymbols.Services;

var root = FindRepositoryRoot(AppContext.BaseDirectory);
var outputRoot = Path.Combine(root, "Experiments", "SvgSymbols");
var samplesRoot = Path.Combine(outputRoot, "Samples");
var depth = GetIntArgument(args, "--depth", 1);

using var http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(60)
};

var commons = new WikimediaCommonsClient(http);
var downloader = new SymbolCorpusDownloader(http);
var gallery = new GalleryBuilder();

Console.WriteLine($"Wikimedia Commons subcategory depth: {depth}");
Console.WriteLine();

Console.WriteLine("Searching G clefs...");
var trebleSources = await commons.GetSvgFilesAsync("Treble", "G clef", depth);
Console.WriteLine($"Found {trebleSources.Count} SVG files. Downloading...");
var treble = await downloader.DownloadAsync(
    trebleSources,
    Path.Combine(samplesRoot, "Treble"));

Console.WriteLine();
Console.WriteLine("Searching F clefs...");
var bassSources = await commons.GetSvgFilesAsync("Bass", "F clef", depth);
Console.WriteLine($"Found {bassSources.Count} SVG files. Downloading...");
var bass = await downloader.DownloadAsync(
    bassSources,
    Path.Combine(samplesRoot, "Bass"));

var galleryPath = await gallery.BuildAsync(outputRoot, treble, bass);

Console.WriteLine();
Console.WriteLine($"Treble downloaded: {treble.Count}");
Console.WriteLine($"Bass downloaded:   {bass.Count}");
Console.WriteLine($"Gallery: {galleryPath}");

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

static int GetIntArgument(string[] args, string name, int defaultValue)
{
    var index = Array.FindIndex(args, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var value))
        return defaultValue;

    return Math.Clamp(value, 0, 3);
}
