using SkiaSharp;
using Svg.Skia;
using SvgStructure.Models;

namespace SvgStructure.Services;

/// <summary>Diagnostic only. Dims the score and redraws recognized semantic objects at full intensity.</summary>
public sealed class MeterOverlayRenderer
{
    private const float RenderScale = 2f;

    /// <summary>Text labels are useful while debugging, but make the overview noisy.</summary>
    public bool DrawDiagnosticLabels { get; init; } = false;

    public string Render(
        PartMeasureResolution structure,
        IReadOnlyList<MeterResolution> meters,
        IReadOnlyList<ClefResolution> clefs,
        IReadOnlyList<LedgerLineResolution> ledgerLines,
        IReadOnlyList<NoteHeadResolution> noteHeads,
        IReadOnlyList<AccidentalResolution> accidentals,
        IReadOnlyList<StemResolution> stems,
        IReadOnlyList<BeamResolution> beams,
        IReadOnlyList<ArcResolution> arcs,
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

        // Keep the source score clearly visible: the semantic overlay should highlight, not erase it.
        using (var veil = new SKPaint { Color = new SKColor(255, 255, 255, 105) })
            canvas.DrawRect(bounds, veil);

        foreach (var meter in meters)
        {
            RedrawRegion(canvas, picture, meter.PhysicalBounds);
            using var border = Border(SKColors.DeepPink);
            canvas.DrawRect(ToRect(meter.PhysicalBounds), border);
            if (DrawDiagnosticLabels)
                DrawMeterLabel(canvas, meter, bounds);
        }

        foreach (var clef in clefs)
        {
            RedrawRegion(canvas, picture, clef.PhysicalBounds);
            using var border = Border(SKColors.DodgerBlue);
            canvas.DrawRect(ToRect(clef.PhysicalBounds), border);
            if (DrawDiagnosticLabels)
                DrawClefLabel(canvas, clef, bounds);
        }

        foreach (var ledger in ledgerLines)
            DrawLedgerLadder(canvas, ledger, logicalGrid);

        foreach (var noteHead in noteHeads)
            DrawNoteHead(canvas, picture, noteHead);

        foreach (var accidental in accidentals)
            DrawAccidental(canvas, picture, accidental, bounds);

        foreach (var stem in stems)
            DrawStem(canvas, stem);

        foreach (var beam in beams)
            DrawBeam(canvas, beam);

        foreach (var arc in arcs)
            DrawArc(canvas, arc);

        if (DrawDiagnosticLabels)
            DrawFirstMeasureNoteSummaries(canvas, noteHeads, logicalGrid, bounds);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return outputPath;
    }

