using ShimSkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed class SvgSceneGeometryReader
{
    public IReadOnlyList<LineSegment> ReadLines(string svgPath)
    {
        using var svg = SKSvg.CreateFromFile(svgPath);
        var picture = svg.Model
            ?? throw new InvalidOperationException("Svg.Skia did not produce a retained scene model.");

        var lines = new List<LineSegment>();
        ReadPicture(picture, SKMatrix.Identity, lines);
        return lines;
    }

    private static void ReadPicture(
        SKPicture picture,
        SKMatrix parentMatrix,
        ICollection<LineSegment> lines)
    {
        if (picture.Commands is null)
            return;

        var matrix = parentMatrix;
        var stack = new Stack<SKMatrix>();

        foreach (var command in picture.Commands)
        {
            switch (command)
            {
                case SaveCanvasCommand:
                case SaveLayerCanvasCommand:
                    stack.Push(matrix);
                    break;

                case RestoreCanvasCommand:
                    if (stack.Count > 0)
                        matrix = stack.Pop();
                    break;

                case SetMatrixCanvasCommand setMatrix:
                    // TotalMatrix is total for this picture. Compose it with the matrix
                    // inherited from an enclosing DrawPicture command.
                    matrix = parentMatrix.PreConcat(setMatrix.TotalMatrix);
                    break;

                case DrawPathCanvasCommand drawPath when drawPath.Path is not null:
                    ReadPath(drawPath.Path, matrix, lines);
                    break;

                case DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    ReadPicture(drawPicture.Picture, matrix, lines);
                    break;
            }
        }
    }

    private static void ReadPath(
        SKPath path,
        SKMatrix matrix,
        ICollection<LineSegment> lines)
    {
        PointD? current = null;
        PointD? contourStart = null;

        foreach (var command in path)
        {
            switch (command)
            {
                case MoveToPathCommand move:
                    current = Map(matrix, move.X, move.Y);
                    contourStart = current;
                    break;

                case LineToPathCommand line when current is not null:
                {
                    var next = Map(matrix, line.X, line.Y);
                    lines.Add(new LineSegment(current.Value, next));
                    current = next;
                    break;
                }

                case AddPolyPathCommand poly when poly.Points is not null:
                    ReadPoly(poly, matrix, lines);
                    current = null;
                    contourStart = null;
                    break;

                case AddRectPathCommand rect:
                    ReadRect(rect.Rect, matrix, lines);
                    current = null;
                    contourStart = null;
                    break;

                case ClosePathCommand when current is not null && contourStart is not null:
                    lines.Add(new LineSegment(current.Value, contourStart.Value));
                    current = contourStart;
                    break;

                // Curves are intentionally ignored at this experiment level. Staff lines
                // and barlines are emitted as straight path commands by Svg.Skia.
                default:
                    current = EndPointOrNull(command, matrix) ?? current;
                    break;
            }
        }
    }

    private static void ReadPoly(
        AddPolyPathCommand poly,
        SKMatrix matrix,
        ICollection<LineSegment> lines)
    {
        if (poly.Count < 2)
            return;

        var first = Map(matrix, poly[0].X, poly[0].Y);
        var previous = first;

        for (var i = 1; i < poly.Count; i++)
        {
            var next = Map(matrix, poly[i].X, poly[i].Y);
            lines.Add(new LineSegment(previous, next));
            previous = next;
        }

        if (poly.Close)
            lines.Add(new LineSegment(previous, first));
    }

    private static void ReadRect(
        SKRect rect,
        SKMatrix matrix,
        ICollection<LineSegment> lines)
    {
        var p1 = Map(matrix, rect.Left, rect.Top);
        var p2 = Map(matrix, rect.Right, rect.Top);
        var p3 = Map(matrix, rect.Right, rect.Bottom);
        var p4 = Map(matrix, rect.Left, rect.Bottom);

        lines.Add(new LineSegment(p1, p2));
        lines.Add(new LineSegment(p2, p3));
        lines.Add(new LineSegment(p3, p4));
        lines.Add(new LineSegment(p4, p1));
    }

    private static PointD? EndPointOrNull(PathCommand command, SKMatrix matrix) => command switch
    {
        QuadToPathCommand q => Map(matrix, q.X1, q.Y1),
        CubicToPathCommand c => Map(matrix, c.X2, c.Y2),
        ArcToPathCommand a => Map(matrix, a.X, a.Y),
        _ => null
    };

    private static PointD Map(SKMatrix matrix, float x, float y)
    {
        var point = matrix.MapPoint(new SKPoint(x, y));
        return new PointD(point.X, point.Y);
    }
}
