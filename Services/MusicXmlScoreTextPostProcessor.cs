using System.Globalization;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

public sealed class MusicXmlScoreTextPostProcessor
{
    public void Apply(string musicXmlPath, ScoreTextMetadata metadata)
    {
        var document = XDocument.Load(musicXmlPath);
        var root = document.Root ?? throw new InvalidOperationException("MusicXML root not found.");
        var part = root.Element("part") ?? throw new InvalidOperationException("MusicXML part not found.");
        var measures = part.Elements("measure").ToDictionary(
            x => (int?)x.Attribute("number") ?? -1,
            x => x);

        AddCredits(root, metadata);

        var divisions = 1;
        var beats = 4;
        var beatType = 4;
        foreach (var measure in part.Elements("measure"))
        {
            UpdateTiming(measure, ref divisions, ref beats, ref beatType);
            var number = (int?)measure.Attribute("number") ?? -1;
            foreach (var placement in metadata.Placements.Where(x => x.Measure == number))
                AddWords(measure, placement, beats * divisions * 4 / Math.Max(1, beatType));
        }

        document.Save(musicXmlPath);
    }

    private static void AddCredits(XElement root, ScoreTextMetadata metadata)
    {
        var insertBefore = root.Element("part-list") ?? root.Elements().FirstOrDefault();
        var credits = new List<XElement>();

        if (!string.IsNullOrWhiteSpace(metadata.Title))
            credits.Add(Credit(metadata.Title!, "center", 595, 1515, 22, "bold"));

        var descriptionY = 1475d;
        foreach (var line in metadata.DescriptionLines)
        {
            credits.Add(Credit(line, "center", 595, descriptionY, 13, "italic"));
            descriptionY -= 26;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Author))
            credits.Add(Credit(metadata.Author!, "right", 1060, 1365, 15, "bold"));

        foreach (var credit in credits)
        {
            if (insertBefore is null) root.AddFirst(credit);
            else insertBefore.AddBeforeSelf(credit);
        }
    }

    private static XElement Credit(string text, string justify, double x, double y, double size, string style) =>
        new("credit",
            new XAttribute("page", 1),
            new XElement("credit-words",
                new XAttribute("default-x", x.ToString("0.###", CultureInfo.InvariantCulture)),
                new XAttribute("default-y", y.ToString("0.###", CultureInfo.InvariantCulture)),
                new XAttribute("justify", justify),
                new XAttribute("halign", justify),
                new XAttribute("font-size", size.ToString("0.###", CultureInfo.InvariantCulture)),
                new XAttribute("font-style", style == "italic" ? "italic" : "normal"),
                new XAttribute("font-weight", style == "bold" ? "bold" : "normal"),
                text));

    private static void AddWords(XElement measure, ScoreTextPlacement placement, int measureDuration)
    {
        var offset = placement.Align switch
        {
            'R' => measureDuration,
            'C' => measureDuration / 2,
            _ => 0
        };

        var words = new XElement("words",
            new XAttribute("font-size", "12"),
            placement.Text);

        if (placement.Align == 'C') words.SetAttributeValue("justify", "center");
        if (placement.Align == 'R') words.SetAttributeValue("justify", "right");

        var direction = new XElement("direction",
            new XAttribute("placement", "above"),
            new XElement("direction-type", words),
            new XElement("offset", offset),
            new XElement("staff", placement.Staff));

        var insertionPoint = measure.Elements().FirstOrDefault(x =>
            x.Name.LocalName is not "attributes" and not "print" and not "direction");
        if (insertionPoint is null) measure.Add(direction);
        else insertionPoint.AddBeforeSelf(direction);
    }

    private static void UpdateTiming(XElement measure, ref int divisions, ref int beats, ref int beatType)
    {
        var attributes = measure.Element("attributes");
        if (attributes is null) return;

        var divisionsValue = (int?)attributes.Element("divisions");
        if (divisionsValue is > 0)
            divisions = divisionsValue.Value;

        var time = attributes.Element("time");
        if (time is null) return;

        var beatsValue = (int?)time.Element("beats");
        if (beatsValue is > 0)
            beats = beatsValue.Value;

        var beatTypeValue = (int?)time.Element("beat-type");
        if (beatTypeValue is > 0)
            beatType = beatTypeValue.Value;
    }
}
