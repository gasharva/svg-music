using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Restores grace-note semantics that are not encoded in the notehead glyph identity itself.
/// MuseScore scales grace noteheads, stems and beams as a group, so the same black-notehead
/// reference is used at about 70% of the normal engraved size. This pass uses the raw SVG path
/// bounds to recognize that scale, removes timed duration, and restores the visible beam levels.
/// SVG CSS classes are intentionally ignored.
/// </summary>
public sealed class MusicXmlGraceNotePostProcessor
{
    private sealed record NoteBinding(XElement Element, RecognizedEvent Event, Staff Staff);
    private sealed record BeamStrip(double Left, double Right, double Top, double Bottom)
    {
        public double CenterY => (Top + Bottom) / 2;
    }

    public void Apply(string path, AnalysisResult analysis)
    {
        if (analysis.Staves.Count == 0 || analysis.DirectPaths.Count == 0) return;

        var document = XDocument.Load(path);
        var groups = BuildStaffGroups(analysis);
        if (groups.Count == 0) return;

        var pathBounds = analysis.DirectPaths
            .Select(x => new { Path = x, Bounds = Bounds(x) })
            .ToDictionary(x => x.Path.SymbolId, x => x.Bounds);

        var beamStrips = FindCompactBeamStrips(analysis);
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
            var bindings = new List<NoteBinding>();

            foreach (var noteElement in measure.Elements("note"))
            {
                var staffNumber = (int?)noteElement.Element("staff") ?? 1;
                if (staffNumber < 1 || staffNumber > group.Count) continue;

                var staff = group[staffNumber - 1];
                if (!queues.TryGetValue(staff.Index, out var queue) || queue.Count == 0) continue;
                bindings.Add(new NoteBinding(noteElement, queue.Dequeue(), staff));
            }

            foreach (var binding in bindings)
            {
                if (!IsScaledGraceHead(binding, pathBounds)) continue;
                MarkGrace(binding);
            }

            foreach (var staffBindings in bindings
                         .Where(x => x.Element.Element("grace") is not null)
                         .GroupBy(x => x.Staff.Index))
            {
                RestoreGraceBeams(staffBindings.OrderBy(x => EffectiveX(x.Event)).ToList(), beamStrips);
            }
        }

