using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Quality;

public sealed class MusicXmlSemanticComparer
{
    private const decimal PositionTolerance = 0.0001m;

    public QualityComparison Compare(string expectedMusicXmlPath, string actualMusicXmlPath)
    {
        var expected = ReadEvents(expectedMusicXmlPath);
        var actual = ReadEvents(actualMusicXmlPath);
        var rows = Match(expected, actual);
        return QualityComparison.Create(rows, expected.Count, actual.Count);
    }

    public IReadOnlyList<MusicXmlEvent> ReadEvents(string path)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var result = new List<MusicXmlEvent>();

        foreach (var part in document.Descendants("part"))
        {
            var partId = (string?)part.Attribute("id") ?? "P1";
            var divisions = 1m;

            foreach (var measure in part.Elements("measure"))
            {
                var measureNumber = (string?)measure.Attribute("number") ?? "?";
                var cursor = 0m;
                var lastOnsetByVoice = new Dictionary<string, decimal>();

                foreach (var child in measure.Elements())
                {
                    if (child.Name.LocalName == "attributes")
                    {
                        var value = DecimalValue(child.Element("divisions"));
                        if (value > 0) divisions = value;
                        continue;
                    }

                    if (child.Name.LocalName == "backup")
                    {
                        cursor -= DecimalValue(child.Element("duration")) / divisions;
                        continue;
                    }

                    if (child.Name.LocalName == "forward")
                    {
                        cursor += DecimalValue(child.Element("duration")) / divisions;
                        continue;
                    }

                    if (child.Name.LocalName != "note") continue;

                    var voice = child.Element("voice")?.Value ?? "1";
                    var isChord = child.Element("chord") is not null;
                    var duration = DecimalValue(child.Element("duration")) / divisions;
                    var onset = isChord && lastOnsetByVoice.TryGetValue(voice, out var chordOnset)
                        ? chordOnset
                        : cursor;

                    var pitch = child.Element("pitch");
                    var isRest = child.Element("rest") is not null;
                    var step = pitch?.Element("step")?.Value;
                    var alter = IntegerValue(pitch?.Element("alter"));
                    var octave = IntegerValue(pitch?.Element("octave"));
                    var eventKind = isRest ? "rest" : "note";
                    var staff = child.Element("staff")?.Value ?? "1";
                    var type = child.Element("type")?.Value;
                    var dots = child.Elements("dot").Count();
                    var accidental = child.Element("accidental")?.Value;

                    result.Add(new MusicXmlEvent(
                        partId,
                        measureNumber,
                        onset,
                        voice,
                        staff,
                        eventKind,
                        step,
                        alter,
                        octave,
                        duration,
                        type,
                        dots,
                        isChord,
                        accidental,
                        CompactXml(child)));

                    if (!isChord)
                    {
                        lastOnsetByVoice[voice] = onset;
                        cursor += duration;
                    }
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<QualityDifference> Match(
        IReadOnlyList<MusicXmlEvent> expected,
        IReadOnlyList<MusicXmlEvent> actual)
    {
        var rows = new List<QualityDifference>();
        var remaining = actual.Select((item, index) => (item, index)).ToDictionary(x => x.index, x => x.item);

        foreach (var wanted in expected)
        {
            var candidates = remaining
                .Where(x => x.Value.Measure == wanted.Measure &&
                            x.Value.Part == wanted.Part &&
                            x.Value.Kind == wanted.Kind)
                .Select(x => new
                {
                    x.Key,
                    Event = x.Value,
                    PositionDistance = Math.Abs(x.Value.Position - wanted.Position),
                    IdentityPenalty = IdentityPenalty(wanted, x.Value)
                })
                .OrderBy(x => x.PositionDistance <= PositionTolerance ? 0 : 1)
                .ThenBy(x => x.PositionDistance)
                .ThenBy(x => x.IdentityPenalty)
                .FirstOrDefault();

            if (candidates is null || candidates.PositionDistance > 0.5m)
            {
                rows.Add(QualityDifference.Missing(wanted));
                continue;
            }

            remaining.Remove(candidates.Key);
            var differences = DescribeDifferences(wanted, candidates.Event);
            rows.Add(differences.Count == 0
                ? QualityDifference.Match(wanted, candidates.Event)
                : QualityDifference.Mismatch(wanted, candidates.Event, differences));
        }

        rows.AddRange(remaining.Values.Select(QualityDifference.Extra));
        return rows
            .OrderBy(x => MeasureSortKey(x.Measure))
            .ThenBy(x => x.Position)
            .ThenBy(x => x.Status)
            .ToArray();
    }

    private static int IdentityPenalty(MusicXmlEvent expected, MusicXmlEvent actual)
    {
        if (expected.Kind == "rest") return 0;
        var penalty = 0;
        if (expected.Step != actual.Step) penalty += 4;
        if (expected.Octave != actual.Octave) penalty += 3;
        if (expected.Alter != actual.Alter) penalty += 2;
        return penalty;
    }

    private static IReadOnlyList<string> DescribeDifferences(MusicXmlEvent expected, MusicXmlEvent actual)
    {
        var result = new List<string>();
        Add(result, "position", expected.Position, actual.Position);
        Add(result, "voice", expected.Voice, actual.Voice);
        Add(result, "staff", expected.Staff, actual.Staff);
        Add(result, "step", expected.Step, actual.Step);
        Add(result, "alter", expected.Alter, actual.Alter);
        Add(result, "octave", expected.Octave, actual.Octave);
        Add(result, "duration", expected.Duration, actual.Duration);
        Add(result, "type", expected.Type, actual.Type);
        Add(result, "dots", expected.Dots, actual.Dots);
        Add(result, "chord", expected.IsChord, actual.IsChord);
        Add(result, "accidental", expected.Accidental, actual.Accidental);
        return result;
    }

    private static void Add<T>(ICollection<string> target, string name, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            target.Add($"{name}: expected={Display(expected)}, actual={Display(actual)}");
    }

    private static string Display<T>(T value) => value?.ToString() ?? "null";

    private static decimal DecimalValue(XElement? element) =>
        decimal.TryParse(element?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;

    private static int? IntegerValue(XElement? element) =>
        int.TryParse(element?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string CompactXml(XElement element) =>
        element.ToString(SaveOptions.DisableFormatting).Replace("\r", string.Empty).Replace("\n", string.Empty);

    private static int MeasureSortKey(string measure) => int.TryParse(measure, out var value) ? value : int.MaxValue;
}

public sealed record MusicXmlEvent(
    string Part,
    string Measure,
    decimal Position,
    string Voice,
    string Staff,
    string Kind,
    string? Step,
    int? Alter,
    int? Octave,
    decimal Duration,
    string? Type,
    int Dots,
    bool IsChord,
    string? Accidental,
    string Xml)
{
    public string Summary => Kind == "rest"
        ? $"rest {Type ?? "?"} duration={Duration}"
        : $"{Step}{AccidentalSuffix(Alter)}{Octave} {Type ?? "?"} duration={Duration}";

    private static string AccidentalSuffix(int? alter) => alter switch
    {
        -2 => "bb",
        -1 => "b",
        1 => "#",
        2 => "##",
        _ => string.Empty
    };
}

public sealed record QualityDifference(
    string Status,
    string Part,
    string Measure,
    decimal Position,
    string? Expected,
    string? Actual,
    string Differences,
    string? ExpectedXml,
    string? ActualXml)
{
    public static QualityDifference Match(MusicXmlEvent expected, MusicXmlEvent actual) =>
        Create("Matched", expected, actual, Array.Empty<string>());

    public static QualityDifference Mismatch(MusicXmlEvent expected, MusicXmlEvent actual, IReadOnlyList<string> differences) =>
        Create("Mismatch", expected, actual, differences);

    public static QualityDifference Missing(MusicXmlEvent expected) =>
        new("Missing", expected.Part, expected.Measure, expected.Position, expected.Summary, null,
            "Expected event was not detected", expected.Xml, null);

    public static QualityDifference Extra(MusicXmlEvent actual) =>
        new("Extra", actual.Part, actual.Measure, actual.Position, null, actual.Summary,
            "Detected event does not exist in golden MusicXML", null, actual.Xml);

    private static QualityDifference Create(
        string status,
        MusicXmlEvent expected,
        MusicXmlEvent actual,
        IReadOnlyList<string> differences) =>
        new(status, expected.Part, expected.Measure, expected.Position, expected.Summary, actual.Summary,
            string.Join("; ", differences), expected.Xml, actual.Xml);
}

public sealed record QualityMetrics(
    int Expected,
    int Actual,
    int Matched,
    int Mismatched,
    int Missing,
    int Extra,
    decimal Precision,
    decimal Recall,
    decimal F1);

public sealed record QualityComparison(QualityMetrics Metrics, IReadOnlyList<QualityDifference> Rows)
{
    public static QualityComparison Create(IReadOnlyList<QualityDifference> rows, int expected, int actual)
    {
        var matched = rows.Count(x => x.Status == "Matched");
        var mismatched = rows.Count(x => x.Status == "Mismatch");
        var missing = rows.Count(x => x.Status == "Missing");
        var extra = rows.Count(x => x.Status == "Extra");
        var recognized = matched + mismatched;
        var precision = actual == 0 ? 0 : decimal.Divide(recognized, actual);
        var recall = expected == 0 ? 0 : decimal.Divide(recognized, expected);
        var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        return new QualityComparison(
            new QualityMetrics(expected, actual, matched, mismatched, missing, extra, precision, recall, f1),
            rows);
    }
}
