using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Adds stem direction to the MusicXML produced by MusicXmlWriter.
/// Events are consumed in the exact order used by the writer: per system, per staff,
/// ordered by X and then by descending Y.
/// </summary>
public sealed class MusicXmlStemPostProcessor
{
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
            foreach (var noteElement in measure.Elements("note"))
            {
                var staffNumber = (int?)noteElement.Element("staff") ?? 1;
                if (staffNumber < 1 || staffNumber > group.Count) continue;

                var staffIndex = group[staffNumber - 1].Index;
                if (!queues.TryGetValue(staffIndex, out var queue) || queue.Count == 0) continue;
                var evt = queue.Dequeue();

                if (evt.Step is null || string.IsNullOrWhiteSpace(evt.StemDirection)) continue;

                var existing = noteElement.Element("stem");
                if (existing is not null)
                {
                    existing.Value = evt.StemDirection;
                    continue;
                }

                var stem = new XElement("stem", evt.StemDirection);
                var insertionPoint = noteElement.Element("beam") ??
                                     noteElement.Element("notations") ??
                                     noteElement.Element("staff");
                if (insertionPoint is not null) insertionPoint.AddBeforeSelf(stem);
                else noteElement.Add(stem);
            }
        }

        document.Save(path);
    }

    private static bool IsTimedEvent(RecognizedEvent evt) =>
        evt.Step is not null ||
        evt.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase) ||
        evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase);
}
