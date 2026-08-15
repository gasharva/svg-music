using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Converts long pedal-bracket geometry below the lower staff into MusicXML sustain directions.
/// Geometry, rather than the rendered "Ped." text, is authoritative so outlined fonts do not
/// need OCR or glyph-name recognition.
/// </summary>
public sealed class MusicXmlSustainPostProcessor
{
    public void Apply(string path, AnalysisResult analysis)
    {
        var staves = analysis.Staves.OrderBy(s => s.Center).ToList();
        if (staves.Count < 2) return;

        var groups = new List<(Staff Upper, Staff Lower)>();
        for (var i = 0; i + 1 < staves.Count; i += 2)
            groups.Add((staves[i], staves[i + 1]));

        var marks = SustainGeometry.Find(analysis, groups.Select(g => (g.Upper, g.Lower)).ToList());
        if (marks.Count == 0) return;

        var document = XDocument.Load(path);
        var measures = document.Descendants("measure").ToList();
        var measureIndex = 0;
        var divisions = 1;
        var beats = 4;
        var beatType = 4;

        for (var groupIndex = 0; groupIndex < groups.Count && measureIndex < measures.Count; groupIndex++)
        {
            var group = (groups[groupIndex].Upper, groups[groupIndex].Lower);
            var boundaries = SustainGeometry.Bounds(analysis, group);

            for (var segment = 0; segment + 1 < boundaries.Count && measureIndex < measures.Count; segment++, measureIndex++)
            {
                var measure = measures[measureIndex];
                UpdateTiming(measure, ref divisions, ref beats, ref beatType);
                var measureDuration = Math.Max(1, beats * divisions * 4 / Math.Max(1, beatType));
                var left = boundaries[segment];
                var right = boundaries[segment + 1];
                var isLast = segment + 2 == boundaries.Count;

                var directions = new List<(int Offset, XElement Element)>();
                foreach (var mark in marks.Where(x => x.Group == groupIndex))
                {
                    if (Inside(mark.Left, left, right, isLast))
                    {
                        var offset = Offset(mark.Left, left, right, measureDuration);
                        directions.Add((offset, CreateDirection("start", offset)));
                    }

                    if (Inside(mark.Right, left, right, isLast))
                    {
                        var offset = Offset(mark.Right, left, right, measureDuration);
                        directions.Add((offset, CreateDirection("stop", offset)));
                    }
                }

                if (directions.Count == 0) continue;

                // All directions must precede timed notes, but keep their own temporal order. This
                // matters when press and release both occur within the same measure.
                var insertionPoint = measure.Elements().FirstOrDefault(x =>
                    x.Name.LocalName is not "attributes" and not "print" and not "direction");

                foreach (var direction in directions.OrderBy(x => x.Offset).ThenBy(x =>
                             (string?)x.Element.Element("direction-type")?.Element("pedal")?.Attribute("type") == "start" ? 0 : 1))
                {
                    if (insertionPoint is null) measure.Add(direction.Element);
                    else insertionPoint.AddBeforeSelf(direction.Element);
                }
            }
        }

        document.Save(path);
    }

    private static XElement CreateDirection(string type, int offset) =>
        new("direction",
            new XAttribute("placement", "below"),
            new XElement("direction-type",
                new XElement("pedal",
                    new XAttribute("type", type),
                    new XAttribute("line", "yes"),
                    new XAttribute("sign", type == "start" ? "yes" : "no"))),
            new XElement("offset", offset),
            new XElement("staff", 2));

    private static int Offset(double x, double left, double right, int duration) =>
        right <= left
            ? 0
            : Math.Clamp(
                (int)Math.Round(Math.Clamp((x - left) / (right - left), 0, 1) * duration),
                0,
                duration);

    private static bool Inside(double x, double left, double right, bool last) =>
        x >= left && (last ? x <= right : x < right);

    private static void UpdateTiming(XElement measure, ref int divisions, ref int beats, ref int beatType)
    {
        var attributes = measure.Element("attributes");
        if (attributes is null) return;

        var newDivisions = (int?)attributes.Element("divisions");
        if (newDivisions is > 0) divisions = newDivisions.Value;

        var time = attributes.Element("time");
        if (time is null) return;

        var newBeats = (int?)time.Element("beats");
        var newBeatType = (int?)time.Element("beat-type");
        if (newBeats is > 0) beats = newBeats.Value;
        if (newBeatType is > 0) beatType = newBeatType.Value;
    }
}
