# AsciiConverter

**AsciiConverter** is a modern WPF application built with .NET that converts standard video files into stylized ASCII art animations. It features a custom playback engine, audio effects processing, and the ability to export your ASCII creations back to MP4 format.

## ✨ Features

* **Video Conversion**: Transforms video frames into ASCII characters sorted by density.
* **Custom File Format**: Saves processed videos as optimized `.asciiv` files containing both frame data and audio.
* **Audio BitCrusher**: Optional "BitCrush" effect to downsample audio for a retro/lo-fi aesthetic.
* **MP4 Export**: Renders the ASCII playback to a standard `.mp4` video file using FFmpeg.
* **Built-in Player**:
    * Synchronized audio/video playback.
    * Play/Pause, Seek, and Volume controls.
    * Fullscreen mode.
* **Customization**: Select from multiple output colors (White, Lime, Red, Cyan, Yellow).
* **Modern UI**: Sleek dark-themed interface with custom-styled controls and gradients.

## 🛠 Technologies & Libraries

* **Framework**: .NET 10.0 (Windows) / WPF
* **[OpenCvSharp4](https://github.com/shimat/opencvsharp)**: For high-performance video frame capturing and image processing.
* **[NAudio](https://github.com/naudio/NAudio)**: For audio extraction, processing (BitCrushing), and playback.
* **[Xabe.FFmpeg](https://ffmpeg.xabe.net/)**: For rendering the final ASCII frames and audio into an MP4 container.

## 🚀 Getting Started

### Prerequisites
* Windows OS
* .NET Desktop Runtime (matching the project target, currently set to .NET 10.0).
* **FFmpeg**: The application attempts to download FFmpeg automatically on first run, but having it installed is recommended.

### Installation / Build
1.  Clone the repository.
2.  Open the solution in Visual Studio or your preferred .NET IDE.
3.  Restore NuGet packages:
    ```bash
    dotnet restore
    ```
4.  Build and Run:
    ```bash
    dotnet run
    ```

## 📖 Usage

### 1. Load a Video
Click the **"Load Video"** button to select a source file (supports `.mp4`, `.avi`, `.mov`, etc.) from your computer.

### 2. Configure Output
* **Project Name**: Enter a custom name for your output files.
* **Crush Audio**: Check this to apply a bit-crushing effect to the video's audio track.
* **Create MP4**: Check this to generate a playable `.mp4` video file alongside the custom `.asciiv` file. *Note: This process may take longer.*
* **Color**: Select the foreground color for the ASCII characters (e.g., "Lime" for a matrix style).

### 3. Convert
Click **"Convert & Save"**. The application will process the video frame-by-frame. A progress bar will indicate the status.

### 4. Play
Once finished, click **"Play Last Output"** to open the built-in player window and watch your creation immediately.

## 📄 License

This project is licensed under the **GNU General Public License v3.0**. See the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.