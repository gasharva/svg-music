using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace MusicXml;

/// <summary>
/// XML boundary for the application. XML stays here; callers only see the compact domain model.
/// The complete source document is kept as an opaque backing store so unknown MusicXML content
/// survives round-trip unchanged.
/// </summary>
public sealed class MusicXmlReader
{
    public MusicXmlDocument Read(string path) => Read(path, validate: false, schemaDirectory: null);

    public MusicXmlDocument Read(string path, bool validate, string? schemaDirectory)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null
        };

        using var stream = File.OpenRead(path);
        using var xml = XmlReader.Create(stream, settings);
        var document = XDocument.Load(xml, LoadOptions.PreserveWhitespace);

        if (validate)
            Validate(document, schemaDirectory ?? throw new ArgumentNullException(nameof(schemaDirectory)));

        var root = document.Root
            ?? throw new InvalidDataException("MusicXML document has no root element.");
        if (!string.Equals(root.Name.LocalName, "score-partwise", StringComparison.Ordinal))
            throw new InvalidDataException($"Expected score-partwise root, got '{root.Name.LocalName}'.");

        var version = Attribute(root, "version");
        var parts = Children(root, "part")
            .Select((part, index) => ReadPart(part, index))
            .ToArray();

        return new MusicXmlDocument(document, version, parts);
    }

    private static MusicXmlPart ReadPart(XElement part, int index)
    {
        var id = Attribute(part, "id") ?? $"part-{index + 1}";
        var measures = Children(part, "measure")
            .Select((measure, measureIndex) => ReadMeasure(measure, measureIndex))
            .ToArray();
        return new MusicXmlPart(id, measures);
    }

    private static MusicXmlMeasure ReadMeasure(XElement measure, int index)
    {
        var number = Attribute(measure, "number") ?? (index + 1).ToString(CultureInfo.InvariantCulture);
        var notes = Children(measure, "note").Select(ReadNote).ToArray();
        return new MusicXmlMeasure(number, notes);
    }

    private static MusicXmlNote ReadNote(XElement note)
    {
        var pitch = Child(note, "pitch");
        return new MusicXmlNote(
            DefaultX: DecimalAttribute(note, "default-x"),
            DefaultY: DecimalAttribute(note, "default-y"),
            IsChordTone: Child(note, "chord") is not null,
            IsRest: Child(note, "rest") is not null,
            Step: pitch is null ? null : Text(pitch, "step"),
            Alter: pitch is null ? null : DecimalElement(pitch, "alter"),
            Octave: pitch is null ? null : IntElement(pitch, "octave"),
            Duration: DecimalElement(note, "duration"),
            Voice: Text(note, "voice"),
            Type: Text(note, "type"),
            Accidental: Text(note, "accidental"),
            Stem: Text(note, "stem"),
            Staff: IntElement(note, "staff"));
    }

    private static void Validate(XDocument document, string schemaDirectory)
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };
        foreach (var name in new[] { "xml.xsd", "xlink.xsd", "musicxml.xsd" })
        {
            var path = Path.Combine(schemaDirectory, name);
            if (!File.Exists(path))
                throw new FileNotFoundException($"MusicXML validation schema not found: {path}", path);
            schemas.Add(null, path);
        }

        var errors = new List<string>();
        document.Validate(schemas, (_, e) => errors.Add(e.Message), addSchemaInfo: false);
        if (errors.Count > 0)
            throw new InvalidDataException("MusicXML schema validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    private static IEnumerable<XElement> Children(XContainer parent, string localName) =>
        parent.Elements().Where(x => string.Equals(x.Name.LocalName, localName, StringComparison.Ordinal));

    private static XElement? Child(XContainer parent, string localName) => Children(parent, localName).FirstOrDefault();

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(x => string.Equals(x.Name.LocalName, localName, StringComparison.Ordinal))?.Value;

    private static string? Text(XContainer parent, string localName) => Child(parent, localName)?.Value;

    private static decimal? DecimalAttribute(XElement parent, string name) => ParseDecimal(Attribute(parent, name));
    private static decimal? DecimalElement(XContainer parent, string name) => ParseDecimal(Text(parent, name));
    private static int? IntElement(XContainer parent, string name) => ParseInt(Text(parent, name));

    private static decimal? ParseDecimal(string? text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
