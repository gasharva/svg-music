using System.Diagnostics;
using GlyphPcaGallery.Geometry;
using GlyphPcaGallery.Models;
using SkiaSharp;

namespace GlyphPcaGallery.Services;

public sealed class GlyphFingerprintAnalyzer
{
    private readonly GlyphModel _model;
    private readonly Dictionary<string, List<GlyphReference>> _byClass;

    public GlyphFingerprintAnalyzer(GlyphModel model)
    {
        _model = model;
        _byClass = model.References.GroupBy(x => x.Class)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

        var expected = model.Sdf.GridSize * model.Sdf.GridSize;
        if (model.Pca.Mean.Length != expected)
            throw new InvalidDataException($"PCA mean has {model.Pca.Mean.Length} values, expected {expected}.");
    }

    public GlyphAnalysis Analyze(string fileName, string assetName)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var path = SvgGlyphLoader.LoadFilledPath(fileName);
            var canonical = BuildCanonicalTransform(path);
            var sdf = BuildSdf(path, canonical);
            var fingerprint = Project(sdf);

            var classMatches = _byClass.Select(pair =>
            {
                var best = pair.Value
                    .Select(r => (Reference: r, Distance: Distance(fingerprint, r.Fingerprint)))
                    .MinBy(x => x.Distance);
                return new ClassMatch(pair.Key, best.Distance, best.Reference.Source);
            }).OrderBy(x => x.Distance).Take(5).ToArray();

            var d1 = classMatches[0].Distance;
            var d2 = classMatches.Length > 1 ? classMatches[1].Distance : d1;
            var margin = Math.Max(0, d2 - d1);
            var relativeMargin = d2 > 1e-12 ? Math.Clamp(margin / d2, 0, 1) : 0;
            var absoluteConfidence = AbsoluteConfidence(d1);
            var confidence = Math.Sqrt(Math.Clamp(absoluteConfidence * relativeMargin, 0, 1));

