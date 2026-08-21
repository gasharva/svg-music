using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MusicXml;
using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed record ResolverCheckSummary(string Resolver, int Matched, int Expected, int Extra);

public sealed record ReferenceValidationResult(
    string ReferencePath,
    string HtmlPath,
    string GeneratedMusicXmlPath,
    IReadOnlyList<ResolverCheckSummary> Summaries);

public static class ReferenceValidation
{
    public static ReferenceValidationResult? Run(
        string svgPath,
        string itemDirectory,
        SvgStructureResolution result)
    {
        var referencePath = Path.ChangeExtension(svgPath, ".musicxml");
        if (!File.Exists(referencePath))
            return null;

        // Read once as XML here because this diagnostic needs layout semantics that are not yet
        // exposed by the compact MusicXml model: staves inside a MusicXML part and system breaks.
        var referenceLayout = ReadReferenceLayout(referencePath);

        var expectedBlocks = referenceLayout.Blocks.ToHashSet();
        var actualBlocks = result.Structure.Map.Blocks
            .Select(x => new BlockItem(x.PartNumber, x.MeasureNumber))
            .ToHashSet();

        var matchedBlocks = expectedBlocks.Intersect(actualBlocks).Count();
        var missingBlocks = expectedBlocks.Except(actualBlocks)
            .OrderBy(x => x.Measure).ThenBy(x => x.Part)
            .ToArray();
        var extraBlocks = actualBlocks.Except(expectedBlocks)
            .OrderBy(x => x.Measure).ThenBy(x => x.Part)
            .ToArray();

        var rows = new List<CheckRow>();
        foreach (var block in expectedBlocks.Intersect(actualBlocks).OrderBy(x => x.Measure).ThenBy(x => x.Part))
            rows.Add(new("PartMeasureResolver", block.Measure, block.Part, "ok", $"P{block.Part}-M{block.Measure}", $"P{block.Part}-M{block.Measure}", "Matched physical staff/measure block."));
        foreach (var block in missingBlocks)
            rows.Add(new("PartMeasureResolver", block.Measure, block.Part, "missing", $"P{block.Part}-M{block.Measure}", "—", "Reference staff/measure block was not resolved."));
        foreach (var block in extraBlocks)
            rows.Add(new("PartMeasureResolver", block.Measure, block.Part, "extra", "—", $"P{block.Part}-M{block.Measure}", "Resolved staff/measure block has no reference counterpart."));

        var expectedClefs = referenceLayout.VisibleClefs;
        var actualClefs = result.Clefs
            .Select(c => new ClefItem(c.PartNumber, c.MeasureNumber, c.Kind.ToString()))
            .ToList();
        var matchedActual = new HashSet<int>();
        var matchedClefs = 0;

        foreach (var expected in expectedClefs.OrderBy(x => x.Measure).ThenBy(x => x.Part))
        {
            var candidate = actualClefs
                .Select((value, index) => (value, index))
                .FirstOrDefault(x => !matchedActual.Contains(x.index) &&
                                     x.value.Part == expected.Part &&
                                     x.value.Measure == expected.Measure &&
                                     string.Equals(x.value.Kind, expected.Kind, StringComparison.OrdinalIgnoreCase));
            if (candidate.value is not null)
            {
                matchedActual.Add(candidate.index);
                matchedClefs++;
                rows.Add(new("ClefResolver", expected.Measure, expected.Part, "ok", expected.Kind, candidate.value.Kind, expected.Reason));
            }
            else
            {
                rows.Add(new("ClefResolver", expected.Measure, expected.Part, "missing", expected.Kind, "—", expected.Reason + " Reference visible clef was not resolved."));
            }
        }

        for (var i = 0; i < actualClefs.Count; i++)
        {
            if (matchedActual.Contains(i))
                continue;
            var extra = actualClefs[i];
            rows.Add(new("ClefResolver", extra.Measure, extra.Part, "extra", "—", extra.Kind, "Resolved visible clef has no matching reference clef at this staff/measure."));
        }

        var htmlPath = Path.Combine(itemDirectory, "reference-checks.html");
        WriteHtml(htmlPath, rows);

        var generatedPath = Path.Combine(itemDirectory, "resolved.musicxml");
        WriteGeneratedMusicXml(generatedPath, result);

        return new ReferenceValidationResult(
            referencePath,
            htmlPath,
            generatedPath,
            new[]
            {
                new ResolverCheckSummary("PartMeasureResolver", matchedBlocks, expectedBlocks.Count, extraBlocks.Length),
                new ResolverCheckSummary("ClefResolver", matchedClefs, expectedClefs.Count, Math.Max(0, actualClefs.Count - matchedClefs))
            });
    }

