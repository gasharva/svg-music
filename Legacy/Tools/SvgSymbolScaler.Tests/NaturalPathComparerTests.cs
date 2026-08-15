using SvgSymbolScaler;

namespace SvgSymbolScaler.Tests;

public sealed class NaturalPathComparerTests
{
    [Fact]
    public void SortsNumericFilenamePartsAsNumbers()
    {
        string[] files =
        [
            "score_19.svg",
            "score_2.svg",
            "score_10.svg",
            "score_1.svg",
            "score_0.svg",
            "score_29.svg",
            "score_3.svg"
        ];

        var sorted = files.OrderBy(x => x, NaturalPathComparer.OrdinalIgnoreCase).ToArray();

        Assert.Equal(
        [
            "score_0.svg",
            "score_1.svg",
            "score_2.svg",
            "score_3.svg",
            "score_10.svg",
            "score_19.svg",
            "score_29.svg"
        ], sorted);
    }

    [Fact]
    public void UsesAllNumericPartsAndKeepsOrderingDeterministic()
    {
        string[] files =
        [
            "book10/page2.svg",
            "book2/page10.svg",
            "book2/page2.svg",
            "book2/page02.svg"
        ];

        var sorted = files.OrderBy(x => x, NaturalPathComparer.OrdinalIgnoreCase).ToArray();

        Assert.Equal(
        [
            "book2/page2.svg",
            "book2/page02.svg",
            "book2/page10.svg",
            "book10/page2.svg"
        ], sorted);
    }
}
