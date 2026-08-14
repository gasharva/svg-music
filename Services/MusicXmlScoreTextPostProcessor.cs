using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

public sealed class MusicXmlScoreTextPostProcessor
{
    // MuseScore's default A4 coordinate space (40 tenths = 7.8232 mm).
    // These values are used only to position credits when the source MusicXML has no page layout.
    // We deliberately do NOT write <defaults>, <scaling> or <page-layout>, because doing so changes
    // the physical scale of the whole imported score.
    private const double DefaultPageWidth = 1103.9;
    private const double DefaultPageHeight = 1428.57;
    private const double DefaultPageMargin = 51.13;
    private const double HeaderSystemDistance = 170;
    private const double SubtitleGap = 52;
    private const double SubtitleLineStep = 22;
    private const double ComposerGap = 145;

    private static readonly Regex TempoRegex = new(
        @"^1\s*/\s*(?<denominator>\d+)\s*=\s*(?<bpm>\d+(?:[.,]\d+)?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public void Apply(string musicXmlPath, ScoreTextMetadata metadata)
    {
        var document = XDocument.Load(musicXmlPath);
        var root = document.Root ?? throw new InvalidOperationException("MusicXML root not found.");
        var part = root.Element("part") ?? throw new InvalidOperationException("MusicXML part not found.");

        AddHeader(root, part, metadata);

        var openingText = metadata.Placements.FirstOrDefault(x =>
            x.Measure == 1 && x.Staff == 1 && x.Align == 'L');
        AddOpeningTempo(part, metadata.Tempo, openingText?.Text);

        var divisions = 1;
        var beats = 4;
        var beatType = 4;
        foreach (var measure in part.Elements("measure"))
        {
            UpdateTiming(measure, ref divisions, ref beats, ref beatType);
            var number = (int?)measure.Attribute("number") ?? -1;
            foreach (var placement in metadata.Placements.Where(x => x.Measure == number))
            {
                if (openingText is not null && placement == openingText)
                    continue;

                AddWords(measure, placement, beats * divisions * 4 / Math.Max(1, beatType));
            }
        }

        document.Save(musicXmlPath);
    }

    private static void AddHeader(XElement root, XElement part, ScoreTextMetadata metadata)
    {
        if (!HasHeader(metadata)) return;

        AddSemanticMetadata(root, metadata);
        var layout = ReadPageLayout(root);
        AddCredits(root, metadata, layout);
        EnsureHeaderClearance(part);
    }

    private static bool HasHeader(ScoreTextMetadata metadata) =>
        !string.IsNullOrWhiteSpace(metadata.Title) ||
        metadata.DescriptionLines.Count > 0 ||
        !string.IsNullOrWhiteSpace(metadata.Author);

    private static void AddSemanticMetadata(XElement root, ScoreTextMetadata metadata)
    {
        var partList = root.Element("part-list")
            ?? throw new InvalidOperationException("MusicXML part-list not found.");

        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            var work = root.Element("work");
            if (work is null)
            {
                work = new XElement("work");
                root.AddFirst(work);
            }

            var workTitle = work.Element("work-title");
            if (workTitle is null)
                work.Add(new XElement("work-title", metadata.Title));
            else
                workTitle.Value = metadata.Title!;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Author))
        {
            var identification = root.Element("identification");
            if (identification is null)
            {
                identification = new XElement("identification");
                var defaults = root.Element("defaults");
                var firstCredit = root.Element("credit");
                var before = defaults ?? firstCredit ?? partList;
                before.AddBeforeSelf(identification);
            }

            var composer = identification.Elements("creator")
                .FirstOrDefault(x => string.Equals((string?)x.Attribute("type"), "composer", StringComparison.OrdinalIgnoreCase));
            if (composer is null)
                identification.AddFirst(new XElement("creator", new XAttribute("type", "composer"), metadata.Author));
            else
                composer.Value = metadata.Author!;
        }
    }

    private static PageLayout ReadPageLayout(XElement root)
    {
        var pageLayout = root.Element("defaults")?.Element("page-layout");
        if (pageLayout is null)
            return new PageLayout(DefaultPageWidth, DefaultPageHeight, DefaultPageMargin, DefaultPageMargin);

        var pageWidth = ReadDouble(pageLayout.Element("page-width"), DefaultPageWidth);
        var pageHeight = ReadDouble(pageLayout.Element("page-height"), DefaultPageHeight);
        var margins = pageLayout.Elements("page-margins")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("type"), "odd", StringComparison.OrdinalIgnoreCase))
            ?? pageLayout.Elements("page-margins")
                .FirstOrDefault(x => string.Equals((string?)x.Attribute("type"), "both", StringComparison.OrdinalIgnoreCase))
            ?? pageLayout.Element("page-margins");

        var right = ReadDouble(margins?.Element("right-margin"), DefaultPageMargin);
        var top = ReadDouble(margins?.Element("top-margin"), DefaultPageMargin);
        return new PageLayout(pageWidth, pageHeight, right, top);
    }

    private static void AddCredits(XElement root, ScoreTextMetadata metadata, PageLayout layout)
    {
        root.Elements("credit")
            .Where(IsManagedHeaderCredit)
            .Remove();

        var insertBefore = root.Element("part-list")
            ?? throw new InvalidOperationException("MusicXML part-list not found.");
        var centerX = layout.PageWidth / 2;
        var rightX = layout.PageWidth - layout.RightMargin;
        var titleY = layout.PageHeight - layout.TopMargin;

        if (!string.IsNullOrWhiteSpace(metadata.Title))
            insertBefore.AddBeforeSelf(Credit(
                "title", metadata.Title!, centerX, titleY,
                justify: "center", valign: "top", fontSize: 22));

        for (var i = 0; i < metadata.DescriptionLines.Count; i++)
        {
            insertBefore.AddBeforeSelf(Credit(
                "subtitle", metadata.DescriptionLines[i], centerX,
                titleY - SubtitleGap - i * SubtitleLineStep,
                justify: "center", valign: "top", fontStyle: "italic"));
        }

        if (!string.IsNullOrWhiteSpace(metadata.Author))
            insertBefore.AddBeforeSelf(Credit(
                "composer", metadata.Author!, rightX, titleY - ComposerGap,
                justify: "right", valign: "bottom"));
    }

    private static bool IsManagedHeaderCredit(XElement credit)
    {
        var type = (string?)credit.Element("credit-type");
        return type is not null &&
               (type.Equals("title", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("subtitle", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("composer", StringComparison.OrdinalIgnoreCase));
    }

    private static XElement Credit(
        string type,
        string text,
        double x,
        double y,
        string justify,
        string valign,
        int? fontSize = null,
        string? fontStyle = null)
    {
        var words = new XElement("credit-words",
            new XAttribute("default-x", F(x)),
            new XAttribute("default-y", F(y)),
            new XAttribute("justify", justify),
            new XAttribute("valign", valign),
            text);
        if (fontSize.HasValue)
            words.SetAttributeValue("font-size", fontSize.Value);
        if (!string.IsNullOrWhiteSpace(fontStyle))
            words.SetAttributeValue("font-style", fontStyle);

        return new XElement("credit",
            new XAttribute("page", 1),
            new XElement("credit-type", type),
            words);
    }

    private static void EnsureHeaderClearance(XElement part)
    {
        var firstMeasure = part.Elements("measure").FirstOrDefault();
        if (firstMeasure is null) return;

        var print = firstMeasure.Element("print");
        if (print is null)
        {
            print = new XElement("print");
            firstMeasure.AddFirst(print);
        }

        var systemLayout = print.Element("system-layout");
        if (systemLayout is null)
        {
            systemLayout = new XElement("system-layout");
            print.Add(systemLayout);
        }

        var distance = systemLayout.Element("top-system-distance");
        if (distance is null)
            systemLayout.Add(new XElement("top-system-distance", F(HeaderSystemDistance)));
        else if (ReadDouble(distance, 0) < HeaderSystemDistance)
            distance.Value = F(HeaderSystemDistance);
    }

    private static void AddOpeningTempo(XElement part, string? tempoText, string? openingText)
    {
        if (string.IsNullOrWhiteSpace(tempoText) && string.IsNullOrWhiteSpace(openingText)) return;

        var firstMeasure = part.Elements("measure").FirstOrDefault();
        if (firstMeasure is null) return;

        // Avoid duplicating an already imported metronome mark.
        if (firstMeasure.Descendants("metronome").Any()) return;

        var visibleText = BuildOpeningText(openingText, tempoText, out var playbackTempo);
        if (string.IsNullOrWhiteSpace(visibleText)) return;

        var direction = new XElement("direction",
            new XAttribute("placement", "above"),
            new XElement("direction-type",
                new XElement("words",
                    new XAttribute("font-size", "12"),
                    new XAttribute("font-weight", "bold"),
                    visibleText)),
            new XElement("staff", 1));

        if (playbackTempo.HasValue)
            direction.Add(new XElement("sound", new XAttribute("tempo", F(playbackTempo.Value))));

        var insertionPoint = firstMeasure.Elements().FirstOrDefault(x =>
            x.Name.LocalName is not "attributes" and not "print" and not "direction");
        if (insertionPoint is null) firstMeasure.Add(direction);
        else insertionPoint.AddBeforeSelf(direction);
    }

    private static string BuildOpeningText(string? openingText, string? tempoText, out double? playbackTempo)
    {
        playbackTempo = null;
        var tempoDisplay = string.Empty;

        if (!string.IsNullOrWhiteSpace(tempoText))
        {
            var match = TempoRegex.Match(tempoText.Trim());
            if (match.Success &&
                int.TryParse(match.Groups["denominator"].Value, out var denominator) &&
                TryBeatSymbol(denominator, out var beatSymbol) &&
                double.TryParse(match.Groups["bpm"].Value.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var bpm))
            {
                tempoDisplay = $"{beatSymbol} = {F(bpm)}";
                playbackTempo = bpm * 4d / denominator;
            }
            else
            {
                tempoDisplay = tempoText.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(openingText) && !string.IsNullOrWhiteSpace(tempoDisplay))
            return $"{openingText.Trim()}  ({tempoDisplay})";
        if (!string.IsNullOrWhiteSpace(openingText))
            return openingText.Trim();
        return tempoDisplay;
    }

    private static bool TryBeatSymbol(int denominator, out string beatSymbol)
    {
        beatSymbol = denominator switch
        {
            1 => "𝅝",
            2 => "𝅗𝅥",
            4 => "♩",
            8 => "♪",
            16 => "𝅘𝅥𝅯",
            _ => string.Empty
        };
        return beatSymbol.Length > 0;
    }

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

    private static double ReadDouble(XElement? element, double fallback) =>
        element is not null && double.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed record PageLayout(
        double PageWidth,
        double PageHeight,
        double RightMargin,
        double TopMargin);
}
