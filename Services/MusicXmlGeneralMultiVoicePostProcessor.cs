using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Repairs polyphony that cannot be represented by the simple "stem up = one voice, stem down = one voice"
/// model. A single engraved staff may contain several independent voices with the same stem direction.
/// We only intervene when the existing timed stream is impossible (a voice overlaps itself or runs past the
/// measure). Reliable voices on either staff provide an X-to-time grid; impossible units are then recolored
/// into non-overlapping lanes. This keeps already-correct ordinary polyphony untouched.
/// </summary>
public sealed class MusicXmlGeneralMultiVoicePostProcessor
{
    private sealed class Unit
    {
        public List<XElement> Notes { get; } = [];
        public int Staff { get; init; }
        public int OriginalVoice { get; init; }
        public int Duration { get; init; }
        public int RawOnset { get; init; }
        public int Onset { get; set; }
        public double X { get; init; }
        public string? Stem { get; init; }
        public int End => Onset + Duration;
    }

    private sealed class Lane
    {
        public List<Unit> Units { get; } = [];
        public int Voice { get; set; }
        public int End => Units.Count == 0 ? 0 : Units.Max(x => x.End);
        public int FirstOnset => Units.Count == 0 ? 0 : Units.Min(x => x.Onset);
        public int DominantOriginalVoice => Units
            .GroupBy(x => x.OriginalVoice)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Select(x => x.Key)
            .FirstOrDefault();
        public string? DominantStem => Units
            .Where(x => x.Stem is not null)
            .GroupBy(x => x.Stem)
            .OrderByDescending(x => x.Count())
            .Select(x => x.Key)
            .FirstOrDefault();
    }

    private readonly record struct Anchor(double X, int Onset);

    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var divisions = 1;
        var beats = 4;
        var beatType = 4;
        var changedAny = false;

        foreach (var measure in document.Descendants("measure"))
        {
            UpdateTiming(measure, ref divisions, ref beats, ref beatType);
            var measureDuration = Math.Max(1, beats * divisions * 4 / Math.Max(1, beatType));
            var units = ParseUnits(measure);
            if (units.Count == 0) continue;

            var invalidVoices = units
                .GroupBy(x => (x.Staff, x.OriginalVoice))
                .Where(group => !IsValidLane(group.OrderBy(x => x.RawOnset).ToList(), measureDuration))
                .Select(x => x.Key)
                .ToHashSet();

            if (invalidVoices.Count == 0) continue;

            var invalidStaves = invalidVoices.Select(x => x.Staff).ToHashSet();
            var anchors = BuildAnchors(units, invalidVoices, measureDuration);
            if (anchors.Select(x => x.Onset).Distinct().Count() < 2) continue;

            var grid = GreatestCommonDivisor(units.Select(x => x.Duration).Where(x => x > 0));
            if (grid <= 0) grid = Math.Max(1, divisions / 2);

            var layouts = new Dictionary<int, List<Lane>>();
            foreach (var staffGroup in units.GroupBy(x => x.Staff).OrderBy(x => x.Key))
            {
                var staffUnits = staffGroup.ToList();
                if (!invalidStaves.Contains(staffGroup.Key))
                {
                    layouts[staffGroup.Key] = PreserveExistingLanes(staffUnits);
                    continue;
                }

                foreach (var unit in staffUnits)
                {
                    unit.Onset = invalidVoices.Contains((unit.Staff, unit.OriginalVoice))
                        ? PredictOnset(unit.X, anchors, grid, measureDuration)
                        : unit.RawOnset;
                }

                var lanes = ColorIntoLanes(staffUnits, grid);
                AssignVoiceNumbers(lanes);
                layouts[staffGroup.Key] = lanes;
            }

            RebuildMeasure(measure, layouts);
            changedAny = true;
        }

