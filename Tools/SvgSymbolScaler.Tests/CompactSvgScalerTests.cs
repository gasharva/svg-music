using System.Xml.Linq;
using SvgSymbolScaler;

namespace SvgSymbolScaler.Tests;

public sealed class CompactSvgScalerTests
{
    [Fact]
    public void ScalesCompactPathAroundItsCenterAndLeavesStaffLineUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "svg-symbol-scaler", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "input.svg");
        var output = Path.Combine(directory, "output.svg");
        File.WriteAllText(input, """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 500 200">
  <polyline class="StaffLines" fill="none" stroke="black" points="0,100 500,100" />
  <path id="note" d="M90 90 C90 80 110 80 110 90 C110 100 90 100 90 90 Z" />
</svg>
""");

        var result = new CompactSvgScaler(1.5, 120, 12).ProcessFile(input, output);

        Assert.Equal(1, result.Scaled);
        var document = XDocument.Load(output);
        XNamespace svg = "http://www.w3.org/2000/svg";
        var note = document.Descendants(svg + "path").Single();
        var scaleGroup = note.Parent;
        Assert.NotNull(scaleGroup);
        Assert.Equal("compact", (string?)scaleGroup!.Attribute("data-svg-symbol-scaler"));
        Assert.Contains("translate(100 90)", (string?)scaleGroup.Attribute("transform"));
        Assert.Contains("scale(1.5)", (string?)scaleGroup.Attribute("transform"));
        Assert.NotNull(document.Descendants(svg + "polyline").SingleOrDefault());
    }
}