    private void DrawAccidental(
        SKCanvas canvas,
        SKPicture picture,
        AccidentalResolution accidental,
        SKRect page)
    {
        RedrawRegion(canvas, picture, accidental.PhysicalBounds);

        var (borderColor, fillColor, noteColor) = accidental.Kind switch
        {
            AccidentalKind.Sharp => (
                SKColors.DarkOrange,
                new SKColor(255, 165, 0, 95),
                new SKColor(255, 235, 59, 105)),

            AccidentalKind.Flat => (
                SKColors.MediumPurple,
                new SKColor(147, 112, 219, 95),
                new SKColor(196, 170, 255, 105)),

            AccidentalKind.Natural => (
                SKColors.DimGray,
                new SKColor(105, 105, 105, 80),
                new SKColor(190, 190, 190, 90)),

            AccidentalKind.DoubleSharp => (
                SKColors.DarkOrange,
                new SKColor(255, 165, 0, 95),
                new SKColor(255, 235, 59, 105)),

            AccidentalKind.DoubleFlat => (
                SKColors.MediumPurple,
                new SKColor(147, 112, 219, 95),
                new SKColor(196, 170, 255, 105)),

            _ => (SKColors.Gray, new SKColor(128, 128, 128, 80), new SKColor(190, 190, 190, 90))
        };

        using (var fill = new SKPaint
        {
            Color = fillColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
            canvas.DrawRect(ToRect(accidental.PhysicalBounds), fill);

        using (var border = Border(borderColor))
            canvas.DrawRect(ToRect(accidental.PhysicalBounds), border);

        if (DrawDiagnosticLabels)
        {
            var prefix = accidental.Kind switch
            {
                AccidentalKind.Flat => "F",
                AccidentalKind.Sharp => "S",
                AccidentalKind.Natural => "N",
                AccidentalKind.DoubleFlat => "FF",
                AccidentalKind.DoubleSharp => "SS",
                _ => "?"
            };
            var label = accidental.Note is null ? prefix : prefix + accidental.Note.Pitch;
            var b = accidental.PhysicalBounds;
            var labelHeight = (float)Math.Max(7, Math.Min(11, b.Height * 0.28));
            DrawReadableLabel(canvas, label, b.Left, b.Top, b.Bottom, labelHeight, borderColor, page);
        }

        if (accidental.Note is null)
            return;

        using var noteFill = new SKPaint
        {
            Color = noteColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawOval(ToRect(accidental.Note.PhysicalBounds), noteFill);
    }

    private static void DrawStem(SKCanvas canvas, StemResolution stem)
    {
        var b = stem.PhysicalBounds;
        var x = (float)b.CenterX;
        var top = (float)b.Top;
        var bottom = (float)b.Bottom;
        var arrowSize = (float)Math.Clamp(b.Height * 0.075, 2.5, 5.0);

        using var paint = new SKPaint
        {
            Color = SKColors.ForestGreen,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Clamp(b.Width * 1.25, 1.2, 2.5),
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        canvas.DrawLine(x, top, x, bottom, paint);

        if (stem.Direction == StemDirection.Up)
        {
            canvas.DrawLine(x, top, x - arrowSize, top + arrowSize, paint);
            canvas.DrawLine(x, top, x + arrowSize, top + arrowSize, paint);
        }
        else
        {
            canvas.DrawLine(x, bottom, x - arrowSize, bottom - arrowSize, paint);
            canvas.DrawLine(x, bottom, x + arrowSize, bottom - arrowSize, paint);
        }
    }

    private static void DrawBeam(SKCanvas canvas, BeamResolution beam)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.ForestGreen,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Clamp(beam.PhysicalBounds.Height * 0.55, 1.8, 4.5),
            StrokeCap = SKStrokeCap.Square,
            IsAntialias = true
        };

        canvas.DrawLine(
            (float)beam.LeftEndpoint.X,
            (float)beam.LeftEndpoint.Y,
            (float)beam.RightEndpoint.X,
            (float)beam.RightEndpoint.Y,
            paint);
    }

    private static void DrawArc(SKCanvas canvas, ArcResolution arc)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Clamp(arc.PhysicalBounds.Height * 0.12, 1.4, 3.0),
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        var controlX = 2.0 * arc.Midpoint.X - 0.5 * (arc.LeftEndpoint.X + arc.RightEndpoint.X);
        var controlY = 2.0 * arc.Midpoint.Y - 0.5 * (arc.LeftEndpoint.Y + arc.RightEndpoint.Y);

        using var path = new SKPath();
        path.MoveTo((float)arc.LeftEndpoint.X, (float)arc.LeftEndpoint.Y);
        path.QuadTo(
            (float)controlX,
            (float)controlY,
            (float)arc.RightEndpoint.X,
            (float)arc.RightEndpoint.Y);
        canvas.DrawPath(path, paint);
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

            var labels = partNotes
                .OrderBy(x => x.LogicalBounds.Left ?? double.MinValue)
                .ThenBy(x => x.LogicalBounds.Top)
                .Select(x => x.IsFilled ? x.Pitch.ToLowerInvariant() : x.Pitch.ToUpperInvariant())
                .ToArray();

            if (labels.Length == 0)
                continue;

            var text = string.Join("   ", labels);
            var height = (float)Math.Clamp(block.PhysicalBounds.Height * 0.22, 7, 11);
            var top = block.PhysicalBounds.Bottom + 3;
            DrawReadableLabel(canvas, text, block.PhysicalBounds.Left, top, top + height, height, SKColors.ForestGreen, page);
        }
    }

