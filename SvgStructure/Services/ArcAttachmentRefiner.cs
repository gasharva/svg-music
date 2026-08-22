using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>
/// Refines the ownership of an already accepted arc. ArcResolver decides whether a primitive is an
/// arc; this step decides which concrete note-note or stem-end/stem-end pair its two physical ends
/// are closest to. Keeping this in SvgStructure prevents physical geometry from leaking upward.
/// </summary>
public sealed class ArcAttachmentRefiner
{
    public IReadOnlyList<ArcResolution> Refine(
        IReadOnlyList<ArcResolution> arcs,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<StemResolution> stems)
    {
        return arcs.Select(arc => RefineOne(arc, noteHeads, stems)).ToArray();
    }

    private static ArcResolution RefineOne(
        ArcResolution arc,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<StemResolution> stems)
    {
        var part = arc.Notes.FirstOrDefault()?.PartNumber ?? arc.Stems.FirstOrDefault()?.PartNumber;
        var measure = arc.Notes.FirstOrDefault()?.MeasureNumber ?? arc.Stems.FirstOrDefault()?.MeasureNumber;
        if (part is null || measure is null)
            return arc;

        var scopedNotes = noteHeads
            .Where(x => x.PartNumber == part && x.MeasureNumber == measure)
            .ToArray();
        var scopedStems = stems
            .Where(x => x.PartNumber == part && x.MeasureNumber == measure)
            .ToArray();

        var notePair = BestNotePair(arc.LeftEndpoint, arc.RightEndpoint, scopedNotes);
        var stemPair = BestStemPair(arc.LeftEndpoint, arc.RightEndpoint, scopedStems);

        if (notePair is null && stemPair is null)
            return arc;

        if (stemPair is not null && (notePair is null || stemPair.Score < notePair.Score))
        {
            return arc with
            {
                Notes = Array.Empty<NoteHeadResolution>(),
                Stems = new[] { stemPair.Left, stemPair.Right }
            };
        }

        return arc with
        {
            Notes = new[] { notePair!.Left, notePair.Right },
            Stems = Array.Empty<StemResolution>()
        };
    }

    private static NotePair? BestNotePair(PointD left, PointD right, IReadOnlyList<NoteHeadResolution> notes)
    {
        NotePair? best = null;
        foreach (var l in notes)
        foreach (var r in notes)
        {
            if (ReferenceEquals(l, r))
                continue;
            var score = DistanceToRect(left, l.PhysicalBounds) + DistanceToRect(right, r.PhysicalBounds);
            if (best is null || score < best.Score)
                best = new NotePair(l, r, score);
        }
        return best;
    }

    private static StemPair? BestStemPair(PointD left, PointD right, IReadOnlyList<StemResolution> stems)
    {
        StemPair? best = null;
        foreach (var l in stems)
        foreach (var r in stems)
        {
            if (ReferenceEquals(l, r))
                continue;
            var score = DistanceToStemEnd(left, l) + DistanceToStemEnd(right, r);
            if (best is null || score < best.Score)
                best = new StemPair(l, r, score);
        }
        return best;
    }

    private static double DistanceToStemEnd(PointD point, StemResolution stem)
    {
        var top = new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Top);
        var bottom = new PointD(stem.PhysicalBounds.CenterX, stem.PhysicalBounds.Bottom);
        return Math.Min(Distance(point, top), Distance(point, bottom));
    }

    private static double DistanceToRect(PointD point, RectD rect)
    {
        var dx = point.X < rect.Left ? rect.Left - point.X : point.X > rect.Right ? point.X - rect.Right : 0;
        var dy = point.Y < rect.Top ? rect.Top - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Distance(PointD a, PointD b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private sealed record NotePair(NoteHeadResolution Left, NoteHeadResolution Right, double Score);
    private sealed record StemPair(StemResolution Left, StemResolution Right, double Score);
}
