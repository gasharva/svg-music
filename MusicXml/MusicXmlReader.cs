using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;

namespace MusicXml;

/// <summary>
/// XML boundary for the application. The concrete serialization model is generated from the
/// official MusicXML 4.0 XSD at build time; the rest of the code only sees the small domain model.
/// </summary>
public sealed class MusicXmlReader
{
    public MusicXmlDocument Read(string path)
    {
        var rootType = FindScorePartwiseType();
        var serializer = new XmlSerializer(rootType);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null
        };

        using var stream = File.OpenRead(path);
        using var xml = XmlReader.Create(stream, settings);
        var root = serializer.Deserialize(xml)
            ?? throw new InvalidDataException("Could not deserialize MusicXML score-partwise document.");

        var version = AttributeText(root, "version");
        var parts = Elements(root, "part")
            .Select((part, index) => ReadPart(part, index))
            .ToArray();

        return new MusicXmlDocument(root, version, parts);
    }

    private static MusicXmlPart ReadPart(object part, int index)
    {
        var id = AttributeText(part, "id") ?? $"part-{index + 1}";
        var measures = Elements(part, "measure")
            .Select((measure, measureIndex) => ReadMeasure(measure, measureIndex))
            .ToArray();
        return new MusicXmlPart(id, measures);
    }

    private static MusicXmlMeasure ReadMeasure(object measure, int index)
    {
        var number = AttributeText(measure, "number") ?? (index + 1).ToString(CultureInfo.InvariantCulture);
        var notes = Elements(measure, "note").Select(ReadNote).ToArray();
        return new MusicXmlMeasure(number, notes);
    }

    private static MusicXmlNote ReadNote(object note)
    {
        var pitch = Element(note, "pitch");
        return new MusicXmlNote(
            DefaultX: DecimalAttribute(note, "default-x"),
            DefaultY: DecimalAttribute(note, "default-y"),
            IsChordTone: Element(note, "chord") is not null,
            IsRest: Element(note, "rest") is not null,
            Step: pitch is null ? null : ElementText(pitch, "step"),
            Alter: pitch is null ? null : DecimalElement(pitch, "alter"),
            Octave: pitch is null ? null : IntElement(pitch, "octave"),
            Duration: DecimalElement(note, "duration"),
            Voice: ElementText(note, "voice"),
            Type: ElementText(note, "type"),
            Accidental: ElementText(note, "accidental"),
            Stem: ElementText(note, "stem"),
            Staff: IntElement(note, "staff"));
    }

    private static Type FindScorePartwiseType()
    {
        var generatedAssembly = typeof(MusicXmlReader).Assembly;
        return generatedAssembly.GetTypes()
            .Where(x => x.Namespace?.StartsWith("MusicXml.Generated", StringComparison.Ordinal) == true)
            .FirstOrDefault(x => x.GetCustomAttribute<XmlRootAttribute>()?.ElementName == "score-partwise")
            ?? throw new InvalidOperationException(
                "Generated MusicXML 4.0 score-partwise model was not found. Build-time XSD generation did not run.");
    }

    private static object? Element(object parent, string xmlName) => Elements(parent, xmlName).FirstOrDefault();

    private static IEnumerable<object> Elements(object parent, string xmlName)
    {
        foreach (var property in parent.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !IsSpecified(parent, property))
                continue;

            var elementAttributes = property.GetCustomAttributes<XmlElementAttribute>().ToArray();
            if (elementAttributes.Length == 0)
                continue;

            var value = property.GetValue(parent);
            if (value is null)
                continue;

            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable.Cast<object?>().Where(x => x is not null))
                {
                    if (MatchesElement(elementAttributes, xmlName, item!))
                        yield return item!;
                }
                continue;
            }

            if (MatchesElement(elementAttributes, xmlName, value))
                yield return value;
        }
    }

    private static bool MatchesElement(IReadOnlyList<XmlElementAttribute> attributes, string xmlName, object value) =>
        attributes.Any(attribute =>
            string.Equals(attribute.ElementName, xmlName, StringComparison.Ordinal) &&
            (attribute.Type is null || attribute.Type.IsInstanceOfType(value)));

    private static string? AttributeText(object parent, string xmlName)
    {
        foreach (var property in parent.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var attribute = property.GetCustomAttribute<XmlAttributeAttribute>();
            if (attribute is null || !string.Equals(attribute.AttributeName, xmlName, StringComparison.Ordinal))
                continue;
            if (!IsSpecified(parent, property))
                return null;
            return SerializedText(property.GetValue(parent));
        }
        return null;
    }

    private static string? ElementText(object parent, string xmlName)
    {
        var value = Element(parent, xmlName);
        return SerializedText(value);
    }

    private static string? SerializedText(object? value)
    {
        if (value is null)
            return null;

        var type = value.GetType();
        if (type.IsEnum)
        {
            var name = Enum.GetName(type, value);
            var member = name is null ? null : type.GetMember(name).FirstOrDefault();
            return member?.GetCustomAttribute<XmlEnumAttribute>()?.Name ?? name;
        }

        if (value is string or decimal or int or long or short or byte or uint or ulong or ushort or sbyte ||
            type.IsPrimitive)
            return Convert.ToString(value, CultureInfo.InvariantCulture);

        var textProperty = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x => x.GetCustomAttribute<XmlTextAttribute>() is not null)
            ?? type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);

        return textProperty is null ? Convert.ToString(value, CultureInfo.InvariantCulture) : SerializedText(textProperty.GetValue(value));
    }

    private static bool IsSpecified(object parent, PropertyInfo property)
    {
        var specified = parent.GetType().GetProperty(property.Name + "Specified", BindingFlags.Instance | BindingFlags.Public);
        return specified?.PropertyType != typeof(bool) || (bool)(specified.GetValue(parent) ?? false);
    }

    private static decimal? DecimalAttribute(object parent, string name) => ParseDecimal(AttributeText(parent, name));
    private static decimal? DecimalElement(object parent, string name) => ParseDecimal(ElementText(parent, name));
    private static int? IntElement(object parent, string name) => ParseInt(ElementText(parent, name));

    private static decimal? ParseDecimal(string? text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
