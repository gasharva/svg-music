using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Repairs a small set of source-font semantic ambiguities after generic glyph matching.
/// The rules are based on staff-relative geometry and layout, never on obfuscated symbol ids.
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

        var staffLocal = uses
            .Where(use => staves.Any(staff =>
                use.X >= staff.Left - staff.Space * 2 &&
                use.X <= staff.Right + staff.Space * 2 &&
                use.Y >= staff.Top - staff.Space * 5 &&
                use.Y <= staff.Bottom + staff.Space * 5))
            .Select(x => x.SymbolId)
            .ToHashSet(StringComparer.Ordinal);

        var timeSlot = FindInitialTimeSignatureSymbols(uses, staves);

        foreach (var symbolId in staffLocal)
        {
            if (!byId.TryGetValue(symbolId, out var index)) continue;
            if (!source.TryGetValue(symbolId, out var geometry)) continue;

            var cls = classification.Symbols[index];
            var mask = FastGlyphMatcher.CreateMask(geometry);

            if (LooksLikeScaledGraceHead(cls.WidthInSpaces, cls.HeightInSpaces))
            {
                classification.Symbols[index] = cls with { Kind = "notehead-black" };
                continue;
            }

            if (timeSlot.Contains(symbolId) && LooksLikeThree(mask, cls))
            {
                classification.Symbols[index] = cls with
                {
                    Kind = "time-signature-digit",
                    ReferenceId = "timeSig3",
                    MusicXmlElement = "attributes/time/digit",
                    MusicXmlValue = "3"
                };
            }
        }
    }

    private static HashSet<string> FindInitialTimeSignatureSymbols(
        IReadOnlyList<SvgUse> uses,
        IReadOnlyList<Staff> staves)
    {
        if (staves.Count == 0) return [];

        var firstSystemTop = staves.Min(x => x.Top);
        var firstSystem = staves
            .Where(x => x.Top <= firstSystemTop + x.Space * 12)
            .OrderBy(x => x.Center)
            .Take(2)
            .ToList();
        if (firstSystem.Count == 0) return [];

        return uses
            .Where(use => firstSystem.Any(staff =>
                use.X >= staff.Left + staff.Space * 3.0 &&
                use.X <= staff.Left + staff.Space * 7.0 &&
                use.Y >= staff.Top - staff.Space * 1.8 &&
                use.Y <= staff.Bottom + staff.Space * 1.8))
            .Select(x => x.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool LooksLikeScaledGraceHead(double widthSpaces, double heightSpaces)
    {
        // Grace heads are the same filled oval semantics at roughly 70% scale. The real exporter
        // outlines them as reusable glyphs (~0.57 x 0.53 spaces), while normal heads are >=0.85
        // spaces wide in this pipeline. The size gap itself is a stronger signal than font IoU.
        if (widthSpaces is < .45 or > .78) return false;
        if (heightSpaces is < .38 or > .70) return false;
        var aspect = widthSpaces / Math.Max(heightSpaces, 1e-6);
        return aspect is >= .82 and <= 1.45;
    }

    private static bool LooksLikeThree(IReadOnlyList<ulong> mask, SymbolClassification cls)
    {
        // Only repair the ambiguous time-signature family currently matched as 7. A three keeps
        // a broad lower lobe through much of the lower half, whereas a seven rapidly narrows to
        // a diagonal stroke. Use a generous projection test because music fonts vary strongly.
        if (!cls.Kind.Equals("time-signature-digit", StringComparison.OrdinalIgnoreCase)) return false;
        if (!cls.ReferenceId.Contains("7", StringComparison.OrdinalIgnoreCase) && cls.MusicXmlValue != "7") return false;
        if (cls.WidthInSpaces is < 1.4 or > 2.5 || cls.HeightInSpaces is < 1.5 or > 2.5) return false;

        var startRow = FastGlyphMatcher.MaskSize * 55 / 100;
        var endRow = FastGlyphMatcher.MaskSize * 90 / 100;
        var broadRows = 0;
        var paintedRows = 0;
        for (var row = startRow; row < endRow; row++)
        {
            var bits = mask[row];
            if (bits == 0) continue;
            paintedRows++;
            var first = 64;
            var last = -1;
            for (var bit = 0; bit < FastGlyphMatcher.MaskSize; bit++)
            {
                if ((bits & (1UL << bit)) == 0) continue;
                first = Math.Min(first, bit);
                last = Math.Max(last, bit);
            }
            if (last - first + 1 >= FastGlyphMatcher.MaskSize * .35) broadRows++;
        }

        return paintedRows >= 5 && broadRows >= Math.Max(3, paintedRows / 2);
    }
}
