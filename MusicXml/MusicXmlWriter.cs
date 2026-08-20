using System.Text;
using System.Xml;

namespace MusicXml;

public sealed class MusicXmlWriter
{
    public void Write(MusicXmlDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.None
        };

        using var writer = XmlWriter.Create(path, settings);
        document.Xml.Save(writer, SaveOptions.DisableFormatting);
    }
}
