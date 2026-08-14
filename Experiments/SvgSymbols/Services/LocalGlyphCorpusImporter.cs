using SvgSymbols.Models;

namespace SvgSymbols.Services;

/// <summary>
/// Builds a negative/control corpus from the repo-local Bravura/SMuFL reference glyphs.
/// Only semantically named SVGs are copied; uniXXXX files are intentionally skipped because
/// their meaning is unknown here and they may contain clefs as well.
/// </summary>
public sealed class LocalGlyphCorpusImporter
{
    public IReadOnlyList<SymbolSource> Import(
        string sourceDirectory,
        string outputDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Reference glyph directory not found: {sourceDirectory}");

        Directory.CreateDirectory(outputDirectory);

        var result = new List<SymbolSource>();

        foreach (var sourcePath in Directory
                     .EnumerateFiles(sourceDirectory, "*.svg", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(sourcePath);
            var stem = Path.GetFileNameWithoutExtension(sourcePath);

            if (!IsUsefulNegativeSample(stem))
                continue;

            var targetPath = Path.Combine(outputDirectory, fileName);
            File.Copy(sourcePath, targetPath, overwrite: true);

            result.Add(new SymbolSource(
                Kind: "Other",
                Category: DetectCategory(stem),
                Title: stem,
                FileName: fileName,
                DescriptionUrl: "../../References/glyphs/" + Uri.EscapeDataString(fileName),
                FileUrl: sourcePath,
                License: "SIL OFL 1.1 (Bravura)",
                LicenseUrl: "https://scripts.sil.org/OFL",
                Artist: "Bravura / SMuFL"));
        }

        return result;
    }

    private static bool IsUsefulNegativeSample(string stem)
    {
        // The named files are the useful semantic subset of References/glyphs.
        // Thousands of uniXXXX files are deliberately left out for now.
        if (stem.StartsWith("uni", StringComparison.OrdinalIgnoreCase))
            return false;

        // No clef of any kind belongs in the negative corpus.
        if (stem.Contains("clef", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static string DetectCategory(string name)
    {
        if (name.StartsWith("accidental", StringComparison.OrdinalIgnoreCase)) return "Accidental";
        if (name.StartsWith("notehead", StringComparison.OrdinalIgnoreCase)) return "Notehead";
        if (name.StartsWith("rest", StringComparison.OrdinalIgnoreCase)) return "Rest";
        if (name.StartsWith("timeSig", StringComparison.OrdinalIgnoreCase)) return "Time signature";
        if (name.StartsWith("flag", StringComparison.OrdinalIgnoreCase)) return "Flag";
        if (name.StartsWith("artic", StringComparison.OrdinalIgnoreCase)) return "Articulation";
        if (name.StartsWith("dynamic", StringComparison.OrdinalIgnoreCase)) return "Dynamic";
        if (name.StartsWith("ornament", StringComparison.OrdinalIgnoreCase)) return "Ornament";
        if (name.StartsWith("augmentation", StringComparison.OrdinalIgnoreCase)) return "Dot";
        if (name.Contains("fermata", StringComparison.OrdinalIgnoreCase)) return "Fermata";

        return "Other musical symbol";
    }
}
