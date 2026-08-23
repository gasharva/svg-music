using System.Xml.Linq;

namespace SvgStructure.Services;

/// <summary>
/// The SVG resolver sees a clef glyph again at every printed system. MusicXML carries clef state,
/// so repeated identical glyphs must not be serialized as clef changes.
/// </summary>
internal static class ClefStateNormalizer
{
    public static void Normalize(string path)
    {
        var doc = XDocument.Load(path);
        var part = doc.Root?.Elements().FirstOrDefault(x => x.Name.LocalName == "part");
        if (part is null)
            return;

        var current = new Dictionary<int, (string Sign, string Line)>();
        foreach (var measure in part.Elements().Where(x => x.Name.LocalName == "measure"))
        {
            foreach (var attributes in measure.Elements().Where(x => x.Name.LocalName == "attributes"))
            {
                foreach (var clef in attributes.Elements().Where(x => x.Name.LocalName == "clef").ToArray())
                {
                    var staff = int.TryParse(clef.Attribute("number")?.Value, out var n) ? n : 1;
                    var sign = clef.Elements().FirstOrDefault(x => x.Name.LocalName == "sign")?.Value.Trim() ?? "";
                    var line = clef.Elements().FirstOrDefault(x => x.Name.LocalName == "line")?.Value.Trim() ?? "";
                    var state = (sign, line);

                    if (current.TryGetValue(staff, out var previous) && previous == state)
                        clef.Remove();
                    else
                        current[staff] = state;
                }
            }
        }

        doc.Save(path);
    }
}
