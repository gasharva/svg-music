using System.Text.RegularExpressions;
using SvgSymbols.Models;
using SvgSymbols.Services;

var root = FindRepositoryRoot(AppContext.BaseDirectory);
var outputRoot = Path.Combine(root, "Experiments", "SvgSymbols");
var samplesRoot = Path.Combine(outputRoot, "Samples");
var referenceGlyphs = Path.Combine(root, "References", "glyphs");
var rhythmRoot = Path.Combine(samplesRoot, "Rhythm");
var depth = GetIntArgument(args, "--depth", 1);
var localOnly = args.Any(x => string.Equals(x, "--local-only", StringComparison.OrdinalIgnoreCase));

var localImporter = new LocalGlyphCorpusImporter();
var rhythmVariants = new RhythmVariantCorpusBuilder();
var other = localImporter.Import(
    referenceGlyphs,
    Path.Combine(samplesRoot, "Other"));

Console.WriteLine($"Local non-clef reference glyphs: {other.Count}");

if (localOnly)
{
    var generatedRhythm = rhythmVariants.Build(referenceGlyphs, rhythmRoot);
    var gallery = new GalleryBuilder();
    var trebleValid = ReadLocalSamples(Path.Combine(samplesRoot, "Treble", "valid"), "Treble");
    var bassValid = ReadLocalSamples(Path.Combine(samplesRoot, "Bass", "valid"), "Bass");
    var rhythm = ReadLocalSamples(rhythmRoot, "Rhythm", useValidPrefix: false);
    var galleryPath = await gallery.BuildAsync(outputRoot, trebleValid, bassValid, rhythm, other);

    Console.WriteLine($"Treble valid:          {trebleValid.Count}");
    Console.WriteLine($"Bass valid:            {bassValid.Count}");
    Console.WriteLine($"Rhythm total:          {rhythm.Count}");
    Console.WriteLine($"Rhythm Bravura built:  {generatedRhythm.Count}");
    Console.WriteLine($"Gallery: {galleryPath}");
    return;
}

using var http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(60)
};

var commons = new WikimediaCommonsClient(http);
var downloader = new SymbolCorpusDownloader(http);
var galleryBuilder = new GalleryBuilder();

Console.WriteLine();
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

Console.WriteLine();
Console.WriteLine("Searching time-signature digits...");
var rhythmSources = (await commons.GetSvgFilesAsync("Rhythm", "SVG Time signatures", 0))
    .Where(IsRhythmNumberSample)
    .ToList();
Console.WriteLine($"Found {rhythmSources.Count} numeric SVG files. Downloading...");
var rhythmDownloaded = await downloader.DownloadAsync(
    rhythmSources,
    rhythmRoot);

var generated = rhythmVariants.Build(referenceGlyphs, rhythmRoot);
var rhythmAll = ReadLocalSamples(rhythmRoot, "Rhythm", useValidPrefix: false);
var fullGalleryPath = await galleryBuilder.BuildAsync(outputRoot, treble, bass, rhythmAll, other);

Console.WriteLine();
Console.WriteLine($"Treble downloaded:      {treble.Count}");
Console.WriteLine($"Bass downloaded:        {bass.Count}");
Console.WriteLine($"Rhythm Wikimedia:       {rhythmDownloaded.Count}");
Console.WriteLine($"Rhythm Bravura built:   {generated.Count}");
Console.WriteLine($"Rhythm total:           {rhythmAll.Count}");
Console.WriteLine($"Other local:            {other.Count}");
Console.WriteLine($"Gallery: {fullGalleryPath}");

static bool IsRhythmNumberSample(SymbolSource source)
{
    // Wikimedia's SVG Time signatures category contains Music0.svg ... Music9.svg,
    // plus useful compound forms such as Music10.svg, Music12.svg, Music16.svg and Music32.svg.
    // Exclude fraction/example files such as Music1-2.svg: here we want only the glyph/number itself.
    return Regex.IsMatch(
        source.FileName,
        @"^Music\d+\.svg$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

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

static IReadOnlyList<SymbolSource> ReadLocalSamples(
    string directory,
    string kind,
    bool useValidPrefix = true)
{
    if (!Directory.Exists(directory))
        return Array.Empty<SymbolSource>();

    return Directory
        .EnumerateFiles(directory, "*.svg", SearchOption.TopDirectoryOnly)
        .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
        .Select(path => new SymbolSource(
            Kind: kind,
            Category: kind == "Rhythm"
                ? (Path.GetFileName(path).StartsWith("Bravura-", StringComparison.OrdinalIgnoreCase)
                    ? "Time-signature number / Bravura"
                    : "Time-signature number / Wikimedia")
                : "Curated valid",
            Title: Path.GetFileNameWithoutExtension(path),
            FileName: useValidPrefix ? "valid/" + Path.GetFileName(path) : Path.GetFileName(path),
            DescriptionUrl: "#",
            FileUrl: path,
            License: null,
            LicenseUrl: null,
            Artist: null))
        .ToList();
}
