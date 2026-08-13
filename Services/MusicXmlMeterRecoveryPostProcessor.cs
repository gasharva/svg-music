using System.Xml.Linq;
using SvgToMusicXmlPoc.Configuration;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

/// <summary>
/// A continuation page often contains no printed time signature. In that case the writer has to
/// start with the configured default, which is commonly wrong for an isolated page. If the source
/// contains no explicit time-signature digits, recover a simple quarter-note meter from the longest
/// repeatedly observed complete rhythmic span in generated measures.
/// </summary>
public sealed class MusicXmlMeterRecoveryPostProcessor
{
    public void Apply(string musicXmlPath, AnalysisResult analysis, RecognitionConfig config)
    {
        if (HasExplicitTimeSignatureGlyphs(analysis)) return;

        var document = XDocument.Load(musicXmlPath);
        var time = document.Descendants("time").FirstOrDefault();
        if (time is null) return;

        var currentBeats = (int?)time.Element("beats");
        var currentBeatType = (int?)time.Element("beat-type");
        if (currentBeats != config.Beats || currentBeatType != config.BeatType) return;

        var totals = new List<int>();
        foreach (var measure in document.Descendants("measure"))
        {
            var byStaff = measure.Elements("note")
                .Where(note => note.Element("rest") is null || note.Element("duration") is not null)
                .Where(note => note.Element("grace") is null)
                .Where(note => note.Element("chord") is null)
                .Select(note => new
                {
                    Staff = (int?)note.Element("staff") ?? 1,
                    Duration = (int?)note.Element("duration") ?? 0
                })
                .Where(x => x.Duration > 0)
                .GroupBy(x => x.Staff)
                .Select(group => group.Sum(x => x.Duration));

            totals.AddRange(byStaff.Where(x => x > 0));
        }

        if (totals.Count == 0) return;

        // Partial measures create lots of 1/4 and 2/4 totals. A complete bar is normally near the
        // upper end of the observed distribution, so score candidate quarter-note capacities by
        // both exact support and closeness to the 90th percentile.
        var ordered = totals.OrderBy(x => x).ToArray();
        var p90 = ordered[(int)Math.Floor((ordered.Length - 1) * .90)];
        var candidates = Enumerable.Range(2, 11)
            .Select(beats => new
            {
                Beats = beats,
                Duration = beats * config.Divisions,
                Exact = totals.Count(x => x == beats * config.Divisions),
                Distance = Math.Abs(p90 - beats * config.Divisions)
            })
            .Where(x => x.Exact > 0)
            .OrderByDescending(x => x.Exact >= 2)
            .ThenBy(x => x.Distance)
            .ThenByDescending(x => x.Exact)
            .ToList();

        var best = candidates.FirstOrDefault();
        if (best is null) return;

        // Do not replace the default on the strength of a single short fragment. One exact full
        // span is acceptable only when it is also the upper observed rhythmic span.
        if (best.Exact < 2 && best.Duration < p90) return;

        time.Element("beats")!.Value = best.Beats.ToString();
        time.Element("beat-type")!.Value = "4";
        document.Save(musicXmlPath);
    }

    private static bool HasExplicitTimeSignatureGlyphs(AnalysisResult analysis)
    {
        return analysis.Classifications.Any(x =>
            x.Kind.Contains("time-signature", StringComparison.OrdinalIgnoreCase) ||
            x.Kind.Contains("timesig", StringComparison.OrdinalIgnoreCase) ||
            x.ReferenceId.Contains("timeSig", StringComparison.OrdinalIgnoreCase) ||
            x.ReferenceId.Contains("timeSignature", StringComparison.OrdinalIgnoreCase));
    }
}
