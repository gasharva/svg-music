using System.Xml.Linq;
using SvgSymbolScaler;

namespace SvgSymbolScaler.Tests;

public sealed class SvgPrintOutputTests
{
    [Fact]
    public void ProtectsHeaderObjectsCropsPageAndWritesPdf()
    {
        var directory = Path.Combine(Path.GetTempPath(), "svg-symbol-scaler-print", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "input.svg");
        var output = Path.Combine(directory, "output.svg");
        var pdf = Path.Combine(directory, "combined.pdf");
        File.WriteAllText(input, """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1000 1000">
  <path id="title" d="M10 10 L30 10 L30 30 L10 30 Z" />
  <polyline class="StaffLines" fill="none" stroke="black" points="100,200 900,200" />
  <path id="note" d="M190 190 C190 180 210 180 210 190 C210 200 190 200 190 190 Z" />
</svg>
""");

        new CompactSvgScaler(1.5, 120, 12).ProcessFile(input, output);
        var result = new SvgPrintPostProcessor(80, 2).Process(output);
        new SvgPdfWriter().Write([output], pdf);

        Assert.Equal(1, result.Protected);
        Assert.True(result.CropWidth < 1000);
        Assert.True(result.CropHeight < 1000);
        var document = XDocument.Load(output);
        var viewBox = (string?)document.Root!.Attribute("viewBox");
        Assert.NotNull(viewBox);
        Assert.DoesNotContain("0 0 1000 1000", viewBox);
        Assert.True(File.Exists(pdf));
        Assert.True(new FileInfo(pdf).Length > 100);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4));
    }
}
