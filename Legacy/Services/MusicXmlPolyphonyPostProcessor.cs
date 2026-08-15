using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Restores simultaneous onsets that engraving may spread horizontally.
/// Chords are treated as structural units (preferably by shared stem), and voices are
/// assigned only after chord/beam relationships are fixed.
/// </summary>
public sealed class MusicXmlPolyphonyPostProcessor
{
    private sealed record NoteBinding(XElement Element, RecognizedEvent Event);

    private sealed class ChordUnit
    {
        public List<NoteBinding> Notes { get; } = [];
        public RecognizedEvent Root => Notes[0].Event;
        public double X => Notes.Where(x => x.Event.StemX.HasValue)
            .Select(x => x.Event.StemX!.Value)
            .DefaultIfEmpty(Notes.Average(x => x.Event.X))
            .Average();
        public string? StemDirection => Notes
            .Select(x => x.Event.StemDirection)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        public int Duration => Math.Max(0, Root.Duration);
        public string? BeamValue => Notes.Select(x => x.Event.BeamValue)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        public bool IsBlackNote => Notes.Any(x => x.Event.Kind == "notehead-black");
    }

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
            var bindingsByStaff = group.ToDictionary(x => x.Index, _ => new List<NoteBinding>());

            foreach (var noteElement in measure.Elements("note").ToList())
            {
                var staffNumber = (int?)noteElement.Element("staff") ?? 1;
                if (staffNumber < 1 || staffNumber > group.Count) continue;

                var staffIndex = group[staffNumber - 1].Index;
                if (!queues.TryGetValue(staffIndex, out var queue) || queue.Count == 0) continue;

                bindingsByStaff[staffIndex].Add(new NoteBinding(noteElement, queue.Dequeue()));
            }

            measure.Elements("note").Remove();
            measure.Elements("backup").Remove();
            measure.Elements("forward").Remove();

