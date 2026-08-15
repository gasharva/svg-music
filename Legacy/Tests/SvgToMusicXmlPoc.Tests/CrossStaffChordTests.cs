using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;
using SvgToMusicXmlPoc.Services;

namespace SvgToMusicXmlPoc.Tests;

public sealed class CrossStaffChordTests
{
    [Fact]
    public void Resolver_AssignsOneCrossStaffChordToHeadsOnBothStaves()
    {
        var upper = Staff(0, 100);
        var lower = Staff(1, 160);
        var topNote = Note(0, 49, 115, "C", 4);
        var bottomNote = Note(1, 49, 165, "A", 3);
        var analysis = new AnalysisResult
        {
            Staves = [upper, lower],
            Events =
            [
                Clef(0, "G"), Clef(1, "F"), topNote, bottomNote
            ],
            LineSegments =
            [
                new SvgLineSegment(52, 110, 52, 166, "path")
            ]
        };

        new CrossStaffChordResolver().Resolve(analysis);

        Assert.NotNull(topNote.CrossStaffChordId);
        Assert.Equal(topNote.CrossStaffChordId, bottomNote.CrossStaffChordId);
        Assert.Equal(52, topNote.StemX);
        Assert.Equal(52, bottomNote.StemX);
        Assert.Equal("up", topNote.StemDirection);
        Assert.Equal("up", bottomNote.StemDirection);
    }

    [Fact]
    public void Resolver_DoesNotTreatGrandStaffBarlineAsChordStem()
    {
        var upper = Staff(0, 100);
        var lower = Staff(1, 160);
        var topNote = Note(0, 80, 115, "C", 4);
        var bottomNote = Note(1, 80, 165, "A", 3);
        var analysis = new AnalysisResult
        {
            Staves = [upper, lower],
            Events = [Clef(0, "G"), Clef(1, "F"), topNote, bottomNote],
            LineSegments = [new SvgLineSegment(52, 100, 52, 180, "path")]
        };

        new CrossStaffChordResolver().Resolve(analysis);

        Assert.Null(topNote.CrossStaffChordId);
        Assert.Null(bottomNote.CrossStaffChordId);
    }

    [Fact]
    public void VoiceLayout_RendersCrossStaffMembersAsOneChordWithoutChangingStaff()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cross-staff-{Guid.NewGuid():N}.musicxml");
        try
        {
            var upper = Staff(0, 100);
            var lower = Staff(1, 160);
            var top = Note(0, 49, 115, "C", 4, 1);
            var lowerA = Note(1, 49, 165, "A", 3, 1);
            var lowerF = Note(1, 49, 175, "F", 3, 1);
            foreach (var note in new[] { top, lowerA, lowerF })
            {
                note.CrossStaffChordId = 1;
                note.StemX = 52;
                note.StemDirection = "up";
                note.Duration = 4;
                note.Type = "quarter";
            }

            var analysis = new AnalysisResult
            {
                Staves = [upper, lower],
                Events = [Clef(0, "G"), Clef(1, "F"), top, lowerA, lowerF]
            };

            var document = new XDocument(
                new XElement("score-partwise",
                    new XElement("part",
                        new XElement("measure", new XAttribute("number", 1),
                            new XElement("attributes",
                                new XElement("divisions", 4),
                                new XElement("time", new XElement("beats", 4), new XElement("beat-type", 4)),
                                new XElement("staves", 2),
                                new XElement("clef", new XAttribute("number", 1), new XElement("sign", "G"), new XElement("line", 2)),
                                new XElement("clef", new XAttribute("number", 2), new XElement("sign", "F"), new XElement("line", 4))),
                            XmlNote("C", 4, 1),
                            XmlNote("A", 3, 2),
                            XmlNote("F", 3, 2)))));
            document.Save(path);

            new MusicXmlVoiceLayoutPostProcessor().Apply(path, analysis);

            var notes = XDocument.Load(path).Descendants("note").ToList();
            Assert.Equal(3, notes.Count);
            Assert.All(notes, x => Assert.Equal("1", x.Element("voice")?.Value));
            Assert.Equal(2, notes.Count(x => x.Element("chord") is not null));
            Assert.Contains(notes, x => x.Element("staff")?.Value == "1");
            Assert.Contains(notes, x => x.Element("staff")?.Value == "2");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static XElement XmlNote(string step, int octave, int staff) =>
        new("note",
            new XElement("pitch", new XElement("step", step), new XElement("octave", octave)),
            new XElement("duration", 4),
            new XElement("voice", staff == 1 ? 1 : 3),
            new XElement("type", "quarter"),
            new XElement("staff", staff));

    private static RecognizedEvent Note(int staff, double x, double y, string step, int octave, int? crossStaffId = null) =>
        new()
        {
            StaffIndex = staff,
            Kind = "notehead-black",
            X = x,
            Y = y,
            Step = step,
            Octave = octave,
            Duration = 4,
            Type = "quarter",
            CrossStaffChordId = crossStaffId
        };

    private static RecognizedEvent Clef(int staff, string sign) =>
        new() { StaffIndex = staff, Kind = sign == "G" ? "clef-treble" : "clef-bass", ClefSign = sign, X = 10 };

    private static Staff Staff(int index, double top)
    {
        const double space = 5;
        return new Staff(index, 20, 200, [top, top + space, top + space * 2, top + space * 3, top + space * 4]);
    }
}
