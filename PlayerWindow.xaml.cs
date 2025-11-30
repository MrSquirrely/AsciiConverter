using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace AsciiConverter {
    public partial class PlayerWindow {
        private string[]? _frames;

        // State flags
        private bool _isWindowOpen = true;
        private bool _isPaused;
        private bool _isDragging;

        private readonly MediaPlayer _mediaPlayer = new();
        private AsciiProject? _projectData;

        // TIMING TOOLS
        private readonly Stopwatch _stopwatch = new();
        // This stores the time we seek to (for videos without audio)
        private TimeSpan _seekOffset = TimeSpan.Zero;

        private bool _hasAudio;
        private readonly string _jsonPathToLoad;

        public PlayerWindow(string jsonPath) {
            InitializeComponent();
            _jsonPathToLoad = jsonPath;
            Loaded += PlayerWindow_Loaded;
        }

        private void PlayerWindow_Loaded(object sender, RoutedEventArgs e) => LoadProject(_jsonPathToLoad);

        // 1. Change the constructor/loader to accept the .asciiv file path
        private void LoadProject(string filePath) {
            try {
                // Check if it's our new Single File format
                if (Path.GetExtension(filePath) == ".asciiv") {
                    LoadSingleFile(filePath);
                }
            }
            catch (Exception ex) {
                CustomMessageBox.Show("Error loading file: " + ex.Message);
            }
        }

        // 2. The New Loader Logic
        private void LoadSingleFile(string filePath) {
            using (FileStream fs = File.OpenRead(filePath))
            using (BinaryReader reader = new(fs)) {
                // A. Read FPS
                double fps = reader.ReadDouble();
                // B. Read Color
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

                // C. Read Audio
                int audioSize = reader.ReadInt32();

                if (audioSize > 0) {
                    byte[] audioBytes = reader.ReadBytes(audioSize);

                    // We MUST save this to a temp file for MediaPlayer to access it
                    string tempAudioPath = Path.Combine(Path.GetTempPath(), "ascii_player_temp_audio.mp3");
                    File.WriteAllBytes(tempAudioPath, audioBytes);

                    _mediaPlayer.Open(new Uri(tempAudioPath));
                    _hasAudio = true;
                }

                // D. Read ASCII Text
                // The BinaryReader cursor is now right after the audio.
                // Everything left in the stream is text.

                // We use StreamReader on the underlying stream to read the rest
                using (StreamReader textReader = new(fs)) {
                    string allText = textReader.ReadToEnd();

                    string[] separator = ["FRAME_END\r\n", "FRAME_END\n", "FRAME_END"];
                    _frames = allText.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                }
            }

            // E. Final Setup
            _mediaPlayer.Volume = VolumeSlider.Value;
            string videoName = Path.GetFileNameWithoutExtension(filePath);
            Title = $"{videoName} - {_frames.Length} Frames";

            if (_frames != null) ProgressSlider.Maximum = _frames.Length - 1;

            StartSyncPlayback();
        }

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

                // --- SYNC LOGIC ---
                TimeSpan currentTime;

                if (_hasAudio && _mediaPlayer.Source != null) {
                    currentTime = _mediaPlayer.Position;

                    if (currentTime == TimeSpan.Zero && _stopwatch.ElapsedMilliseconds > 500) {
                        // Fallback logic uses the offset too
                        currentTime = _stopwatch.Elapsed + _seekOffset;
                    }
                }
                else {
                    // FIXED: Time is Stopwatch + The Manual Offset
                    currentTime = _stopwatch.Elapsed + _seekOffset;
                }

                int frameIndex = (int)(currentTime.TotalSeconds * _projectData!.FramesPerSecond);

                if (frameIndex < _frames!.Length) {
                    DisplayText.Text = _frames[frameIndex].TrimEnd();

                    // Only update slider if user isn't holding it
                    if (!_isDragging) {
                        ProgressSlider.Value = frameIndex;
                    }
                }
                else {
                    // STOP LOGIC
                    _isPaused = true;
                    _stopwatch.Reset();
                    _seekOffset = TimeSpan.Zero; // Reset Offset

                    if (_hasAudio) _mediaPlayer.Stop();

                    PlayPauseButton.Content = "Play";
                    ProgressSlider.Value = 0;
                }

                await Task.Delay(16);
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
                // Just restart stopwatch to keep the "No Audio Fallback" somewhat in sync
                _stopwatch.Restart();
                _seekOffset = newTime;
            }
            else {
                // FIXED: Set the offset manually and restart stopwatch from 0
                // Total Time = 0 (Stopwatch) + Offset
                _seekOffset = newTime;
                _stopwatch.Restart();
            }

            // Force update the visual frame immediately
            if (newFrameIndex < _frames!.Length) {
                DisplayText.Text = _frames[newFrameIndex].TrimEnd();
            }
        }

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

        protected override void OnClosed(EventArgs e) {
            _isWindowOpen = false;
            _stopwatch.Stop();
            _mediaPlayer.Close();
            base.OnClosed(e);
        }

        private void FullScreenButton_OnClick(object sender, RoutedEventArgs e) {
            if (WindowState == WindowState.Maximized) {
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                FullScreenIcon.Data = (Geometry)FindResource("FullScreenIcon");
            }
            else {
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                WindowStyle = WindowStyle.None;
                FullScreenIcon.Data = (Geometry)FindResource("ExitFullScreenIcon");
            }
        }
    }
}