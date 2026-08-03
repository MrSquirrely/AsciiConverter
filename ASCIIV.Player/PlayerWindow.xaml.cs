using ASCIIV.Core;
using ASCIIV.Core.Controls;

using Microsoft.Win32;

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ASCIIV.Player {
    public partial class PlayerWindow  {
        // --- STREAMING FIELDS ---
        private FileStream? _playbackStream;
        private readonly List<long> _frameOffsets = []; // Stores the byte position of each frame
        private readonly byte[] _searchPattern = "FRAME_END"u8.ToArray(); // The marker we look for

        // State flags
        private bool _isWindowOpen = true;
        private bool _isPaused;
        private bool _isDragging;

        private readonly MediaPlayer _mediaPlayer = new();
        private AsciiProject? _projectData;

        // Timing
        private readonly Stopwatch _stopwatch = new();
        private TimeSpan _seekOffset = TimeSpan.Zero;

        private bool _hasAudio;
        private readonly string? _asciiPathToLoad;

        public PlayerWindow(string? asciiPath = null) {
            InitializeComponent();
            _asciiPathToLoad = asciiPath;
            Loaded += PlayerWindow_Loaded;
        }

        private void PlayerWindow_Loaded(object sender, RoutedEventArgs e) {
            if (!string.IsNullOrEmpty(_asciiPathToLoad)) {
                LoadProject(_asciiPathToLoad);
            }
            else {
                OpenFileDialog();
            }
        }

        private void OpenFileDialog() {
            OpenFileDialog openFileDialog = new() {
                Filter = "ASCII Video|*.asciiv"
            };

            if (openFileDialog.ShowDialog() == true) {
                LoadProject(openFileDialog.FileName);
            }
        }

        private void Window_Drop(object sender, DragEventArgs e) {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            // ReSharper disable once AssignNullToNotNullAttribute
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length <= 0) return;
            string file = files[0];
            if (Path.GetExtension(file) != ".asciiv") return;
            StopPlayback();
            LoadProject(file);
        }

        private void StopPlayback() {
            _isPaused = true;
            _stopwatch.Stop();
            _stopwatch.Reset();
            _mediaPlayer.Stop();
            _mediaPlayer.Close();
            _hasAudio = false;
            _seekOffset = TimeSpan.Zero;

            // Close the stream
            if (_playbackStream != null) {
                _playbackStream.Close();
                _playbackStream.Dispose();
                _playbackStream = null;
            }
            _frameOffsets.Clear();
        }

        private void LoadProject(string filePath) {
            try {
                // Open the file for READING (Share.Read allows other apps to see it)
                // We keep this stream open for the entire duration of playback
                _playbackStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                using (BinaryReader reader = new(_playbackStream, Encoding.UTF8, leaveOpen: true)) {
                    // A. Read Headers
                    double fps = reader.ReadDouble();
                    string colorName = reader.ReadString();

                    _projectData = new AsciiProject {
                        FramesPerSecond = fps,
                        ColorName = colorName
                    };

                    try {
                        Color color = (Color)ColorConverter.ConvertFromString(colorName);
                        DisplayText.Foreground = new SolidColorBrush(color);
                    }
                    catch {
                        DisplayText.Foreground = Brushes.White;
                    }
                    
                    // B. Read Audio
                    int audioSize = reader.ReadInt32();
                    if (audioSize > 0) {
                        byte[] audioBytes = reader.ReadBytes(audioSize);
                        string tempAudioPath = Path.Combine(Path.GetTempPath(), "ascii_player_temp_audio.mp3");
                        File.WriteAllBytes(tempAudioPath, audioBytes);

                        _mediaPlayer.Open(new Uri(tempAudioPath));
                        _hasAudio = true;
                    }

                    // C. INDEX THE FRAMES (The Streaming Magic)
                    // The stream is now positioned at the start of the ASCII text data.
                    long textStartPosition = _playbackStream.Position;

                    // Run indexing on a background task so UI doesn't freeze for huge files
                    // But for simplicity in this window, we'll do it synchronously, or you can show a "Loading..." text
                    DisplayText.Text = "Indexing Frames...";
                    Application.Current.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { })); // Force UI refresh

                    IndexFileFrames(textStartPosition);
                }

                // D. Final Setup
                _mediaPlayer.Volume = VolumeSlider.Value;
                string videoName = Path.GetFileNameWithoutExtension(filePath);
                WindowTitleText.Text = $"{videoName} - {_frameOffsets.Count} Frames";

                ProgressSlider.Maximum = _frameOffsets.Count > 0 ? _frameOffsets.Count - 1 : 0;

                StartSyncPlayback();
            }
            catch (Exception ex) {
                CustomMessageBox.Show("Error loading file: " + ex.Message);
                StopPlayback();
            }
        }

        // --- THE INDEXER ---
        // Scans the file for "FRAME_END" and records start positions
        private void IndexFileFrames(long startPosition) {
            _frameOffsets.Clear();
            _playbackStream!.Position = startPosition;

            // The first frame starts right here
            _frameOffsets.Add(startPosition);

            // 1. Read the file in chunks to be fast
            const int bufferSize = 1024 * 64; // 64KB chunks
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            long absolutePosition = startPosition;

            // 2. Scan loop
            while ((bytesRead = _playbackStream.Read(buffer, 0, bufferSize)) > 0) {
                for (int i = 0; i < bytesRead; i++) {
                    // Check if we found the 'F' of "FRAME_END"
                    if (buffer[i] != _searchPattern[0]) continue;
                    // Potential match, check the rest
                    if (!IsMatch(buffer, i, bytesRead)) continue;
                    // Found FRAME_END!
                    // The NEXT frame starts after this marker + potential \r\n
                    // "FRAME_END" is 9 bytes. 

                    // Let's calculate the absolute file position of the end of this marker
                    long markerEndPos = absolutePosition + i + _searchPattern.Length;

                    // We need to verify if there are \r\n to skip.
                    // Since we are inside a buffer, checking forward is tricky if at boundary.
                    // For simplicity, we assume strict "FRAME_END\r\n" or "FRAME_END".
                    // We will store the markerEndPos, and when Reading, we Trim() whitespace.

                    // Important: We only add a new frame if we aren't at the very end of file
                    if (markerEndPos < _playbackStream.Length) {
                        _frameOffsets.Add(markerEndPos);
                    }
                }
                absolutePosition += bytesRead;
            }
        }

        // Helper to check for "FRAME_END" inside the buffer
        private bool IsMatch(byte[] buffer, int index, int count) {
            if (index + _searchPattern.Length > count) return false; // Split across buffer boundary (edge case ignored for simplicity)

            for (int j = 1; j < _searchPattern.Length; j++) {
                if (buffer[index + j] != _searchPattern[j]) return false;
            }
            return true;
        }

        // --- STREAM READER ---
        // Seeks to the specific frame and reads only that text
        private string ReadFrame(int index) {
            if (_playbackStream == null || index < 0 || index >= _frameOffsets.Count) return "";

            try {
                long startPos = _frameOffsets[index];

                // Determine length: It's the distance to the next frame, or end of file
                long endPos = (index + 1 < _frameOffsets.Count)
                    ? _frameOffsets[index + 1]
                    : _playbackStream.Length;

                int length = (int)(endPos - startPos);
                if (length <= 0) return "";

                // buffer
                byte[] frameBytes = new byte[length];

                _playbackStream.Seek(startPos, SeekOrigin.Begin);
                _playbackStream.ReadExactly(frameBytes, 0, length);

                // Convert to string and remove the "FRAME_END" marker that belongs to this frame
                string raw = Encoding.UTF8.GetString(frameBytes);

                // The frame ends with "FRAME_END". We want to remove that.
                // We also trim newlines.
                int markerIndex = raw.LastIndexOf("FRAME_END", StringComparison.Ordinal);
                return markerIndex >= 0 ? raw[..markerIndex].TrimEnd() : raw.TrimEnd();
            }
            catch { return ""; }
        }


        // --- PLAYBACK LOGIC ---

        private async void StartSyncPlayback() {
            try {
                if (_hasAudio) _mediaPlayer.Play();
                _stopwatch.Start();
                await PlayLoop();
            }
            catch (Exception e) {
                Debug.WriteLine(e.Message);
            }
        }

        private async Task PlayLoop() {
            while (_isWindowOpen) {
                if (_isPaused) {
                    await Task.Delay(100);
                    continue;
                }

                TimeSpan currentTime;
                if (_hasAudio && _mediaPlayer.Source != null) {
                    currentTime = _mediaPlayer.Position;
                    // Sync fallback
                    if (currentTime == TimeSpan.Zero && _stopwatch.ElapsedMilliseconds > 500) {
                        currentTime = _stopwatch.Elapsed + _seekOffset;
                    }
                }
                else {
                    currentTime = _stopwatch.Elapsed + _seekOffset;
                }

                int frameIndex = (int)(currentTime.TotalSeconds * _projectData!.FramesPerSecond);

                if (frameIndex < _frameOffsets.Count) {

                    // --- NEW: READ FROM DISK ---
                    DisplayText.Text = ReadFrame(frameIndex);

                    if (!_isDragging) {
                        ProgressSlider.Value = frameIndex;
                    }
                }
                else {
                    // Loop end
                    _isPaused = true;
                    _stopwatch.Reset();
                    _seekOffset = TimeSpan.Zero;
                    if (_hasAudio) _mediaPlayer.Stop();
                    PlayPauseButton.Content = "Play";
                    PlayButtonIcon.Data = (Geometry)FindResource("PlayIcon");
                    ProgressSlider.Value = 0;

                    // Show first frame
                    DisplayText.Text = ReadFrame(0);
                }

                await Task.Delay(16); // ~60 FPS update check
            }
        }

        private void ProgressSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) {
            _isDragging = true;
        }

        private void ProgressSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) {
            _isDragging = false;
            int newFrameIndex = (int)ProgressSlider.Value;
            double newTimeInSeconds = newFrameIndex / _projectData!.FramesPerSecond;
            TimeSpan newTime = TimeSpan.FromSeconds(newTimeInSeconds);

            if (_hasAudio) {
                _mediaPlayer.Position = newTime;
                _stopwatch.Restart();
                _seekOffset = newTime;
            }
            else {
                _seekOffset = newTime;
                _stopwatch.Restart();
            }

            // Force update frame
            DisplayText.Text = ReadFrame(newFrameIndex);
        }

        // --- EXISTING UI EVENTS ---

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e) {
            if (_isPaused) {
                _isPaused = false;
                _stopwatch.Start();
                if (_hasAudio) _mediaPlayer.Play();
                PlayButtonIcon.Data = (Geometry)FindResource("PauseIcon");
            }
            else {
                _isPaused = true;
                _stopwatch.Stop();
                if (_hasAudio) _mediaPlayer.Pause();
                PlayButtonIcon.Data = (Geometry)FindResource("PlayIcon");
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            _mediaPlayer.Volume = VolumeSlider.Value;
        }

        private void FullScreenButton_OnClick(object sender, RoutedEventArgs e) {
            if (WindowState == WindowState.Maximized) {
                WindowState = WindowState.Normal;
                FullScreenIcon.Data = (Geometry)FindResource("FullScreenIcon");
            }
            else {
                WindowState = WindowState.Maximized;
                FullScreenIcon.Data = (Geometry)FindResource("ExitFullScreenIcon");
            }
        }

        private void SnapshotButton_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(DisplayText.Text)) return;
            bool wasPaused = _isPaused;
            if (!_isPaused) PlayPauseButton_Click(this, null!);

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
                    CustomMessageBox.Show($"Error: {ex.Message}", "Error");
                }
            }
            if (!wasPaused) PlayPauseButton_Click(this, null!);
        }

        private void RenderToPng(string outputPath) {
            double fontSize = 24;
            Typeface typeface = new Typeface(DisplayText.FontFamily, DisplayText.FontStyle, DisplayText.FontWeight, DisplayText.FontStretch);
            Brush foreground = DisplayText.Foreground;

            FormattedText text = new FormattedText(
                DisplayText.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, fontSize, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen()) {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, text.Width + 20, text.Height + 20));
                dc.DrawText(text, new Point(10, 10));
            }

            RenderTargetBitmap rtb = new RenderTargetBitmap((int)text.Width + 20, (int)text.Height + 20, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(outputPath)) encoder.Save(fs);
        }

        protected override void OnClosed(EventArgs e) {
            _isWindowOpen = false;
            StopPlayback();
            base.OnClosed(e);
        }
    }
}