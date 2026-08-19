namespace GlyphPcaGallery.Models;

public sealed record GlyphModelBundle(
    IReadOnlyDictionary<string, GlyphModel> Models,
    IReadOnlyDictionary<string, string> Entries)
{
    public GlyphModel GetRequired(string family)
    {
        if (Models.TryGetValue(family, out var model))
            return model;

        throw new InvalidDataException(
            $"Glyph model family '{family}' is missing from the bundle. " +
            $"Available families: {string.Join(", ", Models.Keys.OrderBy(x => x))}");
    }
}

public sealed class GlyphModelBundleManifest
{
    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
