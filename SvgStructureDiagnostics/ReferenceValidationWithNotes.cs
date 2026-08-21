using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using MusicStructure;
using SvgStructure.Models;

namespace SvgStructure.Services;

public static class ReferenceValidationWithNotes
{
    public static ReferenceValidationResult? Run(string svgPath, string itemDirectory, SvgStructureResolution resolved)
    {
        var baseline = ReferenceValidation.Run(svgPath, itemDirectory, resolved);
        if (baseline is null)
            return null;

        var score = new NoteBuilder().Build(SvgToMusicStructureAdapter.Convert(resolved));
        var comparison = CompareNotes(baseline.ReferencePath, score);
        AppendNoteChecks(baseline.HtmlPath, comparison.Rows, comparison.Matched, comparison.Expected, comparison.Extra);
        AppendNotesToMusicXml(baseline.GeneratedMusicXmlPath, score);

        return baseline with
        {
            Summaries = baseline.Summaries.Concat(new[]
            {
                new ResolverCheckSummary("NoteBuilder", comparison.Matched, comparison.Expected, comparison.Extra)
            }).ToArray()
        };
    }

    private static NoteComparison CompareNotes(string referencePath, MusicScore score)
    {
        var doc = XDocument.Load(referencePath, LoadOptions.None);
        var expected = ReadReferenceNotes(doc);
        var actual = score.Notes.ToArray();
        var used = new HashSet<int>();
        var rows = new List<NoteCheckRow>();
        var matched = 0;

        foreach (var exp in expected.OrderBy(x => x.Measure).ThenBy(x => x.Staff).ThenBy(x => x.X ?? decimal.MaxValue))
        {
            var candidates = actual.Select((note, index) => (note, index))
                .Where(x => !used.Contains(x.index) && x.note.Measure == exp.Measure && x.note.Staff == exp.Staff && PitchKey(x.note) == exp.Pitch)
                .OrderBy(x => Math.Abs((decimal)(x.note.LogicalX ?? 0) - (exp.X ?? 0)))
                .ToArray();

            if (candidates.Length == 0)
            {
                rows.Add(new("missing", exp.Measure, exp.Staff, exp.Label, "—", "Reference note was not built from the recognized SVG symbols."));
                continue;
            }

            var candidate = candidates[0];
            used.Add(candidate.index);
            var differences = Differences(exp, candidate.note);
            if (differences.Count == 0)
            {
                matched++;
                rows.Add(new("ok", exp.Measure, exp.Staff, exp.Label, Label(candidate.note), "Matched pitch, note value, stem, accidental, dots and resolved beams."));
            }
            else
            {
                rows.Add(new("missing", exp.Measure, exp.Staff, exp.Label, Label(candidate.note), string.Join("; ", differences)));
            }
        }

        for (var i = 0; i < actual.Length; i++)
        {
            if (used.Contains(i))
                continue;
            var note = actual[i];
            rows.Add(new("extra", note.Measure, note.Staff, "—", Label(note), "Built note has no matching reference note with the same pitch on this staff/measure."));
        }

        return new NoteComparison(matched, expected.Count, actual.Length - used.Count, rows);
    }

    private static List<string> Differences(ReferenceNote expected, MusicNote actual)
    {
        var result = new List<string>();
        if (!string.Equals(expected.Type, actual.Type, StringComparison.OrdinalIgnoreCase)) result.Add($"type expected {expected.Type ?? "—"}, got {actual.Type}");
        if (actual.Stem is not null && !string.Equals(expected.Stem, actual.Stem.ToString(), StringComparison.OrdinalIgnoreCase)) result.Add($"stem expected {expected.Stem ?? "—"}, got {actual.Stem.ToString()!.ToLowerInvariant()}");
        if (actual.Accidental is not null && !string.Equals(expected.Accidental, AccidentalText(actual.Accidental.Value), StringComparison.OrdinalIgnoreCase)) result.Add($"accidental expected {expected.Accidental ?? "—"}, got {AccidentalText(actual.Accidental.Value)}");
        if (expected.DotCount != actual.DotCount) result.Add($"dots expected {expected.DotCount}, got {actual.DotCount}");
        if (actual.Beams.Count > 0)
        {
            var e = string.Join(",", expected.Beams.OrderBy(x => x.Level).Select(x => $"{x.Level}:{x.Position}"));
            var a = string.Join(",", actual.Beams.OrderBy(x => x.Level).Select(x => $"{x.Level}:{x.Position.ToString().ToLowerInvariant()}"));
            if (!string.Equals(e, a, StringComparison.OrdinalIgnoreCase)) result.Add($"beams expected [{e}], got [{a}]");
        }
        return result;
    }

