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
    private sealed record FlagInstance(double X, double Y, int Level, string Direction);

    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        if (analysis.Staves.Count == 0) return;

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        var flags = analysis.Uses
            .Select(use => new { Use = use, Class = classes.GetValueOrDefault(use.SymbolId) })
            .Where(x => x.Class is not null)
            .Select(x =>
            {
                var decoded = DecodeFlag(x.Class!.ReferenceId, x.Class.Kind);
                return decoded is null
                    ? null
                    : new FlagInstance(x.Use.X, x.Use.Y, decoded.Value.Level, decoded.Value.Direction);
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

        foreach (var note in notes)
        {
            var staff = analysis.Staves.FirstOrDefault(x => x.Index == note.StaffIndex);
            if (staff is null || note.StemDirection is not ("up" or "down")) continue;

            var stem = analysis.LineSegments
                .Where(x => Math.Abs(x.CenterX - note.StemX!.Value) <= staff.Space * .18)
                .Where(x => x.Height >= staff.Space * 1.2 && x.Height <= staff.Space * 8.0)
                .Where(x => x.Top <= note.Y + staff.Space * .8 && x.Bottom >= note.Y - staff.Space * .8)
                .OrderBy(x => Math.Abs(x.CenterX - note.StemX!.Value))
                .ThenBy(x => Math.Abs(x.CenterY - note.Y))
                .FirstOrDefault();
            if (stem is null) continue;

            var freeEndY = note.StemDirection == "up" ? stem.Top : stem.Bottom;

            var match = flags
                .Where(x => !consumed.Contains(x))
                .Where(x => x.Direction == note.StemDirection)
                .Select(flag => new
                {
                    Flag = flag,
                    Dx = Math.Abs(flag.X - note.StemX!.Value) / Math.Max(staff.Space, .001),
                    Dy = Math.Abs(flag.Y - freeEndY) / Math.Max(staff.Space, .001)
                })
                // Flag origins differ slightly between exporters/fonts, so keep a generous local
                // window but require the combined normalized distance to be close to the stem end.
                .Where(x => x.Dx <= 1.6 && x.Dy <= 2.4)
                .OrderBy(x => x.Dx * .8 + x.Dy)
                .FirstOrDefault();

            if (match is null) continue;
            if (match.Dx * .8 + match.Dy > 2.55) continue;

            consumed.Add(match.Flag);
            ApplyLevel(note, match.Flag.Level, config.Divisions);
        }
    }

    private static (int Level, string Direction)? DecodeFlag(string referenceId, string kind)
    {
        var value = $"{referenceId} {kind}".ToUpperInvariant();

        // SMuFL Standard Glyph Names / codepoints:
        // E240/E241 = 8th up/down, E242/E243 = 16th, E244/E245 = 32nd.
        // Supporting the following pairs costs nothing and gives us 64th/128th as well.
        var mappings = new (string Code, int Level, string Direction)[]
        {
            ("E240", 1, "up"), ("E241", 1, "down"),
            ("E242", 2, "up"), ("E243", 2, "down"),
            ("E244", 3, "up"), ("E245", 3, "down"),
            ("E246", 4, "up"), ("E247", 4, "down"),
            ("E248", 5, "up"), ("E249", 5, "down")
        };

        foreach (var mapping in mappings)
            if (value.Contains(mapping.Code, StringComparison.Ordinal))
                return (mapping.Level, mapping.Direction);

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
