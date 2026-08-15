using System.Numerics;
using SvgStructure.Models;
using SvgSymbols.Services;

namespace SvgStructure.Services;

/// <summary>
/// Pipeline step 3. Works exclusively on step-2 primitives.
/// A conventional numeric time signature is two vertically stacked number shapes with almost
/// the same horizontal footprint. We detect that cheap geometry first and only then invoke the
/// heavier number recognizer. The source SVG is deliberately not accessible here.
/// </summary>
public sealed class MeterResolver
{
    private static readonly HashSet<(int Beats, int Value)> SupportedMeters = new()
    {
        (2, 2), (2, 4), (2, 8),
        (3, 2), (3, 4), (3, 8),
        (4, 2), (4, 4), (4, 8),
        (5, 4), (5, 8),
        (6, 4), (6, 8),
        (7, 4), (7, 8),
        (9, 8), (9, 16),
        (12, 8), (12, 16)
    };

    private readonly ISvgNumberRecognizer _numberRecognizer;

    public MeterResolver(ISvgNumberRecognizer numberRecognizer) =>
        _numberRecognizer = numberRecognizer;

    public MeterResolution? Resolve(PartMeasureBlock block, PrimitiveResolution primitives)
    {
        var available = primitives.Primitives
            .Where(x =>
                x.Scope == PrimitiveLogicalScope.PartMeasure &&
                x.PartNumber == block.PartNumber &&
                x.MeasureNumber == block.MeasureNumber ||
                x.Scope == PrimitiveLogicalScope.Measure &&
                x.MeasureNumber == block.MeasureNumber)
            .Where(x => x.PhysicalBounds.IntersectsHorizontally(
                block.PhysicalBounds.Left,
                block.PhysicalBounds.Right))
            .ToArray();

        if (available.Length < 2 || block.PhysicalBounds.Height <= 0)
            return null;

        var candidates = BuildCandidates(block, available)
            .OrderByDescending(x => x.GeometryScore)
            .Take(8)
            .ToArray();

        var recognized = new List<ScoredMeter>();
        foreach (var candidate in candidates)
        {
            var meter = RecognizeCandidate(block, candidate);
            if (meter is not null)
                recognized.Add(meter);
        }

        return recognized
            .OrderByDescending(x => x.Score)
            .Select(x => x.Meter)
            .FirstOrDefault();
    }

    private static IReadOnlyList<MeterCandidate> BuildCandidates(
        PartMeasureBlock block,
        IReadOnlyList<ResolvedPrimitive> primitives)
    {
        var b = block.PhysicalBounds;
        var staffHeight = b.Height;
        var middleY = b.CenterY;

        // A time-signature digit normally occupies a substantial fraction of one half of the
        // five-line staff. Tiny dots/accidentals and huge clefs are poor row candidates.
        var rowPrimitives = primitives
            .Where(x => x.PhysicalBounds.Height >= staffHeight * 0.22)
            .Where(x => x.PhysicalBounds.Height <= staffHeight * 0.72)
            .Where(x => x.PhysicalBounds.Width <= staffHeight * 1.15)
            .ToArray();

        var upper = rowPrimitives.Where(x => x.PhysicalBounds.CenterY < middleY).ToArray();
        var lower = rowPrimitives.Where(x => x.PhysicalBounds.CenterY >= middleY).ToArray();

        var upperClusters = BuildRowClusters(upper, staffHeight);
        var lowerClusters = BuildRowClusters(lower, staffHeight);
        var result = new List<MeterCandidate>();

        foreach (var top in upperClusters)
        {
            foreach (var bottom in lowerClusters)
            {
                var xOverlap = HorizontalOverlapRatio(top.Bounds, bottom.Bounds);
                if (xOverlap < 0.58)
                    continue;

                var centerDelta = Math.Abs(top.Bounds.CenterX - bottom.Bounds.CenterX) / staffHeight;
                if (centerDelta > 0.22)
                    continue;

                var widthRatio = Ratio(top.Bounds.Width, bottom.Bounds.Width);
                var heightRatio = Ratio(top.Bounds.Height, bottom.Bounds.Height);
                if (widthRatio < 0.48 || heightRatio < 0.52)
                    continue;

                // Stacked rows should meet around the staff middle, not live on the same side.
                var verticalGap = bottom.Bounds.Top - top.Bounds.Bottom;
                if (verticalGap > staffHeight * 0.24 || verticalGap < -staffHeight * 0.18)
                    continue;

                var total = Union(top.Bounds, bottom.Bounds);
                var verticalCoverage = total.Height / staffHeight;
                if (verticalCoverage < 0.72 || verticalCoverage > 1.35)
                    continue;

                var side = ResolveSide(block, total);
                if (side is null)
                    continue;

                // Alignment is by far the strongest cheap signal. A genuine 4/4 in our samples,
                // for example, has practically identical x-bounds for the two fours.
                var geometryScore =
                    1.8 * xOverlap +
                    0.65 * widthRatio +
                    0.35 * heightRatio +
                    0.35 * Math.Min(verticalCoverage, 1.05) -
                    0.55 * centerDelta;

                result.Add(new MeterCandidate(
                    side.Value,
                    top,
                    bottom,
                    total,
                    geometryScore));
            }
        }

        // Several clusters can describe the same glyphs. Keep only the strongest version of each
        // physical location before invoking the expensive recognizer.
        return result
            .OrderByDescending(x => x.GeometryScore)
            .GroupBy(x => (
                Side: x.Side,
                X: Math.Round(x.Bounds.CenterX / Math.Max(1, staffHeight * 0.12)),
                W: Math.Round(x.Bounds.Width / Math.Max(1, staffHeight * 0.12))))
            .Select(x => x.First())
            .ToArray();
    }

