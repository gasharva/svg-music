using System.Xml.Linq;
using SvgToMusicXmlPoc.Services;

namespace SvgToMusicXmlPoc.Tests;

public sealed class MusicXmlScoreTextPostProcessorTests
{
    [Fact]
    public void Apply_WritesVerifiedMuseScoreDefaultsAndFixedHeader()
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
            var defaults = root.Element("defaults")!;
            Assert.Equal("6.99911", defaults.Element("scaling")?.Element("millimeters")?.Value);
            Assert.Equal("40", defaults.Element("scaling")?.Element("tenths")?.Value);
            Assert.Equal("1696.94", defaults.Element("page-layout")?.Element("page-height")?.Value);
            Assert.Equal("1200.48", defaults.Element("page-layout")?.Element("page-width")?.Value);
            Assert.Equal("Leland", (string?)defaults.Element("music-font")?.Attribute("font-family"));
            Assert.Equal("Edwin", (string?)defaults.Element("word-font")?.Attribute("font-family"));
            Assert.Equal("10", (string?)defaults.Element("word-font")?.Attribute("font-size"));

            var title = Credit(root, "title");
            Assert.Equal("Miniature for Piano #8", title.Value);
            Assert.Equal("600", (string?)title.Attribute("default-x"));
            Assert.Equal("1600", (string?)title.Attribute("default-y"));
            Assert.Equal("17", (string?)title.Attribute("font-size"));
            Assert.Equal("center", (string?)title.Attribute("justify"));

            var subtitle = Credit(root, "subtitle");
            Assert.Equal("Theme from \"Mimino\"", subtitle.Value);
            Assert.Equal("600", (string?)subtitle.Attribute("default-x"));
            Assert.Equal("1500", (string?)subtitle.Attribute("default-y"));
            Assert.Equal("10", (string?)subtitle.Attribute("font-size"));
            Assert.Equal("italic", (string?)subtitle.Attribute("font-style"));
            Assert.Equal("bottom", (string?)subtitle.Attribute("valign"));
            Assert.Single(root.Elements("credit").Where(x => (string?)x.Element("credit-type") == "subtitle"));

            var composer = Credit(root, "composer");
            Assert.Equal("1200", (string?)composer.Attribute("default-x"));
            Assert.Equal("1300", (string?)composer.Attribute("default-y"));
            Assert.Equal("right", (string?)composer.Attribute("justify"));

            var firstMeasure = root.Element("part")!.Element("measure")!;
            var direction = Assert.Single(firstMeasure.Elements("direction"));
            Assert.Equal("Cantabile  (♪ = 62)", direction.Descendants("words").Single().Value);
            Assert.Equal("31", (string?)direction.Element("sound")?.Attribute("tempo"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Apply_ReplacesExistingDefaultsAndTruncatesSubtitleTo25Characters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"score-{Guid.NewGuid():N}.musicxml");
        try
        {
            File.WriteAllText(path, """
                <score-partwise version="4.0">
                  <defaults><page-layout><page-width>9999</page-width></page-layout></defaults>
                  <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
                  <part id="P1"><measure number="1" /></part>
                </score-partwise>
                """);

            var description = "123456789012345678901234567890";
            var metadata = new ScoreTextMetadata("Title", [description], "Composer", null, []);
            new MusicXmlScoreTextPostProcessor().Apply(path, metadata);

            var root = XDocument.Load(path).Root!;
            Assert.Equal("1200.48", root.Element("defaults")?.Element("page-layout")?.Element("page-width")?.Value);
            Assert.Equal(description[..25], Credit(root, "subtitle").Value);
            Assert.Equal(25, Credit(root, "subtitle").Value.Length);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static XElement Credit(XElement root, string type) =>
        root.Elements("credit")
            .Single(x => (string?)x.Element("credit-type") == type)
            .Element("credit-words")!;
}
