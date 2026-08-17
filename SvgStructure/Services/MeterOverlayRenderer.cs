using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic only. Dims the score and redraws recognized semantic objects at full intensity.</summary>
public sealed class MeterOverlayRenderer
{
    private const float RenderScale = 2f;

    public string Render(
        PartMeasureResolution structure,
        IReadOnlyList<MeterResolution> meters,
        IReadOnlyList<ClefResolution> clefs,
        IReadOnlyList<LedgerLineResolution> ledgerLines,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        LogicalGridResolution logicalGrid,
        string outputPath)
    {
        using var svg = SKSvg.CreateFromFile(structure.SvgPath);
        var picture = svg.Picture
            ?? throw new InvalidOperationException("Svg.Skia did not produce a renderable picture.");

        var bounds = picture.CullRect;
        using var bitmap = new SKBitmap(
            (int)Math.Ceiling(bounds.Width * RenderScale),
            (int)Math.Ceiling(bounds.Height * RenderScale),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.White);
        canvas.Scale(RenderScale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);

        using (var veil = new SKPaint { Color = new SKColor(255, 255, 255, 205) })
            canvas.DrawRect(bounds, veil);

        foreach (var meter in meters)
        {
            RedrawRegion(canvas, picture, meter.PhysicalBounds);
            using var border = Border(SKColors.DeepPink);
            canvas.DrawRect(ToRect(meter.PhysicalBounds), border);
            DrawMeterLabel(canvas, meter, bounds);
        }

        foreach (var clef in clefs)
        {
            RedrawRegion(canvas, picture, clef.PhysicalBounds);
            using var border = Border(SKColors.DodgerBlue);
            canvas.DrawRect(ToRect(clef.PhysicalBounds), border);
            DrawClefLabel(canvas, clef, bounds);
        }

        foreach (var ledger in ledgerLines)
            DrawLedgerLadder(canvas, ledger, logicalGrid);

        foreach (var noteHead in noteHeads)
        {
            RedrawRegion(canvas, picture, noteHead.PhysicalBounds);
            using var border = Border(SKColors.ForestGreen);
            canvas.DrawOval(ToRect(noteHead.PhysicalBounds), border);
        }

        DrawFirstMeasureNoteSummaries(canvas, noteHeads, logicalGrid, bounds);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return outputPath;
    }

    private static void DrawFirstMeasureNoteSummaries(
        SKCanvas canvas,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        LogicalGridResolution logicalGrid,
        SKRect page)
    {
        var firstMeasure = noteHeads
            .Where(x => x.MeasureNumber == 1)
            .GroupBy(x => x.PartNumber)
            .OrderBy(x => x.Key)
            .ToArray();

        foreach (var partNotes in firstMeasure)
        {
            if (!logicalGrid.TryGetBlock(partNotes.Key, 1, out var block))
                continue;

            var ordered = partNotes
                .OrderBy(x => x.LogicalBounds.Left ?? double.MinValue)
                .ThenBy(x => x.LogicalBounds.Top)
                .ToArray();

            var labels = ordered
                .Select(x => x.IsFilled
                    ? x.Pitch.ToLowerInvariant()
                    : x.Pitch.ToUpperInvariant())
                .ToArray();

            if (labels.Length == 0)
                continue;

            // Extra spacing makes dense chords/readouts much easier to inspect at full-page scale.
            var text = string.Join("   ", labels);
            var height = (float)Math.Clamp(block.PhysicalBounds.Height * 0.22, 7, 11);
            var top = block.PhysicalBounds.Bottom + 3;

            DrawVectorLabel(
                canvas,
                text,
                block.PhysicalBounds.Left,
                top,
                top + height,
                height,
                SKColors.ForestGreen,
                page);
        }
    }

