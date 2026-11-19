using ASCIIV.Core;
using ASCIIV.Core.Controls;

using Microsoft.Win32;

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ASCIIV.Player;

public partial class PlayerWindow {
    private const double SeekStepSeconds = 5.0;
    private const double VolumeStep = 0.05;
    private const double DefaultVolume = 0.5;

    private readonly MediaPlayer _mediaPlayer = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly string? _asciiPathToLoad;

    // The open file. Frames are read from it on demand, so the whole video never has to fit in memory
    private AsciivReader? _reader;

    // The frame currently on screen, so it is not read and laid out again on every tick
    private int _shownFrameIndex = -1;

    // State flags
    private bool _isWindowOpen = true;
    private bool _isPaused;
    private bool _isDragging;
    private bool _isLooping;
    private bool _isMuted;

    // Only one play loop may run, no matter how many files are loaded
    private bool _isPlayLoopRunning;

    private bool _hasAudio;

    // Once the audio has finished, the stopwatch takes over as the clock so the video can still reach its end
    private bool _hasAudioEnded;

    // The embedded audio is extracted to this file for playback, and deleted on stop/exit
    private string? _tempAudioFilePath;

    private double _previousVolume = DefaultVolume;

    // Timeline position at which the stopwatch was last restarted
    private TimeSpan _seekOffset = TimeSpan.Zero;

    public PlayerWindow(string? asciiPath = null) {
        InitializeComponent();
        _asciiPathToLoad = asciiPath;
        _mediaPlayer.MediaEnded += (_, _) => _hasAudioEnded = true;
        Loaded += PlayerWindow_Loaded;
    }

    private int FrameCount => _reader?.FrameCount ?? 0;

    protected override void OnClosed(EventArgs e) {
        _isWindowOpen = false;
        StopPlayback();
        base.OnClosed(e);
    }

    private void PlayerWindow_Loaded(object sender, RoutedEventArgs e) {
        Focus(); // Enable keyboard navigation immediately
        if (!string.IsNullOrEmpty(_asciiPathToLoad)) {
            LoadProject(_asciiPathToLoad);
        }
        else {
            ShowOpenFileDialog();
        }
    }

    private void ShowOpenFileDialog() {
        OpenFileDialog openFileDialog = new() {
            Filter = $"ASCII Video|*{AsciivFormat.FileExtension}"
        };

        if (openFileDialog.ShowDialog() == true) {
            LoadProject(openFileDialog.FileName);
        }
    }

    private void Window_Drop(object sender, DragEventArgs e) {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) {
            return;
        }

