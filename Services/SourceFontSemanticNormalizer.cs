using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Repairs source-font semantic ambiguities after generic glyph matching. Rules are based on
/// staff-relative geometry/layout and normalized glyph shape, never on obfuscated symbol ids.
/// </summary>
public sealed class SourceFontSemanticNormalizer
{
    private readonly SvgPathGeometry _geometry = new();

    public void Normalize(string svgPath, IReadOnlyList<Staff> staves, ClassificationResult classification)
    {
        if (staves.Count == 0 || classification.Symbols.Count == 0) return;

        var document = System.Xml.Linq.XDocument.Load(svgPath);
        var uses = new SvgParser().ReadUses(document);
        var source = _geometry.ReadScoreGeometries(document)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var byId = classification.Symbols
            .Select((value, index) => new { value.SymbolId, Index = index })
            .ToDictionary(x => x.SymbolId, x => x.Index, StringComparer.Ordinal);

        var staffLocal = uses.Where(use => staves.Any(staff =>
                use.X >= staff.Left - staff.Space * 2 && use.X <= staff.Right + staff.Space * 2 &&
                use.Y >= staff.Top - staff.Space * 5 && use.Y <= staff.Bottom + staff.Space * 5))
            .Select(x => x.SymbolId).ToHashSet(StringComparer.Ordinal);
        var timeSlot = FindInitialTimeSignatureSymbols(uses, staves);

        foreach (var symbolId in staffLocal)
        {
            if (!byId.TryGetValue(symbolId, out var index) || !source.TryGetValue(symbolId, out var geometry)) continue;
            var cls = classification.Symbols[index];
            var mask = FastGlyphMatcher.CreateMask(geometry);

            if (LooksLikeScaledGraceHead(cls.WidthInSpaces, cls.HeightInSpaces))
            {
                classification.Symbols[index] = cls with { Kind = "notehead-black" };
                continue;
            }

            if (timeSlot.Contains(symbolId) && LooksLikeThree(mask, cls))
                classification.Symbols[index] = cls with
                {
                    Kind = "time-signature-digit",
                    ReferenceId = "timeSig3",
                    MusicXmlElement = "attributes/time/digit",
                    MusicXmlValue = "3"
                };
        }
    }

    private static HashSet<string> FindInitialTimeSignatureSymbols(IReadOnlyList<SvgUse> uses, IReadOnlyList<Staff> staves)
    {
        if (staves.Count == 0) return [];
        var firstSystemTop = staves.Min(x => x.Top);
        var firstSystem = staves.Where(x => x.Top <= firstSystemTop + x.Space * 12).OrderBy(x => x.Center).Take(2).ToList();
        return uses.Where(use => firstSystem.Any(staff =>
                use.X >= staff.Left + staff.Space * 3.0 && use.X <= staff.Left + staff.Space * 7.0 &&
                use.Y >= staff.Top - staff.Space * 1.8 && use.Y <= staff.Bottom + staff.Space * 1.8))
            .Select(x => x.SymbolId).ToHashSet(StringComparer.Ordinal);
    }

    private static bool LooksLikeScaledGraceHead(double widthSpaces, double heightSpaces)
    {
        if (widthSpaces is < .45 or > .78 || heightSpaces is < .38 or > .70) return false;
        var aspect = widthSpaces / Math.Max(heightSpaces, 1e-6);
        return aspect is >= .82 and <= 1.45;
    }

    private static double RowSpan(IReadOnlyList<ulong> mask, int percent)
    {
        var row = Math.Clamp(FastGlyphMatcher.MaskSize * percent / 100, 0, FastGlyphMatcher.MaskSize - 1);
        var bits = mask[row];
        if (bits == 0) return 0;
        var first = 64;
        var last = -1;
        for (var bit = 0; bit < FastGlyphMatcher.MaskSize; bit++)
            if ((bits & (1UL << bit)) != 0)
            {
                first = Math.Min(first, bit);
                last = Math.Max(last, bit);
            }
        return (last - first + 1) / (double)FastGlyphMatcher.MaskSize;
    }

    private static bool LooksLikeThree(IReadOnlyList<ulong> mask, SymbolClassification cls)
    {
        // Generic matching confuses some ornate 3s with 7s. A 3 has two rounded lobes separated
        // by a pronounced waist: broad near the top, very narrow around the middle, then broadens
        // again near the lower lobe. A 7 does not have that narrow-then-broad lower profile.
        if (!cls.Kind.Equals("time-signature-digit", StringComparison.OrdinalIgnoreCase)) return false;
        if (!cls.ReferenceId.Contains("7", StringComparison.OrdinalIgnoreCase) && cls.MusicXmlValue != "7") return false;
        if (cls.WidthInSpaces is < 1.4 or > 2.5 || cls.HeightInSpaces is < 1.5 or > 2.5) return false;

        var upper = RowSpan(mask, 20);
        var waist = RowSpan(mask, 50);
        var lower = RowSpan(mask, 80);
        return upper >= .65 && waist <= .22 && lower >= .28;
    }
}