            var previousStaffDuration = 0;
            for (var staffNumber = 1; staffNumber <= group.Count; staffNumber++)
            {
                var staff = group[staffNumber - 1];
                var bindings = bindingsByStaff[staff.Index];
                if (bindings.Count == 0) continue;

                if (previousStaffDuration > 0)
                    measure.Add(new XElement("backup", new XElement("duration", previousStaffDuration)));

                var units = BuildChordUnits(bindings, staff.Space);
                RepairShortBeamContinuations(units, staff.Space);
                var onsetGroups = BuildOnsetGroups(units, staff.Space);
                var staffDuration = 0;
                var baseVoice = (staffNumber - 1) * 2 + 1;

                foreach (var onset in onsetGroups)
                {
                    if (onset.Count == 1)
                    {
                        RenderUnit(measure, onset[0], VoiceForDirection(baseVoice, onset[0].StemDirection));
                        staffDuration += onset[0].Duration;
                        continue;
                    }

                    var maxDuration = onset.Max(x => x.Duration);
                    var currentOffset = 0;

                    foreach (var unit in onset
                                 .OrderBy(x => VoiceOrder(x.StemDirection))
                                 .ThenBy(x => x.X))
                    {
                        if (currentOffset > 0)
                            measure.Add(new XElement("backup", new XElement("duration", currentOffset)));

                        RenderUnit(measure, unit, VoiceForDirection(baseVoice, unit.StemDirection));
                        currentOffset = unit.Duration;
                    }

                    if (currentOffset < maxDuration)
                        measure.Add(new XElement("forward", new XElement("duration", maxDuration - currentOffset)));

                    staffDuration += maxDuration;
                }

                previousStaffDuration = staffDuration;
            }
        }

        document.Save(path);
    }

    private static List<ChordUnit> BuildChordUnits(IReadOnlyList<NoteBinding> bindings, double staffSpace)
    {
        var result = new List<ChordUnit>();
        var consumed = new HashSet<NoteBinding>();
        var stemTolerance = staffSpace * .20;

        // Strong rule: all noteheads sharing the same geometrically detected stem are one chord,
        // regardless of their individual X offsets or their position in the writer's sort order.
        foreach (var binding in bindings
                     .Where(x => x.Event.StemX.HasValue)
                     .OrderBy(x => x.Event.StemX)
                     .ThenByDescending(x => x.Event.Y))
        {
            if (consumed.Contains(binding)) continue;

            var unit = new ChordUnit();
            var stemX = binding.Event.StemX!.Value;
            var members = bindings
                .Where(x => !consumed.Contains(x) && x.Event.StemX.HasValue)
                .Where(x => Math.Abs(x.Event.StemX!.Value - stemX) <= stemTolerance)
                .OrderByDescending(x => x.Event.Y)
                .ToList();

            foreach (var member in members)
            {
                unit.Notes.Add(member);
                consumed.Add(member);
            }
            result.Add(unit);
        }

        // Fallback for stemless notes/rests: retain the pre-existing chord markers.
        ChordUnit? current = null;
        foreach (var binding in bindings.Where(x => !consumed.Contains(x)).OrderBy(x => x.Event.X).ThenByDescending(x => x.Event.Y))
        {
            if (current is null || !binding.Event.Chord)
            {
                current = new ChordUnit();
                result.Add(current);
            }
            current.Notes.Add(binding);
        }

        foreach (var unit in result)
            NormalizeChordMarkup(unit);

        return result.OrderBy(x => x.X).ToList();
    }

    private static void NormalizeChordMarkup(ChordUnit unit)
    {
        var ordered = unit.Notes.OrderByDescending(x => x.Event.Y).ToList();
        unit.Notes.Clear();
        unit.Notes.AddRange(ordered);

        for (var i = 0; i < unit.Notes.Count; i++)
        {
            var element = unit.Notes[i].Element;
            element.Element("chord")?.Remove();
            if (i == 0) continue;

            var chord = new XElement("chord");
            var first = element.Elements().FirstOrDefault();
            if (first is not null) first.AddBeforeSelf(chord);
            else element.Add(chord);
        }
    }

    private static void RepairShortBeamContinuations(IReadOnlyList<ChordUnit> units, double staffSpace)
    {
        for (var i = 0; i + 1 < units.Count; i++)
        {
            var first = units[i];
            var second = units[i + 1];
            if (!string.Equals(first.BeamValue, "begin", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(second.BeamValue)) continue;
            if (!first.IsBlackNote || !second.IsBlackNote) continue;
            if (first.StemDirection != second.StemDirection) continue;
            if (second.X - first.X > staffSpace * 4.0) continue;

            // A beam cannot legally start and then disappear on the immediately following
            // compatible stem. Treat this as a missed beam-end classification, not a quarter note.
            foreach (var binding in second.Notes)
            {
                binding.Event.BeamValue = "end";
                binding.Event.BeamCount = Math.Max(1, binding.Event.BeamCount);
                binding.Event.Type = "eighth";
                binding.Event.Duration = first.Duration;

                binding.Element.Element("type")!.Value = "eighth";
                binding.Element.Element("duration")!.Value = first.Duration.ToString();
                binding.Element.Element("beam")?.Remove();
                var beam = new XElement("beam", new XAttribute("number", 1), "end");
                var insertion = binding.Element.Element("notations") ?? binding.Element.Element("staff");
                if (insertion is not null) insertion.AddBeforeSelf(beam);
                else binding.Element.Add(beam);
            }
        }
    }

    private static List<List<ChordUnit>> BuildOnsetGroups(IReadOnlyList<ChordUnit> units, double staffSpace)
    {
        var result = new List<List<ChordUnit>>();
        var tolerance = staffSpace * 1.35;

        foreach (var unit in units.OrderBy(x => x.X))
        {
            var current = result.LastOrDefault();
            if (current is null)
            {
                result.Add([unit]);
                continue;
            }

            var centerX = current.Average(x => x.X);
            var closeInX = Math.Abs(unit.X - centerX) <= tolerance;
            var hasOppositeStem = current.Any(x => OppositeStemDirections(x.StemDirection, unit.StemDirection));

            if (closeInX && hasOppositeStem)
                current.Add(unit);
            else
                result.Add([unit]);
        }

        return result;
    }

    private static bool OppositeStemDirections(string? a, string? b) =>
        (a == "up" && b == "down") || (a == "down" && b == "up");

    private static int VoiceOrder(string? direction) => direction switch
    {
        "up" => 0,
        "down" => 1,
        _ => 2
    };

    private static int VoiceForDirection(int baseVoice, string? direction) => direction switch
    {
        "down" => baseVoice + 1,
        _ => baseVoice
    };

    private static void RenderUnit(XElement measure, ChordUnit unit, int voice)
    {
        foreach (var binding in unit.Notes)
        {
            var element = binding.Element;
            var voiceElement = element.Element("voice");
            if (voiceElement is null)
            {
                var type = element.Element("type");
                voiceElement = new XElement("voice", voice);
                if (type is not null) type.AddBeforeSelf(voiceElement);
                else element.Add(voiceElement);
            }
            else
            {
                voiceElement.Value = voice.ToString();
            }

            measure.Add(element);
        }
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
