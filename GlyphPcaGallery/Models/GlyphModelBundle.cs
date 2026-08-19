namespace GlyphPcaGallery.Models;

public sealed record GlyphModelBundle(
    IReadOnlyDictionary<string, GlyphModel> Models,
    IReadOnlyDictionary<string, string> Entries);

public sealed class GlyphModelBundleManifest
{
    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