    private static void DrawLedgerLadder(
        SKCanvas canvas,
        LedgerLineResolution ledger,
        LogicalGridResolution logicalGrid)
    {
        if (!logicalGrid.TryGetBlock(ledger.PartNumber, ledger.MeasureNumber, out var block))
            return;

        if (ledger.LogicalBounds.Left is not { } logicalLeft ||
            ledger.LogicalBounds.Right is not { } logicalRight)
            return;

        var left = block.ToPhysical(new LogicalPoint(logicalLeft, 0)).X;
        var right = block.ToPhysical(new LogicalPoint(logicalRight, 0)).X;
        var staffSpace = block.PhysicalBounds.Height / 4.0;

        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(0.8, staffSpace * 0.08),
            StrokeCap = SKStrokeCap.Square,
            IsAntialias = true
        };

        var count = Math.Abs(ledger.Depth);
        var firstLevel = ledger.Depth < 0 ? -2 : 10;
        var step = ledger.Depth < 0 ? -2 : 2;

        for (var i = 0; i < count; i++)
        {
            var logicalY = firstLevel + step * i;
            var y = block.ToPhysical(new LogicalPoint(logicalLeft, logicalY)).Y;
            canvas.DrawLine((float)left, (float)y, (float)right, (float)y, paint);
        }
    }

    private static void RedrawRegion(SKCanvas canvas, SKPicture picture, RectD region)
    {
        var clip = ToRect(region);
        canvas.Save();
        canvas.ClipRect(clip);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static SKPaint Border(SKColor color) => new()
    {
        Color = color,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1.5f,
        IsAntialias = true
    };

    private static SKRect ToRect(RectD r) =>
        new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);

    private static void DrawMeterLabel(SKCanvas canvas, MeterResolution meter, SKRect page)
    {
        var b = meter.PhysicalBounds;
        var height = (float)Math.Max(8, Math.Min(18, b.Height * 0.34));
        DrawVectorLabel(
            canvas,
            $"{meter.BeatNumber}-{meter.BeatValue}",
            b.Left,
            b.Top,
            b.Bottom,
            height,
            SKColors.DeepPink,
            page);
    }

    private static void DrawClefLabel(SKCanvas canvas, ClefResolution clef, SKRect page)
    {
        var b = clef.PhysicalBounds;
        var height = (float)Math.Max(8, Math.Min(14, b.Height * 0.20));
        DrawVectorLabel(
            canvas,
            clef.Kind.ToString(),
            b.Left,
            b.Top,
            b.Bottom,
            height,
            SKColors.DodgerBlue,
            page);
    }

    /// <summary>
    /// All semantic labels use a real system typeface. Keeping this helper name avoids churn at the
    /// call sites, but unlike the old diagnostic seven-segment/vector lettering it preserves case
    /// (lowercase = filled note head, uppercase = hollow) and is much easier to read when zoomed.
    /// </summary>
    private static void DrawVectorLabel(
        SKCanvas canvas,
        string text,
        double left,
        double top,
        double bottom,
        float height,
        SKColor color,
        SKRect page)
    {
        using var typeface = SKTypeface.Default;
        using var font = new SKFont(typeface, height);
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true
        };

        var textWidth = font.MeasureText(text, paint);
        var x = (float)Math.Clamp(
            left,
            page.Left + 2,
            Math.Max(page.Left + 2, page.Right - textWidth - 4));

        var preferredBaseline = (float)(top - 3);
        var fallbackBaseline = (float)Math.Min(page.Bottom - 3, bottom + height + 3);
        var baseline = preferredBaseline - height >= page.Top
            ? preferredBaseline
            : fallbackBaseline;

        using var background = new SKPaint { Color = new SKColor(255, 255, 255, 238) };
        canvas.DrawRoundRect(
            new SKRect(x - 3, baseline - height - 3, x + textWidth + 3, baseline + 3),
            2,
            2,
            background);

        using var blob = SKTextBlob.Create(text, font);
        if (blob is not null)
            canvas.DrawText(blob, x, baseline, paint);
    }
}
