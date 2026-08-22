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
        ResolvedMusicXmlNoteWriter.Append(baseline.GeneratedMusicXmlPath, score);

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
        var expected = ReadReferenceNotes(XDocument.Load(referencePath, LoadOptions.None));
        var actual = score.Notes.ToArray();
        var rows = new List<NoteCheckRow>();
        var matched = 0;

        var keys = expected.Select(x => x.MatchKey)
            .Concat(actual.Select(MatchKey))
            .Distinct()
            .OrderBy(x => x.Measure).ThenBy(x => x.Staff).ThenBy(x => x.Step).ThenBy(x => x.Octave)
            .ToArray();

        foreach (var key in keys)
        {
            var e = expected.Where(x => x.MatchKey == key).OrderBy(x => x.X ?? decimal.MaxValue).ToArray();
            var a = actual.Where(x => MatchKey(x) == key).OrderBy(x => x.LogicalX ?? double.MaxValue).ToArray();
            var pairCount = Math.Min(e.Length, a.Length);

            for (var i = 0; i < pairCount; i++)
            {
                var differences = Differences(e[i], a[i]);
                if (differences.Count == 0)
                {
                    matched++;
                    rows.Add(new("ok", key.Measure, key.Staff, e[i].Label, Label(a[i]), "Matched note properties recovered from SVG."));
                }
                else
                {
                    rows.Add(new("missing", key.Measure, key.Staff, e[i].Label, Label(a[i]), string.Join("; ", differences)));
                }
            }

            for (var i = pairCount; i < e.Length; i++)
                rows.Add(new("missing", key.Measure, key.Staff, e[i].Label, "—", "Reference note was not built from the recognized SVG symbols."));

            for (var i = pairCount; i < a.Length; i++)
                rows.Add(new("extra", key.Measure, key.Staff, "—", Label(a[i]), "Built note has no matching reference note at this staff/measure/pitch."));
        }

        return new NoteComparison(matched, expected.Count, rows.Count(x => x.State == "extra"), rows);
    }

    private static List<string> Differences(ReferenceNote expected, MusicNote actual)
    {
        var result = new List<string>();
        if (!string.Equals(expected.Type, actual.Type, StringComparison.OrdinalIgnoreCase))
            result.Add($"type expected {expected.Type ?? "—"}, got {actual.Type}");

        var actualStem = actual.Stem?.ToString().ToLowerInvariant();
        if (!string.Equals(expected.Stem, actualStem, StringComparison.OrdinalIgnoreCase))
            result.Add($"stem expected {expected.Stem ?? "—"}, got {actualStem ?? "—"}");

        var actualAccidental = actual.Accidental is null ? null : AccidentalText(actual.Accidental.Value);
        if (!string.Equals(expected.Accidental, actualAccidental, StringComparison.OrdinalIgnoreCase))
            result.Add($"accidental expected {expected.Accidental ?? "—"}, got {actualAccidental ?? "—"}");

        if (expected.DotCount != actual.DotCount)
            result.Add($"dots expected {expected.DotCount}, got {actual.DotCount}");

        if (expected.IsChordTone != actual.IsChordTone)
            result.Add($"chord expected {(expected.IsChordTone ? "tone" : "root")}, got {(actual.IsChordTone ? "tone" : "root")}");

        var expectedBeams = string.Join(",", expected.Beams.OrderBy(x => x.Level).Select(x => $"{x.Level}:{x.Position}"));
        var actualBeams = string.Join(",", actual.Beams.OrderBy(x => x.Level).Select(x => $"{x.Level}:{BeamText(x.Position)}"));
        if (!string.Equals(expectedBeams, actualBeams, StringComparison.OrdinalIgnoreCase))
            result.Add($"beams expected [{expectedBeams}], got [{actualBeams}]");

        var expectedSlurs = string.Join(",", expected.Slurs.OrderBy(x => x));
        var actualSlurs = string.Join(",", (actual.Slurs ?? Array.Empty<MusicSlur>())
            .Select(x => x.Type == MusicSlurType.Start ? "start" : "stop")
            .OrderBy(x => x));
        if (!string.Equals(expectedSlurs, actualSlurs, StringComparison.OrdinalIgnoreCase))
            result.Add($"slurs expected [{expectedSlurs}], got [{actualSlurs}]");

        return result;
    }

    private static IReadOnlyList<ReferenceNote> ReadReferenceNotes(XDocument doc)
    {
        var result = new List<ReferenceNote>();
        var partOffset = 0;

        foreach (var part in doc.Root!.Elements().Where(x => x.Name.LocalName == "part"))
        {
            var measures = part.Elements().Where(x => x.Name.LocalName == "measure").ToArray();
            var staffCount = Math.Max(1, part.Descendants().Where(x => x.Name.LocalName == "staves")
                .Select(x => ParseInt(x.Value) ?? 1).DefaultIfEmpty(1).Max());

            for (var m = 0; m < measures.Length; m++)
            {
                foreach (var element in measures[m].Elements().Where(x => x.Name.LocalName == "note" && !x.Elements().Any(c => c.Name.LocalName == "rest")))
                {
                    var pitch = element.Elements().FirstOrDefault(x => x.Name.LocalName == "pitch");
                    if (pitch is null)
                        continue;

                    var step = ChildValue(pitch, "step")?.ToUpperInvariant();
                    var octave = ParseInt(ChildValue(pitch, "octave"));
                    if (step is null || octave is null)
                        continue;

                    var staff = ParseInt(ChildValue(element, "staff")) ?? 1;
                    var alter = ParseInt(ChildValue(pitch, "alter")) ?? 0;
                    var accidental = ChildValue(element, "accidental");
                    var beams = element.Elements().Where(x => x.Name.LocalName == "beam")
                        .Select(x => new RefBeam(
                            ParseInt(x.Attributes().FirstOrDefault(a => a.Name.LocalName == "number")?.Value) ?? 1,
                            x.Value.Trim()))
                        .ToArray();
                    var slurs = element.Descendants()
                        .Where(x => x.Name.LocalName == "slur")
                        .Select(x => x.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .ToArray();

                    result.Add(new ReferenceNote(
                        partOffset + staff,
                        m + 1,
                        decimal.TryParse(element.Attributes().FirstOrDefault(a => a.Name.LocalName == "default-x")?.Value,
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var dx) ? dx : null,
                        step,
                        octave.Value,
                        accidental is null ? 0 : alter,
                        ChildValue(element, "type"),
                        ChildValue(element, "stem"),
                        accidental,
                        element.Elements().Count(x => x.Name.LocalName == "dot"),
                        element.Elements().Any(x => x.Name.LocalName == "chord"),
                        beams,
                        slurs));
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
                sb.Append($"<tr id=\"{id}\" class=\"{row.State} anchor\"><td>NoteBuilder</td><td>{row.Measure}</td><td>{row.Staff}</td><td>{WebUtility.HtmlEncode(row.Expected)}</td><td>{WebUtility.HtmlEncode(row.Actual)}</td><td>{WebUtility.HtmlEncode(row.Description)} <button class=\"row-link\" type=\"button\" onclick=\"copyLink('{id}',this)\">Link</button></td></tr>");
            }
        }

        sb.Append("</tbody></table></details>");
        html = html.Replace("</body>", sb + "</body>", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(htmlPath, html);
    }

    private static NoteMatchKey MatchKey(MusicNote note) => new(note.Measure, note.Staff, note.Pitch.Step, note.Pitch.Octave);

    private static string Label(MusicNote note)
    {
        var chord = note.IsChordTone ? " chord" : "";
        var slurs = (note.Slurs ?? Array.Empty<MusicSlur>()).Count == 0
            ? ""
            : $" slur={string.Join(",", note.Slurs!.Select(x => x.Type.ToString().ToLowerInvariant()))}";
        return $"{VisiblePitch(note)} {note.Type}" +
               (note.DotCount > 0 ? new string('.', note.DotCount) : "") +
               (note.Stem is null ? "" : $" stem={note.Stem.ToString()!.ToLowerInvariant()}") +
               chord + slurs;
    }

    private static string VisiblePitch(MusicNote note) => $"{note.Pitch.Step}{(note.Accidental is null ? "" : AlterText(note.Pitch.Alter))}{note.Pitch.Octave}";
    private static string AlterText(int alter) => alter == 0 ? "" : alter > 0 ? $"+{alter}" : alter.ToString(CultureInfo.InvariantCulture);
    private static string AccidentalText(MusicAccidental a) => a switch
    {
        MusicAccidental.Flat => "flat", MusicAccidental.Sharp => "sharp", MusicAccidental.Natural => "natural",
        MusicAccidental.DoubleSharp => "double-sharp", MusicAccidental.DoubleFlat => "flat-flat", _ => ""
    };
    private static string BeamText(MusicBeamPosition p) => p switch
    {
        MusicBeamPosition.Begin => "begin", MusicBeamPosition.Continue => "continue", MusicBeamPosition.End => "end",
        MusicBeamPosition.ForwardHook => "forward hook", MusicBeamPosition.BackwardHook => "backward hook", _ => ""
    };
    private static string? ChildValue(XContainer e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim();
    private static int? ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private readonly record struct NoteMatchKey(int Measure, int Staff, string Step, int Octave);
    private sealed record RefBeam(int Level, string Position);
    private sealed record ReferenceNote(
        int Staff, int Measure, decimal? X, string Step, int Octave, int ExplicitAlter,
        string? Type, string? Stem, string? Accidental, int DotCount, bool IsChordTone,
        IReadOnlyList<RefBeam> Beams, IReadOnlyList<string> Slurs)
    {
        public NoteMatchKey MatchKey => new(Measure, Staff, Step, Octave);
        public string Label => $"{Step}{(Accidental is null ? "" : AlterText(ExplicitAlter))}{Octave} {Type ?? "?"}" +
                               (DotCount > 0 ? new string('.', DotCount) : "") +
                               (Stem is null ? "" : $" stem={Stem}") +
                               (IsChordTone ? " chord" : "") +
                               (Slurs.Count == 0 ? "" : $" slur={string.Join(",", Slurs)}");
    }
    private sealed record NoteCheckRow(string State, int Measure, int Staff, string Expected, string Actual, string Description);
    private sealed record NoteComparison(int Matched, int Expected, int Extra, IReadOnlyList<NoteCheckRow> Rows);
}
