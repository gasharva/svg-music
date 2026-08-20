using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Microsoft.CSharp;

try
{
    return Run(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine("MusicXmlModelGenerator FAILED:");
    Console.Error.WriteLine(ex.ToString());
    return 1;
}

static int Run(string[] args)
{
    if (args.Length != 3)
    {
        Console.Error.WriteLine("Usage: MusicXmlModelGenerator <schema-dir> <output.cs> <namespace>");
        return 2;
    }

    var schemaDir = Path.GetFullPath(args[0]);
    var outputPath = Path.GetFullPath(args[1]);
    var targetNamespace = args[2];

    Console.WriteLine($"Schema dir: {schemaDir}");
    Console.WriteLine($"Output:     {outputPath}");
    Console.WriteLine($"Namespace:  {targetNamespace}");

    var schemas = new XmlSchemas();
    foreach (var fileName in new[] { "xml.xsd", "xlink.xsd", "musicxml.xsd" })
    {
        var path = Path.Combine(schemaDir, fileName);
        Console.WriteLine($"Reading schema: {path}");
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var schema = XmlSchema.Read(reader, (_, e) => Console.Error.WriteLine($"Schema read: {e.Severity}: {e.Message}"))
            ?? throw new InvalidDataException($"Could not read schema {path}");
        schemas.Add(schema);
    }

    Console.WriteLine("Compiling schemas...");
    schemas.Compile((_, e) => Console.Error.WriteLine($"Schema compile: {e.Severity}: {e.Message}"), fullCompile: true);

    var codeNamespace = new CodeNamespace(targetNamespace);
    var compileUnit = new CodeCompileUnit();
    compileUnit.Namespaces.Add(codeNamespace);

    var importer = new XmlSchemaImporter(schemas);
    var exporter = new XmlCodeExporter(codeNamespace, compileUnit, CodeGenerationOptions.GenerateProperties);

    // Important: behave like xsd.exe /classes and start from document roots only.
    // Exporting every schema type first can create standalone mappings that do not preserve
    // the repeated xs:choice streams used heavily by MusicXML (measure contents, part-list, etc.).
    var musicXmlSchema = schemas.Cast<XmlSchema>()
        .Single(x => string.IsNullOrEmpty(x.TargetNamespace) && x.Elements[new XmlQualifiedName("score-partwise")] is not null);

    foreach (var rootName in new[] { "score-partwise", "score-timewise" })
    {
        var qualifiedName = new XmlQualifiedName(rootName);
        if (musicXmlSchema.Elements[qualifiedName] is null)
            continue;

        Console.WriteLine($"Exporting document root: {rootName}");
        exporter.ExportTypeMapping(importer.ImportTypeMapping(qualifiedName));
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

    // Cheap but useful sanity diagnostics. A healthy xsd.exe-style MusicXML model should
    // contain polymorphic object arrays for repeated choices rather than independent arrays
    // per element kind. Keep this diagnostic visible in normal build output.
    var generated = File.ReadAllText(outputPath);
    var objectArrays = Count(generated, "object[]");
    var choiceIdentifiers = Count(generated, "XmlChoiceIdentifier");
    var noteElementMappings = Count(generated, "XmlElementAttribute(\"note\"");
    var backupElementMappings = Count(generated, "XmlElementAttribute(\"backup\"");
    var directionElementMappings = Count(generated, "XmlElementAttribute(\"direction\"");
    Console.WriteLine($"Generated model diagnostics: object[]={objectArrays}, XmlChoiceIdentifier={choiceIdentifiers}, note mappings={noteElementMappings}, backup mappings={backupElementMappings}, direction mappings={directionElementMappings}");

    return 0;
}

static int Count(string text, string needle)
{
    var count = 0;
    var index = 0;
    while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += needle.Length;
    }
    return count;
}