    private static IReadOnlyList<ReferenceNote> ReadReferenceNotes(XDocument doc)
    {
        var result = new List<ReferenceNote>();
        var partOffset = 0;
        foreach (var part in doc.Root!.Elements().Where(x => x.Name.LocalName == "part"))
        {
            var measures = part.Elements().Where(x => x.Name.LocalName == "measure").ToArray();
            var staffCount = Math.Max(1, part.Descendants().Where(x => x.Name.LocalName == "staves").Select(x => ParseInt(x.Value) ?? 1).DefaultIfEmpty(1).Max());
            for (var m = 0; m < measures.Length; m++)
            {
                foreach (var note in measures[m].Elements().Where(x => x.Name.LocalName == "note" && !x.Elements().Any(c => c.Name.LocalName == "rest")))
                {
                    var pitch = note.Elements().FirstOrDefault(x => x.Name.LocalName == "pitch");
                    if (pitch is null) continue;
                    var step = ChildValue(pitch, "step");
                    var octave = ParseInt(ChildValue(pitch, "octave"));
                    if (step is null || octave is null) continue;
                    var alter = ParseInt(ChildValue(pitch, "alter")) ?? 0;
                    var staff = ParseInt(ChildValue(note, "staff")) ?? 1;
                    var beams = note.Elements().Where(x => x.Name.LocalName == "beam")
                        .Select(x => new RefBeam(ParseInt(x.Attributes().FirstOrDefault(a => a.Name.LocalName == "number")?.Value) ?? 1, x.Value.Trim()))
                        .ToArray();
                    result.Add(new ReferenceNote(
                        partOffset + staff, m + 1,
                        decimal.TryParse(note.Attributes().FirstOrDefault(a => a.Name.LocalName == "default-x")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var dx) ? dx : null,
                        $"{step.ToUpperInvariant()}{(alter == 0 ? "" : alter > 0 ? $"+{alter}" : alter.ToString())}{octave}",
                        ChildValue(note, "type"), ChildValue(note, "stem"), ChildValue(note, "accidental"),
                        note.Elements().Count(x => x.Name.LocalName == "dot"), beams));
                }
            }
            partOffset += staffCount;
        }
        return result;
    }

