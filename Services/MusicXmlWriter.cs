using System.Text.RegularExpressions;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class MusicXmlWriter
{
    private static readonly Regex TimeDigitRegex = new(
        @"(?:timeSig|timeSignature|timesig|numeral)[^0-9]*(?<digit>[0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Write(string path, AnalysisResult analysis, RecognitionConfig config)
    {
        var staffGroups = BuildStaffGroups(analysis);
        var pianoLayout = staffGroups.Any(x => x.Count == 2);
        var time = DetectTimeSignature(analysis, staffGroups.FirstOrDefault(), config);

        var score = new XElement("score-partwise", new XAttribute("version", "4.0"),
            new XElement("part-list",
                new XElement("score-part", new XAttribute("id", "P1"),
                    new XElement("part-name", pianoLayout ? "Piano" : "SVG import"))));
        var part = new XElement("part", new XAttribute("id", "P1"));
        score.Add(part);

        var measureNumber = 1;
        var firstMeasure = true;
        foreach (var group in staffGroups)
        {
            var boundaries = DetectMeasureBoundaries(analysis, group);
            for (var segment = 0; segment + 1 < boundaries.Count; segment++)
            {
                var left = boundaries[segment];
                var right = boundaries[segment + 1];
                var measure = new XElement("measure", new XAttribute("number", measureNumber++));

                if (firstMeasure)
                {
                    var attributes = new XElement("attributes",
                        new XElement("divisions", config.Divisions),
                        new XElement("key", new XElement("fifths", 0)),
                        new XElement("time", new XElement("beats", time.Beats), new XElement("beat-type", time.BeatType)));

                    if (group.Count > 1) attributes.Add(new XElement("staves", group.Count));
                    AddClefs(attributes, analysis, group, config);
                    measure.Add(attributes);
                    firstMeasure = false;
                }
                else if (segment == 0)
                {
                    // A new SVG system may repeat clefs even though it continues the same MusicXML part.
                    var attributes = new XElement("attributes");
                    AddClefs(attributes, analysis, group, config);
                    measure.Add(attributes);
                }

                var firstStaffDuration = 0;
                for (var staffNumber = 1; staffNumber <= group.Count; staffNumber++)
                {
                    var staff = group[staffNumber - 1];
                    var timed = analysis.Events
                        .Where(x => x.StaffIndex == staff.Index && IsTimedEvent(x))
                        .Where(x => x.X >= left && (segment + 2 == boundaries.Count ? x.X <= right : x.X < right))
                        .OrderBy(x => x.X)
                        .ThenByDescending(x => x.Y)
                        .ToList();

                    if (staffNumber > 1 && firstStaffDuration > 0)
                        measure.Add(new XElement("backup", new XElement("duration", firstStaffDuration)));

                    foreach (var evt in timed)
                        measure.Add(CreateNote(evt, staffNumber));

                    var duration = timed.Where(x => !x.Chord).Sum(x => x.Duration);
                    if (staffNumber == 1) firstStaffDuration = duration;
                }

                part.Add(measure);
            }
        }

        new XDocument(new XDeclaration("1.0", "UTF-8", null), score).Save(path);
    }

    private static void AddClefs(XElement attributes, AnalysisResult analysis, IReadOnlyList<Staff> group,
        RecognitionConfig config)
    {
        for (var staffNumber = 1; staffNumber <= group.Count; staffNumber++)
        {
            var clef = ClefForStaff(analysis, group[staffNumber - 1], config);
            attributes.Add(new XElement("clef",
                group.Count > 1 ? new XAttribute("number", staffNumber) : null,
                new XElement("sign", clef.Sign),
                new XElement("line", clef.Line)));
        }
    }

    private static List<double> DetectMeasureBoundaries(AnalysisResult analysis, IReadOnlyList<Staff> group)
    {
        var left = group.Min(x => x.Left);
        var right = group.Max(x => x.Right);
        var averageSpace = group.Average(x => x.Space);
        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);

        var geometricCandidates = analysis.LineSegments
            .Where(x => x.CenterX >= left - averageSpace && x.CenterX <= right + averageSpace)
            .Where(x => IsGeometricBarline(x, group, averageSpace))
            .Select(x => x.CenterX)
            .ToList();

        // Keep the classifier-based path as a fallback for exporters that encode barlines
        // as closed paths rather than SVG line/polyline/rect geometry.
        var classifiedCandidates = analysis.Uses
            .Where(x => x.X >= left - averageSpace && x.X <= right + averageSpace)
            .Select(x => new { Use = x, Class = classes.GetValueOrDefault(x.SymbolId) })
            .Where(x => x.Class is not null)
            .Where(x => IsBarlineClass(x.Class!.Kind, x.Class.ReferenceId,
                x.Class.WidthInSpaces, x.Class.HeightInSpaces))
            .Select(x => x.Use.X);

        var candidates = geometricCandidates
            .Concat(classifiedCandidates)
            .OrderBy(x => x)
            .ToList();

        // Merge double/repeated barlines and the same barline represented once per piano staff.
        var merged = new List<double>();
        foreach (var x in candidates)
        {
            if (merged.Count == 0 || x - merged[^1] > averageSpace * .65)
                merged.Add(x);
            else
                merged[^1] = (merged[^1] + x) / 2;
        }

        // The left edge is a system boundary, not an empty first measure. Internal and right barlines split measures.
        var result = new List<double> { left };
        result.AddRange(merged.Where(x => x > left + averageSpace * 2 && x < right - averageSpace * .8));
        result.Add(right);
        return result.Distinct().OrderBy(x => x).ToList();
    }

    private static bool IsGeometricBarline(SvgLineSegment line, IReadOnlyList<Staff> group, double averageSpace)
    {
        var explicitlyMarked = line.CssClass?.Contains("barline", StringComparison.OrdinalIgnoreCase) == true;
        var nearlyVertical = line.Width <= averageSpace * (explicitlyMarked ? .8 : .35);
        if (!nearlyVertical) return false;

        // A barline may be emitted once for each staff of a piano system. Requiring it to
        // cross the whole grand staff would therefore miss the normal MuseScore representation.
        return group.Any(staff =>
        {
            var minimumHeight = staff.Space * (explicitlyMarked ? 2.8 : 3.4);
            return line.Height >= minimumHeight &&
                   line.Top <= staff.Top + staff.Space * .45 &&
                   line.Bottom >= staff.Bottom - staff.Space * .45;
        });
    }

    private static bool IsBarlineClass(string kind, string referenceId, double width, double height)
    {
        if (kind.Contains("barline", StringComparison.OrdinalIgnoreCase) ||
            referenceId.Contains("barline", StringComparison.OrdinalIgnoreCase) ||
            referenceId.Contains("barLine", StringComparison.OrdinalIgnoreCase))
            return true;

        // Generic direct paths often have no semantic SMuFL name. A barline is tall and very narrow.
        return width is > 0 and <= .55 && height >= 3.2;
    }

    private static (int Beats, int BeatType) DetectTimeSignature(
        AnalysisResult analysis,
        IReadOnlyList<Staff>? firstGroup,
        RecognitionConfig config)
    {
        if (firstGroup is null || firstGroup.Count == 0) return (config.Beats, config.BeatType);
        var staff = firstGroup[0];
        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        var firstNoteX = analysis.Events
            .Where(x => x.StaffIndex == staff.Index && IsTimedEvent(x))
            .Select(x => x.X)
            .DefaultIfEmpty(staff.Left + staff.Space * 12)
            .Min();

        var digits = analysis.Uses
            .Where(x => x.X >= staff.Left && x.X < firstNoteX)
            .Where(x => x.Y >= staff.Top - staff.Space * 1.5 && x.Y <= staff.Bottom + staff.Space * 1.5)
            .Select(x => new { Use = x, Class = classes.GetValueOrDefault(x.SymbolId) })
            .Where(x => x.Class is not null)
            .Select(x => new { x.Use, Digit = ReadTimeDigit(x.Class!.Kind, x.Class.ReferenceId) })
            .Where(x => x.Digit.HasValue)
            .OrderBy(x => x.Use.X)
            .ThenBy(x => x.Use.Y)
            .ToList();

        if (digits.Count < 2) return (config.Beats, config.BeatType);

        // Use the rightmost vertical pair before the first note; clefs and key signatures are farther left.
        var pair = digits
            .GroupBy(x => (int)Math.Round(x.Use.X / (staff.Space * .5)))
            .Select(group =>
            {
                var column = group.ToList();
                var upper = column
                    .Where(x => x.Use.Y < staff.Center)
                    .OrderBy(x => Math.Abs(x.Use.Y - staff.Center))
                    .FirstOrDefault();
                var lower = column
                    .Where(x => x.Use.Y >= staff.Center)
                    .OrderBy(x => Math.Abs(x.Use.Y - staff.Center))
                    .FirstOrDefault();

                return upper is null || lower is null
                    ? null
                    : new { Upper = upper, Lower = lower, X = column.Average(x => x.Use.X) };
            })
            .Where(x => x is not null)
            .OrderByDescending(x => x!.X)
            .FirstOrDefault();

        if (pair is null) return (config.Beats, config.BeatType);
        var beats = pair.Upper.Digit!.Value;
        var beatType = pair.Lower.Digit!.Value;
        return beats is >= 1 and <= 12 && beatType is 1 or 2 or 4 or 8 or 16 or 32
            ? (beats, beatType)
            : (config.Beats, config.BeatType);
    }

    private static int? ReadTimeDigit(string kind, string referenceId)
    {
        foreach (var value in new[] { kind, referenceId })
        {
            var match = TimeDigitRegex.Match(value ?? string.Empty);
            if (match.Success && int.TryParse(match.Groups["digit"].Value, out var digit)) return digit;
        }
        return null;
    }

    private static List<List<Staff>> BuildStaffGroups(AnalysisResult analysis)
    {
        var staves = analysis.Staves.OrderBy(x => x.Center).ToList();
        if (staves.Count < 2) return staves.Select(x => new List<Staff> { x }).ToList();

        var clefs = staves.ToDictionary(
            x => x.Index,
            x => analysis.Events
                .Where(e => e.StaffIndex == x.Index && e.Kind.StartsWith("clef-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.X)
                .FirstOrDefault()?.ClefSign);

        var recognizablePairs = 0;
        for (var i = 0; i + 1 < staves.Count; i += 2)
            if (clefs[staves[i].Index] == "G" && clefs[staves[i + 1].Index] == "F") recognizablePairs++;

        var expectedPairs = staves.Count / 2;
        var usePianoPairs = expectedPairs > 0 && recognizablePairs >= Math.Max(1, expectedPairs / 2);
        if (!usePianoPairs) return staves.Select(x => new List<Staff> { x }).ToList();

        var result = new List<List<Staff>>();
        for (var i = 0; i < staves.Count; i += 2)
            result.Add(i + 1 < staves.Count ? [staves[i], staves[i + 1]] : [staves[i]]);
        return result;
    }

    private static (string Sign, int Line) ClefForStaff(AnalysisResult analysis, Staff staff, RecognitionConfig config)
    {
        var clef = analysis.Events
            .Where(x => x.StaffIndex == staff.Index && x.Kind.StartsWith("clef-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.X)
            .FirstOrDefault();
        return (clef?.ClefSign ?? config.DefaultClef, clef?.ClefLine ?? config.DefaultClefLine);
    }

    private static bool IsTimedEvent(RecognizedEvent evt) =>
        evt.Step is not null || evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase);

    private static XElement CreateNote(RecognizedEvent evt, int staffNumber)
    {
        var note = new XElement("note");
        if (evt.Chord) note.Add(new XElement("chord"));
        if (evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase)) note.Add(new XElement("rest"));
        else note.Add(new XElement("pitch",
            new XElement("step", evt.Step),
            evt.Alter == 0 ? null : new XElement("alter", evt.Alter),
            new XElement("octave", evt.Octave)));

        note.Add(new XElement("duration", evt.Duration));
        note.Add(new XElement("voice", 1));
        note.Add(new XElement("type", evt.Type ?? "quarter"));
        if (evt.Dotted) note.Add(new XElement("dot"));
        if (evt.Alter != 0)
            note.Add(new XElement("accidental", evt.Alter switch
            {
                -2 => "flat-flat", -1 => "flat", 1 => "sharp", 2 => "double-sharp", _ => "natural"
            }));
        note.Add(new XElement("staff", staffNumber));
        return note;
    }
}
