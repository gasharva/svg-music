using System.CodeDom;
using System.CodeDom.Compiler;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Microsoft.CSharp;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: MusicXmlModelGenerator <schema-dir> <output.cs> <namespace>");
    return 2;
}

var schemaDir = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var targetNamespace = args[2];

var schemas = new XmlSchemas();
foreach (var fileName in new[] { "xml.xsd", "xlink.xsd", "musicxml.xsd" })
{
    var path = Path.Combine(schemaDir, fileName);
    using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
    var schema = XmlSchema.Read(reader, (_, e) => Console.Error.WriteLine(e.Message))
        ?? throw new InvalidDataException($"Could not read schema {path}");
    schemas.Add(schema);
}

schemas.Compile((_, e) => Console.Error.WriteLine(e.Message), fullCompile: true);

var codeNamespace = new CodeNamespace(targetNamespace);
var compileUnit = new CodeCompileUnit();
compileUnit.Namespaces.Add(codeNamespace);

var importer = new XmlSchemaImporter(schemas);
var exporter = new XmlCodeExporter(codeNamespace, compileUnit, CodeGenerationOptions.GenerateProperties);
var exported = new HashSet<string>(StringComparer.Ordinal);

foreach (XmlSchema schema in schemas)
{
    foreach (XmlSchemaType schemaType in schema.SchemaTypes.Values)
    {
        if (schemaType.QualifiedName.IsEmpty)
            continue;
        var key = "T:" + schemaType.QualifiedName;
        if (!exported.Add(key))
            continue;
        exporter.ExportTypeMapping(importer.ImportSchemaType(schemaType.QualifiedName));
    }

    foreach (XmlSchemaElement element in schema.Elements.Values)
    {
        if (element.QualifiedName.IsEmpty)
            continue;
        var key = "E:" + element.QualifiedName;
        if (!exported.Add(key))
            continue;
        exporter.ExportTypeMapping(importer.ImportTypeMapping(element.QualifiedName));
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using var provider = new CSharpCodeProvider();
using var writer = new StreamWriter(outputPath, false, new System.Text.UTF8Encoding(false));
provider.GenerateCodeFromCompileUnit(
    compileUnit,
    writer,
    new CodeGeneratorOptions
    {
        BracingStyle = "C",
        BlankLinesBetweenMembers = true
    });

Console.WriteLine($"Generated xsd.exe-style model: {outputPath}");
return 0;