        document.Save(path);
    }

    private static bool IsScaledGraceHead(
        NoteBinding binding,
        IReadOnlyDictionary<string, (double Left, double Top, double Right, double Bottom)> pathBounds)
    {
        var evt = binding.Event;
        if (!evt.Kind.Equals("notehead-black", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(evt.SourceSymbolId)) return false;
        if (!pathBounds.TryGetValue(evt.SourceSymbolId, out var box)) return false;

        var width = box.Right - box.Left;
        var height = box.Bottom - box.Top;

        // Normal black heads in Yellow Leaves are ~1.30 x 1.06 staff spaces. Grace heads are
        // exported at 70%, ~0.91 x 0.74 spaces. Leave a gap between the two populations instead
        // of depending on a score-specific absolute SVG size.
        return width <= binding.Staff.Space * 1.05 &&
               height <= binding.Staff.Space * .88;
    }

    private static void MarkGrace(NoteBinding binding)
    {
        var note = binding.Element;
        if (note.Element("grace") is null)
        {
            var grace = new XElement("grace");
            var first = note.Elements().FirstOrDefault();
            if (first is null) note.Add(grace); else first.AddBeforeSelf(grace);
        }

        note.Element("duration")?.Remove();
        SetType(note, "16th");

        // Grace notes occupy no metrical duration. Mutating the analysis event is deliberate:
        // MusicXmlVoiceLayoutPostProcessor runs after this pass and uses Event.Duration when it
        // computes lane lengths and backup distances.
        binding.Event.Duration = 0;
        binding.Event.Type = "16th";
    }

    private static void RestoreGraceBeams(IReadOnlyList<NoteBinding> graceNotes, IReadOnlyList<BeamStrip> strips)
    {
        if (graceNotes.Count < 2) return;

        for (var i = 0; i + 1 < graceNotes.Count; i++)
        {
            var left = graceNotes[i];
            var right = graceNotes[i + 1];
            var space = (left.Staff.Space + right.Staff.Space) / 2;
            var leftX = EffectiveX(left.Event);
            var rightX = EffectiveX(right.Event);

            if (rightX - leftX > space * 2.0) continue;

            var shared = strips
                .Where(x => leftX >= x.Left - space * .12 && leftX <= x.Right + space * .12)
                .Where(x => rightX >= x.Left - space * .12 && rightX <= x.Right + space * .12)
                .Where(x => IsOnStemSide(left.Event, x.CenterY, space) &&
                            IsOnStemSide(right.Event, x.CenterY, space))
                .OrderBy(x => x.CenterY)
                .ToList();

            if (shared.Count == 0) continue;
            var beamCount = Math.Min(2, shared.Count);
            var type = beamCount >= 2 ? "16th" : "eighth";

            ApplyBeamState(left, "begin", beamCount, type);
            ApplyBeamState(right, "end", beamCount, type);
        }
    }

    private static void ApplyBeamState(NoteBinding binding, string value, int beamCount, string type)
    {
        binding.Event.BeamCount = beamCount;
        binding.Event.BeamValue = value;
        binding.Event.Type = type;
        binding.Event.Duration = 0;

        var note = binding.Element;
        SetType(note, type);
        note.Elements("beam").Remove();

        var staff = note.Element("staff");
        XElement? insertAfter = note.Element("stem") ?? note.Element("type") ?? note.Element("voice");
        for (var level = 1; level <= beamCount; level++)
        {
            var beam = new XElement("beam", new XAttribute("number", level), value);
            if (insertAfter is not null)
            {
                insertAfter.AddAfterSelf(beam);
                insertAfter = beam;
            }
            else if (staff is not null)
            {
                staff.AddBeforeSelf(beam);
                insertAfter = beam;
            }
            else
            {
                note.Add(beam);
                insertAfter = beam;
            }
        }
    }

    private static bool IsOnStemSide(RecognizedEvent note, double beamY, double staffSpace)
    {
        var distance = Math.Abs(beamY - note.Y);
        if (distance > staffSpace * 4.0) return false;

        return note.StemDirection switch
        {
            "up" => beamY < note.Y + staffSpace * .25,
            "down" => beamY > note.Y - staffSpace * .25,
            _ => true
        };
    }

    private static List<BeamStrip> FindCompactBeamStrips(AnalysisResult analysis)
    {
        var result = new List<BeamStrip>();

        foreach (var path in analysis.DirectPaths)
        {
            var points = path.Geometry.Contours.SelectMany(x => x).ToArray();
            if (points.Length is < 3 or > 14) continue;

            var left = points.Min(x => x.X);
            var right = points.Max(x => x.X);
            var top = points.Min(x => x.Y);
            var bottom = points.Max(x => x.Y);
            var width = right - left;
            var height = bottom - top;

            var staff = analysis.Staves
                .Where(s => right >= s.Left - s.Space && left <= s.Right + s.Space)
                .OrderBy(s => Math.Abs((top + bottom) / 2 - s.Center))
                .FirstOrDefault();
            if (staff is null) continue;

            // These are deliberately below the normal beam detector's 1.4-space width floor:
            // a two-note 70%-scaled grace beam in measure 15 is only ~1.29 spaces wide.
            if (width < staff.Space * .60 || width > staff.Space * 3.0) continue;
            if (height > staff.Space * 1.15) continue;
            if (width / Math.Max(height, staff.Space * .05) < 1.5) continue;

            var area = path.Geometry.Contours.Sum(PolygonArea);
            var longAxis = Math.Sqrt(width * width + height * height);
            var thickness = area / Math.Max(longAxis, .001);
            if (thickness < staff.Space * .04 || thickness > staff.Space * .55) continue;

            result.Add(new BeamStrip(left, right, top, bottom));
        }

        return result;
    }

    private static void SetType(XElement note, string type)
    {
        var typeElement = note.Element("type");
        if (typeElement is null)
        {
            typeElement = new XElement("type", type);
            var stem = note.Element("stem");
            if (stem is not null) stem.AddBeforeSelf(typeElement); else note.Add(typeElement);
        }
        else
        {
            typeElement.Value = type;
        }
    }

    private static double EffectiveX(RecognizedEvent evt) => evt.StemX ?? evt.X;

    private static (double Left, double Top, double Right, double Bottom) Bounds(SvgDirectPath path)
    {
        var points = path.Geometry.Contours.SelectMany(x => x).ToArray();
        return (
            points.Min(x => x.X),
            points.Min(x => x.Y),
            points.Max(x => x.X),
            points.Max(x => x.Y));
    }

    private static double PolygonArea(IReadOnlyList<PointD> contour)
    {
        if (contour.Count < 3) return 0;
        double twiceArea = 0;
        for (var i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            twiceArea += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(twiceArea) / 2;
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
