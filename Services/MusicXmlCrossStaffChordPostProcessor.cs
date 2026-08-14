using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Restores cross-staff chords from the semantic identity recovered from SVG geometry.
/// Do not infer a chord from default-x: seconds inside one chord may have intentionally shifted
/// noteheads, and later voice passes may temporarily assign the two staffs different voices.
/// </summary>
public sealed class MusicXmlCrossStaffChordPostProcessor
{
    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var changed = false;

        foreach (var measure in document.Descendants("measure"))
        {
            var tagged = measure.Elements("note")
                .Where(x => x.Attribute(MusicXmlSvgLayoutPostProcessor.CrossStaffIdAttribute) is not null)
                .ToList();
            if (tagged.Count == 0) continue;

            foreach (var semanticGroup in tagged
                         .GroupBy(x => (string)x.Attribute(MusicXmlSvgLayoutPostProcessor.CrossStaffIdAttribute)!)
                         .ToList())
            {
                var members = semanticGroup
                    .GroupBy(x => (string?)x.Attribute(MusicXmlSvgLayoutPostProcessor.SourceSymbolAttribute)
                                  ?? $"xml:{RuntimeHelpers.GetHashCode(x)}")
                    .Select(x => x.First())
                    .ToList();

                foreach (var duplicate in semanticGroup.Except(members).ToList())
                {
                    duplicate.Remove();
                    changed = true;
                }

                if (members.Count < 2 || members.Select(ReadStaff).Distinct().Count() < 2)
                    continue;

                var commonVoice = members.Select(ReadVoice).Where(x => x > 0).DefaultIfEmpty(1).Min();
                var commonStem = members
                    .Select(x => (string?)x.Element("stem"))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                var anchor = members
                    .OrderByDescending(ReadStaff)
                    .ThenBy(ReadPitchMidi)
                    .First();

                var ordered = new List<XElement> { anchor };
                ordered.AddRange(members
                    .Where(x => !ReferenceEquals(x, anchor))
                    .OrderByDescending(ReadStaff)
                    .ThenBy(ReadPitchMidi));

                foreach (var note in ordered)
                {
                    note.Element("chord")?.Remove();
                    SetVoice(note, commonVoice);
                    if (!string.IsNullOrWhiteSpace(commonStem)) SetStem(note, commonStem!);
                }
                foreach (var note in ordered.Skip(1)) InsertChord(note);

                foreach (var note in ordered.Skip(1)) note.Elements("beam").Remove();

                var placeholder = new XElement("cross-staff-placeholder");
                members.OrderBy(x => x.ElementsBeforeSelf().Count()).First().AddBeforeSelf(placeholder);
                foreach (var note in ordered) note.Remove();
                foreach (var note in ordered) placeholder.AddBeforeSelf(note);
                placeholder.Remove();

                changed = true;
            }
        }

        foreach (var note in document.Descendants("note"))
        {
            note.Attribute(MusicXmlSvgLayoutPostProcessor.CrossStaffIdAttribute)?.Remove();
            note.Attribute(MusicXmlSvgLayoutPostProcessor.SourceSymbolAttribute)?.Remove();
        }

        if (changed) document.Save(path);
    }

    private static int ReadVoice(XElement note) => (int?)note.Element("voice") ?? 1;
    private static int ReadStaff(XElement note) => (int?)note.Element("staff") ?? 1;

    private static int ReadPitchMidi(XElement note)
    {
        var pitch = note.Element("pitch");
        if (pitch is null) return int.MaxValue;
        var step = (string?)pitch.Element("step") ?? "C";
        var alter = (int?)pitch.Element("alter") ?? 0;
        var octave = (int?)pitch.Element("octave") ?? 4;
        var semitone = step switch
        {
            "C" => 0, "D" => 2, "E" => 4, "F" => 5,
            "G" => 7, "A" => 9, "B" => 11, _ => 0
        };
        return (octave + 1) * 12 + semitone + alter;
    }

    private static void SetVoice(XElement note, int voice)
    {
        var element = note.Element("voice");
        if (element is not null)
        {
            element.Value = voice.ToString();
            return;
        }
        element = new XElement("voice", voice);
        var type = note.Element("type");
        if (type is not null) type.AddBeforeSelf(element); else note.Add(element);
    }

    private static void InsertChord(XElement note)
    {
        var chord = new XElement("chord");
        var first = note.Elements().FirstOrDefault();
        if (first is not null) first.AddBeforeSelf(chord); else note.Add(chord);
    }

    private static void SetStem(XElement note, string value)
    {
        var stem = note.Element("stem");
        if (stem is not null)
        {
            stem.Value = value;
            return;
        }
        stem = new XElement("stem", value);
        var insertionPoint = note.Element("beam") ?? note.Element("notations") ?? note.Element("staff");
        if (insertionPoint is not null) insertionPoint.AddBeforeSelf(stem); else note.Add(stem);
    }
}
