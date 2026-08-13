using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Restores rhythmic values of isolated flagged notes. Prefer normal SMuFL classification when it
/// is available, but real PDF-derived SVGs often outline a different music font and the flag glyph
/// stays semantically unknown. In that case classify only compact painted shapes sitting at the
/// free end of a real stem, using staff-relative geometry rather than obfuscated symbol ids.
/// </summary>
public sealed class StandaloneFlagRhythmResolver
{
    private sealed record FlagInstance(
        string Key,
        double X,
        double Y,
        double Width,
        double Height,
        int Points,
        int Contours,
        int? ClassifiedLevel,
        string Kind);

    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        if (analysis.Staves.Count == 0) return;

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        var flags = analysis.PageGeometry
            .Where(x => x.SourceKind == "use" && !string.IsNullOrWhiteSpace(x.SourceSymbolId))
            .Select(x =>
            {
                var points = x.Geometry.Contours.SelectMany(c => c).ToArray();
                if (points.Length == 0) return null;

                var left = points.Min(p => p.X);
                var right = points.Max(p => p.X);
                var top = points.Min(p => p.Y);
                var bottom = points.Max(p => p.Y);
                var cls = classes.GetValueOrDefault(x.SourceSymbolId!);
                var classifiedLevel = cls is null ? null : DecodeFlagLevel(cls.ReferenceId, cls.Kind);

                return new FlagInstance(
                    x.InstanceId,
                    (left + right) / 2,
                    (top + bottom) / 2,
                    right - left,
                    bottom - top,
                    points.Length,
                    x.Geometry.Contours.Count,
                    classifiedLevel,
                    cls?.Kind ?? "");
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        var notes = analysis.Events
            .Where(x => x.Kind.Equals("notehead-black", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.BeamCount == 0 && x.StemX.HasValue && x.StaffIndex >= 0)
            .ToList();

        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var processedStemKeys = new HashSet<(int StaffIndex, long StemBucket)>();

        foreach (var note in notes)
        {
            var staff = analysis.Staves.FirstOrDefault(x => x.Index == note.StaffIndex);
            if (staff is null || note.StemDirection is not ("up" or "down")) continue;

            var stemTolerance = staff.Space * .20;
            var stemBucket = (long)Math.Round(note.StemX!.Value / Math.Max(stemTolerance, .001));
            if (!processedStemKeys.Add((note.StaffIndex, stemBucket))) continue;

            var chordMembers = notes
                .Where(x => x.StaffIndex == note.StaffIndex)
                .Where(x => x.StemX.HasValue && Math.Abs(x.StemX.Value - note.StemX.Value) <= stemTolerance)
                .Where(x => x.StemDirection == note.StemDirection)
                .ToList();
            if (chordMembers.Count == 0) chordMembers = [note];

            var chordCenterY = chordMembers.Average(x => x.Y);
            var stem = analysis.LineSegments
                .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .20)
                .Where(x => x.Height >= staff.Space * 1.1 && x.Height <= staff.Space * 11.2)
                .Where(x => x.Top <= chordMembers.Max(n => n.Y) + staff.Space * .9 &&
                            x.Bottom >= chordMembers.Min(n => n.Y) - staff.Space * .9)
                .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
                .ThenBy(x => Math.Abs(x.CenterY - chordCenterY))
                .FirstOrDefault();
            if (stem is null) continue;

            var freeEndY = note.StemDirection == "up" ? stem.Top : stem.Bottom;

            var match = flags
                .Where(x => !consumed.Contains(x.Key))
                .Select(flag =>
                {
                    var level = flag.ClassifiedLevel ?? DecodeGeometricFlagLevel(flag, staff.Space);
                    if (!level.HasValue) return null;

                    var dx = (flag.X - note.StemX!.Value) / Math.Max(staff.Space, .001);
                    var dy = (flag.Y - freeEndY) / Math.Max(staff.Space, .001);

                    var sideOk = note.StemDirection == "up"
                        ? dx is >= -.30 and <= 1.85 && dy is >= .10 and <= 2.55
                        : dx is >= -1.85 and <= .30 && dy is >= -2.55 and <= -.10;
                    if (!sideOk) return null;

                    var score = Math.Abs(dx) * .70 + Math.Abs(dy);
                    return new { Flag = flag, Level = level.Value, Score = score };
                })
                .Where(x => x is not null)
                .Select(x => x!)
                .OrderBy(x => x.Score)
                .FirstOrDefault();

            if (match is null || match.Score > 2.9) continue;
            consumed.Add(match.Flag.Key);

            foreach (var member in chordMembers)
                ApplyLevel(member, match.Level, config.Divisions);
        }
    }

    private static int? DecodeGeometricFlagLevel(FlagInstance flag, double staffSpace)
    {
        if (!string.IsNullOrWhiteSpace(flag.Kind) &&
            !flag.Kind.Equals("smufl-unknown", StringComparison.OrdinalIgnoreCase))
            return null;

        var width = flag.Width / Math.Max(staffSpace, .001);
        var height = flag.Height / Math.Max(staffSpace, .001);

        if (width is >= .72 and <= 1.48 && height is >= 2.05 and <= 3.20 &&
            flag.Points is >= 60 and <= 360 && flag.Contours <= 2)
            return 1;

        // Some outlined fonts draw the two hooks packed vertically: the real page-2 glyph is
        // about 2.09 x 2.81 staff-spaces with two contours and ~585 points. The old 2.90sp lower
        // height bound missed it by less than a tenth of a staff-space. Complexity + two contours
        // and the verified stem-end relation remain strong guards against ordinary text glyphs.
        if (width is >= 1.45 and <= 2.75 && height is >= 2.55 and <= 4.35 &&
            flag.Points >= 220 && flag.Contours >= 2)
            return 2;

        return null;
    }

    private static int? DecodeFlagLevel(string referenceId, string kind)
    {
        var value = $"{referenceId} {kind}".ToUpperInvariant();
        var mappings = new (string[] Codes, int Level)[]
        {
            (["E240", "E241"], 1),
            (["E242", "E243"], 2),
            (["E244", "E245"], 3),
            (["E246", "E247"], 4),
            (["E248", "E249"], 5)
        };

        foreach (var mapping in mappings)
            if (mapping.Codes.Any(code => value.Contains(code, StringComparison.Ordinal)))
                return mapping.Level;

        return null;
    }

    private static void ApplyLevel(RecognizedEvent note, int level, int divisions)
    {
        var type = level switch
        {
            1 => "eighth",
            2 => "16th",
            3 => "32nd",
            4 => "64th",
            _ => "128th"
        };

        var baseDuration = level switch
        {
            1 => Math.Max(1, divisions / 2),
            2 => Math.Max(1, divisions / 4),
            3 => Math.Max(1, divisions / 8),
            4 => Math.Max(1, divisions / 16),
            _ => Math.Max(1, divisions / 32)
        };

        note.Type = type;
        note.Duration = note.Dotted ? baseDuration * 3 / 2 : baseDuration;
    }
}
