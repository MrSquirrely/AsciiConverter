using ASCIIV.Core;

using NAudio.Wave;

using OpenCvSharp;

using System.Diagnostics;
using System.IO;

using Xabe.FFmpeg;

namespace ASCIIV.Converter;

/// <summary>Everything a conversion needs to know. Captured on the UI thread before the work starts.</summary>
/// <param name="VideoPath">The source video.</param>
/// <param name="OutputFolder">Where the finished files are saved.</param>
/// <param name="WorkFolder">Where intermediate files are kept while converting.</param>
/// <param name="ProjectName">File name of the output, without extension. Must already be sanitized.</param>
/// <param name="Ramp">Characters from darkest to lightest.</param>
/// <param name="Width">Characters per row.</param>
/// <param name="Contrast">Contrast multiplier; 1.0 leaves the video unchanged.</param>
/// <param name="Invert">Swap dark and light characters.</param>
/// <param name="ColorName">Named colour or #RRGGBB value of the text.</param>
/// <param name="MatchColors">Give every character the colour of the video at that spot.</param>
/// <param name="BitCrush">Apply the lo-fi audio effect.</param>
/// <param name="BitDepth">Bit depth of the lo-fi effect.</param>
/// <param name="DownsampleFactor">Sample-rate reduction of the lo-fi effect.</param>
/// <param name="Export">Standard video format to render next to the .asciiv file.</param>
/// <param name="FontName">Name of the <see cref="FrameFont"/> the characters are drawn with. Empty for the default font.</param>
public sealed record ConversionOptions(
    string VideoPath,
    string OutputFolder,
    string WorkFolder,
    string ProjectName,
    string Ramp,
    int Width,
    double Contrast = 1.0,
    bool Invert = false,
    string ColorName = "White",
    bool MatchColors = false,
    bool BitCrush = false,
    int BitDepth = 8,
    int DownsampleFactor = 5,
    ExportFormat Export = ExportFormat.None,
    string FontName = "");

/// <param name="AsciivPath">The .asciiv file that was written.</param>
/// <param name="ExportPath">The exported video, or null when no export was requested.</param>
/// <param name="AudioWarning">Explains why the output is silent, or null when it has sound.</param>
public sealed record ConversionResult(string AsciivPath, string? ExportPath, string? AudioWarning);

/// <summary>Callbacks a conversion reports through. Create them on the UI thread.</summary>
/// <param name="Percent">0 to 100, or <see cref="VideoConverter.IndeterminateProgress"/> when the remaining work is unknown.</param>
/// <param name="Status">Short description of the current step.</param>
/// <param name="Preview">A recently converted frame.</param>
public sealed record ConversionProgress(IProgress<double> Percent, IProgress<string> Status, IProgress<AsciiFrame> Preview);

/// <summary>
/// Converts a video file to an .asciiv file, and optionally renders that to a standard video format.
/// Has no dependency on the UI, so it can run on a background thread.
/// </summary>
public static class VideoConverter {
    /// <summary>Reported as the percentage while the amount of remaining work is unknown.</summary>
    public const double IndeterminateProgress = -1;

    // A preview frame is reported once every this many frames
    private const int PreviewInterval = 6;

    private const double FallbackFramesPerSecond = 30.0;

    // In the default font, characters are about twice as tall as they are wide, so only half the rows are needed
    private const double CharacterAspect = 0.5;

