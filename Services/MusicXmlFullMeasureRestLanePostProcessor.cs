using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Repairs a strong polyphonic pattern: one voice contains a single sustained chord plus rests,
/// while a neighbouring voice becomes exactly as long as that sustain when those rests are moved
/// to it. A rest cannot occur after a note that already occupies the whole local voice span, so this
/// is safe evidence even on continuation SVG pages whose temporary standalone meter is synthetic.
/// </summary>
public sealed class MusicXmlFullMeasureRestLanePostProcessor
{
    private sealed class Unit
    {
        public List<XElement> Notes { get; } = [];
        public int Staff { get; init; }
        public int Voice { get; set; }
        public int Duration { get; init; }
        public bool IsRest { get; init; }
        public double X { get; init; }
    }

    public void Apply(string path)
    {
        var document = XDocument.Load(path);
        var changedAny = false;

        foreach (var measure in document.Descendants("measure"))
        {
            var units = ParseUnits(measure);
            if (units.Count == 0) continue;
            var changed = false;

            foreach (var staffGroup in units.GroupBy(x => x.Staff))
            {
                var lanes = staffGroup.GroupBy(x => x.Voice).ToDictionary(x => x.Key, x => x.ToList());
                foreach (var source in lanes.ToList())
                {
                    var sounding = source.Value.Where(x => !x.IsRest).ToList();
                    var strayRests = source.Value.Where(x => x.IsRest).ToList();
                    if (sounding.Count != 1 || strayRests.Count == 0) continue;

                    var sustain = sounding[0].Duration;
                    if (sustain <= 0) continue;
                    var restDuration = strayRests.Sum(x => x.Duration);

                    var target = lanes
                        .Where(x => x.Key != source.Key)
                        .Select(x => new
                        {
                            Voice = x.Key,
                            Units = x.Value,
                            Total = x.Value.Sum(u => u.Duration)
                        })
                        .Where(x => x.Total + restDuration == sustain)
                        .OrderBy(x => Math.Abs(FirstX(x.Units) - FirstX(strayRests)))
                        .FirstOrDefault();
                    if (target is null) continue;

                    foreach (var rest in strayRests) rest.Voice = target.Voice;
                    changed = true;
                }
            }

            if (!changed) continue;
            RebuildMeasure(measure, units);
            changedAny = true;
        }

        if (changedAny) document.Save(path);
    }

    private static List<Unit> ParseUnits(XElement measure)
    {
        var result = new List<Unit>();
        Unit? current = null;
        foreach (var note in measure.Elements("note"))
        {
            var chord = note.Element("chord") is not null;
            if (!chord || current is null)
            {
                current = new Unit
                {
                    Staff = (int?)note.Element("staff") ?? 1,
                    Voice = (int?)note.Element("voice") ?? 1,
                    Duration = (int?)note.Element("duration") ?? 0,
                    IsRest = note.Element("rest") is not null,
                    X = ReadX(note) ?? double.MaxValue
                };
                result.Add(current);
            }
            current.Notes.Add(note);
        }
        return result;
    }

    private static void RebuildMeasure(XElement measure, IReadOnlyList<Unit> units)
    {
        measure.Elements("note").Remove();
        measure.Elements("backup").Remove();
        measure.Elements("forward").Remove();

        var insertionPoint = measure.Elements("barline").FirstOrDefault();
        var firstStaff = true;
        var previousStaffCursor = 0;

        foreach (var staff in units.GroupBy(x => x.Staff).OrderBy(x => x.Key))
        {
            if (!firstStaff && previousStaffCursor > 0)
                Add(new XElement("backup", new XElement("duration", previousStaffCursor)));

            var firstVoice = true;
            var previousVoiceCursor = 0;
            foreach (var voice in staff.GroupBy(x => x.Voice).OrderBy(x => x.Key))
            {
                if (!firstVoice && previousVoiceCursor > 0)
                    Add(new XElement("backup", new XElement("duration", previousVoiceCursor)));

                var cursor = 0;
                foreach (var unit in voice.OrderBy(x => x.X))
                {
                    foreach (var note in unit.Notes)
                    {
                        var voiceElement = note.Element("voice");
                        if (voiceElement is not null)
                            voiceElement.Value = unit.Voice.ToString(CultureInfo.InvariantCulture);
                        Add(note);
                    }
                    cursor += unit.Duration;
                }

                previousVoiceCursor = cursor;
                firstVoice = false;
            }

            previousStaffCursor = previousVoiceCursor;
            firstStaff = false;
        }

        void Add(XElement element)
        {
            if (insertionPoint is null) measure.Add(element);
            else insertionPoint.AddBeforeSelf(element);
        }
    }

    private static double FirstX(IEnumerable<Unit> units) => units.Select(x => x.X).DefaultIfEmpty(double.MaxValue).Min();

    private static double? ReadX(XElement note)
    {
        var text = (string?)note.Attribute("default-x");
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
