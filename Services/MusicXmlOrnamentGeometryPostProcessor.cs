using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Recovers compact high ornaments that survive as outlined vector glyphs but are not yet mapped
/// semantically by the reference catalog. The current real-source shape is a turn/gruppetto-like
/// ornament, not the textual "tr" trill mark. Future recognized variants can map to inverted/slashed
/// turn elements here without changing the geometry-to-note attachment logic.
/// </summary>
public sealed class MusicXmlOrnamentGeometryPostProcessor
{
    public void Apply(string path, AnalysisResult analysis)
    {
        var bindings = BindNotes(path, analysis);
        if (bindings.Count == 0) return;

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);

        foreach (var glyph in analysis.PageGeometry)
        {
            if (glyph.Geometry.Contours.Count != 1) continue;
            var contour = glyph.Geometry.Contours[0];
            if (contour.Count < 120) continue;

            var left = contour.Min(x => x.X);
            var right = contour.Max(x => x.X);
            var top = contour.Min(x => x.Y);
            var bottom = contour.Max(x => x.Y);
            var width = right - left;
            var height = bottom - top;
            if (width <= 0 || height <= 0) continue;

            var centerX = (left + right) / 2;
            var centerY = (top + bottom) / 2;
            var staff = analysis.Staves
                .Where(s => centerX >= s.Left - s.Space * 3 && centerX <= s.Right + s.Space * 3)
                .Select(s => new { Staff = s, Distance = (s.Top - centerY) / Math.Max(s.Space, .001) })
                .Where(x => x.Distance is >= 2.0 and <= 10.5)
                .OrderBy(x => x.Distance)
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            var widthSp = width / staff.Space;
            var heightSp = height / staff.Space;
            if (widthSp is < 1.8 or > 3.2 || heightSp is < .55 or > 1.35) continue;
            if (widthSp / Math.Max(heightSp, .001) < 1.8) continue;

            SymbolClassification? cls = null;
            if (glyph.SourceSymbolId is not null)
                classes.TryGetValue(glyph.SourceSymbolId, out cls);

            if (cls is not null &&
                !cls.Kind.Equals("smufl-unknown", StringComparison.OrdinalIgnoreCase) &&
                !cls.Kind.Contains("trill", StringComparison.OrdinalIgnoreCase) &&
                !cls.Kind.Contains("turn", StringComparison.OrdinalIgnoreCase) &&
                !cls.Kind.Contains("ornament", StringComparison.OrdinalIgnoreCase))
                continue;

            var target = bindings
                .Where(x => x.Event.StaffIndex == staff.Index && x.Event.Step is not null)
                .Where(x => Math.Abs(x.Event.X - centerX) <= staff.Space * 1.75)
                .OrderBy(x => Math.Abs(x.Event.X - centerX))
                .FirstOrDefault();
            if (target is null) continue;

            var notations = target.Element.Element("notations");
            if (notations is null)
            {
                notations = new XElement("notations");
                target.Element.Add(notations);
            }

            var ornaments = notations.Element("ornaments");
            if (ornaments is null)
            {
                ornaments = new XElement("ornaments");
                notations.Add(ornaments);
            }

            var elementName = OrnamentElementName(cls);
            if (ornaments.Element(elementName) is null)
                ornaments.Add(new XElement(elementName, new XAttribute("placement", "above")));
        }

        bindings.Document.Save(path);
    }

    private static string OrnamentElementName(SymbolClassification? cls)
    {
        var semantic = $"{cls?.Kind} {cls?.ReferenceId}";
        if (semantic.Contains("inverted", StringComparison.OrdinalIgnoreCase) &&
            semantic.Contains("turn", StringComparison.OrdinalIgnoreCase))
            return "inverted-turn";

        // A conventional horizontal turn (gruppetto) is the safe geometry fallback for the
        // compact S-shaped ornament on the real score. Do not emit <trill-mark>: MuseScore renders
        // that as the letters "tr", which is visibly a different notation symbol.
        return "turn";
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
        var staves = analysis.Staves.OrderBy(x => x.Center).ToList();
        if (staves.Count == 0) return result;

        var groups = new List<List<Staff>>();
        for (var i = 0; i < staves.Count; i += 2)
            groups.Add(i + 1 < staves.Count ? [staves[i], staves[i + 1]] : [staves[i]]);

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
}
