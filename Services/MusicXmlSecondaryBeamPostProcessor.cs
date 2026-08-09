using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Adds MusicXML beam level 2 after note durations/types have been reconstructed.
/// The primary beam already comes from MusicXmlWriter; this pass distinguishes a full
/// secondary beam from a forward/backward hook using neighbouring 16th notes in the same voice.
/// </summary>
public sealed class MusicXmlSecondaryBeamPostProcessor
{
    public void Apply(string path)
    {
        var document = XDocument.Load(path);

        foreach (var measure in document.Descendants("measure"))
        {
            var notes = measure.Elements("note").ToList();
            foreach (var lane in notes
                         .Where(x => x.Element("chord") is null)
                         .GroupBy(x => new
                         {
                             Voice = (string?)x.Element("voice") ?? "1",
                             Staff = (string?)x.Element("staff") ?? "1"
                         }))
            {
                var sequence = lane.ToList();
                for (var i = 0; i < sequence.Count; i++)
                {
                    var note = sequence[i];
                    if (!string.Equals((string?)note.Element("type"), "16th", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var primary = note.Elements("beam")
                        .FirstOrDefault(x => (int?)x.Attribute("number") == 1);
                    if (primary is null) continue;
                    if (note.Elements("beam").Any(x => (int?)x.Attribute("number") == 2)) continue;

                    var hasPrevious16th = i > 0 && IsBeamed16th(sequence[i - 1]);
                    var hasNext16th = i + 1 < sequence.Count && IsBeamed16th(sequence[i + 1]);

                    var value = (hasPrevious16th, hasNext16th) switch
                    {
                        (true, true) => "continue",
                        (true, false) => "end",
                        (false, true) => "begin",
                        _ => string.Equals(primary.Value, "end", StringComparison.OrdinalIgnoreCase)
                            ? "backward hook"
                            : "forward hook"
                    };

                    primary.AddAfterSelf(new XElement("beam", new XAttribute("number", 2), value));
                }
            }
        }

        document.Save(path);
    }

    private static bool IsBeamed16th(XElement note) =>
        string.Equals((string?)note.Element("type"), "16th", StringComparison.OrdinalIgnoreCase) &&
        note.Elements("beam").Any(x => (int?)x.Attribute("number") == 1);
}
