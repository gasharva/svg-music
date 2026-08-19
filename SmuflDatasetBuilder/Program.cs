using System.Net;
using System.Text;
using System.Text.Json.Nodes;

const string GlyphNamesUrl = "https://raw.githubusercontent.com/w3c/smufl/gh-pages/metadata/glyphnames.json";
const string ClassesUrl = "https://raw.githubusercontent.com/w3c/smufl/gh-pages/metadata/classes.json";

// Canonical SMuFL glyph names selected for the first recognition dataset.
// No project-specific aliases: the dataset label is the SMuFL glyph name itself.
var selectedGlyphNames = new HashSet<string>(StringComparer.Ordinal)
{
    "fClef",
    "gClef",
    "coda",

    "timeSig1",
    "timeSig2",
    "timeSig4",
    "timeSig6",
    "timeSig8",
    "timeSig9",

    "tuplet1",
    "tuplet3",
    "tuplet4",
    "tuplet5",

    "accidentalDoubleFlat",
    "accidentalDoubleSharp",
    "accidentalFlat",
    "accidentalNatural",
    "accidentalSharp",

    "flag8thUp",
    "flag8thDown",
    "flag16thUp",
    "flag16thDown",

    "dynamicForte",
    "dynamicFFF",
    "dynamicMF",
    "dynamicMP",
    "dynamicPiano",
    "dynamicPPP",
    "dynamicSforzando",
    "dynamicSforzato",

    "articMarcatoAbove",
    "articMarcatoBelow",

    "ornamentMordent",
    "ornamentShortTrill",
    "ornamentTurn",

    "keyboardPedalPed",
    "keyboardPedalUp",

    "rest16th",
    "restQuarter",
    "rest8th",

    // We intentionally keep every SMuFL tremolo glyph selected. They can later be
    // collapsed into one recognition category without inventing aliases here.
    "tremolo1",
    "tremolo2",
    "tremolo3",
    "tremolo4",
    "tremolo5"
};

var outputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outputDirectory);

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("svg-music-smufl-dataset-builder/1.0");

Console.WriteLine("Downloading official SMuFL metadata...");
var glyphJson = await http.GetStringAsync(GlyphNamesUrl);
var classJson = await http.GetStringAsync(ClassesUrl);

var glyphs = ParseGlyphs(glyphJson);
var classes = ParseClasses(classJson);

var classesByGlyph = classes
    .SelectMany(c => c.GlyphNames.Select(g => (Glyph: g, Class: c.Name)))
    .GroupBy(x => x.Glyph, StringComparer.Ordinal)
    .ToDictionary(
        g => g.Key,
        g => g.Select(x => x.Class).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
        StringComparer.Ordinal);

var knownNames = glyphs.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
var unknownSelections = selectedGlyphNames.Where(x => !knownNames.Contains(x)).OrderBy(x => x).ToArray();
if (unknownSelections.Length > 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("WARNING: selected names absent from current SMuFL metadata:");
    foreach (var name in unknownSelections)
        Console.WriteLine($"  {name}");
    Console.ResetColor();
}

var selectedGlyphs = glyphs.Where(x => selectedGlyphNames.Contains(x.Name)).ToArray();

var glyphCsvPath = Path.Combine(outputDirectory, "smufl-glyphs.csv");
var selectedCsvPath = Path.Combine(outputDirectory, "selected-glyphs.csv");
var classCsvPath = Path.Combine(outputDirectory, "smufl-classes.csv");
var htmlPath = Path.Combine(outputDirectory, "smufl-inventory.html");

WriteGlyphCsv(glyphCsvPath, glyphs, classesByGlyph);
WriteGlyphCsv(selectedCsvPath, selectedGlyphs, classesByGlyph);
WriteClassCsv(classCsvPath, classes);
WriteHtml(htmlPath, glyphs, classes, classesByGlyph, selectedGlyphNames);

