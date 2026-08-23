using System.Globalization;
using System.Xml.Linq;
using MusicStructure;

namespace SvgStructure.Services;

internal static class ResolvedMusicXmlNoteWriter
{
    private const double LogicalXToTenths = 10.0;

    public static void Append(string path, MusicScore score)
    {
        ClefStateNormalizer.Normalize(path);

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
            for (var staffIndex = 0; staffIndex < staffs.Length; staffIndex++)
            {
                var staff = staffs[staffIndex];
                var staffNotes = musicMeasure.Notes.Where(x => x.Staff == staff).ToArray();
                var voices = staffNotes.GroupBy(x => x.Voice ?? 1).OrderBy(x => x.Key).ToArray();

                var lastVoiceDuration = 0;
                for (var voiceIndex = 0; voiceIndex < voices.Length; voiceIndex++)
                {
                    if (voiceIndex > 0 && lastVoiceDuration > 0)
                        measure.Add(new XElement("backup", new XElement("duration", lastVoiceDuration)));

                    foreach (var note in OrderVoiceNotes(voices[voiceIndex].ToArray()))
                        measure.Add(CreateNote(note));

                    lastVoiceDuration = VoiceDuration(voices[voiceIndex]);
                }

                if (staffIndex < staffs.Length - 1 && lastVoiceDuration > 0)
                    measure.Add(new XElement("backup", new XElement("duration", lastVoiceDuration)));
            }
        }

        doc.Save(path);
    }

    private static IReadOnlyList<MusicNote> OrderVoiceNotes(IReadOnlyList<MusicNote> notes)
    {
        var singleIndex = 0;
        return notes
            .GroupBy(x => x.ChordGroupKey ?? $"single:{++singleIndex}:{x.LogicalX}")
            .Select(group => new
            {
                X = group.Min(x => x.LogicalX ?? double.MaxValue),
                Notes = group.OrderBy(x => x.IsChordTone).ThenBy(x => x.Pitch.Octave).ThenBy(x => x.Pitch.Step).ToArray()
            })
            .OrderBy(x => x.X)
            .SelectMany(x => x.Notes)
            .ToArray();
    }

    private static int VoiceDuration(IEnumerable<MusicNote> notes) => notes.Where(x => !x.IsChordTone).Sum(Duration);

    private static XElement CreateNote(MusicNote note)
    {
        var pitch = new XElement("pitch", new XElement("step", note.Pitch.Step));
        if (note.Pitch.Alter != 0)
            pitch.Add(new XElement("alter", note.Pitch.Alter));
        pitch.Add(new XElement("octave", note.Pitch.Octave));

        var noteEl = new XElement("note");
        if (note.LogicalX.HasValue)
            noteEl.Add(new XAttribute("default-x", (note.LogicalX.Value * LogicalXToTenths).ToString("0.###", CultureInfo.InvariantCulture)));
        if (note.IsChordTone)
            noteEl.Add(new XElement("chord"));
        noteEl.Add(pitch);
        noteEl.Add(new XElement("duration", Duration(note)));
        if (note.Voice is not null)
            noteEl.Add(new XElement("voice", note.Voice.Value));
        noteEl.Add(new XElement("type", note.Type));
        for (var i = 0; i < note.DotCount; i++) noteEl.Add(new XElement("dot"));
        if (note.Accidental is not null) noteEl.Add(new XElement("accidental", AccidentalText(note.Accidental.Value)));
        if (note.Stem is not null) noteEl.Add(new XElement("stem", note.Stem.ToString()!.ToLowerInvariant()));
        noteEl.Add(new XElement("staff", note.Staff));
        foreach (var beam in note.Beams)
            noteEl.Add(new XElement("beam", new XAttribute("number", beam.Level), BeamText(beam.Position)));

        var slurs = note.Slurs ?? Array.Empty<MusicSlur>();
        if (slurs.Count > 0)
        {
            noteEl.Add(new XElement("notations",
                slurs.Select(slur => new XElement("slur",
                    new XAttribute("type", slur.Type == MusicSlurType.Start ? "start" : "stop"),
                    new XAttribute("number", slur.Number),
                    new XAttribute("placement", slur.Placement == MusicSlurPlacement.Above ? "above" : "below")))));
        }

        return noteEl;
    }

    private static int Duration(MusicNote note)
    {
        var denominator = note.Type switch
        {
            "whole" => 1, "half" => 2, "quarter" => 4, "eighth" => 8,
            "16th" => 16, "32nd" => 32, "64th" => 64, _ => 4
        };
        var baseDuration = 128 / denominator;
        return note.DotCount == 0 ? baseDuration : note.DotCount == 1 ? baseDuration * 3 / 2 : baseDuration * 7 / 4;
    }

    private static string AccidentalText(MusicAccidental a) => a switch
    {
        MusicAccidental.Flat => "flat", MusicAccidental.Sharp => "sharp", MusicAccidental.Natural => "natural",
        MusicAccidental.DoubleSharp => "double-sharp", MusicAccidental.DoubleFlat => "flat-flat", _ => ""
    };

    private static string BeamText(MusicBeamPosition p) => p switch
    {
        MusicBeamPosition.Begin => "begin", MusicBeamPosition.Continue => "continue", MusicBeamPosition.End => "end",
        MusicBeamPosition.ForwardHook => "forward hook", MusicBeamPosition.BackwardHook => "backward hook", _ => ""
    };

    private static int? ParseInt(string? value) => int.TryParse(value, out var n) ? n : null;
}
