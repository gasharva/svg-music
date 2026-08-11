using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Restores rhythmic values of isolated flagged notes. Unlike beams, an isolated flag is a
/// compact glyph attached to the free end of a stem. We reuse the normal glyph classifier
/// (SMuFL flag8th/flag16th/flag32nd glyphs) and only use geometry to attach the recognized
/// flag to the correct stem. SVG CSS classes are deliberately ignored.
/// </summary>
public sealed class StandaloneFlagRhythmResolver
{
    private sealed record FlagInstance(double X, double Y, int Level);

    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        if (analysis.Staves.Count == 0) return;

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        var flags = analysis.Uses
            .Select(use => new { Use = use, Class = classes.GetValueOrDefault(use.SymbolId) })
            .Where(x => x.Class is not null)
            .Select(x =>
            {
                var level = DecodeFlagLevel(x.Class!.ReferenceId, x.Class.Kind);
                return level.HasValue
                    ? new FlagInstance(x.Use.X, x.Use.Y, level.Value)
                    : null;
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        if (flags.Count == 0) return;

        var notes = analysis.Events
            .Where(x => x.Kind.Equals("notehead-black", StringComparison.OrdinalIgnoreCase))
            // A beamed note already has stronger rhythmic evidence; standalone flags only
            // repair notes for which beam reconstruction found no beam at all.
            .Where(x => x.BeamCount == 0 && x.StemX.HasValue && x.StaffIndex >= 0)
            .ToList();

        var consumed = new HashSet<FlagInstance>();
        var processedStemKeys = new HashSet<(int StaffIndex, long StemBucket)>();

        foreach (var note in notes)
        {
            var staff = analysis.Staves.FirstOrDefault(x => x.Index == note.StaffIndex);
            if (staff is null || note.StemDirection is not ("up" or "down")) continue;

            // A flag belongs to a stem, not to an individual notehead. Several noteheads of a
            // chord may share the same stem, and processing them independently can consume the
            // flag on one member while leaving the chord root at quarter-note duration.
            var stemTolerance = staff.Space * .20;
            var stemBucket = (long)Math.Round(note.StemX!.Value / Math.Max(stemTolerance, .001));
            if (!processedStemKeys.Add((note.StaffIndex, stemBucket))) continue;

            var chordMembers = notes
                .Where(x => x.StaffIndex == note.StaffIndex)
                .Where(x => x.StemX.HasValue && Math.Abs(x.StemX.Value - note.StemX.Value) <= stemTolerance)
                .Where(x => x.StemDirection == note.StemDirection)
                .ToList();
            if (chordMembers.Count == 0) chordMembers = [note];

            // Use the whole chord span to locate the physical stem. A chord can span several
            // staff positions, while the stem itself is common to every member.
            var chordCenterY = chordMembers.Average(x => x.Y);
            var stem = analysis.LineSegments
                .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .18)
                .Where(x => x.Height >= staff.Space * 1.2 && x.Height <= staff.Space * 8.0)
                .Where(x => x.Top <= chordMembers.Max(n => n.Y) + staff.Space * .8 &&
                            x.Bottom >= chordMembers.Min(n => n.Y) - staff.Space * .8)
                .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
                .ThenBy(x => Math.Abs(x.CenterY - chordCenterY))
                .FirstOrDefault();
            if (stem is null) continue;

            var freeEndY = note.StemDirection == "up" ? stem.Top : stem.Bottom;

            var match = flags
                .Where(x => !consumed.Contains(x))
                .Select(flag => new
                {
                    Flag = flag,
                    Dx = Math.Abs(flag.X - note.StemX!.Value) / Math.Max(staff.Space, .001),
                    Dy = Math.Abs(flag.Y - freeEndY) / Math.Max(staff.Space, .001)
                })
                // The classifier is reliable for flag level, but up/down glyph orientation can
                // be confused by exporter transforms or close shape matches. Stem direction is
                // already known geometrically, so use the glyph only for rhythmic level and let
                // physical proximity to the free stem end decide attachment.
                .Where(x => x.Dx <= 1.6 && x.Dy <= 2.4)
                .OrderBy(x => x.Dx * .8 + x.Dy)
                .FirstOrDefault();

            if (match is null) continue;
            if (match.Dx * .8 + match.Dy > 2.55) continue;

            consumed.Add(match.Flag);

            // Rhythm is a property of the whole stem/chord. Apply the decoded flag level to every
            // notehead sharing that stem so MusicXmlVoiceLayoutPostProcessor cannot later choose a
            // quarter-duration chord root while another chord member was correctly made eighth.
            foreach (var member in chordMembers)
                ApplyLevel(member, match.Flag.Level, config.Divisions);
        }
    }

    private static int? DecodeFlagLevel(string referenceId, string kind)
    {
        var value = $"{referenceId} {kind}".ToUpperInvariant();

        // SMuFL flag glyphs come in up/down pairs. For rhythm we only care about the pair's
        // level; actual stem direction is inferred from note/stem geometry instead of trusting
        // the classifier's orientation result.
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
