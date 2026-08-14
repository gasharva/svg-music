using System.Xml.Linq;
using SvgToMusicXmlPoc.Services;

namespace SvgToMusicXmlPoc.Tests;

public sealed class MusicXmlScoreTextPostProcessorTests
{
    [Fact]
    public void Apply_WritesCompactHeaderAndMergesOpeningTextWithTempo()
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

            var title = root.Elements("credit")
                .Single(x => (string?)x.Element("credit-type") == "title")
                .Element("credit-words")!;
            Assert.Equal("Miniature for Piano #8", title.Value);
            Assert.Equal("center", (string?)title.Attribute("justify"));
            Assert.Equal("551.95", (string?)title.Attribute("default-x"));

            var subtitles = root.Elements("credit")
                .Where(x => (string?)x.Element("credit-type") == "subtitle")
                .Select(x => x.Element("credit-words")!)
                .ToList();
            Assert.Equal(2, subtitles.Count);
            Assert.Equal("Theme from \"Mimino\"", subtitles[0].Value);
            Assert.Equal("Film by Georgi Danelia and Rezo Gabriadze (1977)", subtitles[1].Value);
            Assert.All(subtitles, x => Assert.Equal("center", (string?)x.Attribute("justify")));
            Assert.All(subtitles, x => Assert.Equal("italic", (string?)x.Attribute("font-style")));

            var composer = root.Elements("credit")
                .Single(x => (string?)x.Element("credit-type") == "composer")
                .Element("credit-words")!;
            Assert.Equal("right", (string?)composer.Attribute("justify"));

            // Header positioning must never introduce global page/scaling defaults.
            Assert.Null(root.Element("defaults"));

            var firstMeasure = root.Element("part")!.Element("measure")!;
            Assert.Equal(
                "170",
                firstMeasure.Element("print")?.Element("system-layout")?.Element("top-system-distance")?.Value);

            var directions = firstMeasure.Elements("direction").ToList();
            Assert.Single(directions);
            var openingWords = directions[0].Descendants("words").Single();
            Assert.Equal("Cantabile  (♪ = 62)", openingWords.Value);
            Assert.Equal("31", (string?)directions[0].Element("sound")?.Attribute("tempo"));
            Assert.Empty(firstMeasure.Descendants("metronome"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Apply_ReadsExistingPageLayoutWithoutChangingIt()
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
            var title = root.Elements("credit")
                .Single(x => (string?)x.Element("credit-type") == "title")
                .Element("credit-words")!;
            var composer = root.Elements("credit")
                .Single(x => (string?)x.Element("credit-type") == "composer")
                .Element("credit-words")!;

            Assert.Equal("400", (string?)title.Attribute("default-x"));
            Assert.Equal("940", (string?)title.Attribute("default-y"));
            Assert.Equal("750", (string?)composer.Attribute("default-x"));
            Assert.Equal("795", (string?)composer.Attribute("default-y"));

            var pageLayout = root.Element("defaults")!.Element("page-layout")!;
            Assert.Equal("800", pageLayout.Element("page-width")!.Value);
            Assert.Equal("1000", pageLayout.Element("page-height")!.Value);
            Assert.Equal("60", pageLayout.Element("page-margins")!.Element("top-margin")!.Value);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
