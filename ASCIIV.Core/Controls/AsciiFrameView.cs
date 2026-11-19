using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ASCIIV.Core.Controls;

/// <summary>
/// Draws an <see cref="AsciiFrame"/> on a fixed character grid. Its size depends only on the frame's
/// dimensions and font, never on its content, so the picture cannot shift between frames.
/// Put it in a Viewbox to scale it.
/// </summary>
public sealed class AsciiFrameView : FrameworkElement {
    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(
        nameof(Frame),
        typeof(AsciiFrame),
        typeof(AsciiFrameView),
        new FrameworkPropertyMetadata(AsciiFrame.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnFrameChanged));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground),
        typeof(Brush),
        typeof(AsciiFrameView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontNameProperty = DependencyProperty.Register(
        nameof(FontName),
        typeof(string),
        typeof(AsciiFrameView),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private const char FallbackChar = '?';

    // WPF refuses glyph runs longer than 65535 glyphs; smaller runs also keep each draw call cheap
    private const int MaxGlyphsPerRun = 16384;

    // One frozen brush per palette colour, created on first use
    private static readonly Brush?[] CellBrushes = new Brush?[AsciivColors.Count];

    /// <summary>The frame to draw.</summary>
    public AsciiFrame Frame {
        get => (AsciiFrame)GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    /// <summary>The colour of frames that have no colour per cell.</summary>
    public Brush Foreground {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Name of the <see cref="FrameFont"/> to draw with. Empty or unknown names give the default font.</summary>
    public string FontName {
        get => (string)GetValue(FontNameProperty);
        set => SetValue(FontNameProperty, value);
    }

    /// <summary>
    /// Renders a frame to a bitmap on a black background, for snapshots.
    /// </summary>
    public static BitmapSource RenderToBitmap(AsciiFrame frame, Brush foreground, string? fontName = null, double scale = 2.0, double padding = 10) {
        FrameFont font = FrameFont.Get(fontName);
        Size frameSize = GetFrameSize(frame, font);
        double width = (frameSize.Width + (padding * 2)) * scale;
        double height = (frameSize.Height + (padding * 2)) * scale;

        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen()) {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            context.PushTransform(new ScaleTransform(scale, scale));
            context.PushTransform(new TranslateTransform(padding, padding));
            DrawFrame(context, frame, foreground, font, pixelsPerDip: 1.0f);
        }

        RenderTargetBitmap bitmap = new(
            Math.Max(1, (int)Math.Ceiling(width)),
            Math.Max(1, (int)Math.Ceiling(height)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    protected override Size MeasureOverride(Size availableSize) {
        return GetFrameSize(Frame, FrameFont.Get(FontName));
    }

    protected override void OnRender(DrawingContext drawingContext) {
        DrawFrame(drawingContext, Frame, Foreground, FrameFont.Get(FontName), (float)VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static void OnFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        // Only a change of grid size needs a new layout pass
        AsciiFrame oldFrame = (AsciiFrame)e.OldValue;
        AsciiFrame newFrame = (AsciiFrame)e.NewValue;
        if ((oldFrame.Width != newFrame.Width) || (oldFrame.Height != newFrame.Height)) {
            ((AsciiFrameView)d).InvalidateMeasure();
        }
    }

    private static Size GetFrameSize(AsciiFrame frame, FrameFont font) {
        return new Size(frame.Width * font.CellWidth, frame.Height * font.CellHeight);
    }

    private static void DrawFrame(DrawingContext context, AsciiFrame frame, Brush foreground, FrameFont font, float pixelsPerDip) {
        if (frame.IsEmpty) {
            return;
        }

        GlyphTypeface glyphs = font.Glyphs;
        double cellWidth = font.CellWidth;
        double cellHeight = font.CellHeight;

        // All characters of one colour go into one glyph run, which keeps the number of draw calls small.
        // A single-colour frame uses only the first bucket
        int bucketCount = frame.Colors is null ? 1 : AsciivColors.Count;
        List<ushort>?[] glyphIndices = new List<ushort>?[bucketCount];
        List<Point>?[] glyphOffsets = new List<Point>?[bucketCount];

        for (int y = 0; y < frame.Height; y++) {
            int rowStart = y * frame.Width;
            for (int x = 0; x < frame.Width; x++) {
                char c = frame.Chars[rowStart + x];
                if ((c == ' ')
                    || (!glyphs.CharacterToGlyphMap.TryGetValue(c, out ushort glyph)
                        && !glyphs.CharacterToGlyphMap.TryGetValue(FallbackChar, out glyph))) {
                    continue;
                }

                // Characters narrower than the cell are centred in it; in a monospaced font this adds nothing
                double inset = font.IsMonospace
                    ? 0
                    : Math.Max(0, (cellWidth - (glyphs.AdvanceWidths[glyph] * FrameFont.FontSize)) / 2);

                int bucket = frame.Colors is null ? 0 : Math.Min(frame.Colors[rowStart + x], bucketCount - 1);
                (glyphIndices[bucket] ??= []).Add(glyph);
                // Every glyph is placed by its offset from the run origin; a positive Y offset moves up
                (glyphOffsets[bucket] ??= []).Add(new Point((x * cellWidth) + inset, -(y * cellHeight)));
            }
        }

        for (int bucket = 0; bucket < bucketCount; bucket++) {
            List<ushort>? indices = glyphIndices[bucket];
            List<Point>? offsets = glyphOffsets[bucket];
            if ((indices is null) || (offsets is null)) {
                continue;
            }

            Brush brush = frame.Colors is null ? foreground : GetCellBrush((byte)bucket);

            // A glyph run cannot hold more than 65535 glyphs, and a large frame has more characters than that
            for (int start = 0; start < indices.Count; start += MaxGlyphsPerRun) {
                int count = Math.Min(MaxGlyphsPerRun, indices.Count - start);
                context.DrawGlyphRun(brush, CreateGlyphRun(font, indices.GetRange(start, count), offsets.GetRange(start, count), pixelsPerDip));
            }
        }
    }

    private static GlyphRun CreateGlyphRun(FrameFont font, List<ushort> glyphIndices, List<Point> glyphOffsets, float pixelsPerDip) {
        return new GlyphRun(
            font.Glyphs,
            bidiLevel: 0,
            isSideways: false,
            renderingEmSize: FrameFont.FontSize,
            pixelsPerDip: pixelsPerDip,
            glyphIndices: glyphIndices,
            baselineOrigin: new Point(0, font.Baseline),
            advanceWidths: new double[glyphIndices.Count],
            glyphOffsets: glyphOffsets,
            characters: null,
            deviceFontName: null,
            clusterMap: null,
            caretStops: null,
            language: null);
    }

    private static Brush GetCellBrush(byte colorIndex) {
        Brush? brush = CellBrushes[colorIndex];
        if (brush is null) {
            (byte red, byte green, byte blue) = AsciivColors.ToRgb(colorIndex);
            brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            CellBrushes[colorIndex] = brush;
        }

        return brush;
    }
}