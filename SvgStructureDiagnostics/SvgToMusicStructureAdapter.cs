using MusicStructure;
using SvgStructure.Models;

namespace SvgStructure.Services;

internal static class SvgToMusicStructureAdapter
{
    public static MusicStructureInput Convert(SvgStructureResolution source)
    {
        var stemKeys = source.Stems
            .Select((stem, index) => (stem, key: $"stem:{index}"))
            .ToDictionary(x => x.stem, x => x.key);

        var stems = source.Stems.Select(stem => new RecognizedStemInput(
            stemKeys[stem],
            stem.PartNumber,
            stem.MeasureNumber,
            stem.Direction == StemDirection.Up ? MusicStemDirection.Up : MusicStemDirection.Down,
            stem.AttachedNotes.Select(NoteKey).ToArray())).ToArray();

        var beams = source.Beams.Select(beam => new RecognizedBeamInput(
            beam.MeasureNumber,
            beam.Level,
            beam.Stems.Where(stemKeys.ContainsKey).Select(stem => stemKeys[stem]).ToArray())).ToArray();

        var flags = source.NoteFlags
            .Where(flag => stemKeys.ContainsKey(flag.Stem))
            .Select(flag => new RecognizedFlagInput(
                flag.Stem.PartNumber,
                flag.Stem.MeasureNumber,
                flag.Denominator,
                stemKeys[flag.Stem]))
            .ToArray();

        var notes = source.NoteHeads.Select(head =>
        {
            var accidental = source.Accidentals.FirstOrDefault(x => x.Note is not null && x.Note.Equals(head));
            var dotCount = source.Dots.Count(x => x.Note is not null && x.Note.Equals(head));

            return new RecognizedNoteInput(
                NoteKey(head),
                head.PartNumber,
                head.MeasureNumber,
                CenterX(head),
                head.Pitch,
                head.IsFilled,
                accidental is null ? null : ToAccidental(accidental.Kind),
                dotCount);
        }).ToArray();

        return new MusicStructureInput(
            source.Structure.Parts.Count,
            source.Structure.Measures.Select(x => x.Number).ToArray(),
            source.Structure.Measures.Where(x => x.StartsNewSystem).Select(x => x.Number).ToHashSet(),
            notes,
            stems,
            beams,
            flags);
    }

    private static string NoteKey(NoteHeadResolution head) => $"note:{head.Id}";

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