        if (changedAny) document.Save(path);
    }

    private static List<Unit> ParseUnits(XElement measure)
    {
        var result = new List<Unit>();
        var cursor = 0;
        Unit? current = null;

        foreach (var element in measure.Elements())
        {
            if (element.Name == "backup")
            {
                cursor -= (int?)element.Element("duration") ?? 0;
                current = null;
                continue;
            }
            if (element.Name == "forward")
            {
                cursor += (int?)element.Element("duration") ?? 0;
                current = null;
                continue;
            }
            if (element.Name != "note") continue;

            var chord = element.Element("chord") is not null;
            var duration = (int?)element.Element("duration") ?? 0;
            var onset = chord && current is not null ? current.RawOnset : cursor;
            var staff = (int?)element.Element("staff") ?? 1;
            var voice = (int?)element.Element("voice") ?? 1;

            if (!chord || current is null)
            {
                current = new Unit
                {
                    Staff = staff,
                    OriginalVoice = voice,
                    Duration = duration,
                    RawOnset = onset,
                    Onset = onset,
                    X = ReadX(element) ?? double.NaN,
                    Stem = (string?)element.Element("stem")
                };
                result.Add(current);
            }
            current.Notes.Add(element);

            if (!chord) cursor += duration;
        }

        return result;
    }

    private static bool IsValidLane(IReadOnlyList<Unit> units, int measureDuration)
    {
        var previousEnd = 0;
        foreach (var unit in units.OrderBy(x => x.RawOnset).ThenBy(x => x.X))
        {
            if (unit.RawOnset < 0 || unit.RawOnset + unit.Duration > measureDuration) return false;
            if (unit.RawOnset < previousEnd) return false;
            previousEnd = unit.RawOnset + unit.Duration;
        }
        return true;
    }

    private static List<Anchor> BuildAnchors(
        IReadOnlyList<Unit> units,
        IReadOnlySet<(int Staff, int OriginalVoice)> invalidVoices,
        int measureDuration)
    {
        var raw = units
            .Where(x => !invalidVoices.Contains((x.Staff, x.OriginalVoice)))
            .Where(x => !double.IsNaN(x.X) && x.RawOnset >= 0 && x.RawOnset < measureDuration)
            .Select(x => new Anchor(x.X, x.RawOnset))
            .OrderBy(x => x.X)
            .ToList();

        var result = new List<Anchor>();
        foreach (var anchor in raw)
        {
            var nearby = result.LastOrDefault();
            if (result.Count > 0 && Math.Abs(anchor.X - nearby.X) <= 7.5 && Math.Abs(anchor.Onset - nearby.Onset) <= 1)
                continue;
            result.Add(anchor);
        }
        return result;
    }

    private static int PredictOnset(double x, IReadOnlyList<Anchor> anchors, int grid, int measureDuration)
    {
        if (double.IsNaN(x)) return 0;

        var exact = anchors.OrderBy(a => Math.Abs(a.X - x)).First();
        if (Math.Abs(exact.X - x) <= 10.0)
            return Snap(exact.Onset, grid, measureDuration);

        Anchor? left = null;
        Anchor? right = null;
        foreach (var anchor in anchors)
        {
            if (anchor.X <= x) left = anchor;
            if (anchor.X >= x)
            {
                right = anchor;
                break;
            }
        }

        double predicted;
        if (left.HasValue && right.HasValue && Math.Abs(right.Value.X - left.Value.X) > .001 &&
            right.Value.Onset != left.Value.Onset)
        {
            var ratio = (x - left.Value.X) / (right.Value.X - left.Value.X);
            predicted = left.Value.Onset + ratio * (right.Value.Onset - left.Value.Onset);
        }
        else
        {
            var pair = anchors
                .Zip(anchors.Skip(1), (a, b) => (A: a, B: b))
                .Where(p => p.B.Onset != p.A.Onset && Math.Abs(p.B.X - p.A.X) > .001)
                .OrderBy(p => Math.Min(Math.Abs(x - p.A.X), Math.Abs(x - p.B.X)))
                .FirstOrDefault();

            if (pair == default)
                predicted = exact.Onset;
            else
            {
                var slope = (double)(pair.B.Onset - pair.A.Onset) / (pair.B.X - pair.A.X);
                predicted = pair.A.Onset + (x - pair.A.X) * slope;
            }
        }

        return Snap((int)Math.Round(predicted), grid, measureDuration);
    }

    private static int Snap(int value, int grid, int measureDuration)
    {
        if (grid <= 0) grid = 1;
        var snapped = (int)Math.Round((double)value / grid) * grid;
        return Math.Clamp(snapped, 0, Math.Max(0, measureDuration - grid));
    }

    private static List<Lane> ColorIntoLanes(IReadOnlyList<Unit> units, int grid)
    {
        var lanes = new List<Lane>();
        foreach (var unit in units.OrderBy(x => x.Onset).ThenByDescending(x => x.Duration).ThenBy(x => x.X))
        {
            Lane? best = null;
            var bestScore = int.MinValue;
            var bestOnset = unit.Onset;

            foreach (var lane in lanes)
            {
                var laneEnd = lane.End;
                var candidateOnset = unit.Onset;
                if (candidateOnset < laneEnd && laneEnd - candidateOnset <= grid)
                    candidateOnset = laneEnd;
                if (candidateOnset < laneEnd) continue;

                var score = 0;
                if (lane.DominantOriginalVoice == unit.OriginalVoice) score += 8;
                if (unit.Stem is not null && lane.DominantStem == unit.Stem) score += 4;
                if (candidateOnset == laneEnd) score += 5;
                score -= Math.Abs(candidateOnset - laneEnd) / Math.Max(1, grid);

                if (score > bestScore)
                {
                    best = lane;
                    bestScore = score;
                    bestOnset = candidateOnset;
                }
            }

            if (best is null)
            {
                best = new Lane();
                lanes.Add(best);
            }
            unit.Onset = bestOnset;
            best.Units.Add(unit);
        }

        return lanes;
    }

    private static void AssignVoiceNumbers(IReadOnlyList<Lane> lanes)
    {
        var used = new HashSet<int>();
        foreach (var lane in lanes
                     .OrderBy(x => x.FirstOnset)
                     .ThenBy(x => x.DominantOriginalVoice)
                     .ThenBy(x => x.DominantStem == "down" ? 1 : 0))
        {
            var preferred = lane.DominantOriginalVoice;
            if (preferred > 0 && used.Add(preferred))
            {
                lane.Voice = preferred;
                continue;
            }

            var next = 1;
            while (used.Contains(next)) next++;
            lane.Voice = next;
            used.Add(next);
        }
    }

    private static List<Lane> PreserveExistingLanes(IReadOnlyList<Unit> units)
    {
        return units
            .GroupBy(x => x.OriginalVoice)
            .OrderBy(x => x.Key)
            .Select(group =>
            {
                var lane = new Lane { Voice = group.Key };
                foreach (var unit in group.OrderBy(x => x.RawOnset).ThenBy(x => x.X))
                {
                    unit.Onset = unit.RawOnset;
                    lane.Units.Add(unit);
                }
                return lane;
            })
            .ToList();
    }

    private static void RebuildMeasure(XElement measure, IReadOnlyDictionary<int, List<Lane>> layouts)
    {
        measure.Elements("note").Remove();
        measure.Elements("backup").Remove();
        measure.Elements("forward").Remove();

        var staffCursor = 0;
        foreach (var staff in layouts.OrderBy(x => x.Key))
        {
            if (staffCursor > 0)
                measure.Add(new XElement("backup", new XElement("duration", staffCursor)));

            var laneCursor = 0;
            var firstLane = true;
            foreach (var lane in staff.Value.OrderBy(x => x.Voice))
            {
                if (!firstLane && laneCursor > 0)
                    measure.Add(new XElement("backup", new XElement("duration", laneCursor)));

                var cursor = 0;
                foreach (var unit in lane.Units.OrderBy(x => x.Onset).ThenBy(x => x.X))
                {
                    if (unit.Onset > cursor)
                        measure.Add(new XElement("forward", new XElement("duration", unit.Onset - cursor)));

                    RenderUnit(measure, unit, lane.Voice);
                    cursor = Math.Max(cursor, unit.End);
                }

                laneCursor = cursor;
                firstLane = false;
            }
            staffCursor = laneCursor;
        }
    }

    private static void RenderUnit(XElement measure, Unit unit, int voice)
    {
        foreach (var note in unit.Notes)
        {
            var voiceElement = note.Element("voice");
            if (voiceElement is null)
            {
                voiceElement = new XElement("voice", voice.ToString(CultureInfo.InvariantCulture));
                var type = note.Element("type");
                if (type is not null) type.AddBeforeSelf(voiceElement); else note.Add(voiceElement);
            }
            else voiceElement.Value = voice.ToString(CultureInfo.InvariantCulture);
            measure.Add(note);
        }
    }

    private static double? ReadX(XElement note)
    {
        var text = (string?)note.Attribute("default-x");
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static int GreatestCommonDivisor(IEnumerable<int> values)
    {
        var gcd = 0;
        foreach (var value in values)
            gcd = gcd == 0 ? value : Gcd(gcd, value);
        return gcd;
    }

    private static int Gcd(int a, int b)
    {
        a = Math.Abs(a); b = Math.Abs(b);
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    private static void UpdateTiming(XElement measure, ref int divisions, ref int beats, ref int beatType)
    {
        var attributes = measure.Element("attributes");
        if (attributes is null) return;
        var value = (int?)attributes.Element("divisions");
        if (value is > 0) divisions = value.Value;
        var time = attributes.Element("time");
        if (time is null) return;
        value = (int?)time.Element("beats");
        if (value is > 0) beats = value.Value;
        value = (int?)time.Element("beat-type");
        if (value is > 0) beatType = value.Value;
    }
}
