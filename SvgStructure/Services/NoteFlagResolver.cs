using System.Numerics;
using SvgStructure.Models;

namespace SvgStructure.Services;

public sealed record NoteFlagDiagnosticEntry(
    int PartNumber,
    int MeasureNumber,
    StemResolution Stem,
    MusicSymbolCandidate Candidate,
    double EndpointDistanceInStaffSpaces,
    bool PassedGeometrySanity,
    NoteFlagRecognition? Recognition,
    string Verdict);

public sealed class NoteFlagResolver
{
    private readonly GeometryNoteFlagRecognizer _recognizer;
    private readonly double _maxEndpointDistanceInStaffSpaces;
    private readonly List<NoteFlagDiagnosticEntry> _diagnostics = new();

    public NoteFlagResolver(GeometryNoteFlagRecognizer recognizer, double maxEndpointDistanceInStaffSpaces = 1.25)
    {
        _recognizer = recognizer;
        _maxEndpointDistanceInStaffSpaces = maxEndpointDistanceInStaffSpaces;
    }

    public IReadOnlyList<NoteFlagDiagnosticEntry> LastDiagnostics => _diagnostics;

    public IReadOnlyList<NoteFlagResolution> Resolve(MusicSymbolResolution symbols, LogicalGridResolution grid, IReadOnlyList<StemResolution> stems, IReadOnlyList<BeamResolution> beams)
    {
        _diagnostics.Clear();
        var beamedStems = beams.SelectMany(x => x.Stems).ToHashSet();
        var freeStems = stems.Where(x => !beamedStems.Contains(x)).ToArray();
        if (freeStems.Length == 0) return Array.Empty<NoteFlagResolution>();
        var hits = new List<Hit>();

        foreach (var stem in freeStems)
        {
            if (!grid.TryGetBlock(stem.PartNumber, stem.MeasureNumber, out var block)) continue;
            var staffSpace = block.PhysicalBounds.Height / 4.0;
            var maxDistance = staffSpace * _maxEndpointDistanceInStaffSpaces;
            var endpoint = FreeEndpoint(stem);
            var nearbySymbols = symbols.Candidates.Where(x => x.MeasureNumber == stem.MeasureNumber).Where(x => x.SmoothPaths.Count > 0)
                .Select(x => new { Symbol=x, Distance=Distance(endpoint,x.PhysicalBounds) }).Where(x => x.Distance <= maxDistance).OrderBy(x => x.Distance).ToArray();

            foreach (var item in nearbySymbols)
            {
                var distanceInStaffSpaces = item.Distance / Math.Max(1e-9, staffSpace);
                if (!PassesFlagGeometrySanity(item.Symbol.PhysicalBounds, stem, staffSpace, out var geometryReason))
                {
                    _diagnostics.Add(new(stem.PartNumber,stem.MeasureNumber,stem,item.Symbol,distanceInStaffSpaces,false,null,"rejected before glyph recognition: "+geometryReason));
                    continue;
                }
                var contours = SmoothSymbolContourConverter.ToContours(new[] { item.Symbol });
                if (contours.Count == 0)
                {
                    _diagnostics.Add(new(stem.PartNumber,stem.MeasureNumber,stem,item.Symbol,distanceInStaffSpaces,true,null,"rejected: no usable contours"));
                    continue;
                }
                var wholeRecognition = _recognizer.Recognize(contours);
                RecognitionHit? accepted;
                if (contours.Count == 1 && TryAccepted(wholeRecognition, stem.Direction)) accepted = new(wholeRecognition, Bounds(contours), "single-contour candidate");
                else accepted = TryRecognizeSingleContour(contours, stem.Direction);
                if (accepted is null)
                {
                    var verdict = wholeRecognition.Denominator is not null && wholeRecognition.Direction is not null && wholeRecognition.Direction.Value != stem.Direction
                        ? $"rejected: geometry direction {wholeRecognition.Direction.Value} != stem {stem.Direction}" : "rejected by geometry classifier";
                    _diagnostics.Add(new(stem.PartNumber,stem.MeasureNumber,stem,item.Symbol,distanceInStaffSpaces,true,wholeRecognition,verdict));
                    continue;
                }
                var recognition=accepted.Recognition; var physicalBounds=accepted.PhysicalBounds;
                if (Distance(endpoint, physicalBounds) > maxDistance)
                {
                    _diagnostics.Add(new(stem.PartNumber,stem.MeasureNumber,stem,item.Symbol,distanceInStaffSpaces,true,recognition,"rejected: recognized contour is not near free stem endpoint"));
                    continue;
                }
                var logicalBounds=block.ToLogical(physicalBounds);
                var flag=new NoteFlagResolution(stem.PartNumber,stem.MeasureNumber,recognition.Denominator!.Value,physicalBounds,logicalBounds,stem,recognition.Confidence);
                hits.Add(new(stem,flag,item.Distance));
                _diagnostics.Add(new(stem.PartNumber,stem.MeasureNumber,stem,item.Symbol,distanceInStaffSpaces,true,recognition,$"accepted: 1/{recognition.Denominator.Value} {recognition.Direction!.Value} ({accepted.Source})"));
            }
        }
        return hits.GroupBy(x=>x.Stem).Select(g=>g.OrderByDescending(x=>x.Flag.Confidence).ThenBy(x=>x.EndpointDistance).First().Flag)
            .OrderBy(x=>x.MeasureNumber).ThenBy(x=>x.PartNumber).ThenBy(x=>x.PhysicalBounds.Left).ToArray();
    }

