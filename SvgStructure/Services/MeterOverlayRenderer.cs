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
        IReadOnlyList<AccidentalResolution> accidentals,
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
            DrawNoteHead(canvas, picture, noteHead);

        foreach (var accidental in accidentals)
            DrawAccidental(canvas, picture, accidental, bounds);

        DrawFirstMeasureNoteSummaries(canvas, noteHeads, logicalGrid, bounds);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return outputPath;
    }

    private static void DrawAccidental(
        SKCanvas canvas,
        SKPicture picture,
        AccidentalResolution accidental,
        SKRect page)
    {
        RedrawRegion(canvas, picture, accidental.PhysicalBounds);

        using (var fill = new SKPaint
        {
            Color = new SKColor(255, 165, 0, 95),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        {
            canvas.DrawRect(ToRect(accidental.PhysicalBounds), fill);
        }

        using (var border = Border(SKColors.DarkOrange))
            canvas.DrawRect(ToRect(accidental.PhysicalBounds), border);

        var prefix = accidental.Kind switch
        {
            AccidentalKind.Flat => "b",
            AccidentalKind.Sharp => "s",
            AccidentalKind.Natural => "n",
            AccidentalKind.DoubleFlat => "bb",
            AccidentalKind.DoubleSharp => "ss",
            _ => "?"
        };
        var label = accidental.Note is null
            ? prefix
            : prefix + accidental.Note.Pitch;
        var b = accidental.PhysicalBounds;
        var labelHeight = (float)Math.Max(7, Math.Min(11, b.Height * 0.28));
        DrawReadableLabel(canvas, label, b.Left, b.Top, b.Bottom, labelHeight, SKColors.DarkOrange, page);

        if (accidental.Note is null)
            return;

        var noteRect = ToRect(accidental.Note.PhysicalBounds);
        using var noteFill = new SKPaint
        {
            Color = new SKColor(255, 235, 59, 95),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawOval(noteRect, noteFill);
    }

    private static void DrawNoteHead(SKCanvas canvas, SKPicture picture, NoteHeadResolution noteHead)
    {
        var rect = ToRect(noteHead.PhysicalBounds);

        if (noteHead.IsFilled)
        {
            using var fill = new SKPaint
            {
                Color = new SKColor(144, 238, 144, 170),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawOval(rect, fill);
        }
        else
        {
            RedrawRegion(canvas, picture, noteHead.PhysicalBounds);
        }

        using var border = Border(SKColors.ForestGreen);
        canvas.DrawOval(rect, border);
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

            var text = string.Join("   ", labels);
            var height = (float)Math.Clamp(block.PhysicalBounds.Height * 0.22, 7, 11);
            var top = block.PhysicalBounds.Bottom + 3;

            DrawReadableLabel(
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
        DrawReadableLabel(canvas, $"{meter.BeatNumber}-{meter.BeatValue}", b.Left, b.Top, b.Bottom, height, SKColors.DeepPink, page);
    }

    private static void DrawClefLabel(SKCanvas canvas, ClefResolution clef, SKRect page)
    {
        var b = clef.PhysicalBounds;
        var height = (float)Math.Max(8, Math.Min(14, b.Height * 0.20));
        DrawReadableLabel(canvas, clef.Kind.ToString(), b.Left, b.Top, b.Bottom, height, SKColors.DodgerBlue, page);
    }

    private static void DrawReadableLabel(SKCanvas canvas, string text, double left, double top, double bottom, float height, SKColor color, SKRect page)
    {
        var charWidth = height * 0.58f;
        var charSpacing = charWidth * 0.18f;
        var wordSpacing = charWidth * 0.85f;
        var totalWidth = MeasureVectorText(text, charWidth, charSpacing, wordSpacing);
        var x = (float)Math.Clamp(left, page.Left + 2, Math.Max(page.Left + 2, page.Right - totalWidth - 4));
        var preferredTop = (float)(top - height - 4);
        var y = preferredTop >= page.Top ? preferredTop : (float)Math.Min(page.Bottom - height - 3, bottom + 4);

        using var background = new SKPaint { Color = new SKColor(255, 255, 255, 238) };
        canvas.DrawRoundRect(new SKRect(x - 3, y - 3, x + totalWidth + 3, y + height + 3), 2, 2, background);
        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(0.9f, height * 0.075f), StrokeCap = SKStrokeCap.Round, IsAntialias = true };

        foreach (var ch in text)
        {
            if (ch == ' ') { x += wordSpacing; continue; }
            DrawChar(canvas, ch, x, y, charWidth, height, paint);
            x += charWidth + charSpacing;
        }
    }

    private static float MeasureVectorText(string text, float charWidth, float charSpacing, float wordSpacing)
    {
        var width = 0f;
        foreach (var ch in text)
            width += ch == ' ' ? wordSpacing : charWidth + charSpacing;
        return width;
    }

    private static void DrawChar(SKCanvas canvas, char ch, float x, float y, float w, float h, SKPaint paint)
    {
        if (char.IsDigit(ch)) { DrawDigit(canvas, ch - '0', x, y, w, h, paint); return; }
        var upper = char.ToUpperInvariant(ch);
        switch (upper)
        {
            case '-': canvas.DrawLine(x, y + h * .5f, x + w * .75f, y + h * .5f, paint); break;
            case '.': canvas.DrawPoint(x + w * .35f, y + h, paint); break;
            case 'G': canvas.DrawOval(new SKRect(x, y, x + w, y + h), paint); canvas.DrawLine(x + w * .52f, y + h * .55f, x + w, y + h * .55f, paint); canvas.DrawLine(x + w, y + h * .55f, x + w, y + h * .82f, paint); break;
            case 'F': canvas.DrawLine(x, y, x, y + h, paint); canvas.DrawLine(x, y, x + w, y, paint); canvas.DrawLine(x, y + h * .48f, x + w * .75f, y + h * .48f, paint); break;
            case 'C': canvas.DrawArc(new SKRect(x, y, x + w, y + h), 45, 270, false, paint); break;
            case 'A': canvas.DrawLine(x, y + h, x + w * .5f, y, paint); canvas.DrawLine(x + w * .5f, y, x + w, y + h, paint); canvas.DrawLine(x + w * .22f, y + h * .58f, x + w * .78f, y + h * .58f, paint); break;
            case 'B': canvas.DrawLine(x, y, x, y + h, paint); canvas.DrawArc(new SKRect(x, y, x + w, y + h * .52f), -90, 180, false, paint); canvas.DrawArc(new SKRect(x, y + h * .48f, x + w, y + h), -90, 180, false, paint); break;
            case 'D': canvas.DrawLine(x, y, x, y + h, paint); canvas.DrawArc(new SKRect(x - w * .25f, y, x + w, y + h), -90, 180, false, paint); break;
            case 'E': canvas.DrawLine(x, y, x, y + h, paint); canvas.DrawLine(x, y, x + w, y, paint); canvas.DrawLine(x, y + h * .5f, x + w * .75f, y + h * .5f, paint); canvas.DrawLine(x, y + h, x + w, y + h, paint); break;
            case 'S': canvas.DrawArc(new SKRect(x, y, x + w, y + h * .55f), 210, 250, false, paint); canvas.DrawArc(new SKRect(x, y + h * .45f, x + w, y + h), 30, 250, false, paint); break;
            case 'N': canvas.DrawLine(x, y + h, x, y, paint); canvas.DrawLine(x, y, x + w, y + h, paint); canvas.DrawLine(x + w, y + h, x + w, y, paint); break;
            default: canvas.DrawRect(new SKRect(x, y, x + w, y + h), paint); break;
        }
        if (char.IsLetter(ch) && char.IsLower(ch))
            canvas.DrawLine(x, y + h + 1.5f, x + w, y + h + 1.5f, paint);
    }

    private static void DrawDigit(SKCanvas canvas, int digit, float x, float y, float w, float h, SKPaint paint)
    {
        var segments = digit switch { 0 => "abcdef", 1 => "bc", 2 => "abdeg", 3 => "abcdg", 4 => "bcfg", 5 => "acdfg", 6 => "acdefg", 7 => "abc", 8 => "abcdefg", 9 => "abcdfg", _ => string.Empty };
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case 'a': canvas.DrawLine(x, y, x + w, y, paint); break;
                case 'b': canvas.DrawLine(x + w, y, x + w, y + h / 2, paint); break;
                case 'c': canvas.DrawLine(x + w, y + h / 2, x + w, y + h, paint); break;
                case 'd': canvas.DrawLine(x, y + h, x + w, y + h, paint); break;
                case 'e': canvas.DrawLine(x, y + h / 2, x, y + h, paint); break;
                case 'f': canvas.DrawLine(x, y, x, y + h / 2, paint); break;
                case 'g': canvas.DrawLine(x, y + h / 2, x + w, y + h / 2, paint); break;
            }
        }
    }
}
