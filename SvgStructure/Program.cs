using SvgStructure.Services;

if (args.Length == 0)
{
    Console.WriteLine("Usage: SvgStructure <svg-file> [classified-output.png]");
    return;
}

var svgPath = args[0];
var classifiedOutputPath = args.Length > 1 ? args[1] : null;

var geometryReader = new SvgSceneGeometryReader();
var systemDetector = new StaffSystemDetector();
var structureBuilder = new ScoreStructureBuilder();
var measureOverlayRenderer = new MeasureOverlayRenderer();
var classificationRenderer = new PrimitiveClassificationRenderer(0.25);

var lines = geometryReader.ReadLines(svgPath);
var systems = systemDetector.Detect(lines);
var score = structureBuilder.Build(systems);

var measureImagePath = measureOverlayRenderer.Render(svgPath, systems);
var classifiedImagePath = classificationRenderer.Render(svgPath, systems, classifiedOutputPath);

Console.WriteLine($"lines: {lines.Count}");
Console.WriteLine($"systems: {systems.Count}");
Console.WriteLine();

foreach (var part in score.Parts)
{
    Console.WriteLine($"part {part.Id}");
    foreach (var measure in part.Measures)
        Console.WriteLine($"  measure {measure.Number,2}: width={measure.Width:F2}");
}

Console.WriteLine();
Console.WriteLine($"measure overlay:      {measureImagePath}");
Console.WriteLine($"classified primitives: {classifiedImagePath}");