        string file = files[0];
        if (string.Equals(Path.GetExtension(file), AsciivFormat.FileExtension, StringComparison.OrdinalIgnoreCase)) {
            LoadProject(file);
        }
    }

    // --- LOADING ---

    private void StopPlayback() {
        _isPaused = true;
        _stopwatch.Reset();
        _mediaPlayer.Stop();
        _mediaPlayer.Close();
        _hasAudio = false;
        _hasAudioEnded = false;
        _seekOffset = TimeSpan.Zero;
        _shownFrameIndex = -1;

        _reader?.Dispose();
        _reader = null;

        // Delete the temporary audio file so it does not pile up in %TEMP%
        if (_tempAudioFilePath is not null) {
            try {
                File.Delete(_tempAudioFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                Debug.WriteLine("Temp audio cleanup warning: " + ex.Message);
            }

            _tempAudioFilePath = null;
        }
    }

    private void LoadProject(string filePath) {
        StopPlayback();

        try {
            _reader = AsciivReader.Open(filePath);
            DisplayText.Foreground = ParseBrush(_reader.ColorName);

            if (_reader.HasAudio) {
                byte[] audioBytes = _reader.ReadAudio();

                // MediaPlayer picks its decoder by extension, so detect WAV (RIFF header) versus MP3.
                // The unique name prevents collisions between multiple player instances
                string extension = audioBytes.AsSpan().StartsWith("RIFF"u8) ? ".wav" : ".mp3";
                _tempAudioFilePath = Path.Combine(Path.GetTempPath(), $"asciiv_player_{Guid.NewGuid():N}{extension}");
                File.WriteAllBytes(_tempAudioFilePath, audioBytes);

                _mediaPlayer.Open(new Uri(_tempAudioFilePath));
                _hasAudio = true;
            }

            _mediaPlayer.Volume = VolumeSlider.Value;
            WindowTitleText.Text = $"{Path.GetFileNameWithoutExtension(filePath)} - {FrameCount} Frames";
            ProgressSlider.Maximum = Math.Max(0, FrameCount - 1);
            ProgressSlider.Value = 0;
            UpdateTimecode(TimeSpan.Zero);

            StartSyncPlayback();
        }
        catch (Exception ex) {
            StopPlayback();
            DisplayText.Text = string.Empty;
            CustomMessageBox.Show("Error loading file: " + ex.Message, "Error", CustomMessageBox.MessageBoxType.Warning, this);
        }
    }

    private static Brush ParseBrush(string colorName) {
        try {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorName));
        }
        catch (FormatException) {
            return Brushes.White;
        }
    }

    // --- PLAYBACK LOGIC ---

    private async void StartSyncPlayback() {
        // A newly loaded file always starts playing, even if the previous one was stopped or paused
        _isPaused = false;
        _hasAudioEnded = false;
        PlayButtonIcon.Data = (Geometry)FindResource("PauseIcon");
        if (_hasAudio) {
            _mediaPlayer.Play();
        }

        _stopwatch.Restart();

        // The loop from an earlier file keeps running and picks up the new one
        if (_isPlayLoopRunning) {
            return;
        }

        _isPlayLoopRunning = true;
        try {
            await PlayLoop();
        }
        catch (Exception ex) {
            Debug.WriteLine(ex);
        }
        finally {
            _isPlayLoopRunning = false;
        }
    }

    private async Task PlayLoop() {
        while (_isWindowOpen) {
            if (_isPaused || (_reader is null)) {
                await Task.Delay(100);
                continue;
            }

            TimeSpan currentTime = GetPlaybackTime();
            int frameIndex = (int)(currentTime.TotalSeconds * _reader.FramesPerSecond);

            if (frameIndex < FrameCount) {
                ShowFrame(frameIndex);
                if (!_isDragging) {
                    ProgressSlider.Value = frameIndex;
                }

                UpdateTimecode(currentTime);
            }
            else if (_isLooping && (FrameCount > 0)) {
                // Loop: start again from the first frame without pausing
                _seekOffset = TimeSpan.Zero;
                _stopwatch.Restart();
                _hasAudioEnded = false;
                if (_hasAudio) {
                    _mediaPlayer.Position = TimeSpan.Zero;
                    _mediaPlayer.Play();
                }

                ShowFirstFrame();
            }
            else {
                // End of playback: rewind and wait for the user to press play
                _isPaused = true;
                _stopwatch.Reset();
                _seekOffset = TimeSpan.Zero;
                _hasAudioEnded = false;
                if (_hasAudio) {
                    _mediaPlayer.Stop();
                }

                PlayButtonIcon.Data = (Geometry)FindResource("PlayIcon");
                ShowFirstFrame();
            }

            await Task.Delay(16); // ~60 checks per second
        }
    }

    // The audio position is the master clock while audio is playing; otherwise the stopwatch is
    private TimeSpan GetPlaybackTime() {
        TimeSpan stopwatchTime = _stopwatch.Elapsed + _seekOffset;
        if (!_hasAudio || _hasAudioEnded || (_mediaPlayer.Source is null)) {
            return stopwatchTime;
        }

        // The audio position stays at zero until the media has opened
        TimeSpan audioTime = _mediaPlayer.Position;
        return ((audioTime == TimeSpan.Zero) && (_stopwatch.ElapsedMilliseconds > 500)) ? stopwatchTime : audioTime;
    }

    private void ShowFrame(int index) {
        if ((index == _shownFrameIndex) || (_reader is null)) {
            return;
        }

        try {
            DisplayText.Text = _reader.ReadFrame(index);
            _shownFrameIndex = index;
        }
        catch (IOException ex) {
            Debug.WriteLine("ReadFrame error: " + ex.Message);
        }
    }

    private void ShowFirstFrame() {
        ProgressSlider.Value = 0;
        ShowFrame(0);
        UpdateTimecode(TimeSpan.Zero);
    }

    private void UpdateTimecode(TimeSpan current) {
        TimeSpan total = _reader?.Duration ?? TimeSpan.Zero;
        TimecodeText.Text = $"{FormatTime(current)} / {FormatTime(total)}";
    }

    private static string FormatTime(TimeSpan time) {
        return time.ToString(time.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);
    }

    // --- SEEKING ---

    private void ProgressSlider_DragStarted(object sender, DragStartedEventArgs e) {
        _isDragging = true;
    }

    private void ProgressSlider_DragCompleted(object sender, DragCompletedEventArgs e) {
        _isDragging = false;
        SeekToFrame((int)ProgressSlider.Value);
    }

    private void SeekToFrame(int frameIndex) {
        if ((_reader is null) || (FrameCount == 0)) {
            return;
        }

        frameIndex = Math.Clamp(frameIndex, 0, FrameCount - 1);
        TimeSpan newTime = TimeSpan.FromSeconds(frameIndex / _reader.FramesPerSecond);

        _seekOffset = newTime;
        // The stopwatch must stay stopped while paused, or the paused time is added on resume
        _stopwatch.Reset();
        if (!_isPaused) {
            _stopwatch.Start();
        }

        if (_hasAudio) {
            _hasAudioEnded = false;
            _mediaPlayer.Position = newTime;
            if (!_isPaused) {
                _mediaPlayer.Play();
            }
        }

        ProgressSlider.Value = frameIndex;
        ShowFrame(frameIndex);
        UpdateTimecode(newTime);
    }

    private void SeekRelative(double seconds) {
        if (_reader is null) {
            return;
        }

        int deltaFrames = (int)(seconds * _reader.FramesPerSecond);
        SeekToFrame((int)ProgressSlider.Value + deltaFrames);
    }

    // --- KEYBOARD SHORTCUTS ---

    private void Window_KeyDown(object sender, KeyEventArgs e) {
        switch (e.Key) {
            case Key.Space:
                TogglePlayPause();
                break;

            case Key.Left:
                SeekRelative(-SeekStepSeconds);
                break;

            case Key.Right:
                SeekRelative(SeekStepSeconds);
                break;

            case Key.Up:
                VolumeSlider.Value = Math.Min(1.0, VolumeSlider.Value + VolumeStep);
                break;

            case Key.Down:
                VolumeSlider.Value = Math.Max(0.0, VolumeSlider.Value - VolumeStep);
                break;

            case Key.F:
                ToggleFullScreen();
                break;

            case Key.M:
                ToggleMute();
                break;

            case Key.L:
                ToggleLoop();
                break;

            case Key.S:
                SaveSnapshot();
                break;

            case Key.Escape when WindowState == WindowState.Maximized:
                ToggleFullScreen();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    // --- CONTROLS ---

    private void TogglePlayPause() {
        if (_isPaused) {
            _isPaused = false;
            _stopwatch.Start();
            if (_hasAudio) {
                _mediaPlayer.Play();
            }

            PlayButtonIcon.Data = (Geometry)FindResource("PauseIcon");
        }
        else {
            _isPaused = true;
            _stopwatch.Stop();
            if (_hasAudio) {
                _mediaPlayer.Pause();
            }

            PlayButtonIcon.Data = (Geometry)FindResource("PlayIcon");
        }
    }

    private void ToggleMute() {
        if (_isMuted) {
            VolumeSlider.Value = (_previousVolume > 0) ? _previousVolume : DefaultVolume;
            _isMuted = false;
        }
        else {
            _previousVolume = VolumeSlider.Value;
            VolumeSlider.Value = 0;
            _isMuted = true;
        }
    }

    private void ToggleLoop() {
        _isLooping = !_isLooping;
        LoopButtonText.Opacity = _isLooping ? 1.0 : 0.6;
        LoopButton.BorderThickness = new Thickness(_isLooping ? 1.5 : 0);
        LoopButton.BorderBrush = _isLooping ? Brushes.LightGreen : Brushes.Transparent;
    }

    private void ToggleFullScreen() {
        if (WindowState == WindowState.Maximized) {
            WindowState = WindowState.Normal;
            FullScreenIcon.Data = (Geometry)FindResource("FullScreenIcon");
        }
        else {
            WindowState = WindowState.Maximized;
            FullScreenIcon.Data = (Geometry)FindResource("ExitFullScreenIcon");
        }
    }

    private void SaveSnapshot() {
        if (string.IsNullOrEmpty(DisplayText.Text)) {
            return;
        }

        // Hold the frame still while the dialog is open
        bool wasPaused = _isPaused;
        if (!wasPaused) {
            TogglePlayPause();
        }

        SaveFileDialog saveDialog = new() {
            Filter = "PNG Image|*.png",
            FileName = $"Snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (saveDialog.ShowDialog() == true) {
            try {
                RenderToPng(saveDialog.FileName);
                CustomMessageBox.Show("Snapshot saved!", "Success", CustomMessageBox.MessageBoxType.Ok, this);
            }
            catch (Exception ex) {
                CustomMessageBox.Show($"Error: {ex.Message}", "Error", CustomMessageBox.MessageBoxType.Warning, this);
            }
        }

        if (!wasPaused) {
            TogglePlayPause();
        }
    }

    private void RenderToPng(string outputPath) {
        const double fontSize = 24;
        const double padding = 10;

        Typeface typeface = new(DisplayText.FontFamily, DisplayText.FontStyle, DisplayText.FontWeight, DisplayText.FontStretch);
        FormattedText text = new(
            DisplayText.Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            DisplayText.Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        // Trailing spaces are part of the picture, so they count towards the width
        double width = text.WidthIncludingTrailingWhitespace + (padding * 2);
        double height = text.Height + (padding * 2);

        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen()) {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            context.DrawText(text, new Point(padding, padding));
        }

        RenderTargetBitmap bitmap = new((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    // --- UI EVENTS ---

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

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e) {
        TogglePlayPause();
    }

    private void LoopButton_Click(object sender, RoutedEventArgs e) {
        ToggleLoop();
    }

    private void FullScreenButton_OnClick(object sender, RoutedEventArgs e) {
        ToggleFullScreen();
    }

    private void SnapshotButton_Click(object sender, RoutedEventArgs e) {
        SaveSnapshot();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
        _mediaPlayer.Volume = VolumeSlider.Value;
        if (VolumeSlider.Value > 0) {
            _isMuted = false;
        }
    }
}