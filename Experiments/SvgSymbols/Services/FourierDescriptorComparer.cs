namespace SvgSymbols.Services;

public sealed class FourierDescriptorComparer
{
    private const int MaxContours = 3;
    private const int MagnitudeCount = 8;

    public double Distance(FourierDescriptor a, FourierDescriptor b)
    {
        var sum = 0d;

        for (var i = 0; i < MaxContours; i++)
        {
            var ac = i < a.Contours.Count ? a.Contours[i] : null;
            var bc = i < b.Contours.Count ? b.Contours[i] : null;

            sum += Square(Value(ac, x => x.Weight) - Value(bc, x => x.Weight));
            sum += 0.5 * Square(Value(ac, x => x.CenterX) - Value(bc, x => x.CenterX));
            sum += 0.5 * Square(Value(ac, x => x.CenterY) - Value(bc, x => x.CenterY));
            sum += 0.5 * Square(Value(ac, x => x.Width) - Value(bc, x => x.Width));
            sum += 0.5 * Square(Value(ac, x => x.Height) - Value(bc, x => x.Height));

            for (var k = 0; k < MagnitudeCount; k++)
            {
                var av = ac is not null && k < ac.Magnitudes.Count ? ac.Magnitudes[k] : 0d;
                var bv = bc is not null && k < bc.Magnitudes.Count ? bc.Magnitudes[k] : 0d;
                sum += Square(av - bv);
            }
        }

        // Contour count is useful, but deliberately weak: SVGs often split the same glyph differently.
        sum += 0.02 * Square(Math.Min(a.ContourCount, 10) - Math.Min(b.ContourCount, 10));

        return Math.Sqrt(sum);
    }

    private static double Value(
        ContourFourierDescriptor? contour,
        Func<ContourFourierDescriptor, double> selector) =>
        contour is null ? 0d : selector(contour);

    private static double Square(double value) => value * value;
}
