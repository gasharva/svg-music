using System.Globalization;
using System.Xml.Linq;
using SkiaSharp;
using Svg.Skia;

namespace SvgSymbolScaler;

public sealed class SvgPdfWriter
{
    public void Write(IReadOnlyList<string> svgFiles, string outputPath)
    {
        if (svgFiles.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var stream = File.Create(outputPath);
        using var document = SKDocument.CreatePdf(stream);

        foreach (var file in svgFiles)
        {
            var page = ReadPage(file);
            using var svg = new SKSvg();
            svg.Load(file);
            var picture = svg.Picture ?? throw new InvalidOperationException($"Could not render SVG: {file}");

            var canvas = document.BeginPage((float)page.Width, (float)page.Height);
            canvas.Clear(SKColors.White);
            canvas.Translate((float)-page.X, (float)-page.Y);
            canvas.DrawPicture(picture);
            document.EndPage();
        }

        document.Close();
    }

    private static PageBox ReadPage(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidOperationException("SVG root is missing.");
        var values = ParseNumbers((string?)root.Attribute("viewBox"));
        if (values.Length == 4 && values[2] > 0 && values[3] > 0)
            return new PageBox(values[0], values[1], values[2], values[3]);

        var width = ParseLength((string?)root.Attribute("width"));
        var height = ParseLength((string?)root.Attribute("height"));
        if (width <= 0 || height <= 0) throw new InvalidOperationException($"SVG has no usable page size: {path}");
        return new PageBox(0, 0, width, height);
    }

    private static double ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var number = new string(value.TakeWhile(c => char.IsDigit(c) || c is '.' or '-' or '+').ToArray());
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static double[] ParseNumbers(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : System.Text.RegularExpressions.Regex.Matches(value, @"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?")
            .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();

    private readonly record struct PageBox(double X, double Y, double Width, double Height);
}
