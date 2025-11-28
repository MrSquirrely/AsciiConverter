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
            BtnConvert.IsEnabled = true;
        }

        private async void BtnConvert_Click(object sender, RoutedEventArgs e) {
            try {
                if (string.IsNullOrEmpty(_inputVideoPath)) return;

                BtnConvert.IsEnabled = false;
                BtnLoad.IsEnabled = false;
                TxtStatus.Text = "Converting... Please wait.";

                // Capture the UI state HERE (on the main thread)
                bool useBitCrush = BitCrushCheck.IsChecked == true;
                bool convertToMp4 = ToMp4.IsChecked == true;

                string selectedColor = ((ComboBoxItem)CmbColor.SelectedItem).Content.ToString()!;

                // Pass it to the background task
                await Task.Run(() => ConvertVideoToAscii(_inputVideoPath, useBitCrush, convertToMp4, selectedColor));

                TxtStatus.Text = "Conversion Complete!";
                BtnConvert.IsEnabled = true;
                BtnLoad.IsEnabled = true;
            }
            catch (Exception ex) {
                Debug.WriteLine(ex.Message);
                TxtStatus.Text = "Error during conversion.";
                BtnConvert.IsEnabled = true;
                BtnLoad.IsEnabled = true;
            }
        }

        private async Task ConvertVideoToAscii(string videoPath, bool isBitCrushed, bool toMp4, string colorName) {
            // 1. Setup Paths
            string projectFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            Directory.CreateDirectory(projectFolder);

            const string asciiFileName = "video.txt";
            const string mp3FileName = "audio.mp3";
            const string wavFileName = "audio.wav";
            const string jsonFileName = "project.json";

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

            // --- SCOPE START: Wrap the writing logic in braces ---
            // This ensures the file is closed immediately after we finish the loop
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

            } // --- SCOPE END: writer.Dispose() is called here, releasing video.txt ---


            // 5. Create JSON (Optional now, but good for backup)
            AsciiProject project = new() {
                AsciiTxtPath = asciiFileName,
                AudioPath = finalAudioFileForJson,
                FramesPerSecond = fps,
                TotalFrames = totalFrames,
                ColorName = colorName
            };

            string jsonString = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(fullJsonPath, jsonString);

            // 6. PACK EVERYTHING
            // Now that the writer scope is closed, we can safely access video.txt
            string singleFilePath = Path.Combine(projectFolder, "output.asciiv");
            string audioToPack = isBitCrushed ? fullWavPath : fullMp3Path;

            await PackToSingleFile(singleFilePath, fps, audioToPack, fullAsciiPath, colorName);

            try {
                if (File.Exists(fullAsciiPath)) File.Delete(fullAsciiPath);
                if (File.Exists(fullJsonPath)) File.Delete(fullJsonPath);
                // We delete both potential audio files just to be sure
                if (File.Exists(fullMp3Path)) File.Delete(fullMp3Path);
                if (File.Exists(fullWavPath)) File.Delete(fullWavPath);
            }
            catch (Exception ex) {
                // If cleanup fails (e.g., antivirus locking), don't crash the app, just log it
                Debug.WriteLine("Cleanup warning: " + ex.Message);
            }

            if (toMp4) {
                ConvertToMp4();
            }

            MessageBox.Show($"Conversion Complete! Saved single file:\n{singleFilePath}");
        }

        private static async Task PackToSingleFile(string outputFilePath, double fps, string audioPath, string asciiPath, string colorName) {
            // 1. Open the main file stream
            await using FileStream fs = new(outputFilePath, FileMode.Create);

            // 2. Wrap it in a BinaryWriter
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

                // CRITICAL: Force the writer to push all data to 'fs' now.
                // If we don't do this, the Writer might hold onto the audio bytes 
                // while the Stream starts writing text, causing a corrupted file.
                writer.Flush();
            }

            // 3. Now append the text file safely using the open 'fs'
            await using FileStream textStream = File.OpenRead(asciiPath);
            await textStream.CopyToAsync(fs);
        }

        private async void ConvertToMp4() {
            try {
                // 1. Check if we have a file to convert
                string singleFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "output.asciiv");
                if (!File.Exists(singleFile)) {
                    MessageBox.Show("Please convert a video first!");
                    return;
                }

                await Task.Run(async () => {
                    try {
                        // 2. Unpack the .asciiv file data manually
                        // (We reuse the reading logic from PlayerWindow essentially)
                        double fps;
                        string colorName;
                        string[] frames;
                        string tempAudioPath = Path.Combine(Path.GetTempPath(), "temp_render_audio.wav");

                        await using (FileStream fs = File.OpenRead(singleFile))
                        using (BinaryReader reader = new(fs)) {
                            fps = reader.ReadDouble();
                            colorName = reader.ReadString();
                            int audioSize = reader.ReadInt32();

                            // Extract Audio
                            if (audioSize > 0) {
                                byte[] audioBytes = reader.ReadBytes(audioSize);
                                await File.WriteAllBytesAsync(tempAudioPath, audioBytes);
                            }

                            // Extract Text
                            using (StreamReader textReader = new(fs)) {
                                string allText = await textReader.ReadToEndAsync();
                                string[] separator = ["FRAME_END\r\n", "FRAME_END\n", "FRAME_END"];
                                frames = allText.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                            }
                        }

                        // 3. Render it
                        AsciiRenderer renderer = new();
                        string outputMp4 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "final_render.mp4");
                        
                        await AsciiRenderer.RenderToMp4(frames, fps, tempAudioPath, outputMp4, colorName);

                        Application.Current.Dispatcher.Invoke(() => {
                            MessageBox.Show("MP4 Render Complete!\nSaved to: " + outputMp4);
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
            // Look for the .asciiv file now
            string singleFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "output.asciiv");

            if (File.Exists(singleFile)) {
                PlayerWindow player = new(singleFile);
                player.Show();
            }
            else {
                MessageBox.Show("Please convert a video first!");
            }
        }

        // Helper method to calculate which character to use
        private int MapPixelToCharIndex(byte pixelValue) {
            // Math:  (Pixel / 255) * (TotalChars - 1)
            // Example: If pixel is 255 (white), we want the last index.
            int maxIndex = _asciiChars.Length - 1;
            int index = (int)((pixelValue / 255.0) * maxIndex);
            return index;
        }

    }
}