using MusicStructure;
using SvgStructure.Models;

namespace SvgStructure.Services;

internal static class SvgToMusicStructureAdapter
{
    public static MusicStructureInput Convert(SvgStructureResolution source)
    {
        var notes = source.NoteHeads.Select(head =>
        {
            var stem = source.Stems.FirstOrDefault(x => x.AttachedNotes.Contains(head));
            var accidental = source.Accidentals.FirstOrDefault(x => x.Note is not null && x.Note.Equals(head));
            var dotCount = source.Dots.Count(x => x.Note is not null && x.Note.Equals(head));
            var flag = stem is null ? null : source.NoteFlags.FirstOrDefault(x => x.Stem.Equals(stem));

            var beams = stem is null
                ? Array.Empty<MusicBeam>()
                : source.Beams
                    .Where(x => x.Stems.Contains(stem))
                    .Select(x => ToBeam(x, stem))
                    .Where(x => x is not null)
                    .Select(x => x!)
                    .OrderBy(x => x.Level)
                    .ToArray();

            var logicalX = head.LogicalBounds.Left is { } left && head.LogicalBounds.Right is { } right
                ? (left + right) / 2.0
                : head.LogicalBounds.Left ?? head.LogicalBounds.Right;

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

    private static MusicBeam? ToBeam(BeamResolution beam, StemResolution stem)
    {
        var ordered = beam.Stems.OrderBy(x => x.PhysicalBounds.CenterX).ToArray();
        var index = Array.FindIndex(ordered, x => x.Equals(stem));
        if (index < 0 || ordered.Length < 2)
            return null;
        var position = index == 0 ? MusicBeamPosition.Begin : index == ordered.Length - 1 ? MusicBeamPosition.End : MusicBeamPosition.Continue;
        return new MusicBeam(beam.Level, position);
    }

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
