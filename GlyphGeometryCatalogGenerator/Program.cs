using GlyphGeometry;

var output = args.Length > 0 ? args[0] : Path.Combine("References", "geometry-catalog.json");
var points = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : GeometryGlyphClassifier.DefaultPointCount;
GeometryCatalogExporter.ExportEmbedded(output, points);
Console.WriteLine($"Geometry catalog written: {Path.GetFullPath(output)} ({points} points/contour)");
