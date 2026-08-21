using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
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

        // This diagnostic intentionally reads layout-only MusicXML details directly: physical
        // staves, system breaks and visible/repeated notation are not yet part of the compact model.
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
        var matchedActualClefs = new HashSet<int>();
        var matchedClefs = 0;

        foreach (var expected in expectedClefs.OrderBy(x => x.Measure).ThenBy(x => x.Part))
        {
            var candidate = actualClefs
                .Select((value, index) => (value, index))
                .FirstOrDefault(x => !matchedActualClefs.Contains(x.index) &&
                                     x.value.Part == expected.Part &&
                                     x.value.Measure == expected.Measure &&
                                     string.Equals(x.value.Kind, expected.Kind, StringComparison.OrdinalIgnoreCase));
            if (candidate.value is not null)
            {
                matchedActualClefs.Add(candidate.index);
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
            if (matchedActualClefs.Contains(i))
                continue;
            var extra = actualClefs[i];
            rows.Add(new("ClefResolver", extra.Measure, extra.Part, "extra", "—", extra.Kind, "Resolved visible clef has no matching reference clef at this staff/measure."));
        }

        var expectedMeters = referenceLayout.VisibleMeters;
        var actualMeters = result.Meters
            .Select(m => new MeterItem(m.PartNumber, m.MeasureNumber, m.BeatNumber, m.BeatValue, m.Side))
            .ToList();
        var matchedActualMeters = new HashSet<int>();
        var matchedMeters = 0;

        foreach (var expected in expectedMeters.OrderBy(x => x.Measure).ThenBy(x => x.Part))
        {
            var candidate = actualMeters
                .Select((value, index) => (value, index))
                .FirstOrDefault(x => !matchedActualMeters.Contains(x.index) &&
                                     x.value.Part == expected.Part &&
                                     x.value.Measure == expected.Measure &&
                                     x.value.Beats == expected.Beats &&
                                     x.value.BeatType == expected.BeatType);
            if (candidate.value is not null)
            {
                matchedActualMeters.Add(candidate.index);
                matchedMeters++;
                rows.Add(new("MeterResolver", expected.Measure, expected.Part, "ok", expected.Label, candidate.value.Label, expected.Reason));
            }
            else
            {
                rows.Add(new("MeterResolver", expected.Measure, expected.Part, "missing", expected.Label, "—", expected.Reason + " Reference visible meter was not resolved."));
            }
        }

        for (var i = 0; i < actualMeters.Count; i++)
        {
            if (matchedActualMeters.Contains(i))
                continue;
            var extra = actualMeters[i];
            rows.Add(new("MeterResolver", extra.Measure, extra.Part, "extra", "—", extra.Label, $"Resolved {extra.Side.ToString().ToLowerInvariant()}-side meter has no matching reference meter at this staff/measure."));
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
                new ResolverCheckSummary("MeterResolver", matchedMeters, expectedMeters.Count, Math.Max(0, actualMeters.Count - matchedMeters)),
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
        var visibleMeters = new List<ExpectedMeter>();
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

                var explicitMeters = measure.Descendants()
                    .Where(x => x.Name.LocalName == "time")
                    .Select(x => new
                    {
                        Element = x,
                        Staff = ParsePositiveInt(Attribute(x, "number")),
                        Beats = ParsePositiveInt(Child(x, "beats")?.Value),
                        BeatType = ParsePositiveInt(Child(x, "beat-type")?.Value)
                    })
                    .Where(x => x.Beats.HasValue && x.BeatType.HasValue)
                    .ToArray();

                foreach (var meter in explicitMeters)
                {
                    if (meter.Staff.HasValue)
                    {
                        AddVisibleMeter(visibleMeters, globalStaffOffset + meter.Staff.Value, measureNumber,
                            meter.Beats!.Value, meter.BeatType!.Value,
                            "Explicit staff-specific time signature in reference MusicXML.");
                    }
                    else
                    {
                        // An unnumbered MusicXML <time> applies to the whole multi-staff part and is
                        // engraved on every staff, so compare it with every physical SVG staff.
                        for (var staff = 1; staff <= staffCount; staff++)
                            AddVisibleMeter(visibleMeters, globalStaffOffset + staff, measureNumber,
                                meter.Beats!.Value, meter.BeatType!.Value,
                                "Part-wide time signature in reference MusicXML.");
                    }
                }

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

                foreach (var clef in explicitClefs)
                {
                    currentClefs[clef.Staff] = clef.Kind;
                    AddVisibleClef(visibleClefs, globalStaffOffset + clef.Staff, measureNumber, clef.Kind,
                        "Explicit clef in reference MusicXML.");
                }

                var isSystemStart = measureIndex == 0 || HasSystemBreak(measure);
                if (!isSystemStart)
                    continue;

                // MusicXML usually carries the clef state rather than repeating <clef> at each
                // printed system. SVG contains the visually repeated glyph, so recreate it here.
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

        return new ReferenceLayout(blocks, visibleClefs, visibleMeters);
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

    private static void AddVisibleMeter(List<ExpectedMeter> items, int part, int measure, int beats, int beatType, string reason)
    {
        if (items.Any(x => x.Part == part && x.Measure == measure && x.Beats == beats && x.BeatType == beatType))
            return;
        items.Add(new ExpectedMeter(part, measure, beats, beatType, reason));
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
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:7px}th{background:#eee}.ok{background:#e9f8e9}.missing{background:#ffdede}.extra{background:#ffe8c2}.resolver{margin:0 0 14px}.resolver>summary{cursor:pointer;background:#cfd3d6;padding:9px;font-weight:700;border:1px solid #aaa}.resolver.problem>summary{background:#ffe8c2}.measure td{background:#ddd;font-weight:700}.row-link{float:right;font-size:12px;font-weight:400}.anchor{scroll-margin-top:12px}.anchor:target{outline:3px solid #4378d0;outline-offset:-3px}</style></head><body>");
        sb.AppendLine("<h1>Resolver reference checks</h1><p><b>Part</b> here means a physical staff in the SVG. A single MusicXML piano &lt;part&gt; with &lt;staves&gt;2&lt;/staves&gt; therefore maps to P1 and P2.</p>");

        var resolverGroups = rows
            .OrderBy(x => x.Resolver)
            .ThenBy(x => x.Measure)
            .ThenBy(x => x.Part)
            .GroupBy(x => x.Resolver);

        var sequence = 0;
        foreach (var resolverGroup in resolverGroups)
        {
            var resolverRows = resolverGroup.ToArray();
            var allGreen = resolverRows.All(x => x.State == "ok");
            var okCount = resolverRows.Count(x => x.State == "ok");
            var problemCount = resolverRows.Length - okCount;
            var css = allGreen ? "resolver" : "resolver problem";
            var open = allGreen ? string.Empty : " open";
            sb.Append($"<details class=\"{css}\"{open}><summary>{WebUtility.HtmlEncode(resolverGroup.Key)} — {okCount}/{resolverRows.Length} ok");
            if (problemCount > 0)
                sb.Append($" — {problemCount} problem{(problemCount == 1 ? string.Empty : "s")}");
            sb.AppendLine("</summary>");
            sb.AppendLine("<table><thead><tr><th>Resolver</th><th>Measure</th><th>Part</th><th>Expected</th><th>Actual</th><th>Problem</th></tr></thead><tbody>");

            int? lastMeasure = null;
            foreach (var row in resolverRows)
            {
                if (lastMeasure != row.Measure)
                {
                    sb.Append($"<tr class=\"measure\"><td colspan=\"6\">measure {row.Measure}</td></tr>");
                    lastMeasure = row.Measure;
                }

                var id = $"check-{Slug(row.Resolver)}-m{row.Measure}-p{row.Part}-{++sequence}";
                sb.Append($"<tr id=\"{id}\" class=\"{row.State} anchor\"><td>{WebUtility.HtmlEncode(row.Resolver)}</td><td>{row.Measure}</td><td>{row.Part}</td><td>{WebUtility.HtmlEncode(row.Expected)}</td><td>{WebUtility.HtmlEncode(row.Actual)}</td><td>{WebUtility.HtmlEncode(row.Description)} <button class=\"row-link\" type=\"button\" onclick=\"copyLink('{id}',this)\">Link</button></td></tr>");
            }

            sb.AppendLine("</tbody></table></details>");
        }

        sb.AppendLine("<script>function copyLink(id,button){const u=new URL(window.location.href);u.hash=id;const text=u.toString();const done=()=>{const old=button.textContent;button.textContent='Copied';setTimeout(()=>button.textContent=old,1000);};if(navigator.clipboard&&window.isSecureContext){navigator.clipboard.writeText(text).then(done).catch(()=>fallback(text,done));}else fallback(text,done);}function fallback(text,done){const t=document.createElement('textarea');t.value=text;t.style.position='fixed';t.style.opacity='0';document.body.appendChild(t);t.select();document.execCommand('copy');t.remove();done();}if(location.hash){const el=document.querySelector(location.hash);if(el){const details=el.closest('details');if(details)details.open=true;setTimeout(()=>el.scrollIntoView({block:'center'}),0);}}</script></body></html>");
        File.WriteAllText(path, sb.ToString());
    }

    private static string Slug(string value) =>
        new(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    private static void WriteGeneratedMusicXml(string path, SvgStructureResolution result)
    {
        XNamespace ns = XNamespace.None;
        var staffCount = Math.Max(1, result.Structure.Parts.Count);

        // SvgStructure's P1/P2/... are physical staves, not independent instruments. Emit one
        // multi-staff MusicXML part so a piano grand staff is connected by one brace.
        var scorePart = new XElement(ns + "score-part",
            new XAttribute("id", "P1"),
            new XElement(ns + "part-name", new XAttribute("print-object", "no"), "Resolved score"));

        var part = new XElement(ns + "part", new XAttribute("id", "P1"));
        foreach (var measure in result.Structure.Measures)
        {
            var measureEl = new XElement(ns + "measure", new XAttribute("number", measure.Number));
            if (measure.StartsNewSystem)
                measureEl.Add(new XElement(ns + "print", new XAttribute("new-system", "yes")));

            var meters = result.Meters
                .Where(m => m.MeasureNumber == measure.Number)
                .OrderBy(m => m.PartNumber)
                .ToArray();
            var clefs = result.Clefs
                .Where(c => c.MeasureNumber == measure.Number)
                .OrderBy(c => c.PhysicalBounds.Left)
                .ThenBy(c => c.PartNumber)
                .ToArray();

            var needsAttributes = measure.Number == 1 || meters.Length > 0 || clefs.Length > 0;
            if (needsAttributes)
            {
                var attributes = new XElement(ns + "attributes");

                if (meters.Length > 0)
                {
                    var distinctMeters = meters.Select(m => (m.BeatNumber, m.BeatValue)).Distinct().ToArray();
                    var allStavesCovered = Enumerable.Range(1, staffCount)
                        .All(staff => meters.Any(m => m.PartNumber == staff));

                    if (distinctMeters.Length == 1 && allStavesCovered)
                    {
                        attributes.Add(TimeElement(ns, distinctMeters[0].BeatNumber, distinctMeters[0].BeatValue, staff: null));
                    }
                    else
                    {
                        foreach (var meter in meters)
                            attributes.Add(TimeElement(ns, meter.BeatNumber, meter.BeatValue, meter.PartNumber));
                    }
                }

                if (measure.Number == 1 && staffCount > 1)
                {
                    attributes.Add(new XElement(ns + "staves", staffCount));
                    attributes.Add(new XElement(ns + "part-symbol", "brace"));
                }

                foreach (var clef in clefs)
                {
                    attributes.Add(new XElement(ns + "clef",
                        staffCount > 1 ? new XAttribute("number", clef.PartNumber) : null,
                        new XElement(ns + "sign", clef.Kind.ToString()),
                        new XElement(ns + "line", clef.Kind == ClefKind.G ? 2 : clef.Kind == ClefKind.F ? 4 : 3)));
                }

                measureEl.Add(attributes);
            }

            part.Add(measureEl);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "score-partwise",
                new XAttribute("version", "4.0"),
                new XElement(ns + "part-list", scorePart),
                part));
        doc.Save(path);
    }

    private static XElement TimeElement(XNamespace ns, int beats, int beatType, int? staff) =>
        new(ns + "time",
            staff.HasValue ? new XAttribute("number", staff.Value) : null,
            new XElement(ns + "beats", beats),
            new XElement(ns + "beat-type", beatType));

    private sealed record ReferenceLayout(
        IReadOnlyList<BlockItem> Blocks,
        IReadOnlyList<ExpectedClef> VisibleClefs,
        IReadOnlyList<ExpectedMeter> VisibleMeters);

    private sealed record BlockItem(int Part, int Measure);
    private sealed record ExpectedClef(int Part, int Measure, string Kind, string Reason);
    private sealed record ExpectedMeter(int Part, int Measure, int Beats, int BeatType, string Reason)
    {
        public string Label => $"{Beats}/{BeatType}";
    }
    private sealed record ClefItem(int Part, int Measure, string Kind);
    private sealed record MeterItem(int Part, int Measure, int Beats, int BeatType, MeterSide Side)
    {
        public string Label => $"{Beats}/{BeatType}";
    }
    private sealed record CheckRow(string Resolver, int Measure, int Part, string State, string Expected, string Actual, string Description);
}
