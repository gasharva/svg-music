using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Rewrites only measures that contain grace notes after the normal voice passes have completed.
/// Grace notes consume no metric time, but a parallel voice can still start later in the bar.
/// Its onset is recovered from the preserved SVG default-x columns, using the earliest/full voice
/// as a temporal ruler. This prevents a delayed inner-voice note from snapping to measure start.
/// </summary>
public sealed class MusicXmlGraceVoiceTimingPostProcessor
{
    private const double OnsetXTolerance = 13.5;

    private sealed class Unit
    {
        public List<XElement> Notes { get; } = [];
        public bool IsGrace => Notes.FirstOrDefault()?.Element("grace") is not null;
        public int Duration => IsGrace ? 0 : (int?)Notes.FirstOrDefault()?.Element("duration") ?? 0;
        public double X => Notes
            .Select(ReadDefaultX)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty(double.NaN)
            .Average();
    }

    private sealed class Lane(int voice, IEnumerable<Unit> units)
    {
        public int Voice { get; } = voice;
        public List<Unit> Units { get; } = units.OrderBy(x => double.IsNaN(x.X) ? double.MaxValue : x.X).ToList();
        public int StartOffset { get; set; }
        public int Duration => Units.Sum(x => x.Duration);
        public int EndPosition => StartOffset + Duration;
        public double FirstX => Units
            .Select(x => x.X)
            .Where(x => !double.IsNaN(x))
            .DefaultIfEmpty(double.NaN)
            .Min();
    }

    public void Apply(string path)
    {
        var document = XDocument.Load(path);

        foreach (var measure in document.Descendants("measure"))
        {
            if (!measure.Elements("note").Any(x => x.Element("grace") is not null)) continue;

            var staffLayouts = measure.Elements("note")
                .GroupBy(x => (int?)x.Element("staff") ?? 1)
                .OrderBy(x => x.Key)
                .Select(staffGroup => staffGroup
                    .GroupBy(x => (int?)x.Element("voice") ?? 1)
                    .OrderBy(x => x.Key)
                    .Select(voiceGroup => new Lane(voiceGroup.Key, BuildUnits(voiceGroup.ToList())))
                    .ToList())
                .ToList();

            foreach (var lanes in staffLayouts)
                InferLaneStartOffsets(lanes);

            measure.Elements("note").Remove();
            measure.Elements("backup").Remove();
            measure.Elements("forward").Remove();

            var cursor = 0;
            foreach (var lanes in staffLayouts)
            {
                if (cursor > 0)
                    measure.Add(new XElement("backup", new XElement("duration", cursor)));

                var previousLaneEnd = 0;
                for (var laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
                {
                    var lane = lanes[laneIndex];
                    if (laneIndex > 0 && previousLaneEnd > 0)
                        measure.Add(new XElement("backup", new XElement("duration", previousLaneEnd)));

                    if (lane.StartOffset > 0)
                        measure.Add(new XElement("forward", new XElement("duration", lane.StartOffset)));

                    foreach (var unit in lane.Units)
                        RenderUnit(measure, unit, lane.Voice);

                    previousLaneEnd = lane.EndPosition;
                }

                cursor = previousLaneEnd;
            }
        }

        document.Save(path);
    }

    private static void InferLaneStartOffsets(IReadOnlyList<Lane> lanes)
    {
        foreach (var lane in lanes) lane.StartOffset = 0;
        if (lanes.Count < 2) return;

        var candidates = lanes.Where(x => !double.IsNaN(x.FirstX)).ToList();
        if (candidates.Count < 2) return;

        // Prefer the lane that carries the greatest metrical duration as the ruler. In measure 15
        // this is the upper melody (three quarter-note onsets); the two grace 16ths contribute 0.
        var reference = candidates
            .OrderByDescending(x => x.Duration)
            .ThenBy(x => x.FirstX)
            .First();

        foreach (var lane in candidates)
        {
            if (ReferenceEquals(lane, reference)) continue;
            if (lane.FirstX - reference.FirstX <= OnsetXTolerance) continue;

            lane.StartOffset = reference.Units
                .Where(x => !double.IsNaN(x.X) && x.X < lane.FirstX - OnsetXTolerance)
                .OrderBy(x => x.X)
                .Sum(x => x.Duration);
        }
    }

    private static List<Unit> BuildUnits(IReadOnlyList<XElement> notes)
    {
        var result = new List<Unit>();
        Unit? current = null;

        foreach (var note in notes)
        {
            var chordContinuation = note.Element("chord") is not null;
            if (current is null || !chordContinuation)
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
}
