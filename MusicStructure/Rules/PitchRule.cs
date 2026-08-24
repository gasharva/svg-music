using System.Text.RegularExpressions;

namespace MusicStructure;

/// <summary>
/// Resolves written pitch and keeps the ordinary measure-local accidental state.
/// An explicit accidental changes subsequent notes on the same staff position until a natural
/// (or another accidental) changes it again. Only the explicit note keeps Accidental for engraving.
/// </summary>
public sealed class PitchRule : IMusicNoteRule
{
    private readonly Dictionary<(int Staff, string Step, int Octave), int> _measureAlter = new();

    public PitchRule(MusicMeasureInput input)
    {
        _ = input;
    }

    public MusicNoteDraft Apply(MusicNoteDraft note)
    {
        var text = note.Source.Pitch.Trim();
        var match = Regex.Match(text, "^([A-Ga-g])([#b]?)(-?\\d+)$");
        if (!match.Success)
            throw new InvalidDataException($"Unsupported resolved pitch '{text}'. Expected e.g. A4 or C#5.");

        var step = match.Groups[1].Value.ToUpperInvariant();
        var octave = int.Parse(match.Groups[3].Value);
        var key = (note.Source.Staff, step, octave);
        var textualAlter = match.Groups[2].Value == "#" ? 1 : match.Groups[2].Value == "b" ? -1 : 0;

        int alter;
        if (note.Source.Accidental is { } explicitAccidental)
        {
            alter = Alter(explicitAccidental);
            _measureAlter[key] = alter;
        }
        else if (_measureAlter.TryGetValue(key, out var inherited))
        {
            alter = inherited;
        }
        else
        {
            alter = textualAlter; // key signature support will replace/extend this later.
        }

        return note with
        {
            Pitch = new MusicPitch(step, octave, alter),
            Accidental = note.Source.Accidental
        };
    }

    private static int Alter(MusicAccidental accidental) => accidental switch
    {
        MusicAccidental.Flat => -1,
        MusicAccidental.Sharp => 1,
        MusicAccidental.Natural => 0,
        MusicAccidental.DoubleSharp => 2,
        MusicAccidental.DoubleFlat => -2,
        _ => 0
    };
}
