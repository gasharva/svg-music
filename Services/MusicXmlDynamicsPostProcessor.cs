using System.Globalization;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Writes spatial expression marks after note/voice layout. Directions keep an explicit MusicXML
/// offset, so later voice reordering cannot move a dynamic or collapse the two ends of a hairpin.
/// </summary>
public sealed class MusicXmlDynamicsPostProcessor
{
    private readonly record struct VerticalInterval(double X, double Top, double Bottom);

    public void Apply(string path, AnalysisResult analysis)
    {
        if (analysis.Directions.Count == 0 || analysis.Staves.Count < 2) return;

        var document = XDocument.Load(path);
        var measures = document.Descendants("measure").ToList();
        if (measures.Count == 0) return;

        var groups = BuildStaffGroups(analysis);
        var measureIndex = 0;
        var divisions = 1;
        var beats = 4;
        var beatType = 4;

        foreach (var group in groups)
        {
            var boundaries = DetectMeasureBoundaries(analysis, group);
            for (var segment = 0; segment + 1 < boundaries.Count && measureIndex < measures.Count; segment++, measureIndex++)
            {
                var measure = measures[measureIndex];
                UpdateTiming(measure, ref divisions, ref beats, ref beatType);
                var duration = Math.Max(1, beats * divisions * 4 / Math.Max(1, beatType));
                var left = boundaries[segment];
                var right = boundaries[segment + 1];
                var upper = group[0];

                var entries = new List<(double X, XElement Element)>();
                foreach (var mark in analysis.Directions.Where(x => group.Any(s => s.Index == x.StaffIndex)))
                {
                    if (InSegment(mark.X, left, right, segment + 2 == boundaries.Count))
                        entries.Add((mark.X, CreateStartDirection(mark, upper, left, right, duration)));

                    if (mark.Kind == "wedge" && mark.EndX.HasValue &&
                        InSegment(mark.EndX.Value, left, right, segment + 2 == boundaries.Count))
                        entries.Add((mark.EndX.Value, CreateWedgeStop(mark, upper, left, right, duration)));
                }

                if (entries.Count == 0) continue;
                var anchor = measure.Elements().FirstOrDefault(x => x.Name != "attributes" && x.Name != "print");
                foreach (var entry in entries.OrderBy(x => x.X))
                {
                    if (anchor is null) measure.Add(entry.Element);
                    else anchor.AddBeforeSelf(entry.Element);
                }
            }
        }

        document.Save(path);
    }

    private static XElement CreateStartDirection(
        DirectionMark mark, Staff staff, double left, double right, int measureDuration)
    {
        XElement directionType;
        if (mark.Kind == "dynamic")
        {
            directionType = new XElement("direction-type",
                new XElement("dynamics",
                    new XAttribute("default-x", DefaultX(mark.X, staff, left)),
                    new XElement(mark.Value)));
        }
        else
        {
            directionType = new XElement("direction-type",
                new XElement("wedge",
                    new XAttribute("type", mark.Value),
                    new XAttribute("number", 1),
                    new XAttribute("default-x", DefaultX(mark.X, staff, left))));
        }

        return new XElement("direction",
            new XAttribute("placement", "below"),
            directionType,
            new XElement("offset", Offset(mark.X, left, right, measureDuration)),
            new XElement("staff", 1));
    }

    private static XElement CreateWedgeStop(
        DirectionMark mark, Staff staff, double left, double right, int measureDuration) =>
        new("direction",
            new XAttribute("placement", "below"),
            new XElement("direction-type",
                new XElement("wedge",
                    new XAttribute("type", "stop"),
                    new XAttribute("number", 1),
                    new XAttribute("default-x", DefaultX(mark.EndX!.Value, staff, left)))),
            new XElement("offset", Offset(mark.EndX!.Value, left, right, measureDuration)),
            new XElement("staff", 1));

