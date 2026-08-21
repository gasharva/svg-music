using System.Text.RegularExpressions;

namespace MusicStructure;

public sealed class NoteBuilder
{
    public MusicScore Build(MusicStructureInput input)
    {
        var notes = input.Notes
            .Select(BuildNote)
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
            if (!chordGroups.TryGetValue(key, out var group))
                return note;
            return note with { IsChordTone = Array.IndexOf(group, note) > 0 };
        }).ToArray();

        var measures = input.MeasureNumbers
            .OrderBy(x => x)
            .Select(number => new MusicMeasure(
                number,
                input.SystemStartMeasures.Contains(number),
                withChordFlags.Where(x => x.Measure == number).ToArray()))
            .ToArray();

        return new MusicScore(input.StaffCount, measures);
    }

    private static MusicNote BuildNote(RecognizedNoteInput input)
    {
        var pitch = ParsePitch(input.Pitch, input.Accidental);
        var beamDenominator = input.Beams.Count == 0 ? (int?)null : 4 * (1 << input.Beams.Max(x => x.Level));
        var denominator = input.FlagDenominator ?? beamDenominator;
        var type = denominator is not null
            ? TypeName(denominator.Value)
            : input.Stem is null
                ? input.IsFilled ? "quarter" : "whole"
                : input.IsFilled ? "quarter" : "half";

        return new MusicNote(
            input.Staff,
            input.Measure,
            input.LogicalX,
            pitch,
            type,
            input.Stem,
            input.Accidental,
            input.DotCount,
            input.Beams.OrderBy(x => x.Level).ToArray());
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
