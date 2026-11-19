using ASCIIV.Core;
using ASCIIV.Core.Controls;
using ASCIIV.Player;

using Microsoft.Win32;

using NAudio.Wave;

using OpenCvSharp;

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace ASCIIV.Converter;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow {
    // The ASCII ramps: characters sorted from darkest (@) to lightest (space)
    private const string StandardRamp = "@%#*+=-:. ";
    private const string HighDetailRamp = "$@B%8&WM#*oahkbdpqwmZO0QLCJUYXzcvunxrjft/\\|()1{}[]?-_+~<>i!lI;:,\"^`' ";
    private const string BlockRamp = "█▓▒░ ";
    private const string BinaryRamp = "01";
    private const string GlitchRamp = "@%#*+=~-:,. ";
    private const string OptimizedRamp = "@&%QWNM0gB$#DR8mHXKAUbGOpV4d9h6PkqwSE2]ayjxY5Zoen[ult13If}C{iF|(7J)vTLs?z/*cr!+<>;=^,_:'-. ";

    // The live preview is refreshed once every this many frames
    private const int PreviewInterval = 6;

    // Reported as the progress value while the amount of remaining work is unknown
    private const double IndeterminateProgress = -1;

    private const string FFmpegFolderName = "ffmpeg";

    // Finished .asciiv and .mp4 files
    private static readonly string OutputFolder = Path.Combine(GetUserMediaFolder(), "ASCIIV");

    // Downloaded tools, kept outside the install folder so the app also works from a read-only location
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASCIIV");

    // Intermediate files of a running conversion
    private static readonly string WorkFolder = Path.Combine(Path.GetTempPath(), "ASCIIV");

    // This string will hold the path to the user's video
    private string _inputVideoPath = string.Empty;

    // Stores the specific file created by the last conversion so "Play" works correctly
    private string _lastCreatedFilePath = string.Empty;

    // Default to White in case they select Custom but cancel the dialog
    private string _customColorHex = "#FFFFFF";

    // Allows cancelling long conversions. Not null only while a conversion is running
    private CancellationTokenSource? _conversionCts;

    // Set when the window is closed mid-conversion, so it closes once cleanup has finished
    private bool _closeAfterConversion;

    public MainWindow() {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e) {
        if (_conversionCts is not null) {
            // Cancel and wait for the conversion to clean up its temp files before closing
            _closeAfterConversion = true;
            _conversionCts.Cancel();
            CancelButton.IsEnabled = false;
            StatusText.Text = "Cancelling conversion...";
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private static string GetUserMediaFolder() {
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return string.IsNullOrEmpty(videos)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : videos;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) {
        bool hasFFmpeg = await InitializeFFmpeg();
        LoadButton.IsEnabled = true;

        if (hasFFmpeg) {
            StatusText.Text = "Ready";
            return;
        }

        StatusText.Text = "Ready (FFmpeg unavailable)";
        CustomMessageBox.Show(
            "FFmpeg could not be found or downloaded.\n\n"
            + "Videos can still be converted, but without audio, and MP4 export will not work.\n\n"
            + "Check your internet connection and restart the application to try again.",
            "FFmpeg Unavailable",
            CustomMessageBox.MessageBoxType.Warning,
            this);
    }

    // Uses an FFmpeg next to the application if there is one, otherwise downloads it once per user
    private async Task<bool> InitializeFFmpeg() {
        string bundledFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FFmpegFolderName);
        string downloadFolder = Path.Combine(AppDataFolder, FFmpegFolderName);

        foreach (string folder in (string[])[bundledFolder, downloadFolder]) {
            if (HasFFmpeg(folder)) {
                FFmpeg.SetExecutablesPath(folder);
                return true;
            }
        }

        try {
            StatusText.Text = "Downloading FFmpeg (first run only)...";
            Directory.CreateDirectory(downloadFolder);
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, downloadFolder);
        }
        catch (Exception ex) {
            Debug.WriteLine("FFmpeg download failed: " + ex.Message);
            return false;
        }

        FFmpeg.SetExecutablesPath(downloadFolder);
        return HasFFmpeg(downloadFolder);
    }

    // Xabe.FFmpeg needs both tools: ffprobe reads media info, ffmpeg converts
    private static bool HasFFmpeg(string folder) {
        return File.Exists(Path.Combine(folder, "ffmpeg.exe")) && File.Exists(Path.Combine(folder, "ffprobe.exe"));
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e) {
        OpenFileDialog openFileDialog = new() {
            Filter = "Video Files|*.mp4;*.avi;*.mov"
        };

        if (openFileDialog.ShowDialog() != true) {
            return;
        }

        _inputVideoPath = openFileDialog.FileName;
        StatusText.Text = "Video loaded: " + Path.GetFileName(_inputVideoPath);
        ProjectNameText.Text = Path.GetFileNameWithoutExtension(_inputVideoPath);
        ConvertButton.IsEnabled = true;
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrEmpty(_inputVideoPath)) {
            return;
        }

        // Capture the UI state here, on the UI thread
        string selectedColor = ColorComboBox.SelectedItem is ComboBoxItem item ? item.Content.ToString()! : _customColorHex;
        string styleText = TextStyleComboBox.Text;
        ConversionOptions options = new(
            VideoPath: _inputVideoPath,
            ProjectName: ProjectSanitizer.SanitizeProjectName(ProjectNameText.Text),
            Ramp: styleText switch {
                "Standard" => StandardRamp,
                "Binary" => BinaryRamp,
                "High Detail" => HighDetailRamp,
                "Blocks" => BlockRamp,
                "Glitch" => GlitchRamp,
                "Optimized" => OptimizedRamp,
                // Anything else typed into the box is used as a custom ramp
                _ => string.IsNullOrEmpty(styleText) ? StandardRamp : styleText
            },
            Width: (int)VideoWidthSlider.Value,
            Contrast: ContrastSlider.Value,
            Invert: InvertCheck.IsChecked == true,
            ColorName: selectedColor,
            BitCrush: BitCrushCheck.IsChecked == true,
            BitDepth: (int)BitDepthSlider.Value,
            DownsampleFactor: (int)DownsampleSlider.Value,
            CreateMp4: ToMp4Check.IsChecked == true);

        // Created here, on the UI thread, so the callbacks are marshalled back to it
        ConversionUi ui = new(
            Percent: new Progress<double>(ShowProgress),
            Status: new Progress<string>(text => StatusText.Text = text),
            Preview: new Progress<string>(text => PreviewText.Text = text));

        _conversionCts = new CancellationTokenSource();
        CancellationToken token = _conversionCts.Token;

        ConvertButton.IsEnabled = false;
        LoadButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true;
        StatusText.Text = "Converting... Please wait.";
        ConvertProgressBar.Value = 0;
        PreviewText.Foreground = ParseBrush(selectedColor);

        ConversionResult? result = null;
        try {
            result = await Task.Run(() => ConvertVideo(options, ui, token), token);

            _lastCreatedFilePath = result.AsciivPath;
            ConvertProgressBar.Value = 100;
            StatusText.Text = "Conversion Complete!";
        }
        catch (OperationCanceledException) {
            StatusText.Text = "Conversion Cancelled.";
            ConvertProgressBar.Value = 0;
        }
        catch (Exception ex) {
            Debug.WriteLine(ex);
            StatusText.Text = "Error: " + ex.Message;
            ConvertProgressBar.Value = 0;
        }
        finally {
            ConvertButton.IsEnabled = true;
            LoadButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            CancelButton.Visibility = Visibility.Collapsed;
            ConvertProgressBar.IsIndeterminate = false;
            _conversionCts.Dispose();
            _conversionCts = null;
        }

        if (_closeAfterConversion) {
            Close();
            return;
        }

        if (result is not null) {
            ShowCompletionMessage(result);
        }
    }

    private void ShowProgress(double percent) {
        ConvertProgressBar.IsIndeterminate = percent < 0;
        if (percent >= 0) {
            ConvertProgressBar.Value = Math.Clamp(percent, 0, 100);
        }
    }

    private void ShowCompletionMessage(ConversionResult result) {
        string message = "Conversion Complete! Saved:\n" + result.AsciivPath;
        if (result.Mp4Path is not null) {
            message += "\n" + result.Mp4Path;
        }

        if (result.AudioWarning is not null) {
            message += "\n\nNote: " + result.AudioWarning;
        }

        CustomMessageBox.Show(message, "Success", CustomMessageBox.MessageBoxType.Ok, this);
    }

    private static Brush ParseBrush(string colorName) {
        try {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorName));
        }
        catch (FormatException) {
            return Brushes.White;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        _conversionCts?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling conversion...";
    }

    // Runs on a background thread: everything it shows goes through the progress callbacks in ConversionUi
    private static async Task<ConversionResult> ConvertVideo(ConversionOptions options, ConversionUi ui, CancellationToken cancellationToken) {
        Directory.CreateDirectory(OutputFolder);
        Directory.CreateDirectory(WorkFolder);

        // Unique intermediate filenames so parallel instances never collide
        string workId = Guid.NewGuid().ToString("N");
        string framesPath = Path.Combine(WorkFolder, $"{workId}_frames.bin");
        string mp3Path = Path.Combine(WorkFolder, $"{workId}_audio.mp3");
        string wavPath = Path.Combine(WorkFolder, $"{workId}_audio.wav");

        try {
            (string? audioPath, string? audioWarning) = await ExtractAudio(options, mp3Path, wavPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            AsciivHeader header = WriteFrames(options, framesPath, ui, cancellationToken);

            string asciivPath = Path.Combine(OutputFolder, options.ProjectName + AsciivFormat.FileExtension);
            await AsciivWriter.WriteAsync(asciivPath, header, audioPath, framesPath, cancellationToken);

            string? mp4Path = null;
            if (options.CreateMp4) {
                mp4Path = Path.Combine(OutputFolder, options.ProjectName + ".mp4");
                await RenderMp4(asciivPath, audioPath, mp4Path, ui, cancellationToken);
            }

            return new ConversionResult(asciivPath, mp4Path, audioWarning);
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

    // Converts every video frame to palette indices and appends them to framesPath
    private static AsciivHeader WriteFrames(ConversionOptions options, string framesPath, ConversionUi ui, CancellationToken cancellationToken) {
        // One array lookup per pixel instead of floating-point maths
        (string palette, byte[] indices) = AsciiRamp.BuildIndexedLookup(options.Ramp, options.Contrast, options.Invert);

        using VideoCapture capture = new(options.VideoPath);
        if (!capture.IsOpened()) {
            throw new InvalidOperationException("The video could not be opened.");
        }

        double fps = capture.Get(VideoCaptureProperties.Fps);
        if (!double.IsFinite(fps) || (fps <= 0)) {
            fps = 30.0;
        }

        // Only an estimate: some containers report a frame count that is slightly off
        int estimatedFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);

        using FileStream frameStream = new(framesPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024);
        using Mat frame = new();
        using Mat resized = new();
        using Mat gray = new();

        int width = options.Width;
        int height = 0;
        byte[] cells = [];
        int frameCount = 0;
        int lastPercent = -1;

        while (capture.Read(frame) && !frame.Empty()) {
            cancellationToken.ThrowIfCancellationRequested();

            if (height == 0) {
                // Characters are about twice as tall as they are wide, so only half the rows are needed
                double aspectRatio = (double)frame.Height / frame.Width;
                height = Math.Max(1, (int)(width * aspectRatio * 0.5));
                cells = new byte[width * height];
            }

            // Area interpolation averages the source pixels, which gives the cleanest result when shrinking
            Cv2.Resize(frame, resized, new OpenCvSharp.Size(width, height), 0, 0, InterpolationFlags.Area);
            Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

            // The unsafe indexer avoids per-pixel bounds checking
            Mat.UnsafeIndexer<byte> pixels = gray.GetUnsafeGenericIndexer<byte>();
            for (int y = 0; y < height; y++) {
                int rowStart = y * width;
                for (int x = 0; x < width; x++) {
                    cells[rowStart + x] = indices[pixels[y, x]];
                }
            }

            frameStream.Write(cells);
            frameCount++;

            if ((frameCount % PreviewInterval) == 0) {
                ui.Preview.Report(AsciivReader.ComposeFrameText(cells, width, height, palette));
            }

            if (estimatedFrames > 0) {
                int percent = Math.Min(100, (int)(((double)frameCount / estimatedFrames) * 100));
                if (percent > lastPercent) {
                    lastPercent = percent;
                    ui.Percent.Report(percent);
                }
            }
        }

        if (frameCount == 0) {
            throw new InvalidOperationException("No frames could be read from the video.");
        }

        return new AsciivHeader(fps, options.ColorName, width, height, frameCount, palette);
    }

    private static async Task RenderMp4(string asciivPath, string? audioPath, string mp4Path, ConversionUi ui, CancellationToken cancellationToken) {
        ui.Status.Report("Rendering MP4... (This may take a while)");
        ui.Percent.Report(IndeterminateProgress);

        // Frames are read on demand, so the whole video never has to fit in memory
        using AsciivReader reader = AsciivReader.Open(asciivPath);
        IEnumerable<string> frames = Enumerable.Range(0, reader.FrameCount).Select(reader.ReadFrame);

        await AsciiRenderer.RenderToMp4(
            frames,
            reader.FramesPerSecond,
            audioPath ?? string.Empty,
            mp4Path,
            reader.ColorName,
            reader.FrameCount,
            ui.Percent,
            cancellationToken);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e) {
        // Prefer the file created by the most recent conversion
        string path = _lastCreatedFilePath;

        // Fallback: a file named after the text box input
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            // Sanitized so the name cannot point outside the output folder
            string safeName = ProjectSanitizer.SanitizeProjectName(ProjectNameText.Text, string.Empty);
            path = string.IsNullOrEmpty(safeName)
                ? string.Empty
                : Path.Combine(OutputFolder, safeName + AsciivFormat.FileExtension);
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            CustomMessageBox.Show(
                "Please convert a video first or ensure the Project Name matches an existing file in:\n" + OutputFolder,
                "Nothing to Play",
                CustomMessageBox.MessageBoxType.Warning,
                this);
            return;
        }

        PlayerWindow player = new(path);
        player.Show();
    }

    private void ToMp4Check_Checked(object sender, RoutedEventArgs e) {
        CustomMessageBox.Show(
            "Warning: Creating an MP4 file is a slow process.\n\n"
            + "Depending on the video length, this could take several minutes to render frame-by-frame.",
            "Performance Warning",
            CustomMessageBox.MessageBoxType.Warning,
            this);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left) {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Close();
    }

    private void CustomColorButton_OnClick(object sender, RoutedEventArgs e) {
        ColorPickerWindow picker = new() { Owner = this };
        if (picker.ShowDialog() != true) {
            return;
        }

        Color color = picker.SelectedColor;
        _customColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        if (CustomColorButton.Template.FindName("butonBorder", CustomColorButton) is Border swatch) {
            swatch.Background = new SolidColorBrush(color);
        }

        ColorComboBox.SelectedIndex = -1;
        PreviewText.Foreground = new SolidColorBrush(color);
    }

    private sealed record ConversionOptions(
        string VideoPath,
        string ProjectName,
        string Ramp,
        int Width,
        double Contrast,
        bool Invert,
        string ColorName,
        bool BitCrush,
        int BitDepth,
        int DownsampleFactor,
        bool CreateMp4);

    private sealed record ConversionResult(string AsciivPath, string? Mp4Path, string? AudioWarning);

    // Progress callbacks of a conversion. A negative percentage means the remaining work is unknown
    private sealed record ConversionUi(IProgress<double> Percent, IProgress<string> Status, IProgress<string> Preview);
}