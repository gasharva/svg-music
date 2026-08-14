using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>Adds beam levels implied by the reconstructed note type (16th/32nd/64th).</summary>
public sealed class MusicXmlSecondaryBeamPostProcessor
{
    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        foreach (var measure in document.Descendants("measure"))
        foreach (var lane in measure.Elements("note").Where(x => x.Element("chord") is null)
                     .GroupBy(x => new { Voice = (string?)x.Element("voice") ?? "1", Staff = (string?)x.Element("staff") ?? "1" }))
        {
            var sequence = lane.ToList();
            for (var i = 0; i < sequence.Count; i++)
            {
                var note = sequence[i];
                var levels = BeamLevels((string?)note.Element("type"));
                if (levels < 2 || note.Elements("beam").All(x => (int?)x.Attribute("number") != 1)) continue;

                for (var level = 2; level <= levels; level++)
                {
                    if (note.Elements("beam").Any(x => (int?)x.Attribute("number") == level)) continue;
                    var previous = i > 0 && BeamLevels((string?)sequence[i - 1].Element("type")) >= level && HasPrimary(sequence[i - 1]);
                    var next = i + 1 < sequence.Count && BeamLevels((string?)sequence[i + 1].Element("type")) >= level && HasPrimary(sequence[i + 1]);
                    var primary = note.Elements("beam").First(x => (int?)x.Attribute("number") == 1);
                    var value = (previous, next) switch
                    {
                        (true, true) => "continue", (true, false) => "end", (false, true) => "begin",
                        _ => string.Equals(primary.Value, "end", StringComparison.OrdinalIgnoreCase) ? "backward hook" : "forward hook"
                    };
                    note.Elements("beam").Last().AddAfterSelf(new XElement("beam", new XAttribute("number", level), value));
                }
            }
        }
        document.Save(path);
    }

    private static bool HasPrimary(XElement note) => note.Elements("beam").Any(x => (int?)x.Attribute("number") == 1);
    private static int BeamLevels(string? type) => type?.ToLowerInvariant() switch
    {
        "eighth" => 1, "16th" => 2, "32nd" => 3, "64th" => 4, _ => 0
    };
}
