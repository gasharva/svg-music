using System.Xml.Linq;
using MusicStructure;

namespace SvgStructure.Services;

internal static class ResolvedMusicXmlNoteWriter
{
    public static void Append(string path, MusicScore score)
    {
        var doc = XDocument.Load(path);
        var part = doc.Root!.Elements().First(x => x.Name.LocalName == "part");
        var measures = part.Elements().Where(x => x.Name.LocalName == "measure")
            .ToDictionary(x => ParseInt(x.Attribute("number")?.Value) ?? 0);

        if (measures.TryGetValue(1, out var firstMeasure))
        {
            var attributes = firstMeasure.Elements().FirstOrDefault(x => x.Name.LocalName == "attributes");
            if (attributes is not null && !attributes.Elements().Any(x => x.Name.LocalName == "divisions"))
                attributes.AddFirst(new XElement("divisions", 32));
        }

        foreach (var musicMeasure in score.Measures)
        {
            if (!measures.TryGetValue(musicMeasure.Number, out var measure))
                continue;

            var staffs = musicMeasure.Notes.Select(x => x.Staff).Distinct().OrderBy(x => x).ToArray();
            var firstStaff = true;
            foreach (var staff in staffs)
            {
                var ordered = OrderStaffNotes(musicMeasure.Notes.Where(x => x.Staff == staff).ToArray());
                if (!firstStaff)
                {
                    var previousStaff = staffs[Array.IndexOf(staffs, staff) - 1];
                    var rewind = StaffDuration(musicMeasure.Notes.Where(x => x.Staff == previousStaff));
                    if (rewind > 0)
                        measure.Add(new XElement("backup", new XElement("duration", rewind)));
                }

                foreach (var note in ordered)
                    measure.Add(CreateNote(note));

                firstStaff = false;
            }
        }

        doc.Save(path);
    }

    private static IReadOnlyList<MusicNote> OrderStaffNotes(IReadOnlyList<MusicNote> notes)
    {
        var groups = notes
            .GroupBy(x => x.ChordGroupKey ?? $"single:{Guid.NewGuid():N}")
            .Select(group => new
            {
                X = group.Min(x => x.LogicalX ?? double.MaxValue),
                Notes = group.OrderBy(x => x.IsChordTone).ThenBy(x => x.Pitch.Octave).ThenBy(x => x.Pitch.Step).ToArray()
            })
            .OrderBy(x => x.X)
            .ToArray();

        return groups.SelectMany(x => x.Notes).ToArray();
    }

    private static int StaffDuration(IEnumerable<MusicNote> notes) =>
        notes.Where(x => !x.IsChordTone).Sum(Duration);

    private static XElement CreateNote(MusicNote note)
    {
        var pitch = new XElement("pitch", new XElement("step", note.Pitch.Step));
        if (note.Pitch.Alter != 0)
            pitch.Add(new XElement("alter", note.Pitch.Alter));
        pitch.Add(new XElement("octave", note.Pitch.Octave));

        var noteEl = new XElement("note");
        if (note.IsChordTone)
            noteEl.Add(new XElement("chord"));
        noteEl.Add(pitch);
        noteEl.Add(new XElement("duration", Duration(note)));
        noteEl.Add(new XElement("type", note.Type));
        for (var i = 0; i < note.DotCount; i++)
            noteEl.Add(new XElement("dot"));
        if (note.Accidental is not null)
            noteEl.Add(new XElement("accidental", AccidentalText(note.Accidental.Value)));
        if (note.Stem is not null)
            noteEl.Add(new XElement("stem", note.Stem.ToString()!.ToLowerInvariant()));
        noteEl.Add(new XElement("staff", note.Staff));
        foreach (var beam in note.Beams)
            noteEl.Add(new XElement("beam", new XAttribute("number", beam.Level), BeamText(beam.Position)));
        return noteEl;
    }

    private static int Duration(MusicNote note)
    {
        var denominator = note.Type switch
        {
            "whole" => 1,
            "half" => 2,
            "quarter" => 4,
            "eighth" => 8,
            "16th" => 16,
            "32nd" => 32,
            "64th" => 64,
            _ => 4
        };
        var baseDuration = 128 / denominator;
        return note.DotCount == 0 ? baseDuration : note.DotCount == 1 ? baseDuration * 3 / 2 : baseDuration * 7 / 4;
    }

    private static string AccidentalText(MusicAccidental a) => a switch
    {
        MusicAccidental.Flat => "flat",
        MusicAccidental.Sharp => "sharp",
        MusicAccidental.Natural => "natural",
        MusicAccidental.DoubleSharp => "double-sharp",
        MusicAccidental.DoubleFlat => "flat-flat",
        _ => ""
    };

    private static string BeamText(MusicBeamPosition p) => p switch
    {
        MusicBeamPosition.Begin => "begin",
        MusicBeamPosition.Continue => "continue",
        MusicBeamPosition.End => "end",
        MusicBeamPosition.ForwardHook => "forward hook",
        MusicBeamPosition.BackwardHook => "backward hook",
        _ => ""
    };

    private static int? ParseInt(string? value) => int.TryParse(value, out var n) ? n : null;
}