    private static IReadOnlyList<RowCluster> BuildRowClusters(
        IReadOnlyList<ResolvedPrimitive> primitives,
        double staffHeight)
    {
        if (primitives.Count == 0)
            return Array.Empty<RowCluster>();

        var ordered = primitives.OrderBy(x => x.PhysicalBounds.Left).ToArray();
        var result = new List<RowCluster>();

        // Every primitive is a valid one-symbol cluster.
        foreach (var primitive in ordered)
            result.Add(new RowCluster(new[] { primitive }, primitive.PhysicalBounds));

        // Compound numbers such as 12 and 16 consist of adjacent primitives. Build only short,
        // tightly-spaced horizontal groups; no generic combinatorial search is needed.
        for (var i = 0; i < ordered.Length - 1; i++)
        {
            var first = ordered[i];
            var second = ordered[i + 1];
            var gap = second.PhysicalBounds.Left - first.PhysicalBounds.Right;
            if (gap < -staffHeight * 0.10 || gap > staffHeight * 0.32)
                continue;

            var yOverlap = VerticalOverlapRatio(first.PhysicalBounds, second.PhysicalBounds);
            if (yOverlap < 0.55)
                continue;

            result.Add(new RowCluster(
                new[] { first, second },
                Union(first.PhysicalBounds, second.PhysicalBounds)));
        }

        return result;
    }

    private ScoredMeter? RecognizeCandidate(PartMeasureBlock block, MeterCandidate candidate)
    {
        var top = _numberRecognizer.Recognize(ToContours(candidate.Top.Primitives));
        var bottom = _numberRecognizer.Recognize(ToContours(candidate.Bottom.Primitives));

        var pair = BestSupportedPair(top, bottom);
        if (pair is null)
            return null;

        var confidence = Math.Sqrt(pair.Value.TopConfidence * pair.Value.BottomConfidence);

        return new ScoredMeter(
            new MeterResolution(
                block.PartNumber,
                block.MeasureNumber,
                pair.Value.Beats,
                pair.Value.Value,
                candidate.Side,
                confidence,
                candidate.Bounds,
                candidate.Top.Bounds,
                candidate.Bottom.Bounds),
            confidence + 0.18 * candidate.GeometryScore);
    }

    private static MeterSide? ResolveSide(PartMeasureBlock block, RectD bounds)
    {
        var b = block.PhysicalBounds;
        var localCenter = (bounds.CenterX - b.Left) / Math.Max(1e-9, b.Width);

        // Left: after clef/key signature, but still in the left half of the measure.
        if (localCenter <= 0.48)
            return MeterSide.Left;

        // Right: meter change immediately before the following barline.
        if (localCenter >= 0.72)
            return MeterSide.Right;

        return null;
    }

    private static (int Beats, int Value, double TopConfidence, double BottomConfidence)? BestSupportedPair(
        SvgNumberRecognition top,
        SvgNumberRecognition bottom)
    {
        var topCandidates = CandidateList(top);
        var bottomCandidates = CandidateList(bottom);

        return topCandidates
            .SelectMany(t => bottomCandidates.Select(b => new
            {
                Beats = t.Value,
                Value = b.Value,
                TopConfidence = t.Confidence,
                BottomConfidence = b.Confidence,
                Score = Math.Sqrt(t.Confidence * b.Confidence)
            }))
            .Where(x => SupportedMeters.Contains((x.Beats, x.Value)))
            .Where(x => x.TopConfidence >= 0.005 && x.BottomConfidence >= 0.005)
            .OrderByDescending(x => x.Score)
            .Select(x => ((int Beats, int Value, double TopConfidence, double BottomConfidence)?)
                (x.Beats, x.Value, x.TopConfidence, x.BottomConfidence))
            .FirstOrDefault();
    }

    private static IReadOnlyList<SvgNumberCandidate> CandidateList(SvgNumberRecognition result)
    {
        if (result.Candidates.Count > 0)
            return result.Candidates.Take(8).ToArray();

        return result.Value is not null
            ? new[] { new SvgNumberCandidate(result.Value.Value, result.Confidence) }
            : Array.Empty<SvgNumberCandidate>();
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> ToContours(
        IEnumerable<ResolvedPrimitive> primitives) =>
        primitives
            .Where(x => x.Contour.Points.Count >= 3)
            .Select(x => (IReadOnlyList<Vector2>)x.Contour.Points)
            .ToArray();

    private static double HorizontalOverlapRatio(RectD a, RectD b)
    {
        var overlap = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left));
        return overlap / Math.Max(1e-9, Math.Min(a.Width, b.Width));
    }

    private static double VerticalOverlapRatio(RectD a, RectD b)
    {
        var overlap = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
        return overlap / Math.Max(1e-9, Math.Min(a.Height, b.Height));
    }

    private static double Ratio(double a, double b) =>
        Math.Min(a, b) / Math.Max(1e-9, Math.Max(a, b));

    private static RectD Union(RectD a, RectD b) =>
        new(
            Math.Min(a.Left, b.Left),
            Math.Min(a.Top, b.Top),
            Math.Max(a.Right, b.Right),
            Math.Max(a.Bottom, b.Bottom));

    private sealed record RowCluster(
        IReadOnlyList<ResolvedPrimitive> Primitives,
        RectD Bounds);

    private sealed record MeterCandidate(
        MeterSide Side,
        RowCluster Top,
        RowCluster Bottom,
        RectD Bounds,
        double GeometryScore);

    private sealed record ScoredMeter(MeterResolution Meter, double Score);
}
