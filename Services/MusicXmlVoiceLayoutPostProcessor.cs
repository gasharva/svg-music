using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Rewrites the timed stream as complete MusicXML voices. Engraving may place simultaneous
/// voices at slightly different X coordinates, but a shorter voice must not be followed by
/// a longer simultaneous voice before its own continuation notes are emitted.
/// </summary>
public sealed class MusicXmlVoiceLayoutPostProcessor
{
    private sealed record NoteBinding(XElement Element, RecognizedEvent Event);

    private sealed class ChordUnit
    {
        public List<NoteBinding> Notes { get; } = [];
        public double X => Notes.Where(x => x.Event.StemX.HasValue)
            .Select(x => x.Event.StemX!.Value)
            .DefaultIfEmpty(Notes.Average(x => x.Event.X))
            .Average();
        public string? StemDirection => Notes.Select(x => x.Event.StemDirection)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        public int Duration => Math.Max(0, Notes[0].Event.Duration);
        public bool IsRest => Notes.All(x => x.Event.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class VoiceLane(int key, IEnumerable<ChordUnit> units)
    {
        public int Key { get; } = key;
        public List<ChordUnit> Units { get; } = units.ToList();
        public int Duration => Units.Sum(x => x.Duration);
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

        var currentDivisions = 1;
        var currentBeats = 4;
        var currentBeatType = 4;
        var groupIndex = -1;

        foreach (var measure in document.Descendants("measure"))
        {
            UpdateTiming(measure, ref currentDivisions, ref currentBeats, ref currentBeatType);
            var measureDuration = Math.Max(1, currentBeats * currentDivisions * 4 / Math.Max(1, currentBeatType));

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

            var cursorFromMeasureStart = 0;
            for (var staffNumber = 1; staffNumber <= group.Count; staffNumber++)
            {
                var staff = group[staffNumber - 1];
                var bindings = bindingsByStaff[staff.Index];
                if (bindings.Count == 0) continue;

                if (cursorFromMeasureStart > 0)
                    measure.Add(new XElement("backup", new XElement("duration", cursorFromMeasureStart)));

                var units = BuildChordUnits(bindings, staff.Space);
                var soundingUnits = units.Where(x => !x.IsRest).ToList();
                var restUnits = units.Where(x => x.IsRest).ToList();
                var polyphonic = HasSimultaneousOppositeStemUnits(soundingUnits, staff.Space);
                var baseVoice = (staffNumber - 1) * 2 + 1;

                List<VoiceLane> lanes;
                if (polyphonic)
                {
                    lanes = soundingUnits
                        .GroupBy(x => x.StemDirection == "down" ? 1 : 0)
                        .OrderBy(x => x.Key)
                        .Select(x => new VoiceLane(x.Key, x))
                        .ToList();

                    AssignRestsToLanes(lanes, restUnits, measureDuration);
                }
                else
                {
                    lanes = [new VoiceLane(0, units)];
                }

                var lastLaneDuration = 0;
                for (var laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
                {
                    if (laneIndex > 0 && lastLaneDuration > 0)
                        measure.Add(new XElement("backup", new XElement("duration", lastLaneDuration)));

                    var lane = lanes[laneIndex].Units.OrderBy(x => x.X).ToList();
                    var voice = baseVoice + lanes[laneIndex].Key;
                    foreach (var unit in lane)
                        RenderUnit(measure, unit, voice);

                    lastLaneDuration = lane.Sum(x => x.Duration);
                }

                cursorFromMeasureStart = lastLaneDuration;
            }
        }

        document.Save(path);
    }

    private static void AssignRestsToLanes(
        IReadOnlyList<VoiceLane> lanes,
        IReadOnlyList<ChordUnit> rests,
        int measureDuration)
    {
        if (rests.Count == 0 || lanes.Count == 0) return;

        foreach (var rest in rests.OrderBy(x => x.X))
        {
            var best = lanes
                .Select(lane => new
                {
                    Lane = lane,
                    NewDuration = lane.Duration + rest.Duration,
                    Exact = lane.Duration + rest.Duration == measureDuration,
                    Overshoot = Math.Max(0, lane.Duration + rest.Duration - measureDuration),
                    Remaining = Math.Abs(measureDuration - (lane.Duration + rest.Duration)),
                    XDistance = lane.Units.Count == 0
                        ? double.MaxValue
                        : lane.Units.Min(x => Math.Abs(x.X - rest.X))
                })
                .OrderByDescending(x => x.Exact)
                .ThenBy(x => x.Overshoot > 0)
                .ThenBy(x => x.Remaining)
                .ThenBy(x => x.XDistance)
                .First();

            best.Lane.Units.Add(rest);
        }
    }

    private static void UpdateTiming(
        XElement measure,
        ref int divisions,
        ref int beats,
        ref int beatType)
    {
        var attributes = measure.Element("attributes");
        if (attributes is null) return;

        var newDivisions = (int?)attributes.Element("divisions");
        if (newDivisions.HasValue && newDivisions.Value > 0)
            divisions = newDivisions.Value;

        var time = attributes.Element("time");
        if (time is null) return;

        var newBeats = (int?)time.Element("beats");
        var newBeatType = (int?)time.Element("beat-type");
        if (newBeats.HasValue && newBeats.Value > 0) beats = newBeats.Value;
        if (newBeatType.HasValue && newBeatType.Value > 0) beatType = newBeatType.Value;
    }

    private static List<ChordUnit> BuildChordUnits(IReadOnlyList<NoteBinding> bindings, double staffSpace)
    {
        var result = new List<ChordUnit>();
        var consumed = new HashSet<NoteBinding>();
        var stemTolerance = staffSpace * .20;

        foreach (var binding in bindings.Where(x => x.Event.StemX.HasValue).OrderBy(x => x.Event.StemX))
        {
            if (consumed.Contains(binding)) continue;
            var stemX = binding.Event.StemX!.Value;
            var stemDirection = binding.Event.StemDirection;
            var unit = new ChordUnit();
            foreach (var member in bindings
                         .Where(x => !consumed.Contains(x) && x.Event.StemX.HasValue)
                         .Where(x => Math.Abs(x.Event.StemX!.Value - stemX) <= stemTolerance)
                         // Opposite voices can engrave two separate stems on effectively the same X.
                         // Stem direction is therefore part of chord identity, not just X proximity.
                         .Where(x => string.Equals(x.Event.StemDirection, stemDirection, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(x => x.Event.Y))
            {
                unit.Notes.Add(member);
                consumed.Add(member);
            }
            NormalizeChordMarkup(unit);
            result.Add(unit);
        }

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

        foreach (var unit in result.Where(x => x.Notes.Count > 0)) NormalizeChordMarkup(unit);
        return result.Where(x => x.Notes.Count > 0).OrderBy(x => x.X).ToList();
    }

    private static bool HasSimultaneousOppositeStemUnits(IReadOnlyList<ChordUnit> units, double staffSpace)
    {
        var tolerance = staffSpace * 1.35;
        for (var i = 0; i < units.Count; i++)
        for (var j = i + 1; j < units.Count; j++)
        {
            if (units[j].X - units[i].X > tolerance) break;
            if ((units[i].StemDirection == "up" && units[j].StemDirection == "down") ||
                (units[i].StemDirection == "down" && units[j].StemDirection == "up"))
                return true;
        }
        return false;
    }

    private static void NormalizeChordMarkup(ChordUnit unit)
    {
        if (unit.IsRest) return;

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
            if (first is not null) first.AddBeforeSelf(chord); else element.Add(chord);
        }
    }

    private static void RenderUnit(XElement measure, ChordUnit unit, int voice)
    {
        foreach (var binding in unit.Notes)
        {
            var voiceElement = binding.Element.Element("voice");
            if (voiceElement is null)
            {
                voiceElement = new XElement("voice", voice);
                var type = binding.Element.Element("type");
                if (type is not null) type.AddBeforeSelf(voiceElement); else binding.Element.Add(voiceElement);
            }
            else voiceElement.Value = voice.ToString();
            measure.Add(binding.Element);
        }
    }

    private static bool IsTimedEvent(RecognizedEvent evt) =>
        evt.Step is not null || evt.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase) ||
        evt.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase);

    private static List<List<Staff>> BuildStaffGroups(AnalysisResult analysis)
    {
        var staves = analysis.Staves.OrderBy(x => x.Center).ToList();
        if (staves.Count < 2) return staves.Select(x => new List<Staff> { x }).ToList();
        var clefs = staves.ToDictionary(x => x.Index, x => analysis.Events
            .Where(e => e.StaffIndex == x.Index && e.Kind.StartsWith("clef-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.X).FirstOrDefault()?.ClefSign);
        var recognizablePairs = 0;
        for (var i = 0; i + 1 < staves.Count; i += 2)
            if (clefs[staves[i].Index] == "G" && clefs[staves[i + 1].Index] == "F") recognizablePairs++;
        var expectedPairs = staves.Count / 2;
        if (!(expectedPairs > 0 && recognizablePairs >= Math.Max(1, expectedPairs / 2)))
            return staves.Select(x => new List<Staff> { x }).ToList();
        var result = new List<List<Staff>>();
        for (var i = 0; i < staves.Count; i += 2)
            result.Add(i + 1 < staves.Count ? [staves[i], staves[i + 1]] : [staves[i]]);
        return result;
    }
}