            sw.Stop();
            return new GlyphAnalysis(fileName, assetName, classMatches, confidence, d1, margin,
                relativeMargin, absoluteConfidence,
                sw.ElapsedTicks * 1_000_000L / Stopwatch.Frequency);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new GlyphAnalysis(fileName, assetName, [], 0, double.PositiveInfinity, 0, 0, 0,
                sw.ElapsedTicks * 1_000_000L / Stopwatch.Frequency, ex.Message);
        }
    }

    private Affine2D BuildCanonicalTransform(SKPath path)
    {
        var points = PathSampler.SampleBoundary(path, _model.Normalization.BoundarySamples);
        var meanX = points.Average(p => (double)p.X);
        var meanY = points.Average(p => (double)p.Y);

        double xx = 0, xy = 0, yy = 0;
        foreach (var p in points)
        {
            var x = p.X - meanX; var y = p.Y - meanY;
            xx += x * x; xy += x * y; yy += y * y;
        }
        xx /= points.Length; xy /= points.Length; yy /= points.Length;

        var (axis1X, axis1Y) = PrincipalAxis(xx, xy, yy);
        var axis2X = -axis1Y; var axis2Y = axis1X;

        double m31 = 0, m32 = 0;
        foreach (var p in points)
        {
            var x = p.X - meanX; var y = p.Y - meanY;
            var q1 = axis1X * x + axis1Y * y;
            var q2 = axis2X * x + axis2Y * y;
            m31 += q1 * q1 * q1; m32 += q2 * q2 * q2;
        }
        if (m31 < 0) { axis1X = -axis1X; axis1Y = -axis1Y; }
        if (m32 < 0) { axis2X = -axis2X; axis2Y = -axis2Y; }

        var maxRadius = 0.0;
        foreach (var p in points)
        {
            var x = p.X - meanX; var y = p.Y - meanY;
            var q1 = axis1X * x + axis1Y * y;
            var q2 = axis2X * x + axis2Y * y;
            maxRadius = Math.Max(maxRadius, Math.Sqrt(q1 * q1 + q2 * q2));
        }
        if (maxRadius < 1e-12) maxRadius = 1;

        var scale = _model.Normalization.TargetRadius / maxRadius;
        var a = scale * axis1X; var b = scale * axis1Y;
        var c = scale * axis2X; var d = scale * axis2Y;

        return new Affine2D(a, b, c, d,
            -(a * meanX + b * meanY),
            -(c * meanX + d * meanY));
    }

    private static (double X, double Y) PrincipalAxis(double xx, double xy, double yy)
    {
        var halfDiff = (xx - yy) * 0.5;
        var root = Math.Sqrt(halfDiff * halfDiff + xy * xy);
        var lambda = (xx + yy) * 0.5 + root;

        double x, y;
        if (Math.Abs(xy) > 1e-15) { x = xy; y = lambda - xx; }
        else if (xx >= yy) { x = 1; y = 0; }
        else { x = 0; y = 1; }

        var n = Math.Sqrt(x * x + y * y);
        return (x / n, y / n);
    }

    private double[] BuildSdf(SKPath originalPath, Affine2D canonical)
    {
        var contours = PathSampler.SampleContours(originalPath, _model.Normalization.SdfBoundarySamples);
        var transformed = contours.Select(c => c.Select(canonical.Apply).ToArray()).ToArray();
        var inverse = canonical.Inverse();
        var size = _model.Sdf.GridSize;
        var extent = _model.Sdf.GridExtent;
        var clip = _model.Sdf.Clip;
        var result = new double[size * size];

        for (var iy = 0; iy < size; iy++)
        {
            var y = -extent + 2.0 * extent * iy / (size - 1);
            for (var ix = 0; ix < size; ix++)
            {
                var x = -extent + 2.0 * extent * ix / (size - 1);
                var p = new SKPoint((float)x, (float)y);
                var minSq = double.PositiveInfinity;

                foreach (var contour in transformed)
                    for (var i = 0; i < contour.Length; i++)
                        minSq = Math.Min(minSq,
                            DistanceToSegmentSquared(p, contour[i], contour[(i + 1) % contour.Length]));

                var distance = Math.Sqrt(minSq);
                var original = inverse.Apply(p);
                if (originalPath.Contains(original.X, original.Y)) distance = -distance;
                result[iy * size + ix] = Math.Clamp(distance, -clip, clip) / clip;
            }
        }
        return result;
    }

    private double[] Project(double[] sdf)
    {
        var result = new double[_model.Pca.ComponentsCount];
        for (var component = 0; component < result.Length; component++)
        {
            double value = 0;
            var weights = _model.Pca.Components[component];
            for (var i = 0; i < sdf.Length; i++)
                value += weights[i] * (sdf[i] - _model.Pca.Mean[i]);
            result[component] = value;
        }
        return result;
    }

    private double AbsoluteConfidence(double distance)
    {
        var good = _model.Calibration.NearestSameP95;
        var bad = _model.Calibration.NearestWrongP05;
        if (bad <= good + 1e-12) return distance <= good ? 1 : 0;
        return Math.Clamp((bad - distance) / (bad - good), 0, 1);
    }

    private static double Distance(double[] a, double[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) { var d = a[i] - b[i]; sum += d * d; }
        return Math.Sqrt(sum);
    }

    private static double DistanceToSegmentSquared(SKPoint p, SKPoint a, SKPoint b)
    {
        var vx = b.X - a.X; var vy = b.Y - a.Y;
        var wx = p.X - a.X; var wy = p.Y - a.Y;
        var lenSq = vx * vx + vy * vy;
        if (lenSq <= 1e-20) return wx * wx + wy * wy;
        var t = Math.Clamp((wx * vx + wy * vy) / lenSq, 0f, 1f);
        var dx = p.X - (a.X + t * vx); var dy = p.Y - (a.Y + t * vy);
        return dx * dx + dy * dy;
    }
}
