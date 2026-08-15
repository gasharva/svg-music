using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SvgToMusicXmlPoc.Services;

public sealed class MusicXmlScoreTextPostProcessor
{
    private const double HeaderSystemDistance = 170;
    private const int MaxSubtitleLength = 25;

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
        ReplaceDefaults(root);
        AddCredits(root, metadata);
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

    private static void ReplaceDefaults(XElement root)
    {
        root.Element("defaults")?.Remove();

        var defaults = new XElement("defaults",
            new XElement("scaling",
                new XElement("millimeters", "6.99911"),
                new XElement("tenths", "40")),
            new XElement("page-layout",
                new XElement("page-height", "1696.94"),
                new XElement("page-width", "1200.48"),
                PageMargins("even"),
                PageMargins("odd")),
            new XElement("appearance",
                LineWidth("light barline", "1.8"),
                LineWidth("heavy barline", "5.5"),
                LineWidth("beam", "5"),
                LineWidth("bracket", "4.5"),
                LineWidth("dashes", "1"),
                LineWidth("enclosure", "1"),
                LineWidth("ending", "1.1"),
                LineWidth("extend", "1"),
                LineWidth("leger", "1.6"),
                LineWidth("pedal", "1.1"),
                LineWidth("octave shift", "1.1"),
                LineWidth("slur middle", "2.1"),
                LineWidth("slur tip", "0.5"),
                LineWidth("staff", "1.1"),
                LineWidth("stem", "1"),
                LineWidth("tie middle", "2.1"),
                LineWidth("tie tip", "0.5"),
                LineWidth("tuplet bracket", "1"),
                LineWidth("wedge", "1.2"),
                NoteSize("cue", "70"),
                NoteSize("grace", "70"),
                NoteSize("grace-cue", "49")),
            new XElement("music-font", new XAttribute("font-family", "Leland")),
            new XElement("word-font",
                new XAttribute("font-family", "Edwin"),
                new XAttribute("font-size", "10")),
            new XElement("lyric-font",
                new XAttribute("font-family", "Edwin"),
                new XAttribute("font-size", "10")));

        var firstCredit = root.Element("credit");
        var partList = root.Element("part-list")
            ?? throw new InvalidOperationException("MusicXML part-list not found.");
        (firstCredit ?? partList).AddBeforeSelf(defaults);
    }

    private static XElement PageMargins(string type) =>
        new("page-margins",
            new XAttribute("type", type),
            new XElement("left-margin", "85.7252"),
            new XElement("right-margin", "85.7252"),
            new XElement("top-margin", "85.7252"),
            new XElement("bottom-margin", "85.7252"));

    private static XElement LineWidth(string type, string value) =>
        new("line-width", new XAttribute("type", type), value);

    private static XElement NoteSize(string type, string value) =>
        new("note-size", new XAttribute("type", type), value);

    private static void AddCredits(XElement root, ScoreTextMetadata metadata)
    {
        root.Elements("credit")
            .Where(IsManagedHeaderCredit)
            .Remove();

        var insertBefore = root.Element("part-list")
            ?? throw new InvalidOperationException("MusicXML part-list not found.");

        if (!string.IsNullOrWhiteSpace(metadata.Title))
            insertBefore.AddBeforeSelf(Credit(
                "title", metadata.Title!, 600, 1600,
                justify: "center", valign: "top", fontSize: 17));

        var subtitle = GetSubtitle(metadata);
        if (!string.IsNullOrWhiteSpace(subtitle))
            insertBefore.AddBeforeSelf(Credit(
                "subtitle", subtitle, 600, 1500,
                justify: "center", valign: "bottom", fontSize: 10, fontStyle: "italic"));

        if (!string.IsNullOrWhiteSpace(metadata.Author))
            insertBefore.AddBeforeSelf(Credit(
                "composer", metadata.Author!, 1200, 1300,
                justify: "right", valign: "bottom"));
    }

    private static string? GetSubtitle(ScoreTextMetadata metadata)
    {
        var subtitle = metadata.DescriptionLines.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
        if (string.IsNullOrWhiteSpace(subtitle)) return null;
        return subtitle.Length <= MaxSubtitleLength
            ? subtitle
            : subtitle[..MaxSubtitleLength];
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
}