    private static void AppendNoteChecks(string htmlPath, IReadOnlyList<NoteCheckRow> rows, int matched, int expected, int extra)
    {
        var html = File.ReadAllText(htmlPath);
        var allGreen = rows.All(x => x.State == "ok");
        var sb = new StringBuilder();
        sb.Append($"<details class=\"resolver{(allGreen ? "" : " problem")}\"{(allGreen ? "" : " open")}><summary>NoteBuilder — {matched}/{expected} ok");
        if (!allGreen) sb.Append($" — {rows.Count(x => x.State != "ok")} problems");
        if (extra > 0) sb.Append($" (+{extra} extra)");
        sb.Append("</summary><table><thead><tr><th>Builder</th><th>Measure</th><th>Part</th><th>Expected</th><th>Actual</th><th>Problem</th></tr></thead><tbody>");
        var seq = 0;
        foreach (var group in rows.OrderBy(x => x.Measure).ThenBy(x => x.Staff).GroupBy(x => x.Measure))
        {
            sb.Append($"<tr class=\"measure\"><td colspan=\"6\">NoteBuilder — measure {group.Key}</td></tr>");
            foreach (var row in group)
            {
                var id = $"check-notebuilder-m{row.Measure}-p{row.Staff}-{++seq}";
                sb.Append($"<tr id=\"{id}\" class=\"{row.State} anchor\"><td>NoteBuilder</td><td>{row.Measure}</td><td>{row.Staff}</td><td>{WebUtility.HtmlEncode(row.Expected)}</td><td>{WebUtility.HtmlEncode(row.Actual)}</td><td>{WebUtility.HtmlEncode(row.Description)} <button class=\"row-link\" onclick=\"copyCheckLink('{id}',this)\">Link</button></td></tr>");
            }
        }
        sb.Append("</tbody></table></details>");
        html = html.Replace("</body>", sb + "</body>", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(htmlPath, html);
    }

    private static void AppendNotesToMusicXml(string path, MusicScore score)
    {
        var doc = XDocument.Load(path);
        var part = doc.Root!.Elements().First(x => x.Name.LocalName == "part");
        var measures = part.Elements().Where(x => x.Name.LocalName == "measure").ToDictionary(x => ParseInt(x.Attribute("number")?.Value) ?? 0);
        if (measures.TryGetValue(1, out var firstMeasure))
        {
            var attributes = firstMeasure.Elements().FirstOrDefault(x => x.Name.LocalName == "attributes");
            if (attributes is not null && !attributes.Elements().Any(x => x.Name.LocalName == "divisions")) attributes.AddFirst(new XElement("divisions", 32));
        }

        foreach (var note in score.Notes)
        {
            if (!measures.TryGetValue(note.Measure, out var measure)) continue;
            var pitch = new XElement("pitch", new XElement("step", note.Pitch.Step));
            if (note.Pitch.Alter != 0) pitch.Add(new XElement("alter", note.Pitch.Alter));
            pitch.Add(new XElement("octave", note.Pitch.Octave));
            var noteEl = new XElement("note");
            if (note.IsChordTone) noteEl.Add(new XElement("chord"));
            noteEl.Add(pitch);
            noteEl.Add(new XElement("duration", Duration(note)));
            noteEl.Add(new XElement("type", note.Type));
            for (var i = 0; i < note.DotCount; i++) noteEl.Add(new XElement("dot"));
            if (note.Accidental is not null) noteEl.Add(new XElement("accidental", AccidentalText(note.Accidental.Value)));
            if (note.Stem is not null) noteEl.Add(new XElement("stem", note.Stem.ToString()!.ToLowerInvariant()));
            noteEl.Add(new XElement("staff", note.Staff));
            foreach (var beam in note.Beams) noteEl.Add(new XElement("beam", new XAttribute("number", beam.Level), beam.Position.ToString().ToLowerInvariant()));
            measure.Add(noteEl);
        }
        doc.Save(path);
    }

    private static int Duration(MusicNote note)
    {
        var denominator = note.Type switch { "whole" => 1, "half" => 2, "quarter" => 4, "eighth" => 8, "16th" => 16, "32nd" => 32, "64th" => 64, _ => 4 };
        var baseDuration = 128 / denominator;
        return note.DotCount == 0 ? baseDuration : note.DotCount == 1 ? baseDuration * 3 / 2 : baseDuration * 7 / 4;
    }

    private static string PitchKey(MusicNote note) => note.Pitch.ToString();
    private static string Label(MusicNote note) => $"{note.Pitch} {note.Type}" + (note.DotCount > 0 ? new string('.', note.DotCount) : "") + (note.Stem is null ? "" : $" stem={note.Stem.ToString()!.ToLowerInvariant()}");
    private static string AccidentalText(MusicAccidental a) => a switch { MusicAccidental.Flat => "flat", MusicAccidental.Sharp => "sharp", MusicAccidental.Natural => "natural", MusicAccidental.DoubleSharp => "double-sharp", MusicAccidental.DoubleFlat => "flat-flat", _ => "" };
    private static string? ChildValue(XContainer e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim();
    private static int? ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private sealed record RefBeam(int Level, string Position);
    private sealed record ReferenceNote(int Staff, int Measure, decimal? X, string Pitch, string? Type, string? Stem, string? Accidental, int DotCount, IReadOnlyList<RefBeam> Beams)
    {
        public string Label => $"{Pitch} {Type ?? "?"}" + (DotCount > 0 ? new string('.', DotCount) : "") + (Stem is null ? "" : $" stem={Stem}");
    }
    private sealed record NoteCheckRow(string State, int Measure, int Staff, string Expected, string Actual, string Description);
    private sealed record NoteComparison(int Matched, int Expected, int Extra, IReadOnlyList<NoteCheckRow> Rows);
}
