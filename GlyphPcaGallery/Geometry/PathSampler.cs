using SkiaSharp;

namespace GlyphPcaGallery.Geometry;

public static class PathSampler
{
    public static List<SKPoint[]> SampleContours(SKPath path, int requestedSamples)
    {
        var lengths = GetContourLengths(path);
        var total = lengths.Sum();
        if (total <= 0) throw new InvalidDataException("SVG path has zero boundary length.");

        var result = new List<SKPoint[]>(lengths.Count);
        using var measure = new SKPathMeasure(path, false);

        for (var contourIndex = 0; contourIndex < lengths.Count; contourIndex++)
        {
            var length = lengths[contourIndex];
            var count = Math.Max(8, (int)Math.Round(requestedSamples * length / total));
            var points = new SKPoint[count];

            for (var i = 0; i < count; i++)
            {
                var d = (float)(length * i / count);
                if (!measure.GetPositionAndTangent(d, out var p, out _))
                    throw new InvalidDataException("Could not sample SVG contour.");
                points[i] = p;
            }

            result.Add(points);
            if (contourIndex + 1 < lengths.Count) measure.NextContour();
        }

        return result;
    }

    public static SKPoint[] SampleBoundary(SKPath path, int requestedSamples)
        => SampleContours(path, requestedSamples).SelectMany(x => x).ToArray();

    private static List<double> GetContourLengths(SKPath path)
    {
        var result = new List<double>();
        using var measure = new SKPathMeasure(path, false);
        do
        {
            if (measure.Length > 0) result.Add(measure.Length);
        }
        while (measure.NextContour());
        return result;
    }
}
