using System.Numerics;
using System.Text;
using Svg.Skia;
using SvgStructure.Models;
using Shim = ShimSkiaSharp;

namespace SvgStructure.Services;

/// <summary>
/// Temporary adapter for recognizers and diagnostics that still consume point contours.
/// MusicSymbolCandidate keeps original smooth Bezier geometry; flattening happens only at this boundary.
/// </summary>
public static class SmoothSymbolContourConverter
{
    private const int CurveSteps = 20;

    public static IReadOnlyList<IReadOnlyList<Vector2>> ToContours(
        IEnumerable<MusicSymbolCandidate> symbols)
    {
        var paths = symbols
            .SelectMany(x => x.SmoothPaths)
            .DistinctBy(x => $"{x.SourceAddress}\n{x.PathData}\n{x.Transform}", StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0)
            return Array.Empty<IReadOnlyList<Vector2>>();

        var tempPath = Path.Combine(Path.GetTempPath(), $"music-symbol-{Guid.NewGuid():N}.svg");
        try
        {
            WriteSvg(paths, tempPath);
            using var svg = SKSvg.CreateFromFile(tempPath);
            if (svg.Model is null)
                return Array.Empty<IReadOnlyList<Vector2>>();

            var contours = new List<List<Vector2>>();
            ReadPicture(svg.Model, Shim.SKMatrix.Identity, contours);
            return contours.Cast<IReadOnlyList<Vector2>>().ToArray();
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { }
        }
    }

    private static void WriteSvg(IReadOnlyList<SmoothSvgPath> paths, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" overflow=\"visible\">");
        foreach (var path in paths)
        {
            var transform = string.IsNullOrWhiteSpace(path.Transform)
                ? string.Empty
                : $" transform=\"{System.Net.WebUtility.HtmlEncode(path.Transform)}\"";
            sb.Append("<path fill=\"black\" fill-rule=\"evenodd\"")
                .Append(transform)
                .Append(" d=\"")
                .Append(System.Net.WebUtility.HtmlEncode(path.PathData))
                .AppendLine("\"/>");
        }
        sb.AppendLine("</svg>");
        File.WriteAllText(outputPath, sb.ToString());
    }

    private static void ReadPicture(
        Shim.SKPicture picture,
        Shim.SKMatrix parentMatrix,
        ICollection<List<Vector2>> contours)
    {
        if (picture.Commands is null)
            return;

        var matrix = parentMatrix;
        var stack = new Stack<Shim.SKMatrix>();
        foreach (var command in picture.Commands)
        {
            switch (command)
            {
                case Shim.SaveCanvasCommand:
                case Shim.SaveLayerCanvasCommand:
                    stack.Push(matrix);
                    break;
                case Shim.RestoreCanvasCommand:
                    if (stack.Count > 0) matrix = stack.Pop();
                    break;
                case Shim.SetMatrixCanvasCommand setMatrix:
                    matrix = parentMatrix.PreConcat(setMatrix.TotalMatrix);
                    break;
                case Shim.DrawPathCanvasCommand drawPath when drawPath.Path is not null:
                    ReadPath(drawPath.Path, matrix, contours);
                    break;
                case Shim.DrawPictureCanvasCommand drawPicture when drawPicture.Picture is not null:
                    ReadPicture(drawPicture.Picture, matrix, contours);
                    break;
            }
        }
    }

    private static void ReadPath(
        Shim.SKPath path,
        Shim.SKMatrix matrix,
        ICollection<List<Vector2>> contours)
    {
        List<Vector2>? contour = null;
        Vector2 current = default;
        var hasCurrent = false;

        void Flush()
        {
            if (contour is { Count: >= 3 }) contours.Add(contour);
            contour = null;
            hasCurrent = false;
        }

        void Start(Vector2 point)
        {
            Flush();
            contour = new List<Vector2> { point };
            current = point;
            hasCurrent = true;
        }

        void Add(Vector2 point)
        {
            contour ??= new List<Vector2>();
            if (contour.Count == 0 || Vector2.DistanceSquared(contour[^1], point) > 1e-10f)
                contour.Add(point);
            current = point;
            hasCurrent = true;
        }

        foreach (var command in path)
        {
            switch (command)
            {
                case Shim.MoveToPathCommand move:
                    Start(Map(matrix, move.X, move.Y));
                    break;
                case Shim.LineToPathCommand line when hasCurrent:
                    Add(Map(matrix, line.X, line.Y));
                    break;
                case Shim.QuadToPathCommand quad when hasCurrent:
                {
                    var p0 = current;
                    var p1 = Map(matrix, quad.X0, quad.Y0);
                    var p2 = Map(matrix, quad.X1, quad.Y1);
                    for (var i = 1; i <= CurveSteps; i++)
                    {
                        var t = i / (float)CurveSteps;
                        var mt = 1f - t;
                        Add(mt * mt * p0 + 2f * mt * t * p1 + t * t * p2);
                    }
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
                        var mt = 1f - t;
                        Add(mt * mt * mt * p0 + 3f * mt * mt * t * p1 + 3f * mt * t * t * p2 + t * t * t * p3);
                    }
                    break;
                }
                case Shim.ClosePathCommand:
                    Flush();
                    break;
            }
        }
        Flush();
    }

    private static Vector2 Map(Shim.SKMatrix matrix, float x, float y)
    {
        var point = matrix.MapPoint(new Shim.SKPoint(x, y));
        return new Vector2(point.X, point.Y);
    }
}
