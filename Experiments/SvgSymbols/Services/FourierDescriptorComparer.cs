namespace SvgSymbols.Services;

public sealed class FourierDescriptorComparer
{
    private const int MaxContours = 3;
    private const int CoefficientCount = 8;

    // For this experiment scanline topology/silhouette intentionally carries more weight than Fourier.
    private const double ScanlineIntersectionWeight = 2.5;
    private const double ScanlineSpanWeight = 4.0;
    private const double FourierWeight = 0.65;

    private static readonly int[][] Permutations =
    [
        [0, 1, 2],
        [0, 2, 1],
        [1, 0, 2],
        [1, 2, 0],
        [2, 0, 1],
        [2, 1, 0]
    ];

    /// <summary>
    /// Phase-aware Fourier comparison plus vector scanline features.
    /// </summary>
    public double ComplexDistance(FourierDescriptor a, FourierDescriptor b) =>
        Math.Sqrt(ScanlineDistance(a.Scanlines, b.Scanlines) +
                  Permutations.Min(permutation => DistanceForPermutation(a, b, permutation, usePhase: true)));

    /// <summary>
    /// Magnitude-only Fourier baseline, using the same scanline features and weights.
    /// </summary>
    public double MagnitudeDistance(FourierDescriptor a, FourierDescriptor b) =>
        Math.Sqrt(ScanlineDistance(a.Scanlines, b.Scanlines) +
                  Permutations.Min(permutation => DistanceForPermutation(a, b, permutation, usePhase: false)));

    private static double ScanlineDistance(ScanlineDescriptor a, ScanlineDescriptor b)
    {
        var sum = 0d;
        var count = Math.Min(a.HorizontalIntersections.Count, b.HorizontalIntersections.Count);

        for (var i = 0; i < count; i++)
        {
            // Crossing counts are topological. Normalize the raw difference a little so a single
            // extra hole/stroke matters strongly without completely dwarfing every other feature.
            var horizontalCrossingDelta =
                (a.HorizontalIntersections[i] - b.HorizontalIntersections[i]) / 2d;
            var verticalCrossingDelta =
                (a.VerticalIntersections[i] - b.VerticalIntersections[i]) / 2d;

            sum += ScanlineIntersectionWeight * Square(horizontalCrossingDelta);
            sum += ScanlineIntersectionWeight * Square(verticalCrossingDelta);

            sum += ScanlineSpanWeight * Square(a.HorizontalWidths[i] - b.HorizontalWidths[i]);
            sum += ScanlineSpanWeight * Square(a.VerticalHeights[i] - b.VerticalHeights[i]);
        }

        return sum;
    }

    private static double DistanceForPermutation(
        FourierDescriptor a,
        FourierDescriptor b,
        IReadOnlyList<int> permutation,
        bool usePhase)
    {
        var sum = 0d;

        for (var i = 0; i < MaxContours; i++)
        {
            var ac = i < a.Contours.Count ? a.Contours[i] : null;
            var bIndex = permutation[i];
            var bc = bIndex < b.Contours.Count ? b.Contours[bIndex] : null;
            sum += ContourDistance(ac, bc, usePhase);
        }

        // Keep topology as a weak hint only: equivalent SVG glyphs may be split into different path counts.
        sum += 0.015 * Square(Math.Min(a.ContourCount, 10) - Math.Min(b.ContourCount, 10));

        return sum;
    }

    private static double ContourDistance(
        ContourFourierDescriptor? a,
        ContourFourierDescriptor? b,
        bool usePhase)
    {
        if (a is null && b is null)
            return 0d;

        if (a is null || b is null)
        {
            var existing = a ?? b!;
            return FourierWeight * (1.25 * existing.Weight * existing.Weight + 0.08);
        }

        var sum = 0d;
        sum += 1.5 * Square(a.Weight - b.Weight);
        sum += 0.6 * Square(a.CenterX - b.CenterX);
        sum += 0.6 * Square(a.CenterY - b.CenterY);
        sum += 0.5 * Square(a.Width - b.Width);
        sum += 0.5 * Square(a.Height - b.Height);

        for (var k = 0; k < CoefficientCount; k++)
        {
            var ac = k < a.Coefficients.Count ? a.Coefficients[k] : new FourierCoefficient(0, 0);
            var bc = k < b.Coefficients.Count ? b.Coefficients[k] : new FourierCoefficient(0, 0);

            if (usePhase)
                sum += Square(ac.Real - bc.Real) + Square(ac.Imag - bc.Imag);
            else
                sum += Square(ac.Magnitude - bc.Magnitude);
        }

        return FourierWeight * sum;
    }

    private static double Square(double value) => value * value;
}
