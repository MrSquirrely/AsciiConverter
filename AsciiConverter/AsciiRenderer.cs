using OpenCvSharp;
using OpenCvSharp.Extensions;

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

using Xabe.FFmpeg;

using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Size = OpenCvSharp.Size;

namespace ASCIIV.Converter;

public static class AsciiRenderer {
    // PERFORMANCE & ARCHITECTURE: Accepts IEnumerable<string> for streaming frames without loading all into memory
    // UX: Accepts IProgress and CancellationToken for progress reporting and cancellation
    public static async Task RenderToMp4(
        IEnumerable<string> frames,
        double fps,
        string audioPath,
        string outputPath,
        string colorName,
        int totalFrameCount = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) {

        using IEnumerator<string> enumerator = frames.GetEnumerator();
        if (!enumerator.MoveNext()) {
            return;
        }

        string firstFrame = enumerator.Current;
        int width, height;

        // RESOURCE LEAK FIX: Dispose font and brush properly with using declarations
        using Font font = new("Consolas", 10, FontStyle.Bold);

        Color fontColor = colorName.StartsWith('#') ? ColorTranslator.FromHtml(colorName) : Color.FromName(colorName);
        using SolidBrush fontBrush = new(fontColor);

        using (Bitmap tempBmp = new(1, 1))
        using (Graphics tempG = Graphics.FromImage(tempBmp)) {
            SizeF size = tempG.MeasureString(firstFrame, font);
            width = (int)size.Width + 20;
            height = (int)size.Height + 20;

            // Ensure even dimensions for video codecs
            if ((width % 2) != 0) {
                width++;
            }

            if ((height % 2) != 0) {
                height++;
            }
        }

        // SAFETY FIX: Use unique temporary file in system temp folder to prevent filename collisions
        string tempVideoPath = Path.Combine(Path.GetTempPath(), $"temp_render_{Guid.NewGuid():N}.mp4");
        int fourcc = VideoWriter.FourCC("mp4v");

        try {
            // 2. Setup Video Writer
            using (VideoWriter writer = new(tempVideoPath, fourcc, fps, new Size(width, height))) {
                if (!writer.IsOpened()) {
                    throw new InvalidOperationException("Could not open OpenCV VideoWriter. Make sure OpenH264 or generic codecs are installed.");
                }

                using Bitmap bmp = new(width, height, PixelFormat.Format24bppRgb);
                using Graphics g = Graphics.FromImage(bmp);
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                int renderedFrames = 0;

                void RenderFrame(string frameText) {
                    cancellationToken.ThrowIfCancellationRequested();

                    g.Clear(Color.Black);
                    g.DrawString(frameText, font, fontBrush, 0, 0);

                    // PERFORMANCE OPTIMIZATION: Zero-copy direct memory wrapping via LockBits instead of allocating new Mat per frame
                    BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try {
                        using Mat mat = Mat.FromPixelData(height, width, MatType.CV_8UC3, bmpData.Scan0, bmpData.Stride);
                        writer.Write(mat);
                    }
                    finally {
                        bmp.UnlockBits(bmpData);
                    }

                    renderedFrames++;
                    if ((totalFrameCount > 0) && (progress is not null)) {
                        progress.Report(((double)renderedFrames / totalFrameCount) * 100.0);
                    }
                }

                // Render first frame
                RenderFrame(firstFrame);

                // Render remaining frames
                while (enumerator.MoveNext()) {
                    RenderFrame(enumerator.Current);
                }

                writer.Release();
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 3. Merge Audio
            // Give file system a moment to close the video writer handle
            await Task.Delay(500, cancellationToken);

            IMediaInfo videoInfo = await FFmpeg.GetMediaInfo(tempVideoPath, cancellationToken);

            IConversion conversion = FFmpeg.Conversions.New()
                .AddStream(videoInfo.VideoStreams);

            if (File.Exists(audioPath)) {
                IMediaInfo audioInfo = await FFmpeg.GetMediaInfo(audioPath, cancellationToken);
                conversion.AddStream(audioInfo.AudioStreams);
            }

            conversion.SetOutput(outputPath)
                      .SetOverwriteOutput(true)
                      .SetVideoBitrate(2000000); // 2 Mbps

            await conversion.Start(cancellationToken);
        }
        catch (Exception ex) {
            Debug.WriteLine("Render Error: " + ex.Message);
            throw;
        }
        finally {
            // RESOURCE LEAK FIX: Ensure temp video file is reliably deleted even if exceptions occur
            if (File.Exists(tempVideoPath)) {
                try {
                    File.Delete(tempVideoPath);
                }
                catch (Exception ex) {
                    Debug.WriteLine("Temp video cleanup warning: " + ex.Message);
                }
            }
        }
    }
}