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

        var reader = new MusicXmlReader();
        var reference = reader.Read(referencePath);

        var expectedParts = reference.Parts.Count;
        var expectedMeasures = reference.Parts.Count == 0 ? 0 : reference.Parts.Max(p => p.Measures.Count);
        var actualParts = result.Structure.Parts.Count;
        var actualMeasures = result.Structure.Measures.Count;

        var pmMatched = Math.Min(expectedParts, actualParts) * Math.Min(expectedMeasures, actualMeasures);
        var pmExpected = expectedParts * expectedMeasures;
        var pmActual = actualParts * actualMeasures;
        var pmExtra = Math.Max(0, pmActual - pmExpected);

        var expectedClefs = ReadClefs(referencePath);
        var actualClefs = result.Clefs
            .Select(c => new ClefItem(c.PartNumber, c.MeasureNumber, c.Kind.ToString(), c.PhysicalBounds.Left))
            .ToList();
        var matchedActual = new HashSet<int>();
        var clefRows = new List<CheckRow>();
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
                clefRows.Add(new("ClefResolver", expected.Measure, expected.Part, "ok", expected.Kind, candidate.value.Kind, "Matched reference clef."));
            }
            else
            {
                clefRows.Add(new("ClefResolver", expected.Measure, expected.Part, "missing", expected.Kind, "—", "Reference clef was not resolved."));
            }
        }

        for (var i = 0; i < actualClefs.Count; i++)
        {
            if (matchedActual.Contains(i))
                continue;
            var extra = actualClefs[i];
            clefRows.Add(new("ClefResolver", extra.Measure, extra.Part, "extra", "—", extra.Kind, "Resolved clef has no matching reference clef."));
        }

        var rows = new List<CheckRow>();
        var maxMeasures = Math.Max(expectedMeasures, actualMeasures);
        var maxParts = Math.Max(expectedParts, actualParts);
        for (var measure = 1; measure <= maxMeasures; measure++)
        for (var part = 1; part <= maxParts; part++)
        {
            var expected = part <= expectedParts && measure <= expectedMeasures;
            var actual = part <= actualParts && measure <= actualMeasures;
            rows.Add(expected && actual
                ? new("PartMeasureResolver", measure, part, "ok", $"P{part}-M{measure}", $"P{part}-M{measure}", "Matched logical part/measure block.")
                : expected
                    ? new("PartMeasureResolver", measure, part, "missing", $"P{part}-M{measure}", "—", "Reference part/measure block was not resolved.")
                    : new("PartMeasureResolver", measure, part, "extra", "—", $"P{part}-M{measure}", "Resolved part/measure block has no reference counterpart."));
        }
        rows.AddRange(clefRows);

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
                new ResolverCheckSummary("PartMeasureResolver", pmMatched, pmExpected, pmExtra),
                new ResolverCheckSummary("ClefResolver", matchedClefs, expectedClefs.Count, Math.Max(0, actualClefs.Count - matchedClefs))
            });
    }

    private static IReadOnlyList<ClefItem> ReadClefs(string path)
    {
        using var stream = File.OpenRead(path);
        using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null });
        var doc = XDocument.Load(xml);
        var root = doc.Root!;
        var items = new List<ClefItem>();
        var parts = root.Elements().Where(x => x.Name.LocalName == "part").ToArray();
        for (var p = 0; p < parts.Length; p++)
        {
            var measures = parts[p].Elements().Where(x => x.Name.LocalName == "measure").ToArray();
            for (var m = 0; m < measures.Length; m++)
            {
                foreach (var clef in measures[m].Descendants().Where(x => x.Name.LocalName == "clef"))
                {
                    var sign = clef.Elements().FirstOrDefault(x => x.Name.LocalName == "sign")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(sign))
                        continue;
                    var numberText = clef.Attributes().FirstOrDefault(x => x.Name.LocalName == "number")?.Value;
                    var staff = int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
                    items.Add(new ClefItem(p + staff, m + 1, sign, 0));
                }
            }
        }
        return items;
    }

    private static void WriteHtml(string path, IReadOnlyList<CheckRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Resolver reference checks</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:7px}th{background:#eee}.ok{background:#e9f8e9}.missing{background:#ffdede}.extra{background:#ffe8c2}.group td{background:#ddd;font-weight:700}</style></head><body>");
        sb.AppendLine("<h1>Resolver reference checks</h1><table><thead><tr><th>Resolver</th><th>Measure</th><th>Part</th><th>Expected</th><th>Actual</th><th>Problem</th></tr></thead><tbody>");
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

    private sealed record ClefItem(int Part, int Measure, string Kind, double X);
    private sealed record CheckRow(string Resolver, int Measure, int Part, string State, string Expected, string Actual, string Description);
}
