using System.Xml.Linq;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class MusicXmlWriter
{
    public void Write(string path, AnalysisResult analysis, RecognitionConfig config)
    {
        var score = new XElement("score-partwise", new XAttribute("version", "4.0"),
            new XElement("part-list",
                new XElement("score-part", new XAttribute("id", "P1"),
                    new XElement("part-name", "SVG import"))));
        var part = new XElement("part", new XAttribute("id", "P1"));
        score.Add(part);

        // PoC convention: each detected SVG staff line is emitted as the next measure.
        // This is structurally valid MusicXML and is intentionally isolated from glyph classification;
        // system/part grouping can be improved later without changing the recognition pipeline.
        var measureNumber = 1;
        foreach (var staff in analysis.Staves.OrderBy(x => x.Index))
        {
            var staffEvents = analysis.Events
                .Where(x => x.StaffIndex == staff.Index)
                .OrderBy(x => x.X)
                .ThenByDescending(x => x.Y)
                .ToList();

            var clef = staffEvents.FirstOrDefault(x => x.Kind.StartsWith("clef-", StringComparison.OrdinalIgnoreCase));
            var measure = new XElement("measure", new XAttribute("number", measureNumber++));
            measure.Add(new XElement("attributes",
                new XElement("divisions", config.Divisions),
                new XElement("key", new XElement("fifths", 0)),
                new XElement("time", new XElement("beats", config.Beats), new XElement("beat-type", config.BeatType)),
                new XElement("clef",
                    new XElement("sign", clef?.ClefSign ?? config.DefaultClef),
                    new XElement("line", clef?.ClefLine ?? config.DefaultClefLine))));

            foreach (var evt in staffEvents.Where(IsTimedEvent))
                measure.Add(CreateNote(evt));

            part.Add(measure);
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), score);
        doc.Save(path);
    }

    private static bool IsTimedEvent(RecognizedEvent evt) =>
        evt.Step is not null || evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase);

    private static XElement CreateNote(RecognizedEvent evt)
    {
        var note = new XElement("note");
        if (evt.Chord) note.Add(new XElement("chord"));

        if (evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase))
        {
            note.Add(new XElement("rest"));
        }
        else
        {
            note.Add(new XElement("pitch",
                new XElement("step", evt.Step),
                evt.Alter == 0 ? null : new XElement("alter", evt.Alter),
                new XElement("octave", evt.Octave)));
        }

        note.Add(new XElement("duration", evt.Duration));
        note.Add(new XElement("voice", 1));
        note.Add(new XElement("type", evt.Type ?? "quarter"));
        if (evt.Dotted) note.Add(new XElement("dot"));
        if (evt.Alter != 0)
        {
            note.Add(new XElement("accidental", evt.Alter switch
            {
                -2 => "flat-flat",
                -1 => "flat",
                1 => "sharp",
                2 => "double-sharp",
                _ => "natural"
            }));
        }
        note.Add(new XElement("staff", 1));
        return note;
    }
}