    private static string DefaultX(double x, Staff staff, double left)
    {
        var value = 60.0 + (x - left) * 10.0 / Math.Max(staff.Space, .001);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static int Offset(double x, double left, double right, int duration)
    {
        if (right <= left) return 0;
        var ratio = Math.Clamp((x - left) / (right - left), 0, 1);
        return Math.Clamp((int)Math.Round(ratio * duration), 0, duration);
    }

    private static bool InSegment(double x, double left, double right, bool last) =>
        x >= left && (last ? x <= right : x < right);

    private static void UpdateTiming(XElement measure, ref int divisions, ref int beats, ref int beatType)
    {
        var attributes = measure.Element("attributes");
        if (attributes is null) return;
        var value = (int?)attributes.Element("divisions");
        if (value is > 0) divisions = value.Value;
        var time = attributes.Element("time");
        if (time is null) return;
        value = (int?)time.Element("beats");
        if (value is > 0) beats = value.Value;
        value = (int?)time.Element("beat-type");
        if (value is > 0) beatType = value.Value;
    }

    private static List<List<Staff>> BuildStaffGroups(AnalysisResult analysis)
    {
        var staves = analysis.Staves.OrderBy(x => x.Center).ToList();
        var result = new List<List<Staff>>();
        for (var i = 0; i < staves.Count; i += 2)
            result.Add(i + 1 < staves.Count ? [staves[i], staves[i + 1]] : [staves[i]]);
        return result;
    }

    private static List<double> DetectMeasureBoundaries(AnalysisResult analysis, IReadOnlyList<Staff> group)
    {
        var left = group.Min(x => x.Left);
        var right = group.Max(x => x.Right);
        if (group.Count < 2) return [left, right];

        var upper = group[0];
        var lower = group[1];
        var space = group.Average(x => x.Space);
        var intervals = new List<VerticalInterval>();

        foreach (var path in analysis.DirectPaths)
        foreach (var contour in path.Geometry.Contours)
        for (var i = 1; i < contour.Count; i++)
        {
            var p1 = contour[i - 1];
            var p2 = contour[i];
            if (Math.Abs(p2.X - p1.X) > space * .18 || Math.Abs(p2.Y - p1.Y) < space * .5) continue;
            var x = (p1.X + p2.X) / 2;
            if (x < left - space || x > right + space) continue;
            intervals.Add(new VerticalInterval(x, Math.Min(p1.Y, p2.Y), Math.Max(p1.Y, p2.Y)));
        }

        foreach (var line in analysis.LineSegments)
        {
            if (line.Width > space * .35 || line.Height < space * .5) continue;
            if (line.CenterX < left - space || line.CenterX > right + space) continue;
            intervals.Add(new VerticalInterval(line.CenterX, line.Top, line.Bottom));
        }

        var columns = new List<List<VerticalInterval>>();
        foreach (var interval in intervals.OrderBy(x => x.X))
        {
            var column = columns.LastOrDefault();
            if (column is null || Math.Abs(interval.X - column.Average(x => x.X)) > space * .20)
                columns.Add([interval]);
            else
                column.Add(interval);
        }

        var candidates = new List<double>();
        foreach (var column in columns)
        {
            var ordered = column.OrderBy(x => x.Top).ToList();
            if (ordered.Count == 0) continue;
            var top = ordered[0].Top;
            var bottom = ordered[0].Bottom;
            foreach (var next in ordered.Skip(1))
            {
                if (next.Top <= bottom + space * .15)
                    bottom = Math.Max(bottom, next.Bottom);
                else
                {
                    if (Spans(top, bottom, upper, lower)) candidates.Add(column.Average(x => x.X));
                    top = next.Top;
                    bottom = next.Bottom;
                }
            }
            if (Spans(top, bottom, upper, lower)) candidates.Add(column.Average(x => x.X));
        }

        var merged = new List<double>();
        foreach (var x in candidates.OrderBy(x => x))
        {
            if (merged.Count == 0 || x - merged[^1] > space * .65) merged.Add(x);
            else merged[^1] = (merged[^1] + x) / 2;
        }

        var result = new List<double> { left };
        result.AddRange(merged.Where(x => x > left + space * 2 && x < right - space * .8));
        result.Add(right);
        return result.Distinct().OrderBy(x => x).ToList();
    }

    private static bool Spans(double top, double bottom, Staff upper, Staff lower) =>
        top <= upper.Top + upper.Space * .45 &&
        bottom >= lower.Bottom - lower.Space * .45;
}
