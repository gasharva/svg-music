using System.Numerics;
using Clipper2Lib;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

internal static class FastGlyphMatcher
{
    public const int MaskSize = 64;
    private const double PolygonScale = 10000.0;

    public static ulong[] CreateMask(SymbolGeometry geometry)
    {
        var normalized = NormalizeContours(geometry);
        var rows = new ulong[MaskSize];
        for (var y = 0; y < MaskSize; y++)
        {
            var py = (y + 0.5) / MaskSize;
            ulong row = 0;
            for (var x = 0; x < MaskSize; x++)
            {
                var px = (x + 0.5) / MaskSize;
                if (InsideEvenOdd(normalized, px, py)) row |= 1UL << x;
            }
            rows[y] = row;
        }
        return rows;
    }

    public static double BestMaskIoU(ulong[] source, ulong[] reference)
    {
        var best = MaskIoU(source, reference);
        best = Math.Max(best, MaskIoU(source, Flip(reference, true, false)));
        best = Math.Max(best, MaskIoU(source, Flip(reference, false, true)));
        best = Math.Max(best, MaskIoU(source, Flip(reference, true, true)));
        return best;
    }

    public static double BestVectorIoU(SymbolGeometry source, SymbolGeometry reference)
    {
        var sourcePaths = ToPaths(source, false, false);
        var best = VectorIoU(sourcePaths, ToPaths(reference, false, false));
        best = Math.Max(best, VectorIoU(sourcePaths, ToPaths(reference, true, false)));
        best = Math.Max(best, VectorIoU(sourcePaths, ToPaths(reference, false, true)));
        best = Math.Max(best, VectorIoU(sourcePaths, ToPaths(reference, true, true)));
        return best;
    }

    public static string GeometryKey(SymbolGeometry geometry)
    {
        var normalized = NormalizeContours(geometry);
        return string.Join('|', normalized.Select(c => string.Join(';', c.Select(p => $"{Math.Round(p.X, 4)},{Math.Round(p.Y, 4)}"))));
    }

    private static double MaskIoU(ulong[] a, ulong[] b)
    {
        long intersection = 0, union = 0;
        for (var i = 0; i < MaskSize; i++)
        {
            intersection += BitOperations.PopCount(a[i] & b[i]);
            union += BitOperations.PopCount(a[i] | b[i]);
        }
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static ulong[] Flip(ulong[] source, bool horizontal, bool vertical)
    {
        var result = new ulong[MaskSize];
        for (var y = 0; y < MaskSize; y++)
        {
            var targetY = vertical ? MaskSize - 1 - y : y;
            var row = source[y];
            if (horizontal) row = ReverseBits(row);
            result[targetY] = row;
        }
        return result;
    }

    private static ulong ReverseBits(ulong value)
    {
        value = ((value & 0x5555555555555555UL) << 1) | ((value >> 1) & 0x5555555555555555UL);
        value = ((value & 0x3333333333333333UL) << 2) | ((value >> 2) & 0x3333333333333333UL);
        value = ((value & 0x0F0F0F0F0F0F0F0FUL) << 4) | ((value >> 4) & 0x0F0F0F0F0F0F0F0FUL);
        value = ((value & 0x00FF00FF00FF00FFUL) << 8) | ((value >> 8) & 0x00FF00FF00FF00FFUL);
        value = ((value & 0x0000FFFF0000FFFFUL) << 16) | ((value >> 16) & 0x0000FFFF0000FFFFUL);
        return (value << 32) | (value >> 32);
    }

    private static bool InsideEvenOdd(IReadOnlyList<IReadOnlyList<PointD>> contours, double x, double y)
    {
        var inside = false;
        foreach (var contour in contours)
        {
            if (contour.Count < 3) continue;
            for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
            {
                var pi = contour[i]; var pj = contour[j];
                if (((pi.Y > y) != (pj.Y > y)) && x < (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y + 1e-12) + pi.X)
                    inside = !inside;
            }
        }
        return inside;
    }

    private static IReadOnlyList<IReadOnlyList<PointD>> NormalizeContours(SymbolGeometry geometry)
    {
        var all = geometry.Contours.SelectMany(x => x).ToArray();
        var minX = all.Min(p => p.X); var maxX = all.Max(p => p.X);
        var minY = all.Min(p => p.Y); var maxY = all.Max(p => p.Y);
        var width = Math.Max(maxX - minX, 1e-9); var height = Math.Max(maxY - minY, 1e-9);
        return geometry.Contours.Select(c => (IReadOnlyList<PointD>)c.Select(p => new PointD((p.X - minX) / width, (p.Y - minY) / height)).ToArray()).ToArray();
    }

    private static Paths64 ToPaths(SymbolGeometry geometry, bool flipX, bool flipY)
    {
        var result = new Paths64();
        foreach (var contour in NormalizeContours(geometry))
        {
            if (contour.Count < 3) continue;
            var path = new Path64(contour.Count);
            foreach (var point in contour)
            {
                var x = flipX ? 1 - point.X : point.X;
                var y = flipY ? 1 - point.Y : point.Y;
                path.Add(new Point64((long)Math.Round(x * PolygonScale), (long)Math.Round(y * PolygonScale)));
            }
            result.Add(path);
        }
        return result;
    }

    private static double VectorIoU(Paths64 a, Paths64 b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = Clipper.Intersect(a, b, FillRule.EvenOdd);
        var combined = new Paths64(a); combined.AddRange(b);
        var union = Clipper.Union(combined, FillRule.EvenOdd);
        var intersectionArea = Math.Abs(Clipper.Area(intersection));
        var unionArea = Math.Abs(Clipper.Area(union));
        return unionArea <= 0 ? 0 : intersectionArea / unionArea;
    }
}
