using System.Xml.Linq;
using SvgToMusicXmlPoc.Services;

namespace SvgToMusicXmlPoc.Tests;

public sealed class MusicXmlContinuationCleanupPostProcessorTests
{
    [Fact]
    public void Apply_RemovesRepeatedClefsButKeepsRealChanges()
    {
        var document = XDocument.Parse("""
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1"><attributes>
                  <clef number="1"><sign>G</sign><line>2</line></clef>
                  <clef number="2"><sign>F</sign><line>4</line></clef>
                </attributes></measure>
                <measure number="5"><attributes>
                  <clef number="1"><sign>G</sign><line>2</line></clef>
                  <clef number="2"><sign>F</sign><line>4</line></clef>
                </attributes></measure>
                <measure number="9"><attributes>
                  <clef number="1"><sign>F</sign><line>4</line></clef>
                  <clef number="2"><sign>F</sign><line>4</line></clef>
                </attributes></measure>
              </part>
            </score-partwise>
            """);

        new MusicXmlContinuationCleanupPostProcessor().Apply(document);

        var measures = document.Root!.Element("part")!.Elements("measure").ToList();
        Assert.Equal(2, measures[0].Descendants("clef").Count());
        Assert.Empty(measures[1].Descendants("clef"));

        var changed = measures[2].Descendants("clef").Single();
        Assert.Equal("1", (string?)changed.Attribute("number"));
        Assert.Equal("F", changed.Element("sign")?.Value);
    }

    [Fact]
    public void Apply_ResetsLeftEdgeBassLaneAfterDelayedInnerVoice()
    {
        var document = XDocument.Parse("""
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1">
                <measure number="31">
                  <note default-x="60"><pitch><step>D</step><octave>4</octave></pitch><duration>16</duration><voice>1</voice><staff>1</staff></note>
                  <note default-x="60"><chord/><pitch><step>B</step><octave>4</octave></pitch><duration>16</duration><voice>1</voice><staff>1</staff></note>
                  <note default-x="131"><pitch><step>A</step><octave>4</octave></pitch><duration>16</duration><voice>1</voice><staff>1</staff></note>
                  <note default-x="202"><pitch><step>E</step><octave>4</octave></pitch><duration>16</duration><voice>1</voice><staff>1</staff></note>
                  <backup><duration>48</duration></backup>
                  <forward><duration>16</duration></forward>
                  <note default-x="135"><pitch><step>C</step><octave>4</octave></pitch><duration>32</duration><voice>2</voice><staff>1</staff></note>
                  <backup><duration>32</duration></backup>
                  <note default-x="64"><pitch><step>C</step><octave>3</octave></pitch><duration>48</duration><voice>3</voice><staff>2</staff></note>
                  <note default-x="64"><chord/><pitch><step>B</step><octave>3</octave></pitch><duration>48</duration><voice>3</voice><staff>2</staff></note>
                </measure>
              </part>
            </score-partwise>
            """);

        new MusicXmlContinuationCleanupPostProcessor().Apply(document);

        var backups = document.Descendants("backup").ToList();
        Assert.Equal("48", backups[0].Element("duration")?.Value);
        Assert.Equal("48", backups[1].Element("duration")?.Value);

        // The delayed inner voice stays delayed; only the following bass lane is reset to zero.
        Assert.Equal("16", document.Descendants("forward").Single().Element("duration")?.Value);
    }
}
