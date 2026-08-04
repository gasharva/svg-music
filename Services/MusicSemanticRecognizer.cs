using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class MusicSemanticRecognizer
{
    private static readonly string[] Steps = ["C", "D", "E", "F", "G", "A", "B"];

    public AnalysisResult Recognize(
        IReadOnlyList<SvgUse> uses,
        IReadOnlyList<Staff> staves,
        ClassificationResult classification,
        RecognitionConfig config)
    {
        var warnings = new List<string>();
        var bySymbol = classification.Symbols.ToDictionary(x => x.SymbolId, StringComparer.Ordinal);
        var instances = new List<RecognizedEvent>();

        foreach (var use in uses)
        {
            if (!bySymbol.TryGetValue(use.SymbolId, out var cls))
            {
                warnings.Add($"Нет классификации для symbol #{use.SymbolId}");
                continue;
            }

            if (cls.Score < config.MinClassificationScore)
            {
                warnings.Add($"Низкая уверенность: #{use.SymbolId} -> {cls.ReferenceId}, score={cls.Score:F3}");
                continue;
            }

            var staff = FindStaff(use, staves, config.MaxSymbolDistanceInSpaces);
            if (staff is null)
            {
                warnings.Add($"Не найден стан для #{use.SymbolId} в ({use.X:F1}, {use.Y:F1})");
                continue;
            }

            instances.Add(new RecognizedEvent
            {
                SourceSymbolId = use.SymbolId,
                Kind = cls.Kind,
                ReferenceId = cls.ReferenceId,
                Confidence = cls.Score,
                X = use.X,
                Y = use.Y,
                StaffIndex = staff.Index
            });
        }

        var clefs = DetectClefs(instances, staves, config);
        var events = new List<RecognizedEvent>();

        foreach (var item in instances)
        {
            if (item.Kind.StartsWith("notehead-", StringComparison.OrdinalIgnoreCase))
            {
                var staff = staves[item.StaffIndex];
                var clef = clefs.GetValueOrDefault(item.StaffIndex) ?? DefaultClef(config);
                SetPitch(item, staff, clef);
                SetNoteDuration(item, config);
                events.Add(item);
            }
            else if (item.Kind.StartsWith("rest-", StringComparison.OrdinalIgnoreCase))
            {
                SetRestDuration(item, config);
                events.Add(item);
            }
            else if (item.Kind.StartsWith("clef-", StringComparison.OrdinalIgnoreCase))
            {
                SetClef(item, config);
                events.Add(item);
            }
        }

        AttachAccidentals(instances, events, staves, config, warnings);
        AttachDots(instances, events, staves, config, warnings);
        MarkChords(events, staves);

        return new AnalysisResult
        {
            Staves = staves.ToList(),
            Uses = uses.ToList(),
            Classifications = classification.Symbols,
            Events = events.OrderBy(x => x.StaffIndex).ThenBy(x => x.X).ThenByDescending(x => x.Y).ToList(),
            Warnings = warnings.Distinct().ToList()
        };
    }

    private static Staff? FindStaff(SvgUse use, IReadOnlyList<Staff> staves, double maxDistanceSpaces) =>
        staves
            .Where(s => use.X >= s.Left - s.Space * 3 && use.X <= s.Right + s.Space * 3)
            .Select(s => new { Staff = s, Distance = Math.Abs(use.Y - s.Center) / Math.Max(s.Space, 0.001) })
            .Where(x => x.Distance <= maxDistanceSpaces)
            .OrderBy(x => x.Distance)
            .Select(x => x.Staff)
            .FirstOrDefault();

    private static Dictionary<int, RecognizedEvent> DetectClefs(
        IReadOnlyList<RecognizedEvent> instances,
        IReadOnlyList<Staff> staves,
        RecognitionConfig config)
    {
        var result = new Dictionary<int, RecognizedEvent>();
        foreach (var staff in staves)
        {
            var clef = instances
                .Where(x => x.StaffIndex == staff.Index && x.Kind.StartsWith("clef-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.X)
                .ThenByDescending(x => x.Confidence)
                .FirstOrDefault();
            if (clef is not null)
            {
                SetClef(clef, config);
                result[staff.Index] = clef;
            }
        }
        return result;
    }

    private static RecognizedEvent DefaultClef(RecognitionConfig config) => new()
    {
        Kind = config.DefaultClef switch
        {
            "F" => "clef-bass",
            "C" => "clef-alto",
            _ => "clef-treble"
        },
        ClefSign = config.DefaultClef,
        ClefLine = config.DefaultClefLine
    };

    private static void SetClef(RecognizedEvent item, RecognitionConfig config)
    {
        (item.ClefSign, item.ClefLine) = item.Kind switch
        {
            "clef-bass" => ("F", 4),
            "clef-alto" => ("C", 3),
            "clef-percussion" => ("percussion", 2),
            "clef-treble" => ("G", 2),
            _ => (config.DefaultClef, config.DefaultClefLine)
        };
    }

    private static void SetPitch(RecognizedEvent note, Staff staff, RecognizedEvent clef)
    {
        var diatonicFromBottom = (int)Math.Round((staff.Bottom - note.Y) / (staff.Space / 2.0));

        // Absolute diatonic index of the bottom line for the common clefs.
        var bottomLineAbsolute = clef.ClefSign switch
        {
            "F" => 2 * 7 + 4, // G2
            "C" => 3 * 7 + 3, // F3 for alto clef (C4 on line 3)
            _ => 4 * 7 + 2     // E4
        };

        var absolute = bottomLineAbsolute + diatonicFromBottom;
        var octave = Math.DivRem(absolute, 7, out var stepIndex);
        if (stepIndex < 0)
        {
            stepIndex += 7;
            octave--;
        }

        note.Step = Steps[stepIndex];
        note.Octave = octave;
    }

    private static void SetNoteDuration(RecognizedEvent note, RecognitionConfig config)
    {
        note.Type = note.Kind switch
        {
            "notehead-double-whole" => "breve",
            "notehead-whole" => "whole",
            "notehead-half" => "half",
            _ => "quarter"
        };
        note.Duration = DurationForType(note.Type, config.Divisions);
    }

    private static void SetRestDuration(RecognizedEvent rest, RecognitionConfig config)
    {
        rest.Type = rest.Kind switch
        {
            "rest-double-whole" => "breve",
            "rest-whole" => "whole",
            "rest-half" => "half",
            "rest-quarter" => "quarter",
            "rest-eighth" => "eighth",
            "rest-16th" => "16th",
            "rest-32nd" => "32nd",
            "rest-64th" => "64th",
            "rest-128th" => "128th",
            "rest-256th" => "256th",
            "rest-512th" => "512th",
            "rest-1024th" => "1024th",
            _ => "quarter"
        };
        rest.Duration = DurationForType(rest.Type, config.Divisions);
    }

    private static int DurationForType(string type, int divisions) => type switch
    {
        "breve" => divisions * 8,
        "whole" => divisions * 4,
        "half" => divisions * 2,
        "quarter" => divisions,
        "eighth" => Math.Max(1, divisions / 2),
        "16th" => Math.Max(1, divisions / 4),
        "32nd" => Math.Max(1, divisions / 8),
        "64th" => Math.Max(1, divisions / 16),
        _ => Math.Max(1, divisions)
    };

    private static void AttachAccidentals(
        IReadOnlyList<RecognizedEvent> instances,
        IReadOnlyList<RecognizedEvent> events,
        IReadOnlyList<Staff> staves,
        RecognitionConfig config,
        List<string> warnings)
    {
        foreach (var accidental in instances.Where(x => x.Kind.StartsWith("accidental-", StringComparison.OrdinalIgnoreCase)))
        {
            var staff = staves[accidental.StaffIndex];
            var note = events
                .Where(x => x.StaffIndex == accidental.StaffIndex && x.Step is not null && x.X > accidental.X)
                .Where(x => x.X - accidental.X <= staff.Space * config.MaxAttachmentDistanceInSpaces)
                .Where(x => Math.Abs(x.Y - accidental.Y) <= staff.Space * 1.2)
                .OrderBy(x => x.X - accidental.X)
                .ThenBy(x => Math.Abs(x.Y - accidental.Y))
                .FirstOrDefault();

            if (note is null)
            {
                warnings.Add($"Не удалось привязать {accidental.Kind} в x={accidental.X:F1}");
                continue;
            }

            note.Alter = accidental.Kind switch
            {
                "accidental-flat" => -1,
                "accidental-double-flat" => -2,
                "accidental-sharp" => 1,
                "accidental-double-sharp" => 2,
                _ => 0
            };
            note.AttachedToSymbolId = accidental.SourceSymbolId;
        }
    }

    private static void AttachDots(
        IReadOnlyList<RecognizedEvent> instances,
        IReadOnlyList<RecognizedEvent> events,
        IReadOnlyList<Staff> staves,
        RecognitionConfig config,
        List<string> warnings)
    {
        foreach (var dot in instances.Where(x => x.Kind == "augmentation-dot"))
        {
            var staff = staves[dot.StaffIndex];
            var target = events
                .Where(x => x.StaffIndex == dot.StaffIndex && (x.Step is not null || x.Kind.StartsWith("rest-")))
                .Where(x => x.X < dot.X)
                .Where(x => dot.X - x.X <= staff.Space * config.MaxAttachmentDistanceInSpaces)
                .Where(x => Math.Abs(x.Y - dot.Y) <= staff.Space * 1.25)
                .OrderBy(x => dot.X - x.X)
                .ThenBy(x => Math.Abs(x.Y - dot.Y))
                .FirstOrDefault();

            if (target is null)
            {
                warnings.Add($"Не удалось привязать точку в x={dot.X:F1}");
                continue;
            }

            target.Dotted = true;
            target.Duration = checked(target.Duration * 3 / 2);
            target.AttachedToSymbolId = dot.SourceSymbolId;
        }
    }

    private static void MarkChords(IReadOnlyList<RecognizedEvent> events, IReadOnlyList<Staff> staves)
    {
        foreach (var staffGroup in events.Where(x => x.Step is not null).GroupBy(x => x.StaffIndex))
        {
            var staff = staves[staffGroup.Key];
            foreach (var cluster in staffGroup.OrderBy(x => x.X)
                         .GroupAdjacent((a, b) => Math.Abs(a.X - b.X) <= staff.Space * 0.25))
            {
                var notes = cluster.OrderByDescending(x => x.Y).ToList();
                for (var i = 1; i < notes.Count; i++) notes[i].Chord = true;
            }
        }
    }
}

internal static class EnumerableGroupingExtensions
{
    public static IEnumerable<List<T>> GroupAdjacent<T>(this IEnumerable<T> source, Func<T, T, bool> sameGroup)
    {
        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) yield break;
        var group = new List<T> { enumerator.Current };
        var previous = enumerator.Current;
        while (enumerator.MoveNext())
        {
            var current = enumerator.Current;
            if (!sameGroup(previous, current))
            {
                yield return group;
                group = [];
            }
            group.Add(current);
            previous = current;
        }
        yield return group;
    }
}
