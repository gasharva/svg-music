using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Re-evaluates augmentation-dot attachments after final note/staff geometry is known.
/// In close opposing voices two noteheads can be only half a staff-space apart while their
/// horizontal offsets differ substantially. A dot encodes the note row much more strongly than
/// the closest X distance, so Y/staff position wins and X only breaks ties.
/// </summary>
public sealed class AugmentationDotGeometryResolver
{
    public void Resolve(AnalysisResult analysis, RecognitionConfig config)
    {
        if (analysis.Staves.Count == 0) return;

        var classes = analysis.Classifications.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        var notes = analysis.Events
            .Where(x => x.Step is not null || x.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var use in analysis.Uses)
        {
            if (!classes.TryGetValue(use.SymbolId, out var cls) || cls.Kind != "augmentation-dot")
                continue;

            var staff = analysis.Staves
                .Where(s => use.X >= s.Left - s.Space * 3 && use.X <= s.Right + s.Space * 3)
                .Select(s => new { Staff = s, Distance = Math.Abs(use.Y - s.Center) / Math.Max(s.Space, .001) })
                .Where(x => x.Distance <= 4.0)
                .OrderBy(x => x.Distance)
                .Select(x => x.Staff)
                .FirstOrDefault();
            if (staff is null) continue;

            var target = notes
                .Where(x => x.StaffIndex == staff.Index)
                .Where(x => x.X < use.X)
                .Where(x => use.X - x.X <= staff.Space * Math.Max(config.MaxAttachmentDistanceInSpaces, 3.0))
                .Where(x => Math.Abs(x.Y - use.Y) <= staff.Space * 1.25)
                .Select(x => new
                {
                    Note = x,
                    YDelta = Math.Abs(x.Y - use.Y),
                    XDelta = use.X - x.X
                })
                .OrderBy(x => x.YDelta)
                .ThenBy(x => x.XDelta)
                .Select(x => x.Note)
                .FirstOrDefault();

            if (target is null || target.Dotted) continue;
            target.Dotted = true;
            target.Duration = checked(target.Duration * 3 / 2);
        }
    }
}
