using System.Text.RegularExpressions;

namespace MusicStructure;

public sealed class PitchRule : IMusicNoteRule
{
    public PitchRule(MusicMeasureInput input)
    {
    }

    public MusicNoteDraft Apply(MusicNoteDraft note)
    {
        var text = note.Source.Pitch.Trim();
        var match = Regex.Match(text, "^([A-Ga-g])(?:[#b])?(-?\\d+)$");
        if (!match.Success)
            throw new InvalidDataException($"Unsupported resolved pitch '{text}'. Expected e.g. A4 or C#5.");

        var alter = note.Source.Accidental switch
        {
            MusicAccidental.Flat => -1,
            MusicAccidental.Sharp => 1,
            MusicAccidental.Natural => 0,
            MusicAccidental.DoubleSharp => 2,
            MusicAccidental.DoubleFlat => -2,
            _ => text.Contains('#') ? 1 : text.Contains('b') ? -1 : 0
        };

        return note with
        {
            Pitch = new MusicPitch(
                match.Groups[1].Value.ToUpperInvariant(),
                int.Parse(match.Groups[2].Value),
                alter),
            Accidental = note.Source.Accidental
        };
    }
}
