using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Restores simultaneous onsets that engraving may spread horizontally.
/// Independent chords with opposite stem directions and close X positions are emitted
/// as parallel MusicXML voices using backup/forward instead of being serialized in time.
/// </summary>
public sealed class MusicXmlPolyphonyPostProcessor
{
    private sealed record NoteBinding(XElement Element, RecognizedEvent Event);

    private sealed class ChordUnit
    {
        public List<NoteBinding> Notes { get; } = [];
        public RecognizedEvent Root => Notes[0].Event;
        public double X => Root.StemX ?? Root.X;
        public string? StemDirection => Notes
            .Select(x => x.Event.StemDirection)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        public int Duration => Math.Max(0, Root.Duration);
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

                var evt = queue.Dequeue();
                bindingsByStaff[staffIndex].Add(new NoteBinding(noteElement, evt));
            }

            // Rebuild only the timed stream. Attributes and other metadata stay in place.
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

                var units = BuildChordUnits(bindings);
                var onsetGroups = BuildOnsetGroups(units, staff.Space);
                var staffDuration = 0;
                var baseVoice = (staffNumber - 1) * 2 + 1;

                foreach (var onset in onsetGroups)
                {
                    if (onset.Count == 1)
                    {
                        // Voice identity must stay stable between successive onsets.
                        // Otherwise two beamed notes with the same stem direction can be
                        // written into different voices and notation software breaks the beam.
                        RenderUnit(measure, onset[0], VoiceForDirection(baseVoice, onset[0].StemDirection));
                        staffDuration += onset[0].Duration;
                        continue;
                    }

                    // Multiple independent chords share one logical time position.
                    // Each voice starts at the same cursor position; after the last voice
                    // advance to the longest duration of the simultaneous group.
                    var maxDuration = onset.Max(x => x.Duration);
                    var currentOffset = 0;

                    foreach (var unit in onset
                                 .OrderBy(x => VoiceOrder(x.StemDirection))
                                 .ThenBy(x => x.X))
                    {
                        if (currentOffset > 0)
                            measure.Add(new XElement("backup", new XElement("duration", currentOffset)));

                        // Do not assign voices by the unit's position inside this particular
                        // onset group. Use the same direction -> voice mapping everywhere on
                        // the staff so beam membership survives polyphony reconstruction.
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

    private static List<ChordUnit> BuildChordUnits(IReadOnlyList<NoteBinding> bindings)
    {
        var result = new List<ChordUnit>();
        ChordUnit? current = null;

        foreach (var binding in bindings)
        {
            if (current is null || !binding.Event.Chord)
            {
                current = new ChordUnit();
                result.Add(current);
            }
            current.Notes.Add(binding);
        }

        return result;
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

            // Do not merge an ordinary melodic succession merely because its X distance is small.
            // The strong signal for polyphony is opposite stem direction at nearly the same X.
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

            // The existing element is reused intentionally. Its duration, type, stem and
            // beam elements were already reconstructed from SVG geometry and must survive
            // polyphony processing unchanged.
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
