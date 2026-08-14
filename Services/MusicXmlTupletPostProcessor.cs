using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Infers beamed tuplets from a voice's rhythmic overflow/underflow. The painted beam geometry gives
/// the written note value; the meter tells how much time the run must actually occupy. This handles
/// duplets, triplets, quintuplets, septuplets, etc. without hard-coding a particular score.
/// </summary>
public sealed class MusicXmlTupletPostProcessor
{
    private sealed record TupletRun(List<XElement> Notes, int Actual, int Normal);

    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var runs = new List<TupletRun>();
        var divisions = 1; var beats = 4; var beatType = 4;

        foreach (var measure in document.Descendants("measure"))
        {
            UpdateTiming(measure, ref divisions, ref beats, ref beatType);
            var measureDuration = Math.Max(1, beats * divisions * 4 / Math.Max(1, beatType));

            foreach (var lane in measure.Elements("note").Where(x => x.Element("chord") is null)
                         .GroupBy(x => new { Voice = (string?)x.Element("voice") ?? "1", Staff = (string?)x.Element("staff") ?? "1" }))
            {
                var notes = lane.ToList();
                var laneDuration = notes.Sum(Duration);
                if (laneDuration == measureDuration) continue;

                for (var i = 0; i < notes.Count; i++)
                {
                    if (!IsPrimaryBeam(notes[i], "begin")) continue;
                    var end = i + 1;
                    while (end < notes.Count && !IsPrimaryBeam(notes[end], "end")) end++;
                    if (end >= notes.Count) continue;

                    var group = notes.GetRange(i, end - i + 1);
                    var actual = group.Count;
                    if (actual is < 2 or > 12) { i = end; continue; }
                    var written = group.Sum(Duration);
                    var other = laneDuration - written;
                    var required = measureDuration - other;
                    if (required <= 0 || written <= 0) { i = end; continue; }

                    var normalExact = actual * required / (double)written;
                    var normal = (int)Math.Round(normalExact);
                    if (normal is < 2 or > 12 || normal == actual || Math.Abs(normalExact - normal) > .08)
                    { i = end; continue; }
                    var ratio = normal / (double)actual;
                    if (ratio is < .45 or > 1.60) { i = end; continue; }

                    runs.Add(new TupletRun(group, actual, normal));
                    i = end;
                }
            }
        }

        if (runs.Count == 0) return;
        var scale = runs.Select(x => x.Actual).Aggregate(1, Lcm);

        foreach (var div in document.Descendants("divisions"))
            if (int.TryParse(div.Value, out var value)) div.Value = (value * scale).ToString();
        foreach (var duration in document.Descendants("duration"))
            if (int.TryParse(duration.Value, out var value)) duration.Value = (value * scale).ToString();

        foreach (var run in runs)
        {
            for (var i = 0; i < run.Notes.Count; i++)
            {
                var note = run.Notes[i];
                var duration = note.Element("duration");
                if (duration is not null && int.TryParse(duration.Value, out var scaled))
                    duration.Value = (scaled * run.Normal / run.Actual).ToString();

                note.Element("time-modification")?.Remove();
                var tm = new XElement("time-modification", new XElement("actual-notes", run.Actual), new XElement("normal-notes", run.Normal));
                var type = note.Element("type");
                if (type is not null) type.AddBeforeSelf(tm); else note.Add(tm);

                var notations = note.Element("notations");
                if (notations is null) { notations = new XElement("notations"); note.Add(notations); }
                if (i == 0) notations.Add(new XElement("tuplet", new XAttribute("type", "start"), new XAttribute("number", 1)));
                if (i == run.Notes.Count - 1) notations.Add(new XElement("tuplet", new XAttribute("type", "stop"), new XAttribute("number", 1)));
            }
        }

        // Voice-layout backups normally return to the start of the current lane. Tuplet correction
        // can shorten/lengthen that lane after those backups were emitted; clamp only impossible
        // overshoots, leaving deliberate partial backups untouched.
        foreach (var measure in document.Descendants("measure"))
        {
            var cursor = 0;
            foreach (var element in measure.Elements())
            {
                if (element.Name.LocalName == "note" && element.Element("chord") is null) cursor += Duration(element);
                else if (element.Name.LocalName == "forward") cursor += (int?)element.Element("duration") ?? 0;
                else if (element.Name.LocalName == "backup")
                {
                    var d = element.Element("duration");
                    if (d is null) continue;
                    var value = (int?)d ?? 0;
                    if (value > cursor) { value = cursor; d.Value = value.ToString(); }
                    cursor = Math.Max(0, cursor - value);
                }
            }
        }

        document.Save(path);
    }

    private static int Duration(XElement note) => (int?)note.Element("duration") ?? 0;
    private static bool IsPrimaryBeam(XElement note, string value) => note.Elements("beam").Any(x => (int?)x.Attribute("number") == 1 && string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));

    private static void UpdateTiming(XElement measure, ref int divisions, ref int beats, ref int beatType)
    {
        var a = measure.Element("attributes"); if (a is null) return;
        if ((int?)a.Element("divisions") is int d && d > 0) divisions = d;
        var t = a.Element("time"); if (t is null) return;
        if ((int?)t.Element("beats") is int b && b > 0) beats = b;
        if ((int?)t.Element("beat-type") is int bt && bt > 0) beatType = bt;
    }

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }
    private static int Lcm(int a, int b) => Math.Abs(a / Math.Max(1, Gcd(a, b)) * b);
}
