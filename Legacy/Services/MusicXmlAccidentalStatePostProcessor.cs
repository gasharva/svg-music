using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Applies the musical scope of explicit accidentals within each measure.
/// Existing <accidental> elements are treated as signs that were actually present
/// in the source engraving. Their pitch alteration is propagated to later notes of the
/// same staff/step/octave until the measure ends, without drawing another accidental.
/// </summary>
public sealed class MusicXmlAccidentalStatePostProcessor
{
    private sealed record TimedNote(XElement Note, int Staff, string Step, int Octave, int Onset, int Order);

    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        foreach (var measure in document.Descendants("measure"))
            ApplyMeasure(measure);
        document.Save(path);
    }

    private static void ApplyMeasure(XElement measure)
    {
        var timed = ReadTimedNotes(measure);

        foreach (var pitchGroup in timed.GroupBy(x => new { x.Staff, x.Step, x.Octave }))
        {
            int? currentAlter = null;

            foreach (var onsetGroup in pitchGroup.GroupBy(x => x.Onset).OrderBy(x => x.Key))
            {
                var explicitNote = onsetGroup
                    .OrderBy(x => x.Order)
                    .FirstOrDefault(x => x.Note.Element("accidental") is not null);

                if (explicitNote is not null)
                    currentAlter = AlterFromAccidental((string?)explicitNote.Note.Element("accidental"));

                foreach (var item in onsetGroup.OrderBy(x => x.Order))
                {
                    var accidental = item.Note.Element("accidental");
                    if (accidental is not null)
                    {
                        var explicitAlter = AlterFromAccidental(accidental.Value);
                        if (explicitAlter.HasValue)
                        {
                            SetPitchAlter(item.Note, explicitAlter.Value);
                            currentAlter = explicitAlter;
                        }
                        continue;
                    }

                    if (currentAlter.HasValue)
                        SetPitchAlter(item.Note, currentAlter.Value);
                }
            }
        }
    }

    private static List<TimedNote> ReadTimedNotes(XElement measure)
    {
        var result = new List<TimedNote>();
        var cursor = 0;
        var previousOnset = 0;
        var order = 0;

        foreach (var element in measure.Elements())
        {
            if (element.Name.LocalName == "backup")
            {
                cursor -= (int?)element.Element("duration") ?? 0;
                continue;
            }
            if (element.Name.LocalName == "forward")
            {
                cursor += (int?)element.Element("duration") ?? 0;
                continue;
            }
            if (element.Name.LocalName != "note") continue;

            var duration = (int?)element.Element("duration") ?? 0;
            var isChord = element.Element("chord") is not null;
            var onset = isChord ? previousOnset : cursor;
            if (!isChord) previousOnset = onset;

            var pitch = element.Element("pitch");
            var step = (string?)pitch?.Element("step");
            var octave = (int?)pitch?.Element("octave");
            if (step is not null && octave.HasValue)
            {
                result.Add(new TimedNote(
                    element,
                    (int?)element.Element("staff") ?? 1,
                    step,
                    octave.Value,
                    onset,
                    order++));
            }

            if (!isChord) cursor += duration;
        }

        return result;
    }

    private static int? AlterFromAccidental(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "flat-flat" => -2,
        "flat" => -1,
        "natural" => 0,
        "sharp" => 1,
        "double-sharp" => 2,
        _ => null
    };

    private static void SetPitchAlter(XElement note, int alter)
    {
        var pitch = note.Element("pitch");
        if (pitch is null) return;

        var existing = pitch.Element("alter");
        if (alter == 0)
        {
            existing?.Remove();
            return;
        }

        if (existing is not null)
            existing.Value = alter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        else
            pitch.Element("step")?.AddAfterSelf(new XElement("alter", alter));
    }
}
