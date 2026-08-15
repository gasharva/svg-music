using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Extracts stem-like vertical edges from compound standalone painted paths.
/// PDF-derived SVG exporters can merge a beam and its end stem into one contour, so the contour
/// itself is wide and fails the ordinary narrow-contour stem test even though one of its edges is
/// a perfectly good stem. Reusable glyphs are deliberately excluded here to avoid turning clefs,
/// accidentals and text outlines into false stems.
/// </summary>
public sealed class CompoundVerticalStrokeExtractor
{
    public IReadOnlyList<SvgLineSegment> Extract(
        IReadOnlyList<SvgPageGeometry> pageGeometry,
        IReadOnlyList<Staff> staves,
        IReadOnlyList<SvgLineSegment> existing)
    {
        if (staves.Count == 0) return [];

        var result = new List<SvgLineSegment>();

        foreach (var instance in pageGeometry.Where(x => x.SourceKind == "path"))
        {
            foreach (var contour in instance.Geometry.Contours)
            {
                if (contour.Count < 3) continue;

                var left = contour.Min(p => p.X);
                var right = contour.Max(p => p.X);
                var top = contour.Min(p => p.Y);
                var bottom = contour.Max(p => p.Y);
                var centerY = (top + bottom) / 2;

                var staff = staves
                    .Where(s => right >= s.Left - s.Space * 3 && left <= s.Right + s.Space * 3)
                    .OrderBy(s => Math.Abs(centerY - s.Center) / Math.Max(s.Space, .001))
                    .FirstOrDefault();
                if (staff is null) continue;

                var space = staff.Space;
                var contourWidth = right - left;

                // Only inspect compound structural shapes. A standalone narrow contour is already
                // handled by SvgParser.ReadLineSegments; a wide contour with a long edge is the
                // exporter pattern we are after here (beam + end stem, ledger + stem, etc.).
                if (contourWidth < space * 1.35) continue;

                for (var i = 0; i < contour.Count; i++)
                {
                    var a = contour[i];
                    var b = contour[(i + 1) % contour.Count];
                    var dx = Math.Abs(b.X - a.X);
                    var dy = Math.Abs(b.Y - a.Y);

                    // Sloped beamed passages in the real PDF-derived score contain stems up to
                    // roughly 9.6 staff spaces long. Keep a generous structural ceiling here;
                    // the relation pass later requires one end of the line to touch a notehead.
                    if (dy < space * 1.0 || dy > space * 11.0) continue;
                    if (dx > Math.Max(space * .08, dy * .08)) continue;

                    var x = (a.X + b.X) / 2;
                    var edgeDistance = Math.Min(Math.Abs(x - left), Math.Abs(x - right));
                    if (edgeDistance > space * .28) continue;

                    var candidate = new SvgLineSegment(
                        x,
                        Math.Min(a.Y, b.Y),
                        x,
                        Math.Max(a.Y, b.Y),
                        "compound-path-edge");

                    if (IsDuplicate(candidate, existing, space) || IsDuplicate(candidate, result, space))
                        continue;

                    result.Add(candidate);
                }
            }
        }

        return result;
    }

    private static bool IsDuplicate(
        SvgLineSegment candidate,
        IEnumerable<SvgLineSegment> existing,
        double space) => existing.Any(line =>
            Math.Abs(line.CenterX - candidate.CenterX) <= space * .10 &&
            Math.Abs(line.Top - candidate.Top) <= space * .25 &&
            Math.Abs(line.Bottom - candidate.Bottom) <= space * .25);
}
