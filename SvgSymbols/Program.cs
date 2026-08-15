using System.Text.RegularExpressions;
using SvgSymbols.Models;
using SvgSymbols.Services;

var root = FindRepositoryRoot(AppContext.BaseDirectory);
var outputRoot = Path.Combine(root, "SvgSymbols");
var samplesRoot = Path.Combine(outputRoot, "Samples");
var referenceGlyphs = Path.Combine(root, "References", "glyphs");
var rhythmRoot = Path.Combine(samplesRoot, "Rhythm");
var realMeterDigitsRoot = Path.Combine(samplesRoot, "RealMeterDigits");
var depth = GetIntArgument(args, "--depth", 1);
var localOnly = args.Any(x => string.Equals(x, "--local-only", StringComparison.OrdinalIgnoreCase));

var localImporter = new LocalGlyphCorpusImporter();
var rhythmVariants = new RhythmVariantCorpusBuilder();
var normalizedTopology = new NormalizedTopologyReportBuilder();
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
    var realMeterDigits = ReadRealMeterDigitSamples(realMeterDigitsRoot);
    var rhythmWithReal = rhythm.Concat(realMeterDigits).ToList();
    var normalizedTopologyPath = await normalizedTopology.BuildAsync(outputRoot, rhythmWithReal);
    var galleryPath = await gallery.BuildAsync(outputRoot, trebleValid, bassValid, rhythmWithReal, other);

    Console.WriteLine($"Treble valid:          {trebleValid.Count}");
    Console.WriteLine($"Bass valid:            {bassValid.Count}");
    Console.WriteLine($"Rhythm reference:      {rhythm.Count}");
    Console.WriteLine($"Real meter digits:     {realMeterDigits.Count}");
    Console.WriteLine($"Rhythm total:          {rhythmWithReal.Count}");
    Console.WriteLine($"Rhythm Bravura built:  {generatedRhythm.Count}");
    Console.WriteLine($"Normalized topology:   {normalizedTopologyPath}");
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
var realMeterDigitsAll = ReadRealMeterDigitSamples(realMeterDigitsRoot);
var rhythmWithRealAll = rhythmAll.Concat(realMeterDigitsAll).ToList();
var normalizedTopologyPathFull = await normalizedTopology.BuildAsync(outputRoot, rhythmWithRealAll);
var fullGalleryPath = await galleryBuilder.BuildAsync(outputRoot, treble, bass, rhythmWithRealAll, other);

Console.WriteLine();
Console.WriteLine($"Treble downloaded:      {treble.Count}");
Console.WriteLine($"Bass downloaded:        {bass.Count}");
Console.WriteLine($"Rhythm Wikimedia:       {rhythmDownloaded.Count}");
Console.WriteLine($"Rhythm Bravura built:   {generated.Count}");
Console.WriteLine($"Rhythm reference:       {rhythmAll.Count}");
Console.WriteLine($"Real meter digits:      {realMeterDigitsAll.Count}");
Console.WriteLine($"Rhythm total:           {rhythmWithRealAll.Count}");
Console.WriteLine($"Other local:            {other.Count}");
Console.WriteLine($"Normalized topology:    {normalizedTopologyPathFull}");
Console.WriteLine($"Gallery: {fullGalleryPath}");

static bool IsRhythmNumberSample(SymbolSource source)
{
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

static IReadOnlyList<SymbolSource> ReadRealMeterDigitSamples(string directory)
{
    if (!Directory.Exists(directory))
        return Array.Empty<SymbolSource>();

    var pattern = new Regex(
        @"^Real-(?<digit>\d+)-.+\.svg$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    return Directory
        .EnumerateFiles(directory, "*.svg", SearchOption.TopDirectoryOnly)
        .Select(path => new { Path = path, Match = pattern.Match(Path.GetFileName(path)) })
        .Where(x => x.Match.Success)
        .OrderBy(x => int.Parse(x.Match.Groups["digit"].Value))
        .ThenBy(x => Path.GetFileName(x.Path), StringComparer.OrdinalIgnoreCase)
        .Select(x =>
        {
            var digit = x.Match.Groups["digit"].Value;
            var fileName = Path.GetFileName(x.Path);
            return new SymbolSource(
                Kind: "Rhythm",
                Category: $"Real score meter digit / expected {digit}",
                Title: $"Real meter digit {digit}",
                FileName: "../RealMeterDigits/" + fileName,
                DescriptionUrl: "#",
                FileUrl: x.Path,
                License: null,
                LicenseUrl: null,
                Artist: "SvgStructure pipeline");
        })
        .ToList();
}
