using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace MusicXml;

/// <summary>XML boundary that serializes the generated MusicXML 4.0 XSD model.</summary>
public sealed class MusicXmlWriter
{
    public void Write(MusicXmlDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.None
        };

        var serializer = new XmlSerializer(document.SerializationModel.GetType());
        using var writer = XmlWriter.Create(path, settings);
        serializer.Serialize(writer, document.SerializationModel);
    }
}
