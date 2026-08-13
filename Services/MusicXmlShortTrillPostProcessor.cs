using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// The compact production-font ornament recovered by MusicXmlOrnamentGeometryPostProcessor is the
/// SMuFL short trill (ornamentShortTrill), for which MusicXML has no dedicated element. MusicXML 4
/// represents such ornaments with other-ornament + the canonical SMuFL glyph name.
/// </summary>
public sealed class MusicXmlShortTrillPostProcessor
{
    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var changed = false;

        foreach (var turn in document.Descendants("ornaments").Elements("turn").ToList())
        {
            turn.ReplaceWith(new XElement("other-ornament",
                new XAttribute("placement", (string?)turn.Attribute("placement") ?? "above"),
                new XAttribute("smufl", "ornamentShortTrill")));
            changed = true;
        }

        if (changed) document.Save(path);
    }
}
