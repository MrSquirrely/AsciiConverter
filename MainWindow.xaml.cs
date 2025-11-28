using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Win32;

using NAudio.Wave;

using OpenCvSharp;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace AsciiConverter {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow {

        // This string will hold the path to the user's video
        private string _inputVideoPath = string.Empty;

        // Stores the specific file created by the last conversion so "Play" works correctly
        private string _lastCreatedFilePath = string.Empty;

        // The ASCII ramp: characters sorted from Darkest (@) to Lightest (space)
        private readonly char[] _asciiChars = ['@', '%', '#', '*', '+', '=', '-', ':', '.', ' '];

        public MainWindow() {
            InitializeComponent();
            InitializeFFmpeg();
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

        private void BtnLoad_Click(object sender, RoutedEventArgs e) {
            OpenFileDialog openFileDialog = new() {
                Filter = "Video Files|*.mp4;*.avi;*.mov"
            };

            bool? result = openFileDialog.ShowDialog();

            if (result != true) return;
            _inputVideoPath = openFileDialog.FileName;
            TxtStatus.Text = "Video loaded: " + Path.GetFileName(_inputVideoPath);

            TxtProjectName.Text = Path.GetFileNameWithoutExtension(_inputVideoPath);

            BtnConvert.IsEnabled = true;
        }

        private async void BtnConvert_Click(object sender, RoutedEventArgs e) {
            try {
                if (string.IsNullOrEmpty(_inputVideoPath)) return;

                // 1. Get and Sanitize Project Name
                string rawName = TxtProjectName.Text.Trim();
                if (string.IsNullOrEmpty(rawName)) rawName = "OutputVideo";

                // Remove characters that are illegal in file names (like \ / : * ? " < > |)
                char[] invalidChars = Path.GetInvalidFileNameChars();
                string projectName = string.Join("_", rawName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

                BtnConvert.IsEnabled = false;
                BtnLoad.IsEnabled = false;
                TxtStatus.Text = "Converting... Please wait.";

                // Capture the UI state HERE (on the main thread)
                bool useBitCrush = BitCrushCheck.IsChecked == true;
                bool convertToMp4 = ToMp4.IsChecked == true;

                string selectedColor = ((ComboBoxItem)CmbColor.SelectedItem).Content.ToString()!;

                // Pass the custom 'projectName' to the background task
                await Task.Run(() => ConvertVideoToAscii(_inputVideoPath, useBitCrush, convertToMp4, selectedColor, projectName));

                TxtStatus.Text = "Conversion Complete!";
                BtnConvert.IsEnabled = true;
                BtnLoad.IsEnabled = true;
            }
            catch (Exception ex) {
                Debug.WriteLine(ex.Message);
                TxtStatus.Text = "Error: " + ex.Message;
                BtnConvert.IsEnabled = true;
                BtnLoad.IsEnabled = true;
            }
        }

        // Updated signature to accept projectName
        private async Task ConvertVideoToAscii(string videoPath, bool isBitCrushed, bool toMp4, string colorName, string projectName) {
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

                    const int newWidth = 150;
                    double aspectRatio = (double)frame.Height / frame.Width;
                    int newHeight = (int)(newWidth * aspectRatio * 0.5);

                    Mat resizedFrame = new();
                    Cv2.Resize(frame, resizedFrame, new OpenCvSharp.Size(newWidth, newHeight));

                    Mat grayFrame = new();
                    Cv2.CvtColor(resizedFrame, grayFrame, ColorConversionCodes.BGR2GRAY);

                    StringBuilder sb = new();
                    for (int y = 0; y < grayFrame.Height; y++) {
                        for (int x = 0; x < grayFrame.Width; x++) {
                            byte pixelValue = grayFrame.At<byte>(y, x);
                            int charIndex = MapPixelToCharIndex(pixelValue);
                            sb.Append(_asciiChars[charIndex]);
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
                    Application.Current.Dispatcher.Invoke(() => ProgBar.Value = currentProgress);
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

            string jsonString = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
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
                CustomMessageBox.Show($"Conversion Complete! Saved single file:\n{singleFilePath}", "Success", CustomMessageBox.MessageBoxType.Ok,this);
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
                            TxtStatus.Text = "Done.";
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

        private void BtnPlay_Click(object sender, RoutedEventArgs e) {
            // Check if we have a file in memory from a recent conversion
            if (!string.IsNullOrEmpty(_lastCreatedFilePath) && File.Exists(_lastCreatedFilePath)) {
                PlayerWindow player = new(_lastCreatedFilePath);
                player.Show();
            }
            // Fallback: Check if there is a file named after the text box input
            else {
                string rawName = TxtProjectName.Text.Trim();
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

        private int MapPixelToCharIndex(byte pixelValue) {
            int maxIndex = _asciiChars.Length - 1;
            int index = (int)((pixelValue / 255.0) * maxIndex);
            return index;
        }

        private void ToMp4_Checked(object sender, RoutedEventArgs e) {
            CustomMessageBox.Show(
                "Warning: Creating an MP4 file is a slow process.\n\n" +
                "Depending on the video length, this could take several minutes to render frame-by-frame.",
                "Performance Warning",
                CustomMessageBox.MessageBoxType.Warning, this);
        }
    }
}