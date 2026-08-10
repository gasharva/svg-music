using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Repairs voice reconstruction left after the first layout pass. If a staff is still serialized
/// as one voice but contains sounding events with both stem directions, that alone is sufficient
/// evidence for two parallel voices. Rests are assigned afterwards by onset occupancy, with
/// measure-duration fitting only as a fallback.
/// </summary>
public sealed class MusicXmlRestVoiceConflictPostProcessor
{
    private sealed class Unit
    {
        public List<XElement> Notes { get; } = [];
        public bool IsRest => Notes.Count > 0 && Notes.All(x => x.Element("rest") is not null);
        public string? StemDirection => Notes.Select(x => (string?)x.Element("stem"))
            .FirstOrDefault(x => x is "up" or "down");
        public int Duration => (int?)Notes.FirstOrDefault()?.Element("duration") ?? 0;
        public double X => Notes
            .Select(ReadDefaultX)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty(double.NaN)
            .Average();
        public int ExistingVoice => (int?)Notes.FirstOrDefault()?.Element("voice") ?? 1;
    }

    private sealed class Lane(int voice)
    {
        public int Voice { get; } = voice;
        public List<Unit> Units { get; } = [];
        public int Duration => Units.Sum(x => x.Duration);
    }

    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var divisions = 1;
        var beats = 4;
        var beatType = 4;

        foreach (var measure in document.Descendants("measure"))
        {
            UpdateTiming(measure, ref divisions, ref beats, ref beatType);
            var measureDuration = Math.Max(1, beats * divisions * 4 / Math.Max(1, beatType));

            var notes = measure.Elements("note").ToList();
            if (notes.Count == 0) continue;

            var byStaff = notes
                .GroupBy(x => (int?)x.Element("staff") ?? 1)
                .OrderBy(x => x.Key)
                .ToList();

            var staffLayouts = new List<List<Lane>>();
            var changed = false;

            foreach (var staffGroup in byStaff)
            {
                var units = BuildUnits(staffGroup.ToList());
                var voices = units.Select(x => x.ExistingVoice).Distinct().ToList();
                var rests = units.Where(x => x.IsRest).ToList();
                var sounding = units.Where(x => !x.IsRest).ToList();
                var hasUp = sounding.Any(x => x.StemDirection == "up");
                var hasDown = sounding.Any(x => x.StemDirection == "down");

                // Opposite stem directions on the same staff are already sufficient evidence
                // of parallel voices. Do not require a rest, X proximity, or a perfect duration
                // sum before creating the two lanes.
                if (voices.Count == 1 && hasUp && hasDown)
                {
                    var baseVoice = (staffGroup.Key - 1) * 2 + 1;
                    var upLane = new Lane(baseVoice);
                    var downLane = new Lane(baseVoice + 1);

                    foreach (var unit in sounding)
                    {
                        if (unit.StemDirection == "down") downLane.Units.Add(unit);
                        else upLane.Units.Add(unit);
                    }

                    foreach (var rest in rests.OrderBy(x => x.X))
                        AssignRest(rest, upLane, downLane, measureDuration);

                    staffLayouts.Add([upLane, downLane]);
                    changed = true;
                }
                else
                {
                    staffLayouts.Add(units
                        .GroupBy(x => x.ExistingVoice)
                        .OrderBy(x => x.Key)
                        .Select(group =>
                        {
                            var lane = new Lane(group.Key);
                            lane.Units.AddRange(group);
                            return lane;
                        })
                        .ToList());
                }
            }

            if (!changed) continue;

            measure.Elements("note").Remove();
            measure.Elements("backup").Remove();
            measure.Elements("forward").Remove();

            var cursor = 0;
            foreach (var lanes in staffLayouts)
            {
                if (cursor > 0)
                    measure.Add(new XElement("backup", new XElement("duration", cursor)));

                var previousLaneDuration = 0;
                for (var laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
                {
                    var lane = lanes[laneIndex];
                    if (laneIndex > 0 && previousLaneDuration > 0)
                        measure.Add(new XElement("backup", new XElement("duration", previousLaneDuration)));

                    foreach (var unit in lane.Units.OrderBy(x => double.IsNaN(x.X) ? double.MaxValue : x.X))
                        RenderUnit(measure, unit, lane.Voice);

                    previousLaneDuration = lane.Duration;
                }

                cursor = previousLaneDuration;
            }
        }

        document.Save(path);
    }

    private static void AssignRest(Unit rest, Lane upLane, Lane downLane, int measureDuration)
    {
        var upOccupied = OccupiesOnset(upLane, rest);
        var downOccupied = OccupiesOnset(downLane, rest);

        // Strongest rule: if only one voice already has a sounding event at this engraved onset,
        // the rest belongs to the other voice regardless of imperfect recognized durations.
        if (upOccupied != downOccupied)
        {
            (upOccupied ? downLane : upLane).Units.Add(rest);
            return;
        }

        // Secondary rule: use measure filling only when onset occupancy is ambiguous.
        var upAfter = upLane.Duration + rest.Duration;
        var downAfter = downLane.Duration + rest.Duration;
        var upExact = upAfter == measureDuration;
        var downExact = downAfter == measureDuration;
        if (upExact != downExact)
        {
            (upExact ? upLane : downLane).Units.Add(rest);
            return;
        }

        var upRemaining = Math.Abs(measureDuration - upAfter);
        var downRemaining = Math.Abs(measureDuration - downAfter);
        (upRemaining <= downRemaining ? upLane : downLane).Units.Add(rest);
    }

    private static bool OccupiesOnset(Lane lane, Unit rest)
    {
        if (double.IsNaN(rest.X)) return false;
        return lane.Units.Any(unit =>
            !unit.IsRest && !double.IsNaN(unit.X) && Math.Abs(unit.X - rest.X) <= 13.5);
    }

    private static List<Unit> BuildUnits(IReadOnlyList<XElement> notes)
    {
        var result = new List<Unit>();
        Unit? current = null;

        foreach (var note in notes)
        {
            var isChordContinuation = note.Element("chord") is not null;
            if (current is null || !isChordContinuation)
            {
                current = new Unit();
                result.Add(current);
            }
            current.Notes.Add(note);
        }

        return result;
    }

    private static void RenderUnit(XElement measure, Unit unit, int voice)
    {
        foreach (var note in unit.Notes)
        {
            var voiceElement = note.Element("voice");
            if (voiceElement is null)
            {
                voiceElement = new XElement("voice", voice);
                var type = note.Element("type");
                if (type is not null) type.AddBeforeSelf(voiceElement); else note.Add(voiceElement);
            }
            else
            {
                voiceElement.Value = voice.ToString(CultureInfo.InvariantCulture);
            }

            measure.Add(note);
        }
    }

    private static double? ReadDefaultX(XElement note)
    {
        var value = (string?)note.Attribute("default-x");
        if (value is null) return null;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static void UpdateTiming(XElement measure, ref int divisions, ref int beats, ref int beatType)
    {
        var attributes = measure.Element("attributes");
        if (attributes is null) return;

        var newDivisions = (int?)attributes.Element("divisions");
        if (newDivisions is > 0) divisions = newDivisions.Value;

        var time = attributes.Element("time");
        if (time is null) return;

        var newBeats = (int?)time.Element("beats");
        var newBeatType = (int?)time.Element("beat-type");
        if (newBeats is > 0) beats = newBeats.Value;
        if (newBeatType is > 0) beatType = newBeatType.Value;
    }
}
