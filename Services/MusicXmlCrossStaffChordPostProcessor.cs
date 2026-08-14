using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Normalizes MusicXML cross-staff chord encoding for importers such as MuseScore.
/// The SVG layout pass gives simultaneous notes a common default-x. We use that stable
/// engraving coordinate, together with voice and staff, to rebuild each cross-staff onset
/// instead of trusting possibly stale chord markers left by earlier voice-layout passes.
/// </summary>
public sealed class MusicXmlCrossStaffChordPostProcessor
{
    private sealed record PositionedNote(XElement Element, int Voice, int Staff, double X, int Order);

    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var changed = false;

        foreach (var measure in document.Descendants("measure"))
        {
            var positioned = measure.Elements("note")
                .Select((note, order) => TryRead(note, order))
                .Where(x => x is not null)
                .Cast<PositionedNote>()
                .ToList();

            if (positioned.Count < 2) continue;

            // SVG -> MusicXML layout uses tenths. Rounding to a tenth is deliberately tighter
            // than normal engraving offsets but absorbs harmless floating-point formatting noise.
            foreach (var group in positioned
                         .GroupBy(x => (x.Voice, X: Math.Round(x.X, 1)))
                         .Select(x => x.OrderBy(n => n.Order).ToList())
                         .Where(x => x.Count > 1 && x.Select(n => n.Staff).Distinct().Count() > 1))
            {
                var anchor = group[0].Element;

                // Rebuild chord markup from the onset itself. This prevents an orphan <chord/>
                // at a measure/voice boundary from swallowing later independent onsets.
                foreach (var item in group)
                    item.Element.Element("chord")?.Remove();
                foreach (var item in group.Skip(1))
                    InsertChord(item.Element);

                var stemValue = group
                    .Select(x => (string?)x.Element.Element("stem"))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                if (!string.IsNullOrWhiteSpace(stemValue))
                {
                    foreach (var item in group)
                        SetStem(item.Element, stemValue!);
                }

                // Beam state advances once per rhythmic onset, never once per chord tone.
                foreach (var item in group.Skip(1))
                    item.Element.Elements("beam").Remove();

                var anchorX = (string?)anchor.Attribute("default-x");
                if (!string.IsNullOrWhiteSpace(anchorX))
                    foreach (var item in group)
                        item.Element.SetAttributeValue("default-x", anchorX);

                changed = true;
            }
        }

        if (changed) document.Save(path);
    }

    private static PositionedNote? TryRead(XElement note, int order)
    {
        var text = (string?)note.Attribute("default-x");
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return null;
        return new PositionedNote(
            note,
            (int?)note.Element("voice") ?? 1,
            (int?)note.Element("staff") ?? 1,
            x,
            order);
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
