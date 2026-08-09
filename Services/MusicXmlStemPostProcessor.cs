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
        var groups = BuildStaffGroups(analysis);
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

    private static List<List<Staff>> BuildStaffGroups(AnalysisResult analysis)
    {
        var staves = analysis.Staves.OrderBy(x => x.Center).ToList();
        if (staves.Count < 2) return staves.Select(x => new List<Staff> { x }).ToList();

        var clefs = staves.ToDictionary(
            x => x.Index,
            x => analysis.Events
                .Where(e => e.StaffIndex == x.Index && e.Kind.StartsWith("clef-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.X)
                .FirstOrDefault()?.ClefSign);

        var recognizablePairs = 0;
        for (var i = 0; i + 1 < staves.Count; i += 2)
            if (clefs[staves[i].Index] == "G" && clefs[staves[i + 1].Index] == "F") recognizablePairs++;

        var expectedPairs = staves.Count / 2;
        var usePianoPairs = expectedPairs > 0 && recognizablePairs >= Math.Max(1, expectedPairs / 2);
        if (!usePianoPairs) return staves.Select(x => new List<Staff> { x }).ToList();

        var result = new List<List<Staff>>();
        for (var i = 0; i < staves.Count; i += 2)
            result.Add(i + 1 < staves.Count ? [staves[i], staves[i + 1]] : [staves[i]]);
        return result;
    }
}