    private static void DrawLedgerLadder(
        SKCanvas canvas,
        LedgerLineResolution ledger,
        LogicalGridResolution logicalGrid)
    {
        if (!logicalGrid.TryGetBlock(ledger.PartNumber, ledger.MeasureNumber, out var block))
            return;

        if (ledger.LogicalBounds.Left is not { } logicalLeft || ledger.LogicalBounds.Right is not { } logicalRight)
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
        canvas.Save();
        canvas.ClipRect(ToRect(region));
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

    private static SKRect ToRect(RectD r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);

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
        using var paint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(0.9f, height * 0.075f),
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        foreach (var ch in text)
        {
            if (ch == ' ')
            {
                x += wordSpacing;
                continue;
            }

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
        if (char.IsDigit(ch))
        {
            DrawDigit(canvas, ch - '0', x, y, w, h, paint);
            return;
        }

        var cx = x + w * 0.5f;
        var top = y;
        var mid = y + h * 0.5f;
        var bottom = y + h;
        switch (char)
        {
            case '-': canvas.DrawLine(x + w * 0.15f, mid, x + w * 0.85f, mid, paint); break;
            case 'G': DrawOvalChar(canvas, x, y, w, h, paint, openRight: true); canvas.DrawLine(cx, mid, x + w, mid, paint); break;
            case 'F': canvas.DrawLine(x + w * .18f, top, x + w * .18f, bottom, paint); canvas.DrawLine(x + w * .18f, top, x + w * .88f, top, paint); canvas.DrawLine(x + w * .18f, mid, x + w * .72f, mid, paint); break;
            case 'S': DrawS(canvas, x, y, w, h, paint); break;
            case 'N': canvas.DrawLine(x + w*.15f,bottom,x+w*.15f,top,paint); canvas.DrawLine(x+w*.15f,top,x+w*.85f,bottom,paint); canvas.DrawLine(x+w*.85f,bottom,x+w*.85f,top,paint); break;
            case 'A': canvas.DrawLine(x+w*.12f,bottom,cx,top,paint); canvas.DrawLine(cx,top,x+w*.88f,bottom,paint); canvas.DrawLine(x+w*.28f,mid,x+w*.72f,mid,paint); break;
            case 'B': canvas.DrawLine(x+w*.15f,top,x+w*.15f,bottom,paint); DrawRightLobe(canvas,x+w*.15f,top,w*.7f,h*.5f,paint); DrawRightLobe(canvas,x+w*.15f,mid,w*.7f,h*.5f,paint); break;
            case 'C': DrawOvalChar(canvas, x, y, w, h, paint, openRight: true); break;
            case 'D': canvas.DrawLine(x+w*.15f,top,x+w*.15f,bottom,paint); DrawRightLobe(canvas,x+w*.15f,top,w*.72f,h,paint); break;
            case 'E': canvas.DrawLine(x+w*.15f,top,x+w*.15f,bottom,paint); canvas.DrawLine(x+w*.15f,top,x+w*.88f,top,paint); canvas.DrawLine(x+w*.15f,mid,x+w*.72f,mid,paint); canvas.DrawLine(x+w*.15f,bottom,x+w*.88f,bottom,paint); break;
            default: canvas.DrawRect(x+w*.2f,y+h*.2f,w*.6f,h*.6f,paint); break;
        }
    }

    private static void DrawDigit(SKCanvas canvas, int digit, float x, float y, float w, float h, SKPaint paint)
    {
        var a = (x+w*.2f,y+h*.1f); var b=(x+w*.8f,y+h*.1f); var c=(x+w*.82f,y+h*.5f); var d=(x+w*.8f,y+h*.9f); var e=(x+w*.2f,y+h*.9f); var f=(x+w*.18f,y+h*.5f); var g=(x+w*.2f,y+h*.5f); var gr=(x+w*.8f,y+h*.5f);
        void L((float x,float y)p,(float x,float y)q)=>canvas.DrawLine(p.x,p.y,q.x,q.y,paint);
        switch(digit)
        {
            case 0: L(a,b);L(b,d);L(d,e);L(e,a);break;
            case 1: L((x+w*.5f,y+h*.12f),(x+w*.5f,y+h*.9f));L((x+w*.38f,y+h*.25f),(x+w*.5f,y+h*.12f));break;
            case 2: L(a,b);L(b,c);L(c,g);L(g,e);L(e,d);break;
            case 3: L(a,b);L(b,c);L(g,gr);L(c,d);L(e,d);break;
            case 4: L(a,f);L(f,gr);L(b,d);break;
            case 5: L(b,a);L(a,f);L(f,gr);L(c,d);L(d,e);break;
            case 6: L(b,a);L(a,e);L(e,d);L(d,c);L(c,g);L(g,f);break;
            case 7: L(a,b);L(b,d);break;
            case 8: L(a,b);L(b,d);L(d,e);L(e,a);L(g,gr);break;
            case 9: L(d,b);L(b,a);L(a,f);L(f,gr);L(g,c);break;
        }
    }

    private static void DrawOvalChar(SKCanvas canvas,float x,float y,float w,float h,SKPaint paint,bool openRight)
    {
        using var p=new SKPath(); p.MoveTo(x+w*.78f,y+h*.15f); p.CubicTo(x+w*.15f,y,x+w*.05f,y+h*.85f,x+w*.78f,y+h*.85f); if(!openRight)p.CubicTo(x+w,y+h*.75f,x+w,y+h*.25f,x+w*.78f,y+h*.15f); canvas.DrawPath(p,paint);
    }
    private static void DrawS(SKCanvas canvas,float x,float y,float w,float h,SKPaint paint){using var p=new SKPath();p.MoveTo(x+w*.85f,y+h*.15f);p.CubicTo(x+w*.2f,y,x+w*.05f,y+h*.45f,x+w*.55f,y+h*.5f);p.CubicTo(x+w,y+h*.55f,x+w*.85f,y+h*.98f,x+w*.15f,y+h*.85f);canvas.DrawPath(p,paint);}
    private static void DrawRightLobe(SKCanvas canvas,float x,float y,float w,float h,SKPaint paint){using var p=new SKPath();p.MoveTo(x,y);p.CubicTo(x+w,y,x+w,y+h,x,y+h);canvas.DrawPath(p,paint);}
}