Console.WriteLine($"Glyphs:   {glyphs.Count}");
Console.WriteLine($"Selected: {selectedGlyphs.Length}/{selectedGlyphNames.Count}");
Console.WriteLine($"Classes:  {classes.Count}");
Console.WriteLine();
Console.WriteLine($"HTML:         {htmlPath}");
Console.WriteLine($"Glyph CSV:    {glyphCsvPath}");
Console.WriteLine($"Selected CSV: {selectedCsvPath}");
Console.WriteLine($"Class CSV:    {classCsvPath}");

static List<SmuflGlyph> ParseGlyphs(string json)
{
    var root = JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidDataException("SMuFL glyphnames.json is not a JSON object.");

    return root
        .Select(x =>
        {
            var value = x.Value?.AsObject();
            return new SmuflGlyph(
                x.Key,
                value?["codepoint"]?.GetValue<string>() ?? string.Empty,
                value?["description"]?.GetValue<string>() ?? string.Empty);
        })
        .OrderBy(x => x.Name, StringComparer.Ordinal)
        .ToList();
}

static List<SmuflClass> ParseClasses(string json)
{
    var root = JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidDataException("SMuFL classes.json is not a JSON object.");

    var result = new List<SmuflClass>();
    foreach (var property in root)
    {
        var glyphs = property.Value switch
        {
            JsonArray array => ReadStringArray(array),
            JsonObject obj when obj["glyphs"] is JsonArray array => ReadStringArray(array),
            _ => []
        };
        result.Add(new SmuflClass(property.Key, glyphs));
    }
    return result.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
}

static string[] ReadStringArray(JsonArray array) => array
    .Select(x => x?.GetValue<string>())
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Select(x => x!)
    .Distinct(StringComparer.Ordinal)
    .OrderBy(x => x, StringComparer.Ordinal)
    .ToArray();

static void WriteGlyphCsv(string path, IEnumerable<SmuflGlyph> glyphs, IReadOnlyDictionary<string, string[]> classesByGlyph)
{
    var sb = new StringBuilder();
    sb.AppendLine("name,codepoint,description,smuflClasses");
    foreach (var glyph in glyphs)
    {
        classesByGlyph.TryGetValue(glyph.Name, out var classes);
        sb.Append(Csv(glyph.Name)).Append(',')
            .Append(Csv(glyph.Codepoint)).Append(',')
            .Append(Csv(glyph.Description)).Append(',')
            .Append(Csv(string.Join("; ", classes ?? []))).AppendLine();
    }
    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
}

static void WriteClassCsv(string path, IReadOnlyList<SmuflClass> classes)
{
    var sb = new StringBuilder();
    sb.AppendLine("class,glyphCount,glyphs");
    foreach (var group in classes)
        sb.Append(Csv(group.Name)).Append(',').Append(group.GlyphNames.Length).Append(',').Append(Csv(string.Join("; ", group.GlyphNames))).AppendLine();
    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
}

