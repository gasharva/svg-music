using System.Xml.Linq;
using SvgToMusicXmlPoc.Services;

namespace SvgToMusicXmlPoc.Tests;

public sealed class SvgParserGlyphTests
{
    [Fact]
    public void MixedSvg_ExposesUseAndDirectPathThroughUnifiedGlyphStream()
    {
        var document = XDocument.Parse("""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">
              <defs>
                <symbol id="notehead">
                  <path d="M 0 0 C 2 -2 6 -2 8 0 C 6 2 2 2 0 0 Z" />
                </symbol>
              </defs>
              <g transform="translate(100 50)">
                <use xlink:href="#notehead" x="10" y="20" />
                <path d="M 30 18 C 32 16 36 16 38 18 C 36 20 32 20 30 18 Z" />
              </g>
            </svg>
            """);

        var parser = new SvgParser();
        var instances = parser.ReadUses(document);
        var geometries = new SvgPathGeometry().ReadScoreGeometries(document);

        var use = Assert.Single(instances, x => x.SourceKind == "use");
        Assert.Equal("notehead", use.SymbolId);
        Assert.Equal(110, use.X, 6);
        Assert.Equal(70, use.Y, 6);

        var path = Assert.Single(instances, x => x.SourceKind == "path");
        Assert.StartsWith("path:", path.SymbolId);
        Assert.True(path.X > 100);
        Assert.True(path.Y > 50);

        Assert.Contains("notehead", geometries.Keys);
        Assert.Contains(path.SymbolId, geometries.Keys);
    }
}
