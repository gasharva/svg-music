using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MusicXml;

/// <summary>
/// XML boundary that writes the complete backing document. Unknown MusicXML elements, attributes,
/// ordering and layout data therefore survive a read/write round-trip untouched.
/// </summary>
public sealed class MusicXmlWriter
{
    public void Write(MusicXmlDocument document, string path)
    {
        if (document.BackingStore is not XDocument source)
            throw new InvalidOperationException("MusicXML document has no writable XML backing store.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Clone so writing never mutates the reader's backing tree.
        var output = new XDocument(source);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
            NewLineHandling = NewLineHandling.None
        };

        using var writer = XmlWriter.Create(path, settings);
        output.Save(writer, SaveOptions.DisableFormatting);
    }
}
