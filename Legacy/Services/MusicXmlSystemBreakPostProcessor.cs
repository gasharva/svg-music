using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Preserves line breaks from the source SVG. MusicXmlWriter emits clef attributes at the
/// first measure of every detected staff system; use that existing marker to force a new
/// MusicXML system at the same place.
/// </summary>
public sealed class MusicXmlSystemBreakPostProcessor
{
    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var measures = document.Descendants("measure").ToList();
        if (measures.Count < 2) return;

        for (var i = 1; i < measures.Count; i++)
        {
            var measure = measures[i];
            var startsDetectedSystem = measure
                .Elements("attributes")
                .Elements("clef")
                .Any();

            if (!startsDetectedSystem) continue;

            var print = measure.Element("print");
            if (print is null)
            {
                print = new XElement("print");
                var first = measure.Elements().FirstOrDefault();
                if (first is not null) first.AddBeforeSelf(print);
                else measure.Add(print);
            }

            print.SetAttributeValue("new-system", "yes");
        }

        document.Save(path);
    }
}
