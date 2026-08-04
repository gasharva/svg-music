using System.Xml.Linq;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class MusicXmlWriter
{
    public void Write(string path, AnalysisResult analysis, RecognitionConfig config)
    {
        var staffGroups = BuildStaffGroups(analysis);
        var pianoLayout = staffGroups.Any(x => x.Count == 2);

        var score = new XElement("score-partwise", new XAttribute("version", "4.0"),
            new XElement("part-list",
                new XElement("score-part", new XAttribute("id", "P1"),
                    new XElement("part-name", pianoLayout ? "Piano" : "SVG import"))));
        var part = new XElement("part", new XAttribute("id", "P1"));
        score.Add(part);

        var measureNumber = 1;
        foreach (var group in staffGroups)
        {
            var measure = new XElement("measure", new XAttribute("number", measureNumber++));
            var attributes = new XElement("attributes",
                new XElement("divisions", config.Divisions),
                new XElement("key", new XElement("fifths", 0)),
                new XElement("time", new XElement("beats", config.Beats), new XElement("beat-type", config.BeatType)));

            if (group.Count > 1) attributes.Add(new XElement("staves", group.Count));
            for (var staffNumber = 1; staffNumber <= group.Count; staffNumber++)
            {
                var staff = group[staffNumber - 1];
                var clef = ClefForStaff(analysis, staff, config);
                attributes.Add(new XElement("clef",
                    group.Count > 1 ? new XAttribute("number", staffNumber) : null,
                    new XElement("sign", clef.Sign),
                    new XElement("line", clef.Line)));
            }
            measure.Add(attributes);

            var firstStaffDuration = 0;
            for (var staffNumber = 1; staffNumber <= group.Count; staffNumber++)
            {
                var staff = group[staffNumber - 1];
                var timed = analysis.Events
                    .Where(x => x.StaffIndex == staff.Index && IsTimedEvent(x))
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

        new XDocument(new XDeclaration("1.0", "UTF-8", null), score).Save(path);
    }

    private static List<List<Staff>> BuildStaffGroups(AnalysisResult analysis)
    {
        var staves = analysis.Staves.OrderBy(x => x.Center).ToList();
        if (staves.Count < 2) return staves.Select(x => new List<Staff> { x }).ToList();

        // A piano export normally repeats G-clef/F-clef pairs on every system. Use
        // that strong semantic signal first; without it keep the old single-staff layout.
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
        if (!usePianoPairs)
            return staves.Select(x => new List<Staff> { x }).ToList();

        var result = new List<List<Staff>>();
        for (var i = 0; i < staves.Count; i += 2)
            result.Add(i + 1 < staves.Count ? [staves[i], staves[i + 1]] : [staves[i]]);
        return result;
    }

    private static (string Sign, int Line) ClefForStaff(
        AnalysisResult analysis,
        Staff staff,
        RecognitionConfig config)
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

        if (evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase))
            note.Add(new XElement("rest"));
        else
            note.Add(new XElement("pitch",
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
                -2 => "flat-flat",
                -1 => "flat",
                1 => "sharp",
                2 => "double-sharp",
                _ => "natural"
            }));
        note.Add(new XElement("staff", staffNumber));
        return note;
    }
}
