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

    private sealed record GroupClassification(
        IReadOnlyList<SymbolClassification> Symbols,
        long MaskComparisons,
        long VectorComparisons);

    public ClassifierPerformance LastPerformance { get; private set; } = new();

    public ClassificationResult Classify(
        string scorePath,
        IReadOnlyList<Staff> staves,
        string catalogPath,
        int maxDegreeOfParallelism = 8)
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
            .Select(g => new { Geometry = g.First().Value, SymbolIds = g.Select(x => x.Key).ToArray() })
            .ToArray();

        var classifiedGroups = new GroupClassification?[unique.Length];
        var classifyWatch = Stopwatch.StartNew();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism)
        };

        Parallel.For(0, unique.Length, options, index =>
        {
            var group = unique[index];
            var geometry = group.Geometry;
            var descriptor = SvgPathGeometry.Describe(geometry);
            var widthSpaces = descriptor.Width / staffSpace;
            var heightSpaces = descriptor.Height / staffSpace;
            var mask = FastGlyphMatcher.CreateMask(geometry);
            long maskComparisons = 0;
            long vectorComparisons = 0;

            var finalists = references.Select(reference =>
                {
                    maskComparisons++;
                    var maskIoU = FastGlyphMatcher.BestMaskIoU(mask, reference.Mask);
                    var size = SizeScore(widthSpaces, heightSpaces, reference);
                    var aspect = Math.Exp(-Math.Abs(Math.Log(
                        Math.Max(descriptor.AspectRatio, 1e-6) / Math.Max(reference.AspectRatio, 1e-6))));
                    return (
                        Reference: reference,
                        MaskIoU: maskIoU,
                        Size: size,
                        FastScore: 0.72 * maskIoU + 0.18 * size + 0.10 * aspect);
                })
                .OrderByDescending(x => x.FastScore)
                .Take(FinalCandidateCount)
                .ToArray();

            var best = finalists.Select(candidate =>
                {
                    vectorComparisons++;
                    var vectorIoU = FastGlyphMatcher.BestVectorIoU(geometry, candidate.Reference.Geometry);
                    return (
                        candidate.Reference,
                        Total: 0.52 * candidate.MaskIoU + 0.28 * vectorIoU + 0.20 * candidate.Size,
                        Shape: 0.65 * candidate.MaskIoU + 0.35 * vectorIoU,
                        candidate.Size);
                })
                .OrderByDescending(x => x.Total)
                .FirstOrDefault();

            if (best.Reference is null)
            {
                classifiedGroups[index] = new GroupClassification([], maskComparisons, vectorComparisons);
                return;
            }

            var isUsedNearStaff = group.SymbolIds.Any(staffContextSymbols.Contains);
            var isUsedAtStaffLeft = group.SymbolIds.Any(leftEdgeSymbols.Contains);
            var semanticKind = RecognizeStaffLocalClef(widthSpaces, heightSpaces, isUsedAtStaffLeft)
                               ?? RecognizeStaffLocalDot(widthSpaces, heightSpaces, isUsedNearStaff)
                               ?? RecognizeStaffLocalQuarterRest(geometry, widthSpaces, heightSpaces, isUsedNearStaff)
                               ?? RecognizeStaffLocalAccidental(widthSpaces, heightSpaces, isUsedNearStaff)
                               ?? RecognizeStaffLocalNotehead(mask, widthSpaces, heightSpaces, isUsedNearStaff)
                               ?? NormalizeKind(best.Reference.Id, best.Reference.Kind);

            var symbols = group.SymbolIds
                .Select(symbolId => new SymbolClassification(
                    symbolId,
                    semanticKind,
                    best.Reference.Id,
                    best.Total,
                    best.Shape,
                    best.Size,
                    widthSpaces,
                    heightSpaces,
                    best.Reference.MusicXmlElement,
                    best.Reference.MusicXmlValue))
                .ToArray();

            classifiedGroups[index] = new GroupClassification(symbols, maskComparisons, vectorComparisons);
        });

        classifyWatch.Stop();

        var result = new ClassificationResult();
        long totalMaskComparisons = 0;
        long totalVectorComparisons = 0;
        foreach (var classified in classifiedGroups)
        {
            if (classified is null) continue;
            result.Symbols.AddRange(classified.Symbols);
            totalMaskComparisons += classified.MaskComparisons;
            totalVectorComparisons += classified.VectorComparisons;
        }

        result.Symbols.Sort((a, b) => string.CompareOrdinal(a.SymbolId, b.SymbolId));
        LastPerformance = new ClassifierPerformance
        {
            LoadCatalogMs = catalogWatch.Elapsed.TotalMilliseconds,
            ClassifyMs = classifyWatch.Elapsed.TotalMilliseconds,
            GlyphInstances = source.Count,
            UniqueGeometries = unique.Length,
            CatalogGlyphs = references.Count,
            MaskComparisons = totalMaskComparisons,
            VectorComparisons = totalVectorComparisons,
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

    private static string? RecognizeStaffLocalClef(
        double widthSpaces,
        double heightSpaces,
        bool isUsedAtStaffLeft)
    {
        if (!isUsedAtStaffLeft) return null;
        if (widthSpaces is >= 2.2 and <= 4.2 && heightSpaces is >= 4.5 and <= 8.0)
            return "clef-treble";
        if (widthSpaces is >= 1.5 and <= 3.0 && heightSpaces is >= 1.5 and <= 3.4)
            return "clef-bass";
        return null;
    }

    private static string? RecognizeStaffLocalDot(
        double widthSpaces,
        double heightSpaces,
        bool isUsedNearStaff)
    {
        if (!isUsedNearStaff) return null;
        if (widthSpaces is < .12 or > .45) return null;
        if (heightSpaces is < .10 or > .40) return null;
        var aspect = widthSpaces / Math.Max(heightSpaces, 1e-6);
        return aspect is >= .65 and <= 1.8 ? "augmentation-dot" : null;
    }

    private static string? RecognizeStaffLocalQuarterRest(
        SymbolGeometry geometry,
        double widthSpaces,
        double heightSpaces,
        bool isUsedNearStaff)
    {
        if (!isUsedNearStaff) return null;
        if (widthSpaces is < .82 or > 1.20) return null;
        if (heightSpaces is < 2.30 or > 2.95) return null;
        if (geometry.Contours.Count != 1) return null;

        var points = geometry.Contours[0];
        if (points.Count < 8) return null;
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        var boxArea = Math.Max((maxX - minX) * (maxY - minY), 1e-6);
        var fill = PolygonArea(points) / boxArea;

        return fill >= .22 ? "rest-quarter" : null;
    }

    private static string? RecognizeStaffLocalAccidental(
        double widthSpaces,
        double heightSpaces,
        bool isUsedNearStaff)
    {
        if (!isUsedNearStaff) return null;
        return widthSpaces is >= .45 and <= .85 && heightSpaces is >= 2.10 and <= 2.90
            ? "accidental-flat"
            : null;
    }

    private static string? RecognizeStaffLocalNotehead(
        IReadOnlyList<ulong> mask,
        double widthSpaces,
        double heightSpaces,
        bool isUsedNearStaff)
    {
        if (!isUsedNearStaff) return null;
        if (widthSpaces < 0.85 || widthSpaces > 1.45) return null;
        if (heightSpaces < 0.60 || heightSpaces > 1.05) return null;
        if (widthSpaces / Math.Max(heightSpaces, 1e-6) < 1.05) return null;

        long painted = 0;
        foreach (var row in mask) painted += BitOperations.PopCount(row);
        var fill = painted / (double)(FastGlyphMatcher.MaskSize * FastGlyphMatcher.MaskSize);
        return fill >= 0.62 ? "notehead-black" : "notehead-half";
    }

    private static double PolygonArea(IReadOnlyList<PointD> contour)
    {
        if (contour.Count < 3) return 0;
        double twiceArea = 0;
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            twiceArea += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(twiceArea) / 2;
    }

    private static string NormalizeKind(string referenceId, string kind) => referenceId switch
    {
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
                if (reader.ReadInt32() == CacheVersion && reader.ReadInt64() == catalogStamp)
                    return (ReadCache(reader), true);
            }
            catch { }
        }

        var catalog = JsonSerializer.Deserialize<ReferenceCatalog>(
                          File.ReadAllText(catalogPath),
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? throw new InvalidOperationException("Не удалось прочитать каталог эталонов");
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(catalogPath))!;
        var references = catalog.Symbols.Select(reference =>
        {
            var geometry = _geometry.ReadStandaloneSvg(Path.Combine(baseDir, reference.SvgPath));
            var descriptor = SvgPathGeometry.Describe(geometry);
            return new CachedReference(
                reference.Id,
                reference.Kind,
                reference.MusicXmlElement,
                reference.MusicXmlValue,
                reference.ExpectedWidthInSpaces,
                reference.ExpectedHeightInSpaces,
                reference.SizeTolerance,
                descriptor.AspectRatio,
                FastGlyphMatcher.CreateMask(geometry),
                geometry);
        }).ToList();

        try
        {
            using var stream = File.Create(cachePath);
            using var writer = new BinaryWriter(stream);
            writer.Write(CacheVersion);
            writer.Write(catalogStamp);
            WriteCache(writer, references);
        }
        catch { }
        return (references, false);
    }

    private static double SizeScore(double width, double height, CachedReference reference)
    {
        var parts = new List<double>();
        if (reference.ExpectedWidthInSpaces is double expectedWidth)
            parts.Add(SizeSimilarity(width, expectedWidth, reference.SizeTolerance));
        if (reference.ExpectedHeightInSpaces is double expectedHeight)
            parts.Add(SizeSimilarity(height, expectedHeight, reference.SizeTolerance));
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
            writer.Write(reference.Id ?? "");
            writer.Write(reference.Kind ?? "");
            writer.Write(reference.MusicXmlElement ?? "");
            writer.Write(reference.MusicXmlValue ?? "");
            WriteNullable(writer, reference.ExpectedWidthInSpaces);
            WriteNullable(writer, reference.ExpectedHeightInSpaces);
            writer.Write(reference.SizeTolerance);
            writer.Write(reference.AspectRatio);
            foreach (var row in reference.Mask) writer.Write(row);
            writer.Write(reference.Geometry.Contours.Count);
            foreach (var contour in reference.Geometry.Contours)
            {
                writer.Write(contour.Count);
                foreach (var point in contour)
                {
                    writer.Write(point.X);
                    writer.Write(point.Y);
                }
            }
        }
    }

    private static List<CachedReference> ReadCache(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var result = new List<CachedReference>(count);
        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadString();
            var kind = reader.ReadString();
            var element = EmptyToNull(reader.ReadString());
            var value = EmptyToNull(reader.ReadString());
            var expectedWidth = ReadNullable(reader);
            var expectedHeight = ReadNullable(reader);
            var tolerance = reader.ReadDouble();
            var aspect = reader.ReadDouble();
            var mask = new ulong[FastGlyphMatcher.MaskSize];
            for (var row = 0; row < mask.Length; row++) mask[row] = reader.ReadUInt64();
            var contourCount = reader.ReadInt32();
            var contours = new List<IReadOnlyList<PointD>>(contourCount);
            for (var c = 0; c < contourCount; c++)
            {
                var pointCount = reader.ReadInt32();
                var points = new PointD[pointCount];
                for (var p = 0; p < pointCount; p++)
                    points[p] = new PointD(reader.ReadDouble(), reader.ReadDouble());
                contours.Add(points);
            }
            result.Add(new CachedReference(
                id,
                kind,
                element,
                value,
                expectedWidth,
                expectedHeight,
                tolerance,
                aspect,
                mask,
                new SymbolGeometry(id, contours)));
        }
        return result;
    }

    private static void WriteNullable(BinaryWriter writer, double? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue) writer.Write(value.Value);
    }

    private static double? ReadNullable(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadDouble() : null;
    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private sealed record CachedReference(
        string Id,
        string Kind,
        string? MusicXmlElement,
        string? MusicXmlValue,
        double? ExpectedWidthInSpaces,
        double? ExpectedHeightInSpaces,
        double SizeTolerance,
        double AspectRatio,
        ulong[] Mask,
        SymbolGeometry Geometry);
}
