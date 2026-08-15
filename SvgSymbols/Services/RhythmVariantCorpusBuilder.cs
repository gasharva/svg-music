using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SvgSymbols.Models;

namespace SvgSymbols.Services;

/// <summary>
/// Builds repo-local time-signature number SVGs from Bravura/SMuFL
/// timeSig0..timeSig9 glyphs. Single digits 0..9 are always generated so the
/// recognizer never depends on Wikimedia samples being present. Compound values
/// found in the local Rhythm corpus are composed from the same Bravura digits.
/// </summary>
public sealed class RhythmVariantCorpusBuilder
{
    private static readonly Regex WikimediaNumber = new(
        @"^Music(?<value>\d+)\.svg$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public IReadOnlyList<SymbolSource> Build(
        string referenceGlyphDirectory,
        string rhythmDirectory)
    {
        Directory.CreateDirectory(rhythmDirectory);

        // Bravura is the reference family. Do not make its basic 0..9 corpus
        // conditional on Wikimedia downloads: local-only / CI runs must have a
        // complete single-digit reference set by themselves.
        var values = Enumerable
            .Range(0, 10)
            .Select(x => x.ToString(CultureInfo.InvariantCulture))
            .Concat(
                Directory
                    .EnumerateFiles(rhythmDirectory, "Music*.svg", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Select(TryGetValue)
                    .Where(x => x is not null)
                    .Select(x => x!))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => int.Parse(x, CultureInfo.InvariantCulture))
            .ToList();

        var result = new List<SymbolSource>();
        foreach (var value in values)
        {
            var outputName = $"Bravura-{value}.svg";
            var outputPath = Path.Combine(rhythmDirectory, outputName);
            Compose(referenceGlyphDirectory, value, outputPath);

            result.Add(new SymbolSource(
                Kind: "Rhythm",
                Category: "Time-signature number / Bravura",
                Title: $"Bravura {value}",
                FileName: outputName,
                DescriptionUrl: "../../References/glyphs/timeSig0.svg",
                FileUrl: outputPath,
                License: "SIL OFL 1.1 (Bravura)",
                LicenseUrl: "https://scripts.sil.org/OFL",
                Artist: "Bravura / SMuFL"));
        }

        return result;
    }

    private static string? TryGetValue(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var match = WikimediaNumber.Match(fileName);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static void Compose(string sourceDirectory, string value, string outputPath)
    {
        const double targetHeight = 1000d;
        const double gap = 35d;

        var glyphs = value
            .Select(ch => LoadGlyph(Path.Combine(sourceDirectory, $"timeSig{ch}.svg")))
            .ToList();

        var x = 0d;
        var placements = new List<(Glyph Glyph, double X, double Scale)>();

        foreach (var glyph in glyphs)
        {
            var scale = targetHeight / glyph.Height;
            placements.Add((glyph, x, scale));
            x += glyph.Width * scale + gap;
        }

        var totalWidth = Math.Max(1d, x - gap);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var root = new XElement(ns + "svg",
            new XAttribute("viewBox", $"0 0 {Fmt(totalWidth)} {Fmt(targetHeight)}"));

        foreach (var placement in placements)
        {
            // The repo-local Bravura glyphs use the font/SMuFL coordinate convention:
            // Y grows upwards. SVG's viewport Y grows downwards, so a direct translation
            // renders every digit vertically mirrored. Map the glyph's top (MaxY) to SVG y=0
            // and flip the Y axis while preserving X and the target scale.
            var maxY = placement.Glyph.MinY + placement.Glyph.Height;
            var g = new XElement(ns + "g",
                new XAttribute(
                    "transform",
                    $"translate({Fmt(placement.X)} 0) scale({Fmt(placement.Scale)} {Fmt(-placement.Scale)}) translate({Fmt(-placement.Glyph.MinX)} {Fmt(-maxY)})"));

            foreach (var node in placement.Glyph.Content)
                g.Add(new XElement(node));

            root.Add(g);
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        document.Save(outputPath);
    }

    private static Glyph LoadGlyph(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Bravura time-signature glyph not found.", path);

        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidOperationException($"SVG root not found: {path}");

        var viewBox = ((string?)root.Attribute("viewBox"))?
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.Parse(x, CultureInfo.InvariantCulture))
            .ToArray();

        if (viewBox is null || viewBox.Length != 4 || viewBox[2] <= 0 || viewBox[3] <= 0)
            throw new InvalidOperationException($"Invalid SVG viewBox: {path}");

        return new Glyph(
            viewBox[0],
            viewBox[1],
            viewBox[2],
            viewBox[3],
            root.Elements().Select(x => new XElement(x)).ToList());
    }

    private static string Fmt(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed record Glyph(
        double MinX,
        double MinY,
        double Width,
        double Height,
        IReadOnlyList<XElement> Content);
}
