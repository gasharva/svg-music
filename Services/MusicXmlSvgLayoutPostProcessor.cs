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
    internal const string CrossStaffIdAttribute = "data-cross-staff-id";
    internal const string SourceSymbolAttribute = "data-source-symbol-id";

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

                // Cross-staff identity is semantic information recovered before MusicXML layout.
                // Keep it as a temporary private attribute while later voice/timing passes reorder
                // notes. The final cross-staff pass consumes and removes these attributes.
                if (binding.Event.CrossStaffChordId.HasValue)
                {
                    binding.Element.SetAttributeValue(CrossStaffIdAttribute, binding.Event.CrossStaffChordId.Value);
                    if (!string.IsNullOrWhiteSpace(binding.Event.SourceSymbolId))
                        binding.Element.SetAttributeValue(SourceSymbolAttribute, binding.Event.SourceSymbolId);
                }
            }
        }

        document.Save(path);
    }

    private static bool IsTimedEvent(RecognizedEvent evt) =>
        evt.Step is not null ||
        evt.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase) ||
        evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase);
}