    private static ReferenceLayout ReadReferenceLayout(string path)
    {
        using var stream = File.OpenRead(path);
        using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null });
        var doc = XDocument.Load(xml);
        var root = doc.Root ?? throw new InvalidDataException("MusicXML has no root element.");
        var musicXmlParts = Children(root, "part").ToArray();

        var blocks = new List<BlockItem>();
        var visibleClefs = new List<ExpectedClef>();
        var globalStaffOffset = 0;

        foreach (var musicXmlPart in musicXmlParts)
        {
            var measures = Children(musicXmlPart, "measure").ToArray();
            var staffCount = DetectStaffCount(musicXmlPart);

            for (var measureIndex = 0; measureIndex < measures.Length; measureIndex++)
            {
                for (var staff = 1; staff <= staffCount; staff++)
                    blocks.Add(new BlockItem(globalStaffOffset + staff, measureIndex + 1));
            }

            var currentClefs = new Dictionary<int, string>();
            for (var measureIndex = 0; measureIndex < measures.Length; measureIndex++)
            {
                var measure = measures[measureIndex];
                var measureNumber = measureIndex + 1;
                var explicitClefs = measure.Descendants()
                    .Where(x => x.Name.LocalName == "clef")
                    .Select(x => new
                    {
                        Staff = ParsePositiveInt(Attribute(x, "number")) ?? 1,
                        Kind = Child(x, "sign")?.Value.Trim()
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Kind))
                    .Select(x => (x.Staff, Kind: x.Kind!))
                    .ToArray();

                // An explicit clef is visibly printed, whether it is at the start or in the middle
                // of a measure. It also becomes the active clef for subsequent system starts.
                foreach (var clef in explicitClefs)
                {
                    currentClefs[clef.Staff] = clef.Kind;
                    AddVisibleClef(visibleClefs, globalStaffOffset + clef.Staff, measureNumber, clef.Kind,
                        "Explicit clef in reference MusicXML.");
                }

                var isSystemStart = measureIndex == 0 || HasSystemBreak(measure);
                if (!isSystemStart)
                    continue;

                // MuseScore does not normally repeat <clef> in MusicXML merely because a new
                // printed system starts. The engraving still shows the current clef at that point,
                // so carry the active clef state forward for visual comparison with the SVG.
                for (var staff = 1; staff <= staffCount; staff++)
                {
                    if (!currentClefs.TryGetValue(staff, out var kind))
                        continue;
                    AddVisibleClef(visibleClefs, globalStaffOffset + staff, measureNumber, kind,
                        measureIndex == 0 ? "Initial visible clef." : "Clef repeated visually at a new system/page.");
                }
            }

            globalStaffOffset += staffCount;
        }

        return new ReferenceLayout(blocks, visibleClefs);
    }

    private static int DetectStaffCount(XElement musicXmlPart)
    {
        var values = new List<int> { 1 };
        values.AddRange(musicXmlPart.Descendants()
            .Where(x => x.Name.LocalName == "staves")
            .Select(x => ParsePositiveInt(x.Value))
            .Where(x => x.HasValue)
            .Select(x => x!.Value));
        values.AddRange(musicXmlPart.Descendants()
            .Where(x => x.Name.LocalName == "clef")
            .Select(x => ParsePositiveInt(Attribute(x, "number")))
            .Where(x => x.HasValue)
            .Select(x => x!.Value));
        values.AddRange(musicXmlPart.Descendants()
            .Where(x => x.Name.LocalName == "staff")
            .Select(x => ParsePositiveInt(x.Value))
            .Where(x => x.HasValue)
            .Select(x => x!.Value));
        return values.Max();
    }

    private static bool HasSystemBreak(XElement measure)
    {
        foreach (var print in Children(measure, "print"))
        {
            if (IsYes(Attribute(print, "new-system")) || IsYes(Attribute(print, "new-page")))
                return true;
        }
        return false;
    }

    private static bool IsYes(string? value) =>
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static void AddVisibleClef(List<ExpectedClef> items, int part, int measure, string kind, string reason)
    {
        if (items.Any(x => x.Part == part && x.Measure == measure && string.Equals(x.Kind, kind, StringComparison.OrdinalIgnoreCase)))
            return;
        items.Add(new ExpectedClef(part, measure, kind, reason));
    }

    private static IEnumerable<XElement> Children(XContainer parent, string localName) =>
        parent.Elements().Where(x => x.Name.LocalName == localName);

    private static XElement? Child(XContainer parent, string localName) =>
        Children(parent, localName).FirstOrDefault();

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(x => x.Name.LocalName == localName)?.Value;

    private static int? ParsePositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : null;

    private static void WriteHtml(string path, IReadOnlyList<CheckRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Resolver reference checks</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:7px}th{background:#eee}.ok{background:#e9f8e9}.missing{background:#ffdede}.extra{background:#ffe8c2}.group td{background:#ddd;font-weight:700}</style></head><body>");
        sb.AppendLine("<h1>Resolver reference checks</h1><p><b>Part</b> here means a physical staff in the SVG. A single MusicXML piano &lt;part&gt; with &lt;staves&gt;2&lt;/staves&gt; therefore maps to P1 and P2.</p><table><thead><tr><th>Resolver</th><th>Measure</th><th>Part</th><th>Expected</th><th>Actual</th><th>Problem</th></tr></thead><tbody>");
        string? lastResolver = null;
        int? lastMeasure = null;
        foreach (var row in rows.OrderBy(x => x.Resolver).ThenBy(x => x.Measure).ThenBy(x => x.Part))
        {
            if (lastResolver != row.Resolver || lastMeasure != row.Measure)
            {
                sb.Append($"<tr class=\"group\"><td colspan=\"6\">{WebUtility.HtmlEncode(row.Resolver)} — measure {row.Measure}</td></tr>");
                lastResolver = row.Resolver;
                lastMeasure = row.Measure;
            }
            sb.Append($"<tr class=\"{row.State}\"><td>{WebUtility.HtmlEncode(row.Resolver)}</td><td>{row.Measure}</td><td>{row.Part}</td><td>{WebUtility.HtmlEncode(row.Expected)}</td><td>{WebUtility.HtmlEncode(row.Actual)}</td><td>{WebUtility.HtmlEncode(row.Description)}</td></tr>");
        }
        sb.AppendLine("</tbody></table></body></html>");
        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteGeneratedMusicXml(string path, SvgStructureResolution result)
    {
        XNamespace ns = XNamespace.None;
        var scoreParts = result.Structure.Parts
            .Select(p => new XElement(ns + "score-part", new XAttribute("id", $"P{p.Number}"), new XElement(ns + "part-name", $"Part {p.Number}")));

        var partElements = result.Structure.Parts.Select(part =>
            new XElement(ns + "part",
                new XAttribute("id", $"P{part.Number}"),
                result.Structure.Measures.Select(measure =>
                {
                    var measureEl = new XElement(ns + "measure", new XAttribute("number", measure.Number));
                    var clefs = result.Clefs.Where(c => c.PartNumber == part.Number && c.MeasureNumber == measure.Number).OrderBy(c => c.PhysicalBounds.Left).ToArray();
                    if (clefs.Length > 0)
                    {
                        measureEl.Add(new XElement(ns + "attributes",
                            clefs.Select(c => new XElement(ns + "clef",
                                new XElement(ns + "sign", c.Kind.ToString()),
                                new XElement(ns + "line", c.Kind == ClefKind.G ? 2 : c.Kind == ClefKind.F ? 4 : 3)))));
                    }
                    return measureEl;
                })));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "score-partwise",
                new XAttribute("version", "4.0"),
                new XElement(ns + "part-list", scoreParts),
                partElements));
        doc.Save(path);
    }

    private sealed record ReferenceLayout(IReadOnlyList<BlockItem> Blocks, IReadOnlyList<ExpectedClef> VisibleClefs);
    private sealed record BlockItem(int Part, int Measure);
    private sealed record ExpectedClef(int Part, int Measure, string Kind, string Reason);
    private sealed record ClefItem(int Part, int Measure, string Kind);
    private sealed record CheckRow(string Resolver, int Measure, int Part, string State, string Expected, string Actual, string Description);
}
