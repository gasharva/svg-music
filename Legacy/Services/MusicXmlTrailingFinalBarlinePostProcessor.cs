using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// The two strokes of a final barline can be interpreted as a tiny empty trailing measure by the
/// generic boundary detector. If the final measure contains only a right barline/layout metadata,
/// move that barline onto the preceding musical measure and remove the synthetic measure.
/// </summary>
public sealed class MusicXmlTrailingFinalBarlinePostProcessor
{
    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var measures = document.Descendants("measure").ToList();
        if (measures.Count < 2) return;

        var trailing = measures[^1];
        if (trailing.Elements("note").Any()) return;
        if (trailing.Elements().Any(x => x.Name != "barline" && x.Name != "print" && x.Name != "attributes")) return;

        var sourceBarline = trailing.Elements("barline")
            .LastOrDefault(x => (string?)x.Attribute("location") == "right");
        if (sourceBarline is null) return;

        var previous = measures[^2];
        var existing = previous.Elements("barline")
            .FirstOrDefault(x => (string?)x.Attribute("location") == "right");
        if (existing is null)
            previous.Add(new XElement(sourceBarline));
        else
        {
            var sourceStyle = sourceBarline.Element("bar-style");
            if (sourceStyle is not null)
            {
                var style = existing.Element("bar-style");
                if (style is null) existing.AddFirst(new XElement(sourceStyle));
                else style.Value = sourceStyle.Value;
            }
        }

        trailing.Remove();
        document.Save(path);
    }
}
