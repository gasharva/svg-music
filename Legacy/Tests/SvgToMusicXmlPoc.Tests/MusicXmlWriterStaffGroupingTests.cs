using SvgToMusicXmlPoc.Models;
using SvgToMusicXmlPoc.Services;

namespace SvgToMusicXmlPoc.Tests;

public sealed class MusicXmlWriterStaffGroupingTests
{
    [Fact]
    public void BuildStaffGroups_UsesGeometryWhenTrebleClefsAreMissed()
    {
        var analysis = new AnalysisResult
        {
            Staves =
            [
                Staff(0, 124.49), Staff(1, 178.45),
                Staff(2, 255.11), Staff(3, 309.07),
                Staff(4, 392.20), Staff(5, 446.17),
                Staff(6, 522.82), Staff(7, 576.78),
                Staff(8, 653.44), Staff(9, 707.40)
            ],
            Events =
            [
                new RecognizedEvent { StaffIndex = 4, Kind = "clef-bass", ClefSign = "F", ClefLine = 4 },
                new RecognizedEvent { StaffIndex = 7, Kind = "clef-bass", ClefSign = "F", ClefLine = 4 }
            ]
        };

        var groups = MusicXmlWriter.BuildStaffGroups(analysis);

        Assert.Equal(5, groups.Count);
        Assert.All(groups, x => Assert.Equal(2, x.Count));
        Assert.Equal([0, 1], groups[0].Select(x => x.Index));
        Assert.Equal([8, 9], groups[4].Select(x => x.Index));
    }

    [Fact]
    public void BuildStaffGroups_DoesNotPairUniformIndependentStaves()
    {
        var analysis = new AnalysisResult
        {
            Staves =
            [
                Staff(0, 100), Staff(1, 150), Staff(2, 200), Staff(3, 250),
                Staff(4, 300), Staff(5, 350)
            ]
        };

        var groups = MusicXmlWriter.BuildStaffGroups(analysis);

        Assert.Equal(6, groups.Count);
        Assert.All(groups, x => Assert.Single(x));
    }

    private static Staff Staff(int index, double top)
    {
        const double space = 4.576;
        return new Staff(index, 70, 537,
            [top, top + space, top + space * 2, top + space * 3, top + space * 4]);
    }
}
