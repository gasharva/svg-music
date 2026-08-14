using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Normalizes MusicXML cross-staff chord encoding for importers such as MuseScore.
/// A cross-staff onset is one chord in one voice whose tones retain their own staff numbers.
/// Stem direction belongs to the whole onset, while beam elements belong only to the anchor note.
/// </summary>
public sealed class MusicXmlCrossStaffChordPostProcessor
{
    public void Apply(string path)
    {
        var document = XDocument.Load(path);

        foreach (var measure in document.Descendants("measure"))
        {
            var notes = measure.Elements("note").ToList();
            for (var i = 0; i < notes.Count; i++)
            {
                if (notes[i].Element("chord") is not null) continue;

                var run = new List<XElement> { notes[i] };
                var j = i + 1;
                while (j < notes.Count && notes[j].Element("chord") is not null)
                {
                    run.Add(notes[j]);
                    j++;
                }

                if (run.Count < 2)
                {
                    i = j - 1;
                    continue;
                }

                var staffs = run
                    .Select(x => (int?)x.Element("staff") ?? 1)
                    .Distinct()
                    .ToList();
                if (staffs.Count < 2)
                {
                    i = j - 1;
                    continue;
                }

                var anchor = run[0];
                var stemValue = run
                    .Select(x => (string?)x.Element("stem"))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                if (!string.IsNullOrWhiteSpace(stemValue))
                {
                    foreach (var note in run)
                    {
                        var stem = note.Element("stem");
                        if (stem is not null)
                        {
                            stem.Value = stemValue;
                            continue;
                        }

                        stem = new XElement("stem", stemValue);
                        var insertionPoint = note.Element("beam") ?? note.Element("notations") ?? note.Element("staff");
                        if (insertionPoint is not null) insertionPoint.AddBeforeSelf(stem);
                        else note.Add(stem);
                    }
                }

                // MusicXML's cross-staff examples put beam information on the first note of the
                // chord onset. Repeating beam begin/continue on every chord tone confuses MuseScore.
                foreach (var chordTone in run.Skip(1))
                    chordTone.Elements("beam").Remove();

                // When a producer already supplied explicit horizontal placement, make every tone
                // use the anchor's position. Never invent page coordinates here.
                var defaultX = (string?)anchor.Attribute("default-x")
                               ?? run.Select(x => (string?)x.Attribute("default-x"))
                                   .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                if (!string.IsNullOrWhiteSpace(defaultX))
                    foreach (var note in run) note.SetAttributeValue("default-x", defaultX);

                i = j - 1;
            }
        }

        document.Save(path);
    }
}
