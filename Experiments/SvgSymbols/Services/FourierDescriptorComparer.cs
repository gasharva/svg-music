namespace SvgSymbols.Services;

public sealed class FourierDescriptorComparer
{
    private const int MaxContours = 3;
    private const int CoefficientCount = 8;

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
    /// New metric: retains complex Fourier phase and tries every matching of the three largest contours.
    /// </summary>
    public double ComplexDistance(FourierDescriptor a, FourierDescriptor b) =>
        Permutations.Min(permutation => DistanceForPermutation(a, b, permutation, usePhase: true));

    /// <summary>
    /// Baseline metric shown beside the new one: same structural comparison, but Fourier phase is discarded.
    /// </summary>
    public double MagnitudeDistance(FourierDescriptor a, FourierDescriptor b) =>
        Permutations.Min(permutation => DistanceForPermutation(a, b, permutation, usePhase: false));

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

        return Math.Sqrt(sum);
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
            // Missing a significant contour should hurt much more than missing a tiny dot/hole.
            return 1.25 * existing.Weight * existing.Weight + 0.08;
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
            {
                sum += Square(ac.Real - bc.Real) + Square(ac.Imag - bc.Imag);
            }
            else
            {
                sum += Square(ac.Magnitude - bc.Magnitude);
            }
        }

        return sum;
    }

    private static double Square(double value) => value * value;
}
