using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Keeps conventional upper-staff numbering after a third lane is reconstructed: the primary
/// up-stem lane is voice 1, the down-stem lane is voice 2, and an additional up-stem lane is voice 3.
/// Voice numbers are semantically arbitrary in MusicXML, but this convention gives notation editors
/// the expected rest/stem placement for three-voice piano writing.
/// </summary>
public sealed class MusicXmlMultiVoiceNumberPostProcessor
{
    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var changed = false;

        foreach (var measure in document.Descendants("measure"))
        foreach (var staffGroup in measure.Elements("note").GroupBy(x => (int?)x.Element("staff") ?? 1))
        {
            var lanes = staffGroup
                .GroupBy(x => (int?)x.Element("voice") ?? 1)
                .Select(group => new
                {
                    Voice = group.Key,
                    Notes = group.ToList(),
                    Stem = group.Select(x => (string?)x.Element("stem"))
                        .FirstOrDefault(x => x is "up" or "down"),
                    X = group.Select(ReadX).Where(x => x.HasValue).Select(x => x!.Value)
                        .DefaultIfEmpty(double.MaxValue).Min()
                })
                .ToList();

            if (lanes.Count < 3) continue;
            var down = lanes.Where(x => x.Stem == "down").OrderBy(x => x.X).FirstOrDefault();
            var ups = lanes.Where(x => x.Stem == "up").OrderBy(x => x.X).ToList();
            if (down is null || ups.Count < 2) continue;

            var primaryUp = ups[0];
            var extraUp = ups[1];
            var mapping = new Dictionary<int, int>
            {
                [primaryUp.Voice] = 1,
                [down.Voice] = 2,
                [extraUp.Voice] = 3
            };
            if (mapping.All(x => x.Key == x.Value)) continue;

            // Use temporary values first so swaps cannot collide in-place.
            foreach (var lane in lanes.Where(x => mapping.ContainsKey(x.Voice)))
                foreach (var note in lane.Notes)
                    note.Element("voice")!.Value = (100 + lane.Voice).ToString();

            foreach (var lane in lanes.Where(x => mapping.ContainsKey(x.Voice)))
                foreach (var note in lane.Notes)
                    note.Element("voice")!.Value = mapping[lane.Voice].ToString();

            changed = true;
        }

        if (changed) document.Save(path);
    }

    private static double? ReadX(XElement note)
    {
        var text = (string?)note.Attribute("default-x");
        return double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
