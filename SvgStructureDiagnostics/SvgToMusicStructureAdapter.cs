using MusicStructure;
using SvgStructure.Models;

namespace SvgStructure.Services;

internal static class SvgToMusicStructureAdapter
{
    public static MusicStructureInput Convert(SvgStructureResolution source)
    {
        var noteKeys = source.NoteHeads
            .Select((note, index) => (note, key: $"note:{index}"))
            .ToDictionary(x => x.note, x => x.key);

        var stemKeys = source.Stems
            .Select((stem, index) => (stem, key: $"stem:{index}"))
            .ToDictionary(x => x.stem, x => x.key);

        var stems = source.Stems.Select(stem => new RecognizedStemInput(
            stemKeys[stem],
            stem.PartNumber,
            stem.MeasureNumber,
            CenterX(stem.LogicalBounds),
            stem.Direction == StemDirection.Up ? MusicStemDirection.Up : MusicStemDirection.Down,
            stem.AttachedNotes.Where(noteKeys.ContainsKey).Select(note => noteKeys[note]).ToArray())).ToArray();

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
                noteKeys[head],
                head.PartNumber,
                head.MeasureNumber,
                CenterX(head.LogicalBounds),
                head.Pitch,
                head.IsFilled,
                accidental is null ? null : ToAccidental(accidental.Kind),
                dotCount);
        }).ToArray();

        var arcs = source.Arcs.Select(arc =>
        {
            // ArcResolver stores accepted attachments in left-to-right order. Preserve that order
            // verbatim here; the adapter only transports identity, it does not reinterpret it.
            var leftNote = arc.Notes.Count > 0 && noteKeys.TryGetValue(arc.Notes[0], out var ln) ? ln : null;
            var rightNote = arc.Notes.Count > 1 && noteKeys.TryGetValue(arc.Notes[1], out var rn) ? rn : null;
            var leftStem = arc.Stems.Count > 0 && stemKeys.TryGetValue(arc.Stems[0], out var ls) ? ls : null;
            var rightStem = arc.Stems.Count > 1 && stemKeys.TryGetValue(arc.Stems[1], out var rs) ? rs : null;

            var measure = arc.Notes.FirstOrDefault()?.MeasureNumber
                          ?? arc.Stems.FirstOrDefault()?.MeasureNumber
                          ?? 0;

            return new RecognizedArcInput(measure, leftNote, leftStem, rightNote, rightStem);
        }).Where(x => x.Measure > 0).ToArray();

        return new MusicStructureInput(
            source.Structure.Parts.Count,
            source.Structure.Measures.Select(x => x.Number).ToArray(),
            source.Structure.Measures.Where(x => x.StartsNewSystem).Select(x => x.Number).ToHashSet(),
            notes,
            stems,
            beams,
            flags,
            arcs);
    }

    private static double? CenterX(LogicalRectD bounds) =>
        bounds.Left is { } left && bounds.Right is { } right
            ? (left + right) / 2.0
            : bounds.Left ?? bounds.Right;

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
