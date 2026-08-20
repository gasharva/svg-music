using System.Globalization;
using System.Xml.Linq;

namespace MusicXml;

public sealed class MusicXmlReader
{
    public MusicXmlDocument Read(string path)
    {
        using var stream = File.OpenRead(path);
        var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var root = document.Root ?? throw new InvalidDataException("MusicXML document has no root element.");
        if (root.Name.LocalName != "score-partwise")
            throw new NotSupportedException($"Only score-partwise is supported by this PoC, got '{root.Name.LocalName}'.");
        return new MusicXmlDocument(document);
    }
}

public sealed class MusicXmlDocument
{
    private readonly XDocument _xml;

    internal MusicXmlDocument(XDocument xml) => _xml = xml;

    public string? Version => _xml.Root?.Attribute("version")?.Value;

    public IReadOnlyList<MusicXmlPart> Parts =>
        _xml.Root!
            .Elements("part")
            .Select((part, index) => new MusicXmlPart(
                part.Attribute("id")?.Value ?? $"part-{index + 1}",
                part.Elements("measure")
                    .Select((measure, measureIndex) => ReadMeasure(measure, measureIndex))
                    .ToArray()))
            .ToArray();

    internal XDocument Xml => _xml;

    public IReadOnlyList<MusicXmlNote> Notes => Parts.SelectMany(x => x.Measures).SelectMany(x => x.Notes).ToArray();

    private static MusicXmlMeasure ReadMeasure(XElement measure, int index)
    {
        var number = measure.Attribute("number")?.Value ?? (index + 1).ToString(CultureInfo.InvariantCulture);
        var notes = measure.Elements("note").Select(ReadNote).ToArray();
        return new MusicXmlMeasure(number, notes);
    }

    private static MusicXmlNote ReadNote(XElement note)
    {
        var pitch = note.Element("pitch");
        return new MusicXmlNote(
            DefaultX: DecimalAttribute(note, "default-x"),
            DefaultY: DecimalAttribute(note, "default-y"),
            IsChordTone: note.Element("chord") is not null,
            IsRest: note.Element("rest") is not null,
            Step: pitch?.Element("step")?.Value,
            Alter: DecimalElement(pitch, "alter"),
            Octave: IntElement(pitch, "octave"),
            Duration: DecimalElement(note, "duration"),
            Voice: note.Element("voice")?.Value,
            Type: note.Element("type")?.Value,
            Accidental: note.Element("accidental")?.Value,
            Stem: note.Element("stem")?.Value,
            Staff: IntElement(note, "staff"));
    }

    private static decimal? DecimalAttribute(XElement element, string name) =>
        decimal.TryParse(element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static decimal? DecimalElement(XElement? parent, string name) =>
        decimal.TryParse(parent?.Element(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? IntElement(XElement? parent, string name) =>
        int.TryParse(parent?.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}

public sealed record MusicXmlPart(string Id, IReadOnlyList<MusicXmlMeasure> Measures);
public sealed record MusicXmlMeasure(string Number, IReadOnlyList<MusicXmlNote> Notes);

public sealed record MusicXmlNote(
    decimal? DefaultX,
    decimal? DefaultY,
    bool IsChordTone,
    bool IsRest,
    string? Step,
    decimal? Alter,
    int? Octave,
    decimal? Duration,
    string? Voice,
    string? Type,
    string? Accidental,
    string? Stem,
    int? Staff)
{
    public string Pitch => IsRest ? "rest" : $"{Step ?? "?"}{(Alter is null or 0 ? "" : Alter > 0 ? $"+{Alter}" : Alter.ToString())}{Octave?.ToString() ?? "?"}";
}
