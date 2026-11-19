using ASCIIV.Core;

using OpenCvSharp;

using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

using Xabe.FFmpeg;

using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Size = OpenCvSharp.Size;

namespace ASCIIV.Converter;

/// <summary>
/// Renders ASCII frames to a standard video file: MP4, WebM or animated GIF.
/// </summary>
public static class AsciiRenderer {
    private const string FontName = "Consolas";
    private const float FontSize = 10;
    private const int Padding = 10;

    // Number of characters measured to get an accurate average cell width
    private const int MeasureLength = 100;

    /// <summary>
    /// Draws every frame and encodes the result to <paramref name="outputPath"/>.
    /// Frames are pulled one at a time, so the whole video never has to fit in memory.
    /// </summary>
    /// <param name="audioPath">Audio to add, or null for a silent video. Ignored for formats without sound.</param>
    /// <param name="colorName">Named colour or #RRGGBB value for frames that have no colour per cell.</param>
    /// <param name="totalFrameCount">Number of frames, for progress reporting. Zero when unknown.</param>
    public static async Task RenderAsync(
        IEnumerable<AsciiFrame> frames,
        double fps,
        string? audioPath,
        string outputPath,
        ExportFormat format,
        string colorName,
        string? fontName = null,
        int totalFrameCount = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) {

        // The frames are first drawn to an intermediate video, which FFmpeg then encodes to the final format
        string tempVideoPath = Path.Combine(Path.GetTempPath(), $"asciiv_render_{Guid.NewGuid():N}.mp4");
        try {
            if (!DrawFrames(frames, fps, tempVideoPath, colorName, FrameFont.Get(fontName), totalFrameCount, progress, cancellationToken)) {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            bool hasAudio = format.SupportsAudio() && !string.IsNullOrEmpty(audioPath) && File.Exists(audioPath);
            string arguments = BuildEncodeArguments(tempVideoPath, hasAudio ? audioPath : null, outputPath, format);
            await FFmpeg.Conversions.New().Start(arguments, cancellationToken);
        }
        finally {
            try {
                File.Delete(tempVideoPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                System.Diagnostics.Debug.WriteLine("Temp video cleanup warning: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Builds the FFmpeg command line that encodes the intermediate video, and optional audio, to the final format.
    /// </summary>
    public static string BuildEncodeArguments(string videoPath, string? audioPath, string outputPath, ExportFormat format) {
        string inputs = $"-y -i \"{videoPath}\"";
        string audioMap = string.Empty;
        if (audioPath is not null) {
            inputs += $" -i \"{audioPath}\"";
            audioMap = " -map 1:a:0";
        }

        return format switch {
            ExportFormat.Mp4 => $"{inputs} -map 0:v:0{audioMap} -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p"
                + (audioPath is null ? string.Empty : " -c:a aac -b:a 192k")
                + $" -movflags +faststart \"{outputPath}\"",

            ExportFormat.WebM => $"{inputs} -map 0:v:0{audioMap} -c:v libvpx-vp9 -crf 32 -b:v 0 -row-mt 1 -deadline good -cpu-used 4 -pix_fmt yuv420p"
                + (audioPath is null ? string.Empty : " -c:a libopus -b:a 128k")
                + $" \"{outputPath}\"",

            // A palette generated from the video itself looks far better than the default GIF palette
            ExportFormat.Gif => $"{inputs} -filter_complex \"[0:v]split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5\""
                + $" -loop 0 \"{outputPath}\"",

            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "This format cannot be rendered.")
        };
    }

    // Returns false when there were no frames to draw
    private static bool DrawFrames(
        IEnumerable<AsciiFrame> frames,
        double fps,
        string videoPath,
        string colorName,
        FrameFont frameFont,
        int totalFrameCount,
        IProgress<double>? progress,
        CancellationToken cancellationToken) {

        using IEnumerator<AsciiFrame> enumerator = frames.GetEnumerator();
        if (!enumerator.MoveNext() || enumerator.Current.IsEmpty) {
            return false;
        }

        AsciiFrame firstFrame = enumerator.Current;

        // A font that ships as a file is loaded privately; it is not installed on the system
        using PrivateFontCollection fontFiles = new();
        bool useFontFile = !frameFont.IsMonospace && File.Exists(frameFont.FilePath);
        if (useFontFile) {
            fontFiles.AddFontFile(frameFont.FilePath!);
        }

        using Font font = useFontFile
            ? new Font(fontFiles.Families[0], FontSize, GetAvailableStyle(fontFiles.Families[0]))
            : new Font(FontName, FontSize, FontStyle.Bold);
        using StringFormat stringFormat = new(StringFormat.GenericTypographic);
        stringFormat.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;

        float cellWidth;
        float cellHeight;
        // Converts the font's own measurements, given at FrameFont.FontSize, to pixels of this drawing surface
        float fontScale;
        using (Bitmap measureBitmap = new(1, 1))
        using (Graphics measureGraphics = Graphics.FromImage(measureBitmap)) {
            measureGraphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            fontScale = (float)(((font.SizeInPoints * measureGraphics.DpiY) / 72.0) / FrameFont.FontSize);
            if (useFontFile) {
                cellWidth = (float)frameFont.CellWidth * fontScale;
                cellHeight = (float)frameFont.CellHeight * fontScale;
            }
            else {
                cellWidth = measureGraphics.MeasureString(new string('M', MeasureLength), font, PointF.Empty, stringFormat).Width / MeasureLength;
                cellHeight = font.GetHeight(measureGraphics);
            }
        }

        // Only needed for fonts that are not monospaced: where each character sits inside its cell
        CellLayout? cellLayout = useFontFile ? new CellLayout(frameFont, fontScale) : null;

        // Video codecs need even dimensions
        int width = MakeEven((int)Math.Ceiling(firstFrame.Width * cellWidth) + (Padding * 2));
        int height = MakeEven((int)Math.Ceiling(firstFrame.Height * cellHeight) + (Padding * 2));

        using VideoWriter writer = new(videoPath, VideoWriter.FourCC("mp4v"), fps, new Size(width, height));
        if (!writer.IsOpened()) {
            throw new InvalidOperationException("Could not open the OpenCV video writer.");
        }

        using Bitmap bitmap = new(width, height, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

        using SolidBrush defaultBrush = new(ParseColor(colorName));
        SolidBrush?[] cellBrushes = new SolidBrush?[AsciivColors.Count];
        Rectangle bounds = new(0, 0, width, height);
        int renderedFrames = 0;

        try {
            do {
                cancellationToken.ThrowIfCancellationRequested();

                graphics.Clear(Color.Black);
                if (cellLayout is null) {
                    DrawFrame(graphics, enumerator.Current, font, stringFormat, defaultBrush, cellBrushes, cellWidth, cellHeight);
                }
                else {
                    DrawFrameByCell(graphics, enumerator.Current, font, stringFormat, defaultBrush, cellBrushes, cellWidth, cellHeight, cellLayout);
                }

                // Wrap the bitmap's memory directly instead of copying it into a new Mat per frame
                BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try {
                    using Mat mat = Mat.FromPixelData(height, width, MatType.CV_8UC3, data.Scan0, data.Stride);
                    writer.Write(mat);
                }
                finally {
                    bitmap.UnlockBits(data);
                }

                renderedFrames++;
                if ((totalFrameCount > 0) && (progress is not null)) {
                    progress.Report(((double)renderedFrames / totalFrameCount) * 100.0);
                }
            }
            while (enumerator.MoveNext());
        }
        finally {
            foreach (SolidBrush? brush in cellBrushes) {
                brush?.Dispose();
            }
        }

        writer.Release();
        return true;
    }

    private static void DrawFrame(
        Graphics graphics,
        AsciiFrame frame,
        Font font,
        StringFormat stringFormat,
        SolidBrush defaultBrush,
        SolidBrush?[] cellBrushes,
        float cellWidth,
        float cellHeight) {

        for (int y = 0; y < frame.Height; y++) {
            int rowStart = y * frame.Width;
            float top = Padding + (y * cellHeight);

            if (frame.Colors is null) {
                string row = new(frame.Chars, rowStart, frame.Width);
                graphics.DrawString(row, font, defaultBrush, Padding, top, stringFormat);
                continue;
            }

            // Draw each stretch of same-coloured characters in one call
            int x = 0;
            while (x < frame.Width) {
                byte color = frame.Colors[rowStart + x];
                int runStart = x;
                while ((x < frame.Width) && (frame.Colors[rowStart + x] == color)) {
                    x++;
                }

                string run = new(frame.Chars, rowStart + runStart, x - runStart);
                if (!string.IsNullOrWhiteSpace(run)) {
                    graphics.DrawString(run, font, GetCellBrush(cellBrushes, color), Padding + (runStart * cellWidth), top, stringFormat);
                }
            }
        }
    }

    // Draws one character at a time, centred in its cell, for fonts whose characters differ in width
    private static void DrawFrameByCell(
        Graphics graphics,
        AsciiFrame frame,
        Font font,
        StringFormat stringFormat,
        SolidBrush defaultBrush,
        SolidBrush?[] cellBrushes,
        float cellWidth,
        float cellHeight,
        CellLayout cellLayout) {

        for (int y = 0; y < frame.Height; y++) {
            int rowStart = y * frame.Width;
            float top = Padding + (y * cellHeight);

            for (int x = 0; x < frame.Width; x++) {
                char character = frame.Chars[rowStart + x];
                if ((character == ' ') || (cellLayout.GetInset(character, cellWidth) is not float inset)) {
                    continue;
                }

                SolidBrush brush = frame.Colors is null ? defaultBrush : GetCellBrush(cellBrushes, frame.Colors[rowStart + x]);
                graphics.DrawString(cellLayout.GetText(character), font, brush, Padding + (x * cellWidth) + inset, top, stringFormat);
            }
        }
    }

    private static FontStyle GetAvailableStyle(FontFamily family) {
        // Display fonts often come in a single style, and asking for a missing one throws
        foreach (FontStyle style in (FontStyle[])[FontStyle.Regular, FontStyle.Bold, FontStyle.Italic]) {
            if (family.IsStyleAvailable(style)) {
                return style;
            }
        }

        return FontStyle.Regular;
    }

    private static SolidBrush GetCellBrush(SolidBrush?[] cellBrushes, byte colorIndex) {
        int slot = Math.Min(colorIndex, cellBrushes.Length - 1);
        SolidBrush? brush = cellBrushes[slot];
        if (brush is null) {
            (byte red, byte green, byte blue) = AsciivColors.ToRgb(colorIndex);
            brush = new SolidBrush(Color.FromArgb(red, green, blue));
            cellBrushes[slot] = brush;
        }

        return brush;
    }

    private static Color ParseColor(string colorName) {
        try {
            Color color = colorName.StartsWith('#') ? ColorTranslator.FromHtml(colorName) : Color.FromName(colorName);
            // An unknown name gives a colour with every component zero, which would be invisible
            return (color.IsKnownColor || colorName.StartsWith('#')) ? color : Color.White;
        }
        catch (Exception) {
            return Color.White;
        }
    }

    private static int MakeEven(int value) {
        return ((value % 2) == 0) ? value : value + 1;
    }

    // Caches, per character, the one-character string to draw and how far it sits from the left edge of its cell
    private sealed class CellLayout(FrameFont frameFont, float fontScale) {
        private readonly Dictionary<char, (string Text, float Width)?> _characters = [];

        // Null when the font does not have the character
        public float? GetInset(char character, float cellWidth) {
            return Lookup(character) is (string _, float width) ? Math.Max(0, (cellWidth - width) / 2) : null;
        }

        public string GetText(char character) {
            return Lookup(character)?.Text ?? string.Empty;
        }

        private (string Text, float Width)? Lookup(char character) {
            if (!_characters.TryGetValue(character, out (string Text, float Width)? entry)) {
                entry = frameFont.GetAdvance(character) is double advance
                    ? (character.ToString(), (float)advance * fontScale)
                    : null;
                _characters[character] = entry;
            }

            return entry;
        }
    }
}