using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Natural signs have pitch alter=0, so they cannot be inferred from Alter alone. Recover explicit
/// naturals directly from the classified SVG signs and attach them to the matching MusicXML note.
/// </summary>
public sealed class MusicXmlNaturalAccidentalPostProcessor
{
    public void Apply(string path, AnalysisResult analysis)
    {
        var naturalSymbols = analysis.Classifications
            .Where(x => x.Kind == "accidental-natural")
            .Select(x => x.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        if (naturalSymbols.Count == 0) return;

        var bindings = BindNotes(path, analysis);
        if (bindings.Count == 0) return;

        foreach (var use in analysis.Uses.Where(x => naturalSymbols.Contains(x.SymbolId)))
        {
            var staff = analysis.Staves
                .Where(s => use.X >= s.Left - s.Space * 3 && use.X <= s.Right + s.Space * 3)
                .Select(s => new { Staff = s, Distance = Math.Abs(use.Y - s.Center) / Math.Max(s.Space, .001) })
                .Where(x => x.Distance <= 4.0)
                .OrderBy(x => x.Distance)
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            var accidentalPosition = StaffPosition(use.Y, staff);
            var target = bindings
                .Where(x => x.Event.StaffIndex == staff.Index && x.Event.Step is not null)
                .Where(x => x.Event.X > use.X && x.Event.X - use.X <= staff.Space * 5.25)
                .Where(x => Math.Abs(x.Event.Y - use.Y) <= staff.Space * 1.35)
                .Select(x => new
                {
                    Binding = x,
                    PositionDelta = Math.Abs(StaffPosition(x.Event.Y, staff) - accidentalPosition),
                    YDelta = Math.Abs(x.Event.Y - use.Y),
                    XDelta = x.Event.X - use.X
                })
                .OrderBy(x => x.PositionDelta)
                .ThenBy(x => x.YDelta)
                .ThenBy(x => x.XDelta)
                .Select(x => x.Binding)
                .FirstOrDefault();
            if (target is null) continue;

            var pitch = target.Element.Element("pitch");
            pitch?.Element("alter")?.Remove();
            var accidental = target.Element.Element("accidental");
            if (accidental is null)
            {
                accidental = new XElement("accidental", "natural");
                var type = target.Element.Element("type");
                if (type is not null) type.AddAfterSelf(accidental);
                else target.Element.Add(accidental);
            }
            else accidental.Value = "natural";
        }

        bindings.Document.Save(path);
    }

    private sealed record Binding(XElement Element, RecognizedEvent Event);
    private sealed class BindingSet : List<Binding>
    {
        public required XDocument Document { get; init; }
    }

    private static BindingSet BindNotes(string path, AnalysisResult analysis)
    {
        var document = XDocument.Load(path);
        var result = new BindingSet { Document = document };
        var groups = BuildStaffGroups(analysis);
        if (groups.Count == 0) return result;

        var queues = analysis.Staves.ToDictionary(
            staff => staff.Index,
            staff => new Queue<RecognizedEvent>(analysis.Events
                .Where(x => x.StaffIndex == staff.Index && IsTimedEvent(x))
                .OrderBy(x => x.X)
                .ThenByDescending(x => x.Y)));

        var groupIndex = -1;
        foreach (var measure in document.Descendants("measure"))
        {
            if (measure.Elements("attributes").Elements("clef").Any()) groupIndex++;
            if (groupIndex < 0) groupIndex = 0;
            if (groupIndex >= groups.Count) break;
            var group = groups[groupIndex];

            foreach (var note in measure.Elements("note"))
            {
                var staffNumber = (int?)note.Element("staff") ?? 1;
                if (staffNumber < 1 || staffNumber > group.Count) continue;
                var staffIndex = group[staffNumber - 1].Index;
                if (!queues.TryGetValue(staffIndex, out var queue) || queue.Count == 0) continue;
                result.Add(new Binding(note, queue.Dequeue()));
            }
        }

        return result;
    }

    private static bool IsTimedEvent(RecognizedEvent evt) =>
        evt.Step is not null || evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase);

    private static int StaffPosition(double y, Staff staff) =>
        (int)Math.Round((staff.Bottom - y) / Math.Max(staff.Space / 2, .001));

    private static List<List<Staff>> BuildStaffGroups(AnalysisResult analysis)
    {
        var staves = analysis.Staves.OrderBy(x => x.Center).ToList();
        if (staves.Count < 2) return staves.Select(x => new List<Staff> { x }).ToList();
        var result = new List<List<Staff>>();
        for (var i = 0; i < staves.Count; i += 2)
            result.Add(i + 1 < staves.Count ? [staves[i], staves[i + 1]] : [staves[i]]);
        return result;
    }
}
