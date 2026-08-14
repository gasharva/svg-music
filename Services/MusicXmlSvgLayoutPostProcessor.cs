using System.Globalization;
using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Carries horizontal engraving information from the source SVG into MusicXML.
/// Timing and voices remain authoritative; default-x only preserves the source layout
/// when notation software performs collision/layout after import.
/// </summary>
public sealed class MusicXmlSvgLayoutPostProcessor
{
    private sealed record NoteBinding(XElement Element, RecognizedEvent Event);

    public void Apply(string path, AnalysisResult analysis)
    {
        var document = XDocument.Load(path);
        var groups = MusicXmlWriter.BuildStaffGroups(analysis);
        if (groups.Count == 0) return;

        var queues = analysis.Staves.ToDictionary(
            staff => staff.Index,
            staff => new Queue<RecognizedEvent>(analysis.Events
                .Where(x => x.StaffIndex == staff.Index && IsTimedEvent(x))
                .OrderBy(x => x.X)
                .ThenByDescending(x => x.Y)));

        var groupIndex = -1;
        foreach (var measure in document.Descendants("measure"))
        {
            var startsSystem = measure.Elements("attributes").Elements("clef").Any();
            if (startsSystem) groupIndex++;
            if (groupIndex < 0) groupIndex = 0;
            if (groupIndex >= groups.Count) break;

            var group = groups[groupIndex];
            var bindings = new List<NoteBinding>();

            foreach (var noteElement in measure.Elements("note"))
            {
                var staffNumber = (int?)noteElement.Element("staff") ?? 1;
                if (staffNumber < 1 || staffNumber > group.Count) continue;

                var staffIndex = group[staffNumber - 1].Index;
                if (!queues.TryGetValue(staffIndex, out var queue) || queue.Count == 0) continue;
                bindings.Add(new NoteBinding(noteElement, queue.Dequeue()));
            }

            if (bindings.Count == 0) continue;

            // MusicXML default-x is measured in tenths. One staff space is conventionally
            // ten tenths, so this preserves SVG spacing without depending on SVG page units.
            // A common origin/scale for the whole grand staff keeps simultaneous notes on
            // different staves on exactly the same vertical engraving line.
            var originX = bindings.Min(x => x.Event.X);
            var staffSpace = group.Average(x => x.Space);
            var scale = 10.0 / Math.Max(staffSpace, .001);
            const double leftInsetTenths = 60.0;

            foreach (var binding in bindings)
            {
                var defaultX = leftInsetTenths + (binding.Event.X - originX) * scale;
                binding.Element.SetAttributeValue(
                    "default-x",
                    defaultX.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        document.Save(path);
    }

    private static bool IsTimedEvent(RecognizedEvent evt) =>
        evt.Step is not null ||
        evt.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase) ||
        evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase);
}