static void WriteHtml(string path, IReadOnlyList<SmuflGlyph> glyphs, IReadOnlyList<SmuflClass> classes,
    IReadOnlyDictionary<string, string[]> classesByGlyph, IReadOnlySet<string> selected)
{
    var sb = new StringBuilder();
    sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>SMuFL inventory</title>");
    sb.AppendLine("<style>body{font-family:system-ui,Arial,sans-serif;margin:24px;color:#222;background:#fafafa}input{font:inherit;padding:8px 10px;width:min(520px,90vw);border:1px solid #bbb;border-radius:6px}.stats{color:#666}.toolbar{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:14px 0}.toolbar label{display:flex;gap:6px;align-items:center}.toolbar input[type=checkbox]{width:auto}.tabs{display:flex;gap:8px;margin:20px 0}.tabs button{font:inherit;padding:8px 14px}.panel{display:none}.panel.active{display:block}table{border-collapse:collapse;width:100%;background:#fff}th,td{border-bottom:1px solid #ddd;text-align:left;padding:7px 9px;vertical-align:top}th{position:sticky;top:0;background:#eee}.mono{font-family:ui-monospace,Consolas,monospace}.tag{display:inline-block;padding:2px 6px;margin:1px 3px 1px 0;border-radius:10px;background:#eee;font-size:11px}.chosen{background:#e8f6e8}.yes{font-weight:700}.muted{color:#777}.class-card{background:#fff;border:1px solid #ddd;border-radius:8px;padding:12px;margin:10px 0}.glyphs{font:12px ui-monospace,Consolas,monospace;line-height:1.6}</style></head><body>");
    sb.AppendLine("<h1>SMuFL inventory</h1>");
    sb.AppendLine($"<p class=\"stats\"><b>{glyphs.Count}</b> canonical glyph names · <b>{selected.Count}</b> selected for the first dataset · <b>{classes.Count}</b> SMuFL classes/groups.</p>");
    sb.AppendLine("<p><b>Dataset labels use canonical SMuFL glyph names only.</b> No bass/treble/dgt/... aliases are introduced.</p>");
    sb.AppendLine("<div class=\"toolbar\"><input id=\"q\" placeholder=\"Filter: clef, accidental, tuplet, tremolo...\" autofocus><label><input id=\"selectedOnly\" type=\"checkbox\"> selected only</label></div>");
    sb.AppendLine("<div class=\"tabs\"><button data-tab=\"glyphs\">Glyphs</button><button data-tab=\"classes\">SMuFL classes</button></div>");
    sb.AppendLine("<section id=\"glyphs\" class=\"panel active\"><table><thead><tr><th>Selected</th><th>Name</th><th>Codepoint</th><th>Description</th><th>SMuFL classes</th></tr></thead><tbody>");
    foreach (var glyph in glyphs)
    {
        classesByGlyph.TryGetValue(glyph.Name, out var glyphClasses);
        var isSelected = selected.Contains(glyph.Name);
        var search = $"{glyph.Name} {glyph.Codepoint} {glyph.Description} {string.Join(' ', glyphClasses ?? [])}".ToLowerInvariant();
        sb.Append($"<tr class=\"{(isSelected ? "chosen" : "")}\" data-selected=\"{(isSelected ? "1" : "0")}\" data-search=\"{H(search)}\"><td class=\"yes\">{(isSelected ? "✓" : "")}</td><td class=\"mono\">{H(glyph.Name)}</td><td class=\"mono\">{H(glyph.Codepoint)}</td><td>{H(glyph.Description)}</td><td>");
        foreach (var c in glyphClasses ?? []) sb.Append($"<span class=\"tag\">{H(c)}</span>");
        sb.AppendLine("</td></tr>");
    }
    sb.AppendLine("</tbody></table></section><section id=\"classes\" class=\"panel\">");
    foreach (var group in classes)
    {
        var search = $"{group.Name} {string.Join(' ', group.GlyphNames)}".ToLowerInvariant();
        sb.AppendLine($"<div class=\"class-card\" data-search=\"{H(search)}\"><h3>{H(group.Name)} <span class=\"muted\">({group.GlyphNames.Length})</span></h3><div class=\"glyphs\">{H(string.Join(", ", group.GlyphNames))}</div></div>");
    }
    sb.AppendLine("</section>");
    sb.AppendLine("<script>const q=document.querySelector('#q'),so=document.querySelector('#selectedOnly');function filter(){const s=q.value.trim().toLowerCase();document.querySelectorAll('.panel.active [data-search]').forEach(x=>{const text=!s||x.dataset.search.includes(s);const sel=!so.checked||x.dataset.selected==='1';x.style.display=text&&sel?'':'none'})}q.addEventListener('input',filter);so.addEventListener('change',filter);document.querySelectorAll('[data-tab]').forEach(b=>b.onclick=()=>{document.querySelectorAll('.panel').forEach(x=>x.classList.remove('active'));document.querySelector('#'+b.dataset.tab).classList.add('active');filter()});</script></body></html>");
    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
}

static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
static string H(string value) => WebUtility.HtmlEncode(value);

internal sealed record SmuflGlyph(string Name, string Codepoint, string Description);
internal sealed record SmuflClass(string Name, string[] GlyphNames);
