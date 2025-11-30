using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ASCIIV.Core;
using ASCIIV.Core.Controls;

namespace ASCIIV.Player {
    public partial class PlayerWindow {
        private string[]? _frames;

        private bool _isWindowOpen = true;
        private bool _isPaused;
        private bool _isDragging;

        private readonly MediaPlayer _mediaPlayer = new();
        private AsciiProject? _projectData;

        private readonly Stopwatch _stopwatch = new();
        private TimeSpan _seekOffset = TimeSpan.Zero;

        private bool _hasAudio;
        private readonly string? _asciivPathToLoad;

        public PlayerWindow(string? asciivPath) {
            InitializeComponent();
            _asciivPathToLoad = asciivPath;
            Loaded += PlayerWindow_Loaded;
        }

        private void PlayerWindow_Loaded(object sender, RoutedEventArgs e) => LoadProject(_asciivPathToLoad);
        
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            Close();
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
        
        private void LoadProject(string? filePath) {
            try {
                if (Path.GetExtension(filePath) == ".asciiv") {
                    LoadSingleFile(filePath);
                }
            }
            catch (Exception ex) {
                CustomMessageBox.Show("Error loading file: " + ex.Message);
            }
        }

        private void LoadSingleFile(string? filePath) {
            using (FileStream fs = File.OpenRead(filePath!))
            using (BinaryReader reader = new(fs)) {
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

                int audioSize = reader.ReadInt32();

                if (audioSize > 0) {
                    byte[] audioBytes = reader.ReadBytes(audioSize);
                    string tempAudioPath = Path.Combine(Path.GetTempPath(), "ascii_player_temp_audio.mp3");
                    File.WriteAllBytes(tempAudioPath, audioBytes);

                    _mediaPlayer.Open(new Uri(tempAudioPath));
                    _hasAudio = true;
                }

                using (StreamReader textReader = new(fs)) {
                    string allText = textReader.ReadToEnd();
                    string[] separator = ["FRAME_END\r\n", "FRAME_END\n", "FRAME_END"];
                    _frames = allText.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                }
            }

            _mediaPlayer.Volume = VolumeSlider.Value;
            string? videoName = Path.GetFileNameWithoutExtension(filePath);

            WindowTitleText.Text = $"{videoName} - {_frames.Length} Frames";

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

                TimeSpan currentTime;

                if (_hasAudio && _mediaPlayer.Source != null) {
                    currentTime = _mediaPlayer.Position;
                    if (currentTime == TimeSpan.Zero && _stopwatch.ElapsedMilliseconds > 500) {
                        currentTime = _stopwatch.Elapsed + _seekOffset;
                    }
                }
                else {
                    currentTime = _stopwatch.Elapsed + _seekOffset;
                }

                int frameIndex = (int)(currentTime.TotalSeconds * _projectData!.FramesPerSecond);

                if (frameIndex < _frames!.Length) {
                    DisplayText.Text = _frames[frameIndex].TrimEnd();

                    if (!_isDragging) {
                        ProgressSlider.Value = frameIndex;
                    }
                }
                else {
                    _isPaused = true;
                    _stopwatch.Reset();
                    _seekOffset = TimeSpan.Zero;
                    if (_hasAudio) _mediaPlayer.Stop();
                    PlayPauseButton.Content = "Play"; // Ensure this matches UI update logic if using Icon
                    PlayButtonIcon.Data = (Geometry)FindResource("PlayIcon"); // Sync Icon
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
                _stopwatch.Restart();
                _seekOffset = newTime;
            }
            else {
                _seekOffset = newTime;
                _stopwatch.Restart();
            }

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
    }
}