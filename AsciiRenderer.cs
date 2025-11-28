using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;

using OpenCvSharp;
using OpenCvSharp.Extensions;

using Xabe.FFmpeg;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace AsciiConverter {
    public class AsciiRenderer {
        public async Task RenderToMp4(string[] frames, double fps, string audioPath, string outputPath) {
            if (frames.Length == 0) return;

            // 1. Measure Text
            int width, height;
            Font font = new Font("Consolas", 10, FontStyle.Bold);

            using (Bitmap tempBmp = new Bitmap(1, 1))
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
            string tempVideoPath = Path.Combine(Path.GetDirectoryName(outputPath), "temp_render.mp4");
            var fourcc = VideoWriter.FourCC("mp4v");

            using (var writer = new VideoWriter(tempVideoPath, fourcc, fps, new OpenCvSharp.Size(width, height), true)) {
                if (!writer.IsOpened()) {
                    throw new Exception("Could not open OpenCV VideoWriter. Make sure OpenH264 or generic codecs are installed.");
                }

                // FIX: Force PixelFormat.Format24bppRgb (3 Channels)
                // Default is 32bpp (4 Channels) which breaks VideoWriter
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bmp)) {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    foreach (var frame in frames) {
                        g.Clear(Color.Black);
                        g.DrawString(frame, font, Brushes.LimeGreen, 0, 0);

                        using (Mat mat = BitmapConverter.ToMat(bmp)) {
                            writer.Write(mat);
                        }
                    }
                }
                writer.Release();
            }

            // 3. Merge Audio
            try {
                // Give file system a moment to close the handle
                await Task.Delay(500);

                IMediaInfo videoInfo = await FFmpeg.GetMediaInfo(tempVideoPath);

                var conversion = FFmpeg.Conversions.New()
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
                System.Diagnostics.Debug.WriteLine("FFmpeg Error: " + ex.Message);
                throw;
            }
        }
    }
}