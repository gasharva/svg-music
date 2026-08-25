using System.Text.Json;

namespace GlyphGeometry;

public static class GeometryCatalogExporter
{
    public static void ExportEmbedded(string outputPath, int pointCount = GeometryGlyphClassifier.DefaultPointCount)
    {
        var refs = GeometryReferenceCatalog.LoadEmbedded(pointCount);
        var dto = refs.Select(r => new
        {
            @class = r.ClassName,
            font = r.FontName,
            descriptor = new
            {
                aspect = r.Descriptor.Aspect,
                holes = r.Descriptor.Holes,
                maxDepth = r.Descriptor.MaxDepth,
                contours = r.Descriptor.Contours.Select(c => new
                {
                    perimeterRatio = c.PerimeterRatio,
                    areaRatio = c.AreaRatio,
                    center = new[] { c.Center.X, c.Center.Y },
                    size = new[] { c.Size.X, c.Size.Y },
                    depth = c.Depth,
                    points = c.Points.Select(p => new[] { p.X, p.Y })
                })
            }
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
    }
}
