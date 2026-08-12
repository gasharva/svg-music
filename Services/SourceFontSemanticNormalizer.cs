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

            // FastGlyphMatcher currently unions contour masks, which visually fills an inner hole
            // and can make a hollow source-font notehead look like a black head. The SVG outline
            // itself still preserves the strongest font-independent signal: a compact notehead
            // contour containing a second, nested contour for the hole.
            if (LooksLikeHollowNotehead(geometry, cls.WidthInSpaces, cls.HeightInSpaces))
            {
                classification.Symbols[index] = cls with { Kind = "notehead-half" };
                continue;
            }

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

    private static bool LooksLikeHollowNotehead(SymbolGeometry geometry, double widthSpaces, double heightSpaces)
    {
        if (widthSpaces is < .85 or > 1.45 || heightSpaces is < .58 or > 1.08) return false;
        if (widthSpaces / Math.Max(heightSpaces, 1e-6) < 1.05) return false;
        if (geometry.Contours.Count < 2) return false;

        var boxes = geometry.Contours
            .Where(x => x.Count >= 3)
            .Select(contour => new
            {
                Left = contour.Min(p => p.X),
                Right = contour.Max(p => p.X),
                Top = contour.Min(p => p.Y),
                Bottom = contour.Max(p => p.Y)
            })
            .ToArray();

        for (var outerIndex = 0; outerIndex < boxes.Length; outerIndex++)
        for (var innerIndex = 0; innerIndex < boxes.Length; innerIndex++)
        {
            if (outerIndex == innerIndex) continue;
            var outer = boxes[outerIndex];
            var inner = boxes[innerIndex];
            var outerWidth = outer.Right - outer.Left;
            var outerHeight = outer.Bottom - outer.Top;
            var innerWidth = inner.Right - inner.Left;
            var innerHeight = inner.Bottom - inner.Top;
            if (outerWidth <= 0 || outerHeight <= 0 || innerWidth <= 0 || innerHeight <= 0) continue;

            var contained = inner.Left > outer.Left && inner.Right < outer.Right &&
                            inner.Top > outer.Top && inner.Bottom < outer.Bottom;
            if (!contained) continue;

            // The hole must be substantial enough to be engraving semantics, not a tiny internal
            // detail. Half/whole noteheads typically retain a large central opening.
            if (innerWidth / outerWidth >= .20 && innerHeight / outerHeight >= .20)
                return true;
        }

        return false;
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
        if (!cls.Kind.Equals("time-signature-digit", StringComparison.OrdinalIgnoreCase)) return false;
        if (!cls.ReferenceId.Contains("7", StringComparison.OrdinalIgnoreCase) && cls.MusicXmlValue != "7") return false;
        if (cls.WidthInSpaces is < 1.4 or > 2.5 || cls.HeightInSpaces is < 1.5 or > 2.5) return false;

        var upper = RowSpan(mask, 20);
        var waist = RowSpan(mask, 50);
        var lower = RowSpan(mask, 80);
        return upper >= .65 && waist <= .22 && lower >= .28;
    }
}
