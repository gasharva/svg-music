using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// Cleans up artifacts introduced when independently converted SVG systems/pages are joined.
/// Repeated clefs are suppressed unless the clef actually changes, and a new voice/staff lane
/// whose first note is geometrically at the left edge is forced back to measure time zero.
/// </summary>
public sealed class MusicXmlContinuationCleanupPostProcessor
{
    private const double LeftEdgeTolerance = 15;

    public void Apply(XDocument document)
    {
        var part = document.Root?.Element("part")
            ?? throw new InvalidOperationException("MusicXML part not found.");

        RemoveUnchangedClefs(part);

        foreach (var measure in part.Elements("measure"))
            NormalizeLaneStarts(measure);
    }

    private static void RemoveUnchangedClefs(XElement part)
    {
        var current = new Dictionary<int, ClefState>();

        foreach (var measure in part.Elements("measure"))
        {
            foreach (var attributes in measure.Elements("attributes").ToList())
            {
                foreach (var clef in attributes.Elements("clef").ToList())
                {
                    var staff = (int?)clef.Attribute("number") ?? 1;
                    var state = new ClefState(
                        clef.Element("sign")?.Value ?? string.Empty,
                        clef.Element("line")?.Value ?? string.Empty,
                        clef.Element("clef-octave-change")?.Value ?? string.Empty);

                    if (current.TryGetValue(staff, out var previous) && previous == state)
                    {
                        clef.Remove();
                    }
                    else
                    {
                        current[staff] = state;
                    }
                }

                if (!attributes.Elements().Any())
                    attributes.Remove();
            }
        }
    }

    private static void NormalizeLaneStarts(XElement measure)
    {
        var firstX = measure.Elements("note")
            .Where(x => x.Element("chord") is null)
            .Select(ReadDefaultX)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty(double.NaN)
            .Min();

        if (double.IsNaN(firstX)) return;

        var seenLanes = new HashSet<(int Staff, int Voice)>();
        var cursor = 0;
        XElement? lastBackup = null;
        var lastMotion = CursorMotion.None;

        foreach (var element in measure.Elements().ToList())
        {
            switch (element.Name.LocalName)
            {
                case "backup":
                {
                    var duration = ReadDuration(element);
                    cursor -= duration;
                    lastBackup = element;
                    lastMotion = CursorMotion.Backup;
                    break;
                }
                case "forward":
                {
                    cursor += ReadDuration(element);
                    lastMotion = CursorMotion.Forward;
                    break;
                }
                case "note":
                {
                    var chord = element.Element("chord") is not null;
                    var staff = (int?)element.Element("staff") ?? 1;
                    var voice = (int?)element.Element("voice") ?? 1;
                    var lane = (staff, voice);

                    if (!chord && seenLanes.Add(lane))
                    {
                        var x = ReadDefaultX(element);
                        var startsAtLeftEdge = x.HasValue && x.Value <= firstX + LeftEdgeTolerance;

                        // A lane starting at the left edge must be at metric time zero. This is
                        // especially important when a delayed inner voice was rendered as
                        // backup + forward: backing up only that voice's duration can leave the
                        // cursor at its start offset before the next staff begins.
                        if (startsAtLeftEdge && cursor > 0 && lastMotion == CursorMotion.Backup && lastBackup is not null)
                        {
                            var duration = ReadDuration(lastBackup) + cursor;
                            lastBackup.Element("duration")!.Value = duration.ToString(CultureInfo.InvariantCulture);
                            cursor = 0;
                        }
                    }

                    if (!chord)
                        cursor += ReadNoteDuration(element);
                    break;
                }
            }
        }
    }

    private static int ReadDuration(XElement movement) =>
        (int?)movement.Element("duration") ?? 0;

    private static int ReadNoteDuration(XElement note) =>
        note.Element("grace") is null ? (int?)note.Element("duration") ?? 0 : 0;

    private static double? ReadDefaultX(XElement note)
    {
        var text = (string?)note.Attribute("default-x");
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private readonly record struct ClefState(string Sign, string Line, string OctaveChange);

    private enum CursorMotion
    {
        None,
        Backup,
        Forward
    }
}