    public static async Task<ConversionResult> ConvertAsync(
        ConversionOptions options,
        ConversionProgress progress,
        CancellationToken cancellationToken = default) {

        Directory.CreateDirectory(options.OutputFolder);
        Directory.CreateDirectory(options.WorkFolder);

        // Unique intermediate filenames so parallel conversions never collide
        string workId = Guid.NewGuid().ToString("N");
        string framesPath = Path.Combine(options.WorkFolder, $"{workId}_frames.bin");
        string mp3Path = Path.Combine(options.WorkFolder, $"{workId}_audio.mp3");
        string wavPath = Path.Combine(options.WorkFolder, $"{workId}_audio.wav");

        try {
            (string? audioPath, string? audioWarning) = await ExtractAudio(options, mp3Path, wavPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            AsciivHeader header = WriteFrames(options, framesPath, progress, cancellationToken);

            string asciivPath = Path.Combine(options.OutputFolder, options.ProjectName + AsciivFormat.FileExtension);
            await AsciivWriter.WriteAsync(asciivPath, header, audioPath, framesPath, cancellationToken);

            string? exportPath = null;
            if (options.Export != ExportFormat.None) {
                exportPath = Path.Combine(options.OutputFolder, options.ProjectName + options.Export.GetFileExtension());
                await RenderExport(asciivPath, audioPath, exportPath, options.Export, progress, cancellationToken);
            }

            return new ConversionResult(asciivPath, exportPath, audioWarning);
        }
        finally {
            // Intermediate files are removed even on failure or cancellation
            foreach (string path in (string[])[framesPath, mp3Path, wavPath]) {
                try {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                    Debug.WriteLine("Cleanup warning: " + ex.Message);
                }
            }
        }
    }

    // Returns the audio file to embed, or a warning explaining why the output has no sound
    private static async Task<(string? AudioPath, string? Warning)> ExtractAudio(
        ConversionOptions options,
        string mp3Path,
        string wavPath,
        CancellationToken cancellationToken) {

        try {
            IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(options.VideoPath, cancellationToken);
            if (!mediaInfo.AudioStreams.Any()) {
                return (null, "The video has no audio track, so the output is silent.");
            }

            await FFmpeg.Conversions.New()
                .AddStream(mediaInfo.AudioStreams)
                .SetOutput(mp3Path)
                .Start(cancellationToken);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            Debug.WriteLine("Audio extraction failed: " + ex);
            return (null, "The audio could not be extracted, so the output is silent.\n" + ex.Message);
        }

        if (!options.BitCrush) {
            return (mp3Path, null);
        }

        await using AudioFileReader reader = new(mp3Path);
        BitCrusher crusher = new(reader);
        crusher.SetBitDepth(options.BitDepth);
        crusher.SetDownSampleFactor(options.DownsampleFactor);
        WaveFileWriter.CreateWaveFile16(wavPath, crusher);
        return (wavPath, null);
    }

    // Converts every video frame to palette indices (and colours) and appends them to framesPath
    private static AsciivHeader WriteFrames(
        ConversionOptions options,
        string framesPath,
        ConversionProgress progress,
        CancellationToken cancellationToken) {

        // One array lookup per pixel instead of floating-point maths
        (string palette, byte[] indices) = AsciiRamp.BuildIndexedLookup(options.Ramp, options.Contrast, options.Invert);

        // The row count follows the shape of a character cell, so the picture keeps its proportions in any font
        FrameFont font = FrameFont.Get(options.FontName);
        double cellAspect = font.IsDefault ? CharacterAspect : font.CellAspect;

        using VideoCapture capture = new(options.VideoPath);
        if (!capture.IsOpened()) {
            throw new InvalidOperationException("The video could not be opened.");
        }

        double fps = capture.Get(VideoCaptureProperties.Fps);
        if (!double.IsFinite(fps) || (fps <= 0)) {
            fps = FallbackFramesPerSecond;
        }

        // Only an estimate: some containers report a frame count that is slightly off, and GIFs often report none
        int estimatedFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);
        if (estimatedFrames <= 0) {
            progress.Percent.Report(IndeterminateProgress);
        }

        using FileStream frameStream = new(framesPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024);
        using Mat frame = new();
        using Mat bgr = new();
        using Mat resized = new();
        using Mat gray = new();

        int width = options.Width;
        int height = 0;
        int cellCount = 0;
        byte[] frameBytes = [];
        int frameCount = 0;
        int lastPercent = -1;

        while (capture.Read(frame) && !frame.Empty()) {
            cancellationToken.ThrowIfCancellationRequested();

            if (height == 0) {
                double aspectRatio = (double)frame.Height / frame.Width;
                height = Math.Max(1, (int)(width * aspectRatio * cellAspect));
                cellCount = width * height;
                frameBytes = new byte[options.MatchColors ? (cellCount * 2) : cellCount];
            }

            // Area interpolation averages the source pixels, which gives the cleanest result when shrinking
            Cv2.Resize(ToBgr(frame, bgr), resized, new Size(width, height), 0, 0, InterpolationFlags.Area);
            Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

            // The unsafe indexers avoid per-pixel bounds checking
            Mat.UnsafeIndexer<byte> grayPixels = gray.GetUnsafeGenericIndexer<byte>();
            for (int y = 0; y < height; y++) {
                int rowStart = y * width;
                for (int x = 0; x < width; x++) {
                    frameBytes[rowStart + x] = indices[grayPixels[y, x]];
                }
            }

            if (options.MatchColors) {
                Mat.UnsafeIndexer<Vec3b> colorPixels = resized.GetUnsafeGenericIndexer<Vec3b>();
                for (int y = 0; y < height; y++) {
                    int rowStart = cellCount + (y * width);
                    for (int x = 0; x < width; x++) {
                        Vec3b pixel = colorPixels[y, x];
                        (byte red, byte green, byte blue) = AsciivColors.Brighten(pixel.Item2, pixel.Item1, pixel.Item0);
                        frameBytes[rowStart + x] = AsciivColors.Quantize(red, green, blue);
                    }
                }
            }

            frameStream.Write(frameBytes);
            frameCount++;

            if ((frameCount % PreviewInterval) == 1) {
                ReadOnlySpan<byte> colors = options.MatchColors ? frameBytes.AsSpan(cellCount) : [];
                progress.Preview.Report(AsciivReader.CreateFrame(frameBytes.AsSpan(0, cellCount), colors, width, height, palette));
            }

            if (estimatedFrames > 0) {
                int percent = Math.Min(100, (int)(((double)frameCount / estimatedFrames) * 100));
                if (percent > lastPercent) {
                    lastPercent = percent;
                    progress.Percent.Report(percent);
                }
            }
        }

        if (frameCount == 0) {
            throw new InvalidOperationException("No frames could be read from the video.");
        }

        return new AsciivHeader(fps, options.ColorName, width, height, frameCount, palette, options.MatchColors, options.FontName);
    }

