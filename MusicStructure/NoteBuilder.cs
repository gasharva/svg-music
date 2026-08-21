using System.Text.RegularExpressions;

namespace MusicStructure;

public sealed class NoteBuilder
{
    public MusicScore Build(MusicStructureInput input)
    {
        var stemsByNote = input.Stems
            .SelectMany(stem => stem.AttachedNoteKeys.Select(noteKey => (noteKey, stem)))
            .GroupBy(x => x.noteKey)
            .ToDictionary(g => g.Key, g => g.Select(x => x.stem).ToArray());

        var flagsByStem = input.Flags
            .GroupBy(x => x.StemKey)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Denominator).First());

        var beamsByStem = input.Beams
            .SelectMany(beam => beam.StemKeys.Select(stemKey => (stemKey, beam)))
            .GroupBy(x => x.stemKey)
            .ToDictionary(g => g.Key, g => g.Select(x => x.beam).OrderBy(x => x.Level).ToArray());

        var notes = input.Notes
            .Select(note => BuildNote(note, stemsByNote, flagsByStem, beamsByStem))
            .OrderBy(x => x.Measure)
            .ThenBy(x => x.LogicalX ?? double.MaxValue)
            .ThenBy(x => x.Staff)
            .ThenBy(x => x.Pitch.Octave)
            .ThenBy(x => x.Pitch.Step)
            .ToArray();

        var chordGroups = notes
            .Where(x => x.LogicalX.HasValue)
            .GroupBy(x => (x.Measure, X: Math.Round(x.LogicalX!.Value, 2)))
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Staff).ThenBy(x => x.Pitch.Octave).ThenBy(x => x.Pitch.Step).ToArray());

        var withChordFlags = notes.Select(note =>
        {
            if (!note.LogicalX.HasValue)
                return note;
            var key = (note.Measure, Math.Round(note.LogicalX.Value, 2));
            return chordGroups.TryGetValue(key, out var group)
                ? note with { IsChordTone = Array.IndexOf(group, note) > 0 }
                : note;
        }).ToArray();

        var measures = input.MeasureNumbers
            .OrderBy(x => x)
            .Select(number => new MusicMeasure(number, input.SystemStartMeasures.Contains(number), withChordFlags.Where(x => x.Measure == number).ToArray()))
            .ToArray();

        return new MusicScore(input.StaffCount, measures);
    }

    private static MusicNote BuildNote(
        RecognizedNoteInput input,
        IReadOnlyDictionary<string, RecognizedStemInput[]> stemsByNote,
        IReadOnlyDictionary<string, RecognizedFlagInput> flagsByStem,
        IReadOnlyDictionary<string, RecognizedBeamInput[]> beamsByStem)
    {
        var stem = stemsByNote.TryGetValue(input.Key, out var attachedStems) ? attachedStems.FirstOrDefault() : null;
        var semanticBeams = stem is null || !beamsByStem.TryGetValue(stem.Key, out var rawBeams)
            ? Array.Empty<MusicBeam>()
            : BuildSemanticBeams(rawBeams, stem, input);

        var flagDenominator = stem is not null && flagsByStem.TryGetValue(stem.Key, out var flag) ? flag.Denominator : (int?)null;
        var beamDenominator = semanticBeams.Length == 0 ? (int?)null : 4 * (1 << semanticBeams.Max(x => x.Level));
        var denominator = flagDenominator ?? beamDenominator;
        var type = denominator is not null
            ? TypeName(denominator.Value)
            : stem is null
                ? input.IsFilled ? "quarter" : "whole"
                : input.IsFilled ? "quarter" : "half";

        return new MusicNote(
            input.Staff,
            input.Measure,
            input.LogicalX,
            ParsePitch(input.Pitch, input.Accidental),
            type,
            stem?.Direction,
            input.Accidental,
            input.DotCount,
            semanticBeams);
    }

    private static MusicBeam[] BuildSemanticBeams(IReadOnlyList<RecognizedBeamInput> rawBeams, RecognizedStemInput stem, RecognizedNoteInput note)
    {
        var distinct = rawBeams
            .OrderBy(x => x.Level)
            .GroupBy(x => x.Level)
            .Select(g => g.First())
            .ToArray();

        var result = new List<MusicBeam>();
        for (var i = 0; i < distinct.Length; i++)
        {
            var beam = distinct[i];
            var stemIndex = Array.IndexOf(beam.StemKeys.ToArray(), stem.Key);
            if (stemIndex < 0)
                continue;

            var position = beam.StemKeys.Count <= 1
                ? MusicBeamPosition.Begin
                : stemIndex == 0
                    ? MusicBeamPosition.Begin
                    : stemIndex == beam.StemKeys.Count - 1
                        ? MusicBeamPosition.End
                        : MusicBeamPosition.Continue;
            result.Add(new MusicBeam(i + 1, position));
        }
        return result.ToArray();
    }

    private static MusicPitch ParsePitch(string text, MusicAccidental? accidental)
    {
        var match = Regex.Match(text.Trim(), "^([A-Ga-g])(?:[#b])?(-?\\d+)$");
        if (!match.Success)
            throw new InvalidDataException($"Unsupported resolved pitch '{text}'. Expected e.g. A4 or C#5.");

        var alter = accidental switch
        {
            MusicAccidental.Flat => -1,
            MusicAccidental.Sharp => 1,
            MusicAccidental.Natural => 0,
            MusicAccidental.DoubleSharp => 2,
            MusicAccidental.DoubleFlat => -2,
            _ => text.Contains('#') ? 1 : text.Contains('b') ? -1 : 0
        };

        return new MusicPitch(match.Groups[1].Value.ToUpperInvariant(), int.Parse(match.Groups[2].Value), alter);
    }

    private static string TypeName(int denominator) => denominator switch
    {
        1 => "whole",
        2 => "half",
        4 => "quarter",
        8 => "eighth",
        16 => "16th",
        32 => "32nd",
        64 => "64th",
        _ => $"1/{denominator}"
    };
}
