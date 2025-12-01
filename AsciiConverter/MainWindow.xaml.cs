using ASCIIV.Core;
using ASCIIV.Core.Controls;
using ASCIIV.Player;
using Microsoft.Win32;
using NAudio.Wave;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace ASCIIV.Converter {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow {

        // This string will hold the path to the user's video
        private string _inputVideoPath = string.Empty;

        // Stores the specific file created by the last conversion so "Play" works correctly
        private string _lastCreatedFilePath = string.Empty;

        // The ASCII ramp: characters sorted from Darkest (@) to Lightest (space) 
        private readonly char[] _standardRamp = new string("@%#*+=-:. ").ToCharArray();
        private readonly char[] _highDetailRamp = new string("$@B%8&WM#*oahkbdpqwmZO0QLCJUYXzcvunxrjft/\\\\|()1{}[]?-_+~<>i!lI;:,\\\"^`' ").ToCharArray();
        private readonly char[] _blockRamp = new string("█▓▒░ ").ToCharArray();
        private readonly char[] _binaryRamp = new string("01").ToCharArray();
        private readonly char[] _glitchRamp = "@%#*+=~-:,. ".ToCharArray();
        private readonly char[] _optimizedRamp = "@&%QWNM0gB$#DR8mHXKAUbGOpV4d9h6PkqwSE2]ayjxY5Zoen[ult13If}C{iF|(|7J)vTLs?z/*cr!+<>;=^,_:'-. ".ToCharArray();

        // Default to White in case they select Custom but cancel the dialog
        private static string _customColorHex = "#FFFFFF";


        public MainWindow() {
            InitializeComponent();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            // 1. Check for "Protected Folder" status
            if (!HasWritePermission()) {
                CustomMessageBox.Show(
                    "The application is running in a protected folder (e.g., Program Files) " +
                    "and cannot save necessary files.\n\n" +
                    "Please move the application to a writable location like your Desktop or Documents.",
                    "Permission Denied",
                    CustomMessageBox.MessageBoxType.Warning,
                    this);

                Application.Current.Shutdown();
                return;
            }

            // 2. Safe to initialize
            InitializeFFmpeg();
        }
        private bool HasWritePermission() {
            try {
                // Try to create and delete a temporary file in the app's folder
                string testPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".perm_test");
                File.WriteAllText(testPath, "test");
                File.Delete(testPath);
                return true;
            }
            catch (UnauthorizedAccessException) {
                return false;
            }
            catch (Exception) {
                return false;
            }
        }

        private static async void InitializeFFmpeg() {
            try {
                string execPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
                FFmpeg.SetExecutablesPath(execPath);

                if (Directory.Exists(execPath)) return;
                Directory.CreateDirectory(execPath);
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, execPath);
            }
            catch (Exception ex) {
                Debug.WriteLine(ex.Message);
            }
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e) {
            OpenFileDialog openFileDialog = new() {
                Filter = "Video Files|*.mp4;*.avi;*.mov"
            };

            bool? result = openFileDialog.ShowDialog();

            if (result != true) return;
            _inputVideoPath = openFileDialog.FileName;
            StatusText.Text = "Video loaded: " + Path.GetFileName(_inputVideoPath);

            ProjectNameText.Text = Path.GetFileNameWithoutExtension(_inputVideoPath);

            ConvertButton.IsEnabled = true;
        }

        private async void ConvertButton_Click(object sender, RoutedEventArgs e) {
            try {
                if (string.IsNullOrEmpty(_inputVideoPath)) return;

                // 1. Get and Sanitize Project Name
                string rawName = ProjectNameText.Text.Trim();
                if (string.IsNullOrEmpty(rawName)) rawName = "OutputVideo";

                // Remove characters that are illegal in file names (like \ / : * ? " < > |)
                char[] invalidChars = Path.GetInvalidFileNameChars();
                string projectName = string.Join("_", rawName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

                ConvertButton.IsEnabled = false;
                LoadButton.IsEnabled = false;
                StatusText.Text = "Converting... Please wait.";

                // Capture the UI state HERE (on the main thread)
                bool useBitCrush = BitCrushCheck.IsChecked == true;
                bool convertToMp4 = ToMp4Check.IsChecked == true;
                string selectedColor = ColorComboBox.SelectedItem is ComboBoxItem item ? item.Content.ToString()! : _customColorHex;
                int videoWidth = (int)VideoWidthSlider.Value;
                string styleText = TextStyleComboBox.Text;
                char[] selectedRamp = styleText switch {
                    "Standard" => _standardRamp,
                    "Binary" => _binaryRamp,
                    "High Detail" => _highDetailRamp,
                    "Blocks" => _blockRamp,
                    "Glitch" => _glitchRamp,
                    "Optimized" => _optimizedRamp,
                    _ => string.IsNullOrEmpty(styleText) ? _standardRamp : styleText.ToCharArray()
                };

                // Pass the custom 'projectName' to the background task
                await Task.Run(() => ConvertVideoToAscii(
                    _inputVideoPath,
                    useBitCrush,
                    convertToMp4,
                    selectedColor,
                    projectName,
                    selectedRamp,
                    videoWidth));

                StatusText.Text = "Conversion Complete!";
                ConvertButton.IsEnabled = true;
                LoadButton.IsEnabled = true;
            }
            catch (Exception ex) {
                Debug.WriteLine(ex.Message);
                StatusText.Text = "Error: " + ex.Message;
                ConvertButton.IsEnabled = true;
                LoadButton.IsEnabled = true;
            }
        }

        // Updated signature to accept projectName
        private async Task ConvertVideoToAscii(string videoPath, bool isBitCrushed, bool toMp4, string colorName, string projectName, char[] asciiRamp, int videoWidth) {
            // 1. Setup Paths
            string projectFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            Directory.CreateDirectory(projectFolder);

            // Temporary intermediate filenames
            const string asciiFileName = "temp_video.txt";
            const string mp3FileName = "temp_audio.mp3";
            const string wavFileName = "temp_audio.wav";
            const string jsonFileName = "temp_project.json";

            string fullAsciiPath = Path.Combine(projectFolder, asciiFileName);
            string fullMp3Path = Path.Combine(projectFolder, mp3FileName);
            string fullWavPath = Path.Combine(projectFolder, wavFileName);
            string fullJsonPath = Path.Combine(projectFolder, jsonFileName);

            // 2. Extract Base Audio
            try {
                if (File.Exists(fullMp3Path)) File.Delete(fullMp3Path);
                if (File.Exists(fullWavPath)) File.Delete(fullWavPath);

                IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(videoPath);
                IConversion conversion = FFmpeg.Conversions.New()
                    .AddStream(mediaInfo.AudioStreams)
                    .SetOutput(fullMp3Path);

                await conversion.Start();
            }
            catch (Exception ex) {
                Debug.WriteLine("Audio extraction skipped: " + ex.Message);
            }

            // 3. Handle BitCrushing
            string finalAudioFileForJson = null!;

            if (File.Exists(fullMp3Path)) {
                if (isBitCrushed) {
                    await using (AudioFileReader reader = new(fullMp3Path)) {
                        BitCrusher crusher = new(reader);
                        crusher.SetBitDepth(9);
                        crusher.SetDownSampleFactor(10);
                        WaveFileWriter.CreateWaveFile16(fullWavPath, crusher);
                    }
                    finalAudioFileForJson = wavFileName;
                }
                else {
                    finalAudioFileForJson = mp3FileName;
                }
            }

            // 4. Video Capture for ASCII
            double fps;
            int totalFrames;

            {
                VideoCapture capture = new(videoPath);
                fps = capture.Get(VideoCaptureProperties.Fps);
                totalFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);
                if (fps <= 0) fps = 30.0;

                await using StreamWriter writer = new(fullAsciiPath);
                Mat frame = new();
                int currentFrameIndex = 0;
                int lastProgress = -1;

                while (capture.Read(frame)) {
                    if (frame.Empty()) break;

                    double aspectRatio = (double)frame.Height / frame.Width;
                    int newHeight = (int)(videoWidth * aspectRatio * 0.5);

                    Mat resizedFrame = new();
                    Cv2.Resize(frame, resizedFrame, new OpenCvSharp.Size(videoWidth, newHeight));

                    Mat grayFrame = new();
                    Cv2.CvtColor(resizedFrame, grayFrame, ColorConversionCodes.BGR2GRAY);

                    StringBuilder sb = new();
                    for (int y = 0; y < grayFrame.Height; y++) {
                        for (int x = 0; x < grayFrame.Width; x++) {
                            byte pixelValue = grayFrame.At<byte>(y, x);
                            int charIndex = MapPixelToCharIndex(pixelValue, asciiRamp);
                            sb.Append(asciiRamp[charIndex]);
                        }
                        sb.AppendLine();
                    }
                    sb.AppendLine("FRAME_END");
                    await writer.WriteAsync(sb.ToString());

                    currentFrameIndex++;
                    if (totalFrames <= 0) continue;
                    int currentProgress = (int)((double)currentFrameIndex / totalFrames * 100);
                    if (currentProgress <= lastProgress) continue;
                    lastProgress = currentProgress;
                    await Application.Current.Dispatcher.InvokeAsync(() => ConvertProgressBar.Value = currentProgress);
                }

                capture.Release();
            }

            // 5. Create JSON
            AsciiProject project = new() {
                AsciiTxtPath = asciiFileName,
                AudioPath = finalAudioFileForJson,
                FramesPerSecond = fps,
                TotalFrames = totalFrames,
                ColorName = colorName
            };

            string jsonString = JsonSerializer.Serialize(project, options: new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(fullJsonPath, jsonString);

            // 6. PACK EVERYTHING using the custom Project Name
            string singleFilePath = Path.Combine(projectFolder, $"{projectName}.asciiv");
            string audioToPack = isBitCrushed ? fullWavPath : fullMp3Path;

            await PackToSingleFile(singleFilePath, fps, audioToPack, fullAsciiPath, colorName);

            // Update the variable so the "Play" button knows what to play
            _lastCreatedFilePath = singleFilePath;

            // Cleanup
            try {
                if (File.Exists(fullAsciiPath)) File.Delete(fullAsciiPath);
                if (File.Exists(fullJsonPath)) File.Delete(fullJsonPath);
                if (File.Exists(fullMp3Path)) File.Delete(fullMp3Path);
                if (File.Exists(fullWavPath)) File.Delete(fullWavPath);
            }
            catch (Exception ex) {
                Debug.WriteLine("Cleanup warning: " + ex.Message);
            }

            if (toMp4) {
                // Pass project name to MP4 converter too
                ConvertToMp4(projectName);
            }

            Application.Current.Dispatcher.Invoke(() => {
                CustomMessageBox.Show($"Conversion Complete! Saved single file:\n{singleFilePath}", "Success", CustomMessageBox.MessageBoxType.Ok, this);
            });
        }

        private static async Task PackToSingleFile(string outputFilePath, double fps, string audioPath, string asciiPath, string colorName) {
            await using FileStream fs = new(outputFilePath, FileMode.Create);
            await using (BinaryWriter writer = new(fs, Encoding.UTF8, leaveOpen: true)) {
                writer.Write(fps);
                writer.Write(colorName);


                if (File.Exists(audioPath)) {
                    byte[] audioBytes = await File.ReadAllBytesAsync(audioPath);
                    writer.Write(audioBytes.Length);
                    writer.Write(audioBytes);
                }
                else {
                    writer.Write(0);
                }
                writer.Flush();
            }

            await using FileStream textStream = File.OpenRead(asciiPath);
            await textStream.CopyToAsync(fs);
        }
        
        // Updated to accept projectName
        private async void ConvertToMp4(string projectName) {
            try {
                // Use the variable we just set
                string singleFile = _lastCreatedFilePath;

                if (!File.Exists(singleFile)) {
                    CustomMessageBox.Show("Source .asciiv file not found for MP4 conversion.");
                    return;
                }

                await Task.Run(async () => {
                    try {
                        double fps;
                        string colorName;
                        string[] frames;
                        string tempAudioPath = Path.Combine(Path.GetTempPath(), "temp_render_audio.wav");

                        await using (FileStream fs = File.OpenRead(singleFile))
                        using (BinaryReader reader = new(fs)) {
                            fps = reader.ReadDouble();
                            colorName = reader.ReadString();
                            int audioSize = reader.ReadInt32();

                            if (audioSize > 0) {
                                byte[] audioBytes = reader.ReadBytes(audioSize);
                                await File.WriteAllBytesAsync(tempAudioPath, audioBytes);
                            }

                            using (StreamReader textReader = new(fs)) {
                                string allText = await textReader.ReadToEndAsync();
                                string[] separator = ["FRAME_END\r\n", "FRAME_END\n", "FRAME_END"];
                                frames = allText.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                            }
                        }

                        // Use Project Name for MP4 path
                        string outputMp4 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", $"{projectName}.mp4");

                        await AsciiRenderer.RenderToMp4(frames, fps, tempAudioPath, outputMp4, colorName);

                        Application.Current.Dispatcher.Invoke(() => {
                            CustomMessageBox.Show("MP4 Render Complete!\nSaved to: " + outputMp4, "Render Done", CustomMessageBox.MessageBoxType.Ok, this);
                            StatusText.Text = "Done.";
                        });
                    }
                    catch (Exception ex) {
                        Debug.WriteLine(ex.Message);
                    }
                });
            }
            catch (Exception e) {
                Debug.WriteLine(e.Message);
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e) {
            // Check if we have a file in memory from a recent conversion
            if (!string.IsNullOrEmpty(_lastCreatedFilePath) && File.Exists(_lastCreatedFilePath)) {
                PlayerWindow player = new(_lastCreatedFilePath);
                player.Show();
            }
            // Fallback: Check if there is a file named after the text box input
            else {
                string rawName = ProjectNameText.Text.Trim();
                if (!string.IsNullOrEmpty(rawName)) {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", $"{rawName}.asciiv");
                    if (File.Exists(path)) {
                        PlayerWindow player = new(path);
                        player.Show();
                        return;
                    }
                }

                MessageBox.Show("Please convert a video first or ensure the Project Name matches an existing file.");
            }
        }

        private int MapPixelToCharIndex(byte pixelValue, char[] asciiChars) {
            int maxIndex = asciiChars.Length - 1;
            int index = (int)(pixelValue / 255.0 * maxIndex);
            return index;
        }

        private void ToMp4Check_Checked(object sender, RoutedEventArgs e) {
            CustomMessageBox.Show(
                "Warning: Creating an MP4 file is a slow process.\n\n" +
                "Depending on the video length, this could take several minutes to render frame-by-frame.",
                "Performance Warning",
                CustomMessageBox.MessageBoxType.Warning, this);
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
        }

    }
}