using System.Globalization;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Preserves the source SVG vertical position of rests. Voice reconstruction is semantic; this
/// pass only carries engraving Y into MusicXML so notation software does not move a correctly
/// assigned polyphonic rest to the opposite side of the staff.
/// </summary>
public sealed class MusicXmlRestYPostProcessor
{
    public void Apply(string path, AnalysisResult analysis)
    {
        var document = XDocument.Load(path);
        var staves = analysis.Staves.OrderBy(s => s.Center).ToList();
        if (staves.Count == 0) return;

        var queues = analysis.Staves.ToDictionary(
            s => s.Index,
            s => new Queue<RecognizedEvent>(analysis.Events
                .Where(e => e.StaffIndex == s.Index && (e.Step is not null || e.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(e => e.X)
                .ThenByDescending(e => e.Y)));

        var system = -1;
        var changed = false;
        foreach (var measure in document.Descendants("measure"))
        {
            if (measure.Element("attributes")?.Elements("clef").Any() == true) system++;
            if (system < 0) system = 0;

            foreach (var note in measure.Elements("note"))
            {
                var staffNumber = (int?)note.Element("staff") ?? 1;
                var staffOrder = system * 2 + staffNumber - 1;
                if (staffOrder < 0 || staffOrder >= staves.Count) continue;

                var staff = staves[staffOrder];
                if (!queues.TryGetValue(staff.Index, out var queue) || queue.Count == 0) continue;
                var evt = queue.Dequeue();

                if (note.Element("rest") is null || !evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase)) continue;

                // MusicXML default-y is in tenths relative to the top staff line; positive is up.
                var defaultY = (staff.Top - evt.Y) * 10.0 / Math.Max(staff.Space, .001);
                note.SetAttributeValue("default-y", defaultY.ToString("0.###", CultureInfo.InvariantCulture));
                changed = true;
            }
        }

        if (changed) document.Save(path);
    }
}
