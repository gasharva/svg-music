using System.Xml.Linq;
using SvgToMusicXmlPoc.Models;

namespace SvgToMusicXmlPoc.Services;

public sealed class MusicXmlFinalBarlinePostProcessor
{
    public void Apply(string path, AnalysisResult analysis)
    {
        if (analysis.Staves.Count < 2) return;

        var ordered = analysis.Staves.OrderBy(x => x.Center).ToList();
        var upper = ordered[^2];
        var lower = ordered[^1];
        var space = (upper.Space + lower.Space) / 2;
        var right = Math.Max(upper.Right, lower.Right);

        var columns = analysis.LineSegments
            .Where(x => x.Width <= space * .15)
            .Where(x => x.CenterX >= right - space * 1.6 && x.CenterX <= right + space * .35)
            .Where(x => x.Top <= upper.Top + space * .35)
            .Where(x => x.Bottom >= lower.Bottom - space * .35)
            .Select(x => x.CenterX)
            .OrderBy(x => x)
            .Aggregate(new List<double>(), (result, x) =>
            {
                if (result.Count == 0 || x - result[^1] > space * .18) result.Add(x);
                else result[^1] = (result[^1] + x) / 2;
                return result;
            });

        if (columns.Count < 2) return;
        var pairFound = columns
            .SelectMany((a, i) => columns.Skip(i + 1).Select(b => b - a))
            .Any(delta => delta >= .35 * space && delta <= 1.25 * space);
        if (!pairFound) return;

        var document = XDocument.Load(path);
        var lastMeasure = document.Descendants("measure").LastOrDefault();
        if (lastMeasure is null) return;

        var barline = lastMeasure.Elements("barline")
            .FirstOrDefault(x => (string?)x.Attribute("location") == "right");
        if (barline is null)
        {
            barline = new XElement("barline", new XAttribute("location", "right"));
            lastMeasure.Add(barline);
        }

        var style = barline.Element("bar-style");
        if (style is null) barline.AddFirst(new XElement("bar-style", "light-heavy"));
        else style.Value = "light-heavy";

        document.Save(path);
    }
}
