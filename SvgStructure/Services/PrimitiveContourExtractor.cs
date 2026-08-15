using System.Numerics;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

/// <summary>
/// Flattens one already-resolved vector contour into physical points. This is the only lossy
/// bridge we keep after PrimitiveResolver: all later recognition consumes these points and never
/// reopens the source SVG.
/// </summary>
public sealed class PrimitiveContourExtractor
{
    private const int CurveSteps = 16;
    private const int EllipseSteps = 48;

    public IReadOnlyList<Vector2> Extract(Shim.SKPath path, Shim.SKMatrix matrix)
    {
        var points = new List<Vector2>();
        Vector2 current = default;
        Vector2 start = default;
        var hasCurrent = false;

        foreach (var command in path)
        {
            switch (command)
            {
                case Shim.MoveToPathCommand move:
                    current = start = Map(matrix, move.X, move.Y);
                    points.Add(current);
                    hasCurrent = true;
                    break;

                case Shim.LineToPathCommand line when hasCurrent:
                    current = Map(matrix, line.X, line.Y);
                    points.Add(current);
                    break;

                case Shim.QuadToPathCommand quad when hasCurrent:
                {
                    var p0 = current;
                    var p1 = Map(matrix, quad.X0, quad.Y0);
                    var p2 = Map(matrix, quad.X1, quad.Y1);
                    for (var i = 1; i <= CurveSteps; i++)
                    {
                        var t = i / (float)CurveSteps;
                        var u = 1f - t;
                        points.Add(u * u * p0 + 2f * u * t * p1 + t * t * p2);
                    }
                    current = p2;
                    break;
                }

                case Shim.CubicToPathCommand cubic when hasCurrent:
                {
                    var p0 = current;
                    var p1 = Map(matrix, cubic.X0, cubic.Y0);
                    var p2 = Map(matrix, cubic.X1, cubic.Y1);
                    var p3 = Map(matrix, cubic.X2, cubic.Y2);
                    for (var i = 1; i <= CurveSteps; i++)
                    {
                        var t = i / (float)CurveSteps;
                        var u = 1f - t;
                        points.Add(
                            u * u * u * p0 +
                            3f * u * u * t * p1 +
                            3f * u * t * t * p2 +
                            t * t * t * p3);
                    }
                    current = p3;
                    break;
                }

                case Shim.ArcToPathCommand arc when hasCurrent:
                    current = Map(matrix, arc.X, arc.Y);
                    points.Add(current);
                    break;

                case Shim.ClosePathCommand when hasCurrent:
                    if (points.Count > 0 && Vector2.DistanceSquared(points[^1], start) > 1e-8f)
                        points.Add(start);
                    current = start;
                    break;

                case Shim.AddPolyPathCommand poly:
                    for (var i = 0; i < poly.Count; i++)
                        points.Add(Map(matrix, poly[i].X, poly[i].Y));
                    if (poly.Close && points.Count > 0)
                        points.Add(points[0]);
                    hasCurrent = false;
                    break;

                case Shim.AddRectPathCommand rect:
                    AddRect(points, rect.Rect, matrix);
                    hasCurrent = false;
                    break;

                case Shim.AddRoundRectPathCommand roundRect:
                    AddRect(points, roundRect.Rect, matrix);
                    hasCurrent = false;
                    break;

                case Shim.AddCirclePathCommand circle:
                    AddEllipse(points, circle.X, circle.Y, circle.Radius, circle.Radius, matrix);
                    hasCurrent = false;
                    break;

                case Shim.AddOvalPathCommand oval:
                    AddEllipse(
                        points,
                        (oval.Rect.Left + oval.Rect.Right) / 2f,
                        (oval.Rect.Top + oval.Rect.Bottom) / 2f,
                        oval.Rect.Width / 2f,
                        oval.Rect.Height / 2f,
                        matrix);
                    hasCurrent = false;
                    break;
            }
        }

        return points;
    }

    private static void AddRect(ICollection<Vector2> points, Shim.SKRect rect, Shim.SKMatrix matrix)
    {
        var p0 = Map(matrix, rect.Left, rect.Top);
        points.Add(p0);
        points.Add(Map(matrix, rect.Right, rect.Top));
        points.Add(Map(matrix, rect.Right, rect.Bottom));
        points.Add(Map(matrix, rect.Left, rect.Bottom));
        points.Add(p0);
    }

    private static void AddEllipse(
        ICollection<Vector2> points,
        float cx,
        float cy,
        float rx,
        float ry,
        Shim.SKMatrix matrix)
    {
        for (var i = 0; i <= EllipseSteps; i++)
        {
            var angle = 2d * Math.PI * i / EllipseSteps;
            points.Add(Map(
                matrix,
                cx + rx * (float)Math.Cos(angle),
                cy + ry * (float)Math.Sin(angle)));
        }
    }

    private static Vector2 Map(Shim.SKMatrix matrix, float x, float y)
    {
        var point = matrix.MapPoint(new Shim.SKPoint(x, y));
        return new Vector2(point.X, point.Y);
    }
}
