using SkiaSharp;

namespace GlyphPcaGallery.Geometry;

public readonly record struct Affine2D(double M11, double M12, double M21, double M22, double Tx, double Ty)
{
    public static Affine2D Identity => new(1, 0, 0, 1, 0, 0);

    public SKPoint Apply(SKPoint p) => new(
        (float)(M11 * p.X + M12 * p.Y + Tx),
        (float)(M21 * p.X + M22 * p.Y + Ty));

    public Affine2D Then(Affine2D next) => new(
        next.M11 * M11 + next.M12 * M21,
        next.M11 * M12 + next.M12 * M22,
        next.M21 * M11 + next.M22 * M21,
        next.M21 * M12 + next.M22 * M22,
        next.M11 * Tx + next.M12 * Ty + next.Tx,
        next.M21 * Tx + next.M22 * Ty + next.Ty);

    public Affine2D Inverse()
    {
        var det = M11 * M22 - M12 * M21;
        if (Math.Abs(det) < 1e-15) throw new InvalidOperationException("Singular affine transform.");
        var a = M22 / det; var b = -M12 / det; var c = -M21 / det; var d = M11 / det;
        return new Affine2D(a, b, c, d, -(a * Tx + b * Ty), -(c * Tx + d * Ty));
    }

    public SKMatrix ToSkMatrix() => new()
    {
        ScaleX = (float)M11,
        SkewX = (float)M12,
        TransX = (float)Tx,
        SkewY = (float)M21,
        ScaleY = (float)M22,
        TransY = (float)Ty,
        Persp0 = 0,
        Persp1 = 0,
        Persp2 = 1
    };
}
