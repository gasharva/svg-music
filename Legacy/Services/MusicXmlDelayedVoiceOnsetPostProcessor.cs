using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Repairs a quiet but important polyphony case: a secondary voice can begin after an implicit rest
/// even when the rest is not engraved. If that voice is otherwise a perfectly valid lane, the more
/// aggressive multi-voice repair has no reason to touch it. We use the already-preserved SVG
/// default-x columns to align the late first note with a known onset in another voice/staff and
/// insert the missing MusicXML forward.
/// </summary>
public sealed class MusicXmlDelayedVoiceOnsetPostProcessor
{
    private sealed record TimedNote(XElement Element, int Staff, int Voice, int Onset, int Duration, double X)
    {
        public int End => Onset + Duration;
    }

    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var divisions = 1;
        var beats = 4;
        var beatType = 4;
        var changed = false;

        foreach (var measure in document.Descendants("measure"))
        {
            UpdateTiming(measure, ref divisions, ref beats, ref beatType);
            var measureDuration = Math.Max(1, beats * divisions * 4 / Math.Max(1, beatType));
            var timed = ReadTimedNotes(measure);
            if (timed.Count < 2) continue;

            var earliestX = timed.Where(x => !double.IsNaN(x.X)).Select(x => x.X).DefaultIfEmpty(double.NaN).Min();
            if (double.IsNaN(earliestX)) continue;

            foreach (var lane in timed.GroupBy(x => (x.Staff, x.Voice)))
            {
                var ordered = lane.OrderBy(x => x.Onset).ThenBy(x => x.X).ToList();
                if (ordered.Count == 0 || ordered[0].Onset != 0) continue;

                var first = ordered[0];
                if (double.IsNaN(first.X) || first.X - earliestX < 18.0) continue;

                var laneSpan = ordered.Max(x => x.End);
                if (laneSpan >= measureDuration) continue;

                var anchor = timed
                    .Where(x => x.Voice != first.Voice || x.Staff != first.Staff)
                    .Where(x => x.Onset > 0 && !double.IsNaN(x.X))
                    .Where(x => Math.Abs(x.X - first.X) <= 12.0)
                    .Where(x => x.Onset + laneSpan <= measureDuration)
                    .OrderBy(x => Math.Abs(x.X - first.X))
                    .ThenBy(x => x.Onset)
                    .FirstOrDefault();
                if (anchor is null) continue;

                // The first note is serialized immediately after a backup/reset for this lane.
                // Insert the implicit silence right there; do not invent a visible rest.
                first.Element.AddBeforeSelf(new XElement("forward", new XElement("duration", anchor.Onset)));
                changed = true;
            }
        }

        if (changed) document.Save(path);
    }

    private static List<TimedNote> ReadTimedNotes(XElement measure)
    {
        var result = new List<TimedNote>();
        var cursor = 0;
        var previousOnset = 0;

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

            var chord = element.Element("chord") is not null;
            var duration = (int?)element.Element("duration") ?? 0;
            var onset = chord ? previousOnset : cursor;
            if (!chord) previousOnset = onset;

            var xText = (string?)element.Attribute("default-x");
            var x = double.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : double.NaN;
            result.Add(new TimedNote(
                element,
                (int?)element.Element("staff") ?? 1,
                (int?)element.Element("voice") ?? 1,
                onset,
                duration,
                x));

            if (!chord) cursor += duration;
        }
        return result;
    }

    private static void UpdateTiming(XElement measure, ref int divisions, ref int beats, ref int beatType)
    {
        var attributes = measure.Element("attributes");
        if (attributes is null) return;
        if ((int?)attributes.Element("divisions") is > 0 and var d) divisions = d;
        var time = attributes.Element("time");
        if (time is null) return;
        if ((int?)time.Element("beats") is > 0 and var b) beats = b;
        if ((int?)time.Element("beat-type") is > 0 and var bt) beatType = bt;
    }
}
