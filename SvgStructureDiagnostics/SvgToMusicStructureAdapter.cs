using MusicStructure;
using SvgStructure.Models;

namespace SvgStructure.Services;

internal static class SvgToMusicStructureAdapter
{
    private const double SharedStemXTolerance = 0.40;

    public static MusicStructureInput Convert(SvgStructureResolution source)
    {
        var notes = source.NoteHeads.Select(head =>
        {
            var stem = FindStem(source, head);
            var accidental = source.Accidentals.FirstOrDefault(x => x.Note is not null && x.Note.Equals(head));
            var dotCount = source.Dots.Count(x => x.Note is not null && x.Note.Equals(head));
            var flag = stem is null ? null : source.NoteFlags.FirstOrDefault(x => x.Stem.Equals(stem));
            var beams = stem is null ? Array.Empty<MusicBeam>() : BuildSemanticBeams(source, stem);

            var logicalX = CenterX(head);

            return new RecognizedNoteInput(
                head.PartNumber,
                head.MeasureNumber,
                logicalX,
                head.Pitch,
                head.IsFilled,
                stem is null ? null : stem.Direction == StemDirection.Up ? MusicStemDirection.Up : MusicStemDirection.Down,
                accidental is null ? null : ToAccidental(accidental.Kind),
                dotCount,
                flag?.Denominator,
                beams);
        }).ToArray();

        return new MusicStructureInput(
            source.Structure.Parts.Count,
            source.Structure.Measures.Select(x => x.Number).ToArray(),
            source.Structure.Measures.Where(x => x.StartsNewSystem).Select(x => x.Number).ToHashSet(),
            notes);
    }

    private static StemResolution? FindStem(SvgStructureResolution source, NoteHeadResolution head)
    {
        var direct = source.Stems.FirstOrDefault(x => x.AttachedNotes.Contains(head));
        if (direct is not null)
            return direct;

        // In a chord one physical stem may touch only the outer note head geometrically. At the
        // semantic boundary all note heads on the same logical X share that stem. This inference is
        // intentionally done here, while SVG geometry is still available; MusicStructure never sees it.
        var headX = CenterX(head);
        if (headX is null)
            return null;

        return source.Stems
            .Where(x => x.PartNumber == head.PartNumber && x.MeasureNumber == head.MeasureNumber)
            .Select(x => new
            {
                Stem = x,
                X = x.AttachedNotes.Select(CenterX).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(double.NaN).Average()
            })
            .Where(x => !double.IsNaN(x.X) && Math.Abs(x.X - headX.Value) <= SharedStemXTolerance)
            .OrderBy(x => Math.Abs(x.X - headX.Value))
            .ThenBy(x => VerticalDistance(head, x.Stem))
            .Select(x => x.Stem)
            .FirstOrDefault();
    }

    private static IReadOnlyList<MusicBeam> BuildSemanticBeams(SvgStructureResolution source, StemResolution stem)
    {
        var touching = source.Beams
            .Where(x => x.Stems.Contains(stem))
            .OrderBy(x => BeamDistanceFromFreeStemEnd(x, stem))
            .ThenBy(x => x.PhysicalBounds.CenterY)
            .ToArray();

        if (touching.Length == 0)
            return Array.Empty<MusicBeam>();

        // BeamResolver levels are useful during geometric recognition, but several visually distinct
        // secondary beams may temporarily carry the same level. At the semantic boundary the levels
        // are simply the ordered beam depth at this stem: 1, 2, 3, ... .
        var result = new List<MusicBeam>();
        for (var i = 0; i < touching.Length; i++)
        {
            var position = BeamPosition(touching[i], stem);
            if (position is null)
                continue;
            result.Add(new MusicBeam(i + 1, position.Value));
        }
        return result;
    }

    private static MusicBeamPosition? BeamPosition(BeamResolution beam, StemResolution stem)
    {
        var ordered = beam.Stems
            .Distinct()
            .OrderBy(x => x.PhysicalBounds.CenterX)
            .ToArray();
        var index = Array.FindIndex(ordered, x => x.Equals(stem));
        if (index < 0)
            return null;

        // A one-stem secondary beam is a hook. We do not yet distinguish forward/backward hook in
        // MusicStructure, so preserve it as a one-note beam endpoint rather than dropping it.
        if (ordered.Length == 1)
            return MusicBeamPosition.End;

        return index == 0
            ? MusicBeamPosition.Begin
            : index == ordered.Length - 1
                ? MusicBeamPosition.End
                : MusicBeamPosition.Continue;
    }

    private static double BeamDistanceFromFreeStemEnd(BeamResolution beam, StemResolution stem)
    {
        var freeY = stem.Direction == StemDirection.Up
            ? stem.PhysicalBounds.Top
            : stem.PhysicalBounds.Bottom;
        return Math.Abs(beam.PhysicalBounds.CenterY - freeY);
    }

    private static double VerticalDistance(NoteHeadResolution head, StemResolution stem)
    {
        if (head.PhysicalBounds.Bottom < stem.PhysicalBounds.Top)
            return stem.PhysicalBounds.Top - head.PhysicalBounds.Bottom;
        if (stem.PhysicalBounds.Bottom < head.PhysicalBounds.Top)
            return head.PhysicalBounds.Top - stem.PhysicalBounds.Bottom;
        return 0;
    }

    private static double? CenterX(NoteHeadResolution head) =>
        head.LogicalBounds.Left is { } left && head.LogicalBounds.Right is { } right
            ? (left + right) / 2.0
            : head.LogicalBounds.Left ?? head.LogicalBounds.Right;

    private static MusicAccidental ToAccidental(AccidentalKind kind) => kind switch
    {
        AccidentalKind.Flat => MusicAccidental.Flat,
        AccidentalKind.Sharp => MusicAccidental.Sharp,
        AccidentalKind.Natural => MusicAccidental.Natural,
        AccidentalKind.DoubleSharp => MusicAccidental.DoubleSharp,
        AccidentalKind.DoubleFlat => MusicAccidental.DoubleFlat,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