    // Most sources decode to BGR already; grayscale and BGRA frames are converted into the scratch Mat
    private static Mat ToBgr(Mat frame, Mat scratch) {
        switch (frame.Channels()) {
            case 1:
                Cv2.CvtColor(frame, scratch, ColorConversionCodes.GRAY2BGR);
                return scratch;

            case 4:
                Cv2.CvtColor(frame, scratch, ColorConversionCodes.BGRA2BGR);
                return scratch;

            default:
                return frame;
        }
    }

    private static async Task RenderExport(
        string asciivPath,
        string? audioPath,
        string exportPath,
        ExportFormat format,
        ConversionProgress progress,
        CancellationToken cancellationToken) {

        progress.Status.Report($"Rendering {format.GetFileExtension().TrimStart('.').ToUpperInvariant()}... (This may take a while)");
        progress.Percent.Report(IndeterminateProgress);

        // Frames are read on demand, so the whole video never has to fit in memory
        using AsciivReader reader = AsciivReader.Open(asciivPath);
        IEnumerable<AsciiFrame> frames = Enumerable.Range(0, reader.FrameCount).Select(reader.GetFrame);

        await AsciiRenderer.RenderAsync(
            frames,
            reader.FramesPerSecond,
            audioPath,
            exportPath,
            format,
            reader.ColorName,
            reader.FontName,
            reader.FrameCount,
            progress.Percent,
            cancellationToken);
    }
}