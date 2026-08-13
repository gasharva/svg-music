using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Repairs rests after voices have already been split. A rest can be geometrically associated with
/// a down-stem lane even when the first voice-layout pass has put it into the up-stem lane. When a
/// rest is immediately to the left of a note in another voice, move it to that lane and rebuild the
/// timed stream. If that moved rest becomes the first unit of the lane, the lane starts at measure
/// time zero: the rest itself represents the initial silence, so an extra forward would be wrong.
/// </summary>
public sealed class MusicXmlRestLaneGeometryPostProcessor
{
    private const double NearXTenths = 12.0;

    private sealed class Unit
    {
        public List<XElement> Notes { get; } = [];
        public int Voice { get; set; }
        public int Duration => (int?)Notes[0].Element("duration") ?? 0;
        public bool IsRest => Notes.All(x => x.Element("rest") is not null);
        public string? Stem => Notes.Select(x => (string?)x.Element("stem")).FirstOrDefault(x => x is "up" or "down");
        public double X => Notes.Select(ReadX).Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(double.NaN).Average();
    }

    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var changedAny = false;

        foreach (var measure in document.Descendants("measure"))
        {
            var notes = measure.Elements("note").ToList();
            if (notes.Count == 0) continue;

            var unitsByStaff = notes
                .GroupBy(x => (int?)x.Element("staff") ?? 1)
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => BuildUnits(x.ToList()));

            var changed = false;
            foreach (var staffUnits in unitsByStaff.Values)
            {
                var voices = staffUnits.Select(x => x.Voice).Distinct().ToList();
                if (voices.Count < 2) continue;

                foreach (var rest in staffUnits.Where(x => x.IsRest && !double.IsNaN(x.X)).ToList())
                {
                    var target = staffUnits
                        .Where(x => !x.IsRest && x.Voice != rest.Voice && x.Stem == "down" && !double.IsNaN(x.X))
                        .Where(x => x.X >= rest.X - 2.0 && x.X - rest.X <= NearXTenths)
                        .OrderBy(x => Math.Abs(x.X - rest.X))
                        .FirstOrDefault();
                    if (target is null) continue;

                    // Do not steal a rest when its current lane has equally strong nearby evidence.
                    var currentDistance = staffUnits
                        .Where(x => !x.IsRest && x.Voice == rest.Voice && !double.IsNaN(x.X))
                        .Select(x => Math.Abs(x.X - rest.X))
                        .DefaultIfEmpty(double.MaxValue)
                        .Min();
                    if (currentDistance <= Math.Abs(target.X - rest.X) + 4.0) continue;

                    rest.Voice = target.Voice;
                    changed = true;
                }
            }

            if (!changed) continue;
            changedAny = true;

            measure.Elements("note").Remove();
            measure.Elements("backup").Remove();
            measure.Elements("forward").Remove();

            var cursor = 0;
            foreach (var staffPair in unitsByStaff.OrderBy(x => x.Key))
            {
                if (cursor > 0)
                    measure.Add(new XElement("backup", new XElement("duration", cursor)));

                var lanes = staffPair.Value
                    .GroupBy(x => x.Voice)
                    .OrderBy(x => x.Key)
                    .Select(x => x.OrderBy(u => double.IsNaN(u.X) ? double.MaxValue : u.X).ToList())
                    .ToList();

                var previousEnd = 0;
                for (var laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
                {
                    if (laneIndex > 0 && previousEnd > 0)
                        measure.Add(new XElement("backup", new XElement("duration", previousEnd)));

                    var lane = lanes[laneIndex];
                    var startsWithRest = lane.Count > 0 && lane[0].IsRest;
                    var startOffset = startsWithRest ? 0 : InferStartOffset(lane, lanes[0]);
                    if (startOffset > 0)
                        measure.Add(new XElement("forward", new XElement("duration", startOffset)));

                    foreach (var unit in lane)
                        RenderUnit(measure, unit);

                    previousEnd = startOffset + lane.Sum(x => x.Duration);
                }

                cursor = previousEnd;
            }
        }

        if (changedAny) document.Save(path);
    }

    private static int InferStartOffset(IReadOnlyList<Unit> lane, IReadOnlyList<Unit> reference)
    {
        if (lane.Count == 0 || reference.Count == 0 || double.IsNaN(lane[0].X) || double.IsNaN(reference[0].X)) return 0;
        if (lane[0].X - reference[0].X <= 13.5) return 0;
        return reference
            .Where(x => !double.IsNaN(x.X) && x.X < lane[0].X - 13.5)
            .Sum(x => x.Duration);
    }

    private static List<Unit> BuildUnits(IReadOnlyList<XElement> notes)
    {
        var result = new List<Unit>();
        Unit? current = null;
        foreach (var note in notes)
        {
            if (current is null || note.Element("chord") is null)
            {
                current = new Unit { Voice = (int?)note.Element("voice") ?? 1 };
                result.Add(current);
            }
            current.Notes.Add(note);
        }
        return result;
    }

    private static void RenderUnit(XElement measure, Unit unit)
    {
        foreach (var note in unit.Notes)
        {
            var voice = note.Element("voice");
            if (voice is null)
            {
                voice = new XElement("voice", unit.Voice);
                note.Add(voice);
            }
            else voice.Value = unit.Voice.ToString(CultureInfo.InvariantCulture);
            measure.Add(note);
        }
    }

    private static double? ReadX(XElement note)
    {
        var text = (string?)note.Attribute("default-x");
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
