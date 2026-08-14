using System.Xml.Linq;
using SvgToMusicXmlPoc.Services;

namespace SvgToMusicXmlPoc.Tests;

public sealed class MusicXmlScoreTextPostProcessorTests
{
    [Fact]
    public void Apply_WritesHeaderWithoutCreatingPageLayout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            File.WriteAllText(path, """
                <?xml version="1.0" encoding="utf-8"?>
                <score-partwise version="4.0">
                  <part-list>
                    <score-part id="P1"><part-name>Piano</part-name></score-part>
                  </part-list>
                  <part id="P1">
                    <measure number="1">
                      <attributes>
                        <divisions>4</divisions>
                        <time><beats>3</beats><beat-type>4</beat-type></time>
                      </attributes>
                    </measure>
                  </part>
                </score-partwise>
                """);

            var metadata = new ScoreTextMetadata(
                "Miniature for Piano #8",
                ["Theme from \"Mimino\"", "Film by Georgi Danelia and Rezo Gabriadze (1977)"],
                "Giya Kancheli (1935-2019)",
                "1/8=62",
                [new ScoreTextPlacement(1, 1, 'L', "Cantabile")]);

            new MusicXmlScoreTextPostProcessor().Apply(path, metadata);

            var root = XDocument.Load(path).Root!;
            Assert.Equal("Miniature for Piano #8", (string?)root.Element("work")?.Element("work-title"));
            Assert.Equal(
                "Giya Kancheli (1935-2019)",
                (string?)root.Element("identification")?.Elements("creator")
                    .Single(x => (string?)x.Attribute("type") == "composer"));

            // Critical regression check: score text must never establish global page/scaling geometry.
            Assert.Null(root.Element("defaults"));

            var credits = root.Elements("credit").ToDictionary(
                x => (string)x.Element("credit-type")!,
                x => x.Element("credit-words")!);

            Assert.Equal("Miniature for Piano #8", credits["title"].Value);
            Assert.Equal("center", (string?)credits["title"].Attribute("justify"));
            Assert.Equal("22", (string?)credits["title"].Attribute("font-size"));
            Assert.Equal("551.95", (string?)credits["title"].Attribute("default-x"));
            Assert.Equal("1377.44", (string?)credits["title"].Attribute("default-y"));

            Assert.Contains("Theme from \"Mimino\"", credits["subtitle"].Value);
            Assert.Contains("Film by Georgi Danelia", credits["subtitle"].Value);
            Assert.Equal("right", (string?)credits["composer"].Attribute("justify"));

            var firstMeasure = root.Element("part")!.Element("measure")!;
            Assert.Equal(
                "170",
                firstMeasure.Element("print")?.Element("system-layout")?.Element("top-system-distance")?.Value);

            var metronome = firstMeasure.Descendants("metronome").Single();
            Assert.Equal("eighth", metronome.Element("beat-unit")?.Value);
            Assert.Equal("62", metronome.Element("per-minute")?.Value);
            Assert.Equal("31", (string?)metronome.Parent?.Parent?.Element("sound")?.Attribute("tempo"));

            var cantabile = firstMeasure.Elements("direction")
                .SelectMany(x => x.Descendants("words"))
                .Single();
            Assert.Equal("Cantabile", cantabile.Value);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Apply_ReadsButDoesNotChangeExistingPageLayout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            File.WriteAllText(path, """
                <score-partwise version="4.0">
                  <defaults>
                    <page-layout>
                      <page-height>1000</page-height>
                      <page-width>800</page-width>
                      <page-margins type="both">
                        <left-margin>40</left-margin>
                        <right-margin>50</right-margin>
                        <top-margin>60</top-margin>
                        <bottom-margin>40</bottom-margin>
                      </page-margins>
                    </page-layout>
                  </defaults>
                  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
                  <part id="P1"><measure number="1" /></part>
                </score-partwise>
                """);

            var metadata = new ScoreTextMetadata("Title", [], "Composer", null, []);
            new MusicXmlScoreTextPostProcessor().Apply(path, metadata);

            var root = XDocument.Load(path).Root!;
            var pageLayout = root.Element("defaults")!.Element("page-layout")!;
            Assert.Equal("1000", pageLayout.Element("page-height")?.Value);
            Assert.Equal("800", pageLayout.Element("page-width")?.Value);
            Assert.Equal("50", pageLayout.Element("page-margins")?.Element("right-margin")?.Value);

            var credits = root.Elements("credit").ToDictionary(
                x => (string)x.Element("credit-type")!,
                x => x.Element("credit-words")!);

            Assert.Equal("400", (string?)credits["title"].Attribute("default-x"));
            Assert.Equal("940", (string?)credits["title"].Attribute("default-y"));
            Assert.Equal("750", (string?)credits["composer"].Attribute("default-x"));
            Assert.Equal("838", (string?)credits["composer"].Attribute("default-y"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