    private RecognitionHit? TryRecognizeSingleContour(IReadOnlyList<IReadOnlyList<Vector2>> contours, StemDirection direction)
    {
        RecognitionHit? best=null;
        for(var i=0;i<contours.Count;i++)
        {
            var contour=contours[i]; if(contour.Count<3)continue;
            var recognition=_recognizer.Recognize(new[] { contour }); if(!TryAccepted(recognition,direction))continue;
            var hit=new RecognitionHit(recognition,Bounds(new[] { contour }),$"contour {i+1}/{contours.Count}");
            if(best is null || hit.Recognition.Confidence>best.Recognition.Confidence)best=hit;
        }
        return best;
    }

    private static bool TryAccepted(NoteFlagRecognition recognition, StemDirection direction) => recognition.Denominator is not null && recognition.Direction is not null && recognition.Direction.Value == direction;
    private static bool PassesFlagGeometrySanity(RectD candidate, StemResolution stem, double staffSpace, out string reason)
    {
        var minWidth=Math.Max(stem.PhysicalBounds.Width*2.0,staffSpace*0.12); if(candidate.Width<minWidth){reason=$"width {candidate.Width/staffSpace:0.###}sp < {minWidth/staffSpace:0.###}sp";return false;}
        var minHeight=staffSpace*0.20; if(candidate.Height<minHeight){reason=$"height {candidate.Height/staffSpace:0.###}sp < {minHeight/staffSpace:0.###}sp";return false;}
        reason="ok"; return true;
    }
    private static RectD Bounds(IEnumerable<IReadOnlyList<Vector2>> contours){var p=contours.SelectMany(x=>x).ToArray();return new(p.Min(x=>x.X),p.Min(x=>x.Y),p.Max(x=>x.X),p.Max(x=>x.Y));}
    private static PointD FreeEndpoint(StemResolution stem)=>stem.Direction==StemDirection.Up?new(stem.PhysicalBounds.CenterX,stem.PhysicalBounds.Top):new(stem.PhysicalBounds.CenterX,stem.PhysicalBounds.Bottom);
    private static double Distance(PointD point, RectD rect){var dx=point.X<rect.Left?rect.Left-point.X:point.X>rect.Right?point.X-rect.Right:0;var dy=point.Y<rect.Top?rect.Top-point.Y:point.Y>rect.Bottom?point.Y-rect.Bottom:0;return Math.Sqrt(dx*dx+dy*dy);}
    private sealed record RecognitionHit(NoteFlagRecognition Recognition, RectD PhysicalBounds, string Source);
    private sealed record Hit(StemResolution Stem, NoteFlagResolution Flag, double EndpointDistance);
}
