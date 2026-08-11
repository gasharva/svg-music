using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class SymbolClassifier
{
    private const int FinalCandidateCount = 5;
    private const int CacheVersion = 1;
    private readonly SvgPathGeometry _geometry = new();

    public ClassifierPerformance LastPerformance { get; private set; } = new();

    public ClassificationResult Classify(string scorePath, IReadOnlyList<Staff> staves, string catalogPath)
    {
        var scoreDoc = System.Xml.Linq.XDocument.Load(scorePath);
        var source = _geometry.ReadScoreGeometries(scoreDoc);
        var staffSpace = staves.Count > 0 ? staves.Average(s => s.Space) : 1.0;
        var staffContextSymbols = FindStaffContextSymbols(scoreDoc, staves);
        var leftEdgeSymbols = FindLeftEdgeSymbols(scoreDoc, staves);
        var catalogWatch = Stopwatch.StartNew();
        var (references, cacheHit) = LoadReferences(catalogPath);
        catalogWatch.Stop();

        var unique = source.GroupBy(x => FastGlyphMatcher.GeometryKey(x.Value), StringComparer.Ordinal)
            .Select(g => new { Geometry = g.First().Value, SymbolIds = g.Select(x => x.Key).ToArray() }).ToArray();
        long maskComparisons = 0, vectorComparisons = 0;
        var result = new ClassificationResult();
        var classifyWatch = Stopwatch.StartNew();

        foreach (var group in unique)
        {
            var geometry = group.Geometry;
            var descriptor = SvgPathGeometry.Describe(geometry);
            var widthSpaces = descriptor.Width / staffSpace;
            var heightSpaces = descriptor.Height / staffSpace;
            var mask = FastGlyphMatcher.CreateMask(geometry);
            var finalists = references.Select(reference =>
                {
                    maskComparisons++;
                    var maskIoU = FastGlyphMatcher.BestMaskIoU(mask, reference.Mask);
                    var size = SizeScore(widthSpaces, heightSpaces, reference);
                    var aspect = Math.Exp(-Math.Abs(Math.Log(Math.Max(descriptor.AspectRatio, 1e-6) / Math.Max(reference.AspectRatio, 1e-6))));
                    return (Reference: reference, MaskIoU: maskIoU, Size: size, FastScore: 0.72 * maskIoU + 0.18 * size + 0.10 * aspect);
                })
                .OrderByDescending(x => x.FastScore).Take(FinalCandidateCount).ToArray();

            var best = finalists.Select(candidate =>
                {
                    vectorComparisons++;
                    var vectorIoU = FastGlyphMatcher.BestVectorIoU(geometry, candidate.Reference.Geometry);
                    return (candidate.Reference, Total: 0.52 * candidate.MaskIoU + 0.28 * vectorIoU + 0.20 * candidate.Size,
                        Shape: 0.65 * candidate.MaskIoU + 0.35 * vectorIoU, candidate.Size);
                })
                .OrderByDescending(x => x.Total).FirstOrDefault();
            if (best.Reference is null) continue;

            var isUsedNearStaff = group.SymbolIds.Any(staffContextSymbols.Contains);
            var isUsedAtStaffLeft = group.SymbolIds.Any(leftEdgeSymbols.Contains);
            var semanticKind = RecognizeStaffLocalClef(widthSpaces, heightSpaces, isUsedAtStaffLeft)
                               ?? RecognizeStaffLocalNotehead(mask, widthSpaces, heightSpaces, isUsedNearStaff)
                               ?? NormalizeKind(best.Reference.Id, best.Reference.Kind);
            foreach (var symbolId in group.SymbolIds)
                result.Symbols.Add(new SymbolClassification(symbolId, semanticKind, best.Reference.Id, best.Total,
                    best.Shape, best.Size, widthSpaces, heightSpaces,
                    best.Reference.MusicXmlElement, best.Reference.MusicXmlValue));
        }

        classifyWatch.Stop();
        result.Symbols.Sort((a, b) => string.CompareOrdinal(a.SymbolId, b.SymbolId));
        LastPerformance = new ClassifierPerformance
        {
            LoadCatalogMs = catalogWatch.Elapsed.TotalMilliseconds,
            ClassifyMs = classifyWatch.Elapsed.TotalMilliseconds,
            GlyphInstances = source.Count,
            UniqueGeometries = unique.Length,
            CatalogGlyphs = references.Count,
            MaskComparisons = maskComparisons,
            VectorComparisons = vectorComparisons,
            CatalogCacheHit = cacheHit
        };
        return result;
    }

    private static HashSet<string> FindStaffContextSymbols(
        System.Xml.Linq.XDocument scoreDoc,
        IReadOnlyList<Staff> staves)
    {
        if (staves.Count == 0) return [];

        var uses = new SvgParser().ReadUses(scoreDoc);
        return uses
            .Where(use => staves.Any(staff =>
                use.X >= staff.Left - staff.Space * 2 &&
                use.X <= staff.Right + staff.Space * 2 &&
                use.Y >= staff.Top - staff.Space * 5 &&
                use.Y <= staff.Bottom + staff.Space * 5))
            .Select(use => use.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> FindLeftEdgeSymbols(
        System.Xml.Linq.XDocument scoreDoc,
        IReadOnlyList<Staff> staves)
    {
        if (staves.Count == 0) return [];

        var uses = new SvgParser().ReadUses(scoreDoc);
        return uses
            .Where(use => staves.Any(staff =>
                use.X >= staff.Left - staff.Space * .5 &&
                use.X <= staff.Left + staff.Space * 2.2 &&
                use.Y >= staff.Top - staff.Space * 5 &&
                use.Y <= staff.Bottom + staff.Space * 5))
            .Select(use => use.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Clefs are exceptionally stable by layout even when their exact outline comes from a
    /// non-Bravura font: they sit immediately after the left edge of every staff. Use that
    /// structural position plus a broad size envelope before trusting a font-specific glyph match.
    /// </summary>
    private static string? RecognizeStaffLocalClef(
        double widthSpaces,
        double heightSpaces,
        bool isUsedAtStaffLeft)
    {
        if (!isUsedAtStaffLeft) return null;

        // Treble clef: tall symbol spanning well beyond the five staff lines.
        if (widthSpaces is >= 2.2 and <= 4.2 && heightSpaces is >= 4.5 and <= 8.0)
            return "clef-treble";

        // Bass clef: compact, roughly two staff spaces in both dimensions. Time signatures
        // occupy a similar box but are several staff spaces to the right, outside leftEdgeSymbols.
        if (widthSpaces is >= 1.5 and <= 3.0 && heightSpaces is >= 1.5 and <= 3.4)
            return "clef-bass";

        return null;
    }

    /// <summary>
    /// Real-world score SVGs frequently outline a music font that is not Bravura. A filled or
    /// hollow oval notehead is nevertheless much more stable geometrically than its exact font
    /// outline. Use this only for symbols actually instantiated around a detected staff, so text
    /// glyphs elsewhere on the page cannot be mistaken for notes.
    /// </summary>
    private static string? RecognizeStaffLocalNotehead(
        IReadOnlyList<ulong> mask,
        double widthSpaces,
        double heightSpaces,
        bool isUsedNearStaff)
    {
        if (!isUsedNearStaff) return null;

        // Normal noteheads are roughly one staff-space wide and distinctly wider than tall.
        // Keep the window deliberately conservative: accidentals, rests, clefs and dynamics are
        // either much taller or have a very different bounding-box aspect.
        if (widthSpaces < 0.85 || widthSpaces > 1.45) return null;
        if (heightSpaces < 0.60 || heightSpaces > 1.05) return null;
        if (widthSpaces / Math.Max(heightSpaces, 1e-6) < 1.05) return null;

        long painted = 0;
        foreach (var row in mask) painted += BitOperations.PopCount(row);
        var fill = painted / (double)(FastGlyphMatcher.MaskSize * FastGlyphMatcher.MaskSize);

        // Filled noteheads occupy most of their normalized oval box; half-note heads retain a
        // substantial central hole. Both are independent of symbol ids and of the source font.
        return fill >= 0.62 ? "notehead-black" : "notehead-half";
    }

    private static string NormalizeKind(string referenceId, string kind) => referenceId switch
    {
        // MuseScore's round noteheads are geometrically closer to SMuFL shape-note
        // variants than to Bravura's default oval noteheads. Musically they have the
        // same filled/hollow semantics and must not remain smufl-unknown.
        "uniE1B1" => "notehead-black",
        "uniE1B0" => "notehead-half",
        _ => kind
    };

    private (List<CachedReference> References, bool CacheHit) LoadReferences(string catalogPath)
    {
        var cachePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!, "catalog.bin");
        var catalogStamp = File.GetLastWriteTimeUtc(catalogPath).Ticks;
        if (File.Exists(cachePath))
        {
            try
            {
                using var stream = File.OpenRead(cachePath);
                using var reader = new BinaryReader(stream);
                if (reader.ReadInt32() == CacheVersion && reader.ReadInt64() == catalogStamp) return (ReadCache(reader), true);
            }
            catch { }
        }

        var catalog = JsonSerializer.Deserialize<ReferenceCatalog>(File.ReadAllText(catalogPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Не удалось прочитать каталог эталонов");
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(catalogPath))!;
        var references = catalog.Symbols.Select(reference =>
        {
            var geometry = _geometry.ReadStandaloneSvg(Path.Combine(baseDir, reference.SvgPath));
            var descriptor = SvgPathGeometry.Describe(geometry);
            return new CachedReference(reference.Id, reference.Kind, reference.MusicXmlElement, reference.MusicXmlValue,
                reference.ExpectedWidthInSpaces, reference.ExpectedHeightInSpaces, reference.SizeTolerance,
                descriptor.AspectRatio, FastGlyphMatcher.CreateMask(geometry), geometry);
        }).ToList();

        try
        {
            using var stream = File.Create(cachePath);
            using var writer = new BinaryWriter(stream);
            writer.Write(CacheVersion); writer.Write(catalogStamp); WriteCache(writer, references);
        }
        catch { }
        return (references, false);
    }

    private static double SizeScore(double width, double height, CachedReference reference)
    {
        var parts = new List<double>();
        if (reference.ExpectedWidthInSpaces is double expectedWidth) parts.Add(SizeSimilarity(width, expectedWidth, reference.SizeTolerance));
        if (reference.ExpectedHeightInSpaces is double expectedHeight) parts.Add(SizeSimilarity(height, expectedHeight, reference.SizeTolerance));
        return parts.Count == 0 ? 0.5 : parts.Average();
    }

    private static double SizeSimilarity(double actual, double expected, double tolerance)
    {
        if (actual <= 0 || expected <= 0) return 0;
        return Math.Exp(-Math.Abs(Math.Log(actual / expected)) / Math.Max(tolerance, 0.05));
    }

    private static void WriteCache(BinaryWriter writer, IReadOnlyList<CachedReference> references)
    {
        writer.Write(references.Count);
        foreach (var reference in references)
        {
            writer.Write(reference.Id ?? ""); writer.Write(reference.Kind ?? "");
            writer.Write(reference.MusicXmlElement ?? ""); writer.Write(reference.MusicXmlValue ?? "");
            WriteNullable(writer, reference.ExpectedWidthInSpaces); WriteNullable(writer, reference.ExpectedHeightInSpaces);
            writer.Write(reference.SizeTolerance); writer.Write(reference.AspectRatio);
            foreach (var row in reference.Mask) writer.Write(row);
            writer.Write(reference.Geometry.Contours.Count);
            foreach (var contour in reference.Geometry.Contours)
            {
                writer.Write(contour.Count);
                foreach (var point in contour) { writer.Write(point.X); writer.Write(point.Y); }
            }
        }
    }

    private static List<CachedReference> ReadCache(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var result = new List<CachedReference>(count);
        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadString(); var kind = reader.ReadString();
            var element = EmptyToNull(reader.ReadString()); var value = EmptyToNull(reader.ReadString());
            var expectedWidth = ReadNullable(reader); var expectedHeight = ReadNullable(reader);
            var tolerance = reader.ReadDouble(); var aspect = reader.ReadDouble();
            var mask = new ulong[FastGlyphMatcher.MaskSize];
            for (var row = 0; row < mask.Length; row++) mask[row] = reader.ReadUInt64();
            var contourCount = reader.ReadInt32();
            var contours = new List<IReadOnlyList<PointD>>(contourCount);
            for (var c = 0; c < contourCount; c++)
            {
                var pointCount = reader.ReadInt32(); var points = new PointD[pointCount];
                for (var p = 0; p < pointCount; p++) points[p] = new PointD(reader.ReadDouble(), reader.ReadDouble());
                contours.Add(points);
            }
            result.Add(new CachedReference(id, kind, element, value, expectedWidth, expectedHeight, tolerance,
                aspect, mask, new SymbolGeometry(id, contours)));
        }
        return result;
    }

    private static void WriteNullable(BinaryWriter writer, double? value) { writer.Write(value.HasValue); if (value.HasValue) writer.Write(value.Value); }
    private static double? ReadNullable(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadDouble() : null;
    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private sealed record CachedReference(string Id, string Kind, string? MusicXmlElement, string? MusicXmlValue,
        double? ExpectedWidthInSpaces, double? ExpectedHeightInSpaces, double SizeTolerance, double AspectRatio,
        ulong[] Mask, SymbolGeometry Geometry);
}
