using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;

using OpenCvSharp;
using OpenCvSharp.Extensions;

using Xabe.FFmpeg;

using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Size = OpenCvSharp.Size;

namespace AsciiConverter {
    public class AsciiRenderer {
        public static async Task RenderToMp4(string[] frames, double fps, string audioPath, string outputPath, string colorName) {
            if (frames.Length == 0) return;

            // 1. Measure Text
            int width, height;
            Font font = new("Consolas", 10, FontStyle.Bold);

            // Create brush based on color name
            Color fontColor = Color.FromName(colorName);
            SolidBrush fontBrush = new(fontColor);

            using (Bitmap tempBmp = new(1, 1))
            using (Graphics tempG = Graphics.FromImage(tempBmp)) {
                string firstFrame = frames[0];
                SizeF size = tempG.MeasureString(firstFrame, font);
                width = (int)size.Width + 20;
                height = (int)size.Height + 20;

                // Ensure even dimensions
                if (width % 2 != 0) width++;
                if (height % 2 != 0) height++;
            }

            // 2. Setup Video Writer
            // We use .mp4 extension for the temp file and 'mp4v' codec.
            string tempVideoPath = Path.Combine(Path.GetDirectoryName(outputPath)!, "temp_render.mp4");
            int fourcc = VideoWriter.FourCC("mp4v");

            using (VideoWriter writer = new(tempVideoPath, fourcc, fps, new Size(width, height))) {
                if (!writer.IsOpened()) {
                    throw new Exception("Could not open OpenCV VideoWriter. Make sure OpenH264 or generic codecs are installed.");
                }

                using (Bitmap bmp = new(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp)) {
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;

                    foreach (string frame in frames) {
                        g.Clear(Color.Black);
                        g.DrawString(frame, font, fontBrush, 0, 0);

                        using Mat mat = bmp.ToMat();
                        writer.Write(mat);
                    }
                }
                fontBrush.Dispose();
                writer.Release();
            }

            // 3. Merge Audio
            try {
                // Give file system a moment to close the handle
                await Task.Delay(500);

                IMediaInfo videoInfo = await FFmpeg.GetMediaInfo(tempVideoPath);

                IConversion? conversion = FFmpeg.Conversions.New()
                    .AddStream(videoInfo.VideoStreams);

                if (File.Exists(audioPath)) {
                    IMediaInfo audioInfo = await FFmpeg.GetMediaInfo(audioPath);
                    conversion.AddStream(audioInfo.AudioStreams);
                }

                conversion.SetOutput(outputPath)
                          .SetOverwriteOutput(true)
                          .SetVideoBitrate(2000000); // 2 Mbps

                await conversion.Start();

                if (File.Exists(tempVideoPath)) File.Delete(tempVideoPath);
            }
            catch (Exception ex) {
                Debug.WriteLine("FFmpeg Error: " + ex.Message);
                throw;
            }
        }
    }
}