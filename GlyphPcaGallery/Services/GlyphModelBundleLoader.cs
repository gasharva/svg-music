using System.IO.Compression;
using System.Text.Json;
using GlyphPcaGallery.Models;

namespace GlyphPcaGallery.Services;

public static class GlyphModelBundleLoader
{
    public static GlyphModelBundle Load(string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Glyph model bundle not found.", zipPath);

        using var zip = ZipFile.OpenRead(zipPath);

        var manifestEntry = zip.Entries.FirstOrDefault(x =>
            string.Equals(Path.GetFileName(x.FullName), "glyph-models-manifest.json", StringComparison.OrdinalIgnoreCase));

        Dictionary<string, string> entries;
        if (manifestEntry is not null)
        {
            using var stream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<GlyphModelBundleManifest>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Could not deserialize glyph-models-manifest.json.");

            entries = new Dictionary<string, string>(manifest.Models, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            entries = zip.Entries
                .Where(x => Path.GetFileName(x.FullName).StartsWith("glyph-model-", StringComparison.OrdinalIgnoreCase))
                .Where(x => x.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    x => Path.GetFileNameWithoutExtension(x.FullName)["glyph-model-".Length..],
                    x => x.FullName,
                    StringComparer.OrdinalIgnoreCase);
        }

        if (entries.Count == 0)
            throw new InvalidDataException("The bundle contains no glyph models.");

        var models = new Dictionary<string, GlyphModel>(StringComparer.OrdinalIgnoreCase);
        var resolvedEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (family, requestedEntry) in entries)
        {
            var entry = zip.GetEntry(requestedEntry)
                ?? zip.Entries.FirstOrDefault(x =>
                    string.Equals(Path.GetFileName(x.FullName), Path.GetFileName(requestedEntry), StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Model '{family}' points to missing ZIP entry '{requestedEntry}'.");

            using var stream = entry.Open();
            var model = JsonSerializer.Deserialize<GlyphModel>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException($"Could not deserialize model '{family}'.");

            models[family] = model;
            resolvedEntries[family] = entry.FullName;
        }

        return new GlyphModelBundle(models, resolvedEntries);
    }
}
