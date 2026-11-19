# AsciiConverter

**AsciiConverter** is a modern WPF application built with .NET that converts standard video files into stylized ASCII art animations. It features a custom playback engine, audio effects processing, and the ability to export your ASCII creations back to MP4 format.

## ✨ Features

* **Video Conversion**: Transforms video frames into ASCII characters sorted by density.
* **Custom File Format**: Saves processed videos as optimized `.asciiv` files containing both frame data and audio.
* **Audio BitCrusher**: Optional "BitCrush" effect to downsample audio for a retro/lo-fi aesthetic.
* **Video Export**: Renders the ASCII playback to a standard `.mp4`, `.webm` or animated `.gif` file using FFmpeg.
* **Built-in Player**:
    * Synchronized audio/video playback.
    * Play/Pause, Seek, and Volume controls.
    * Fullscreen mode.
* **Customization**: Pick a text color (White, Lime, Red, Cyan, Yellow or any custom color), or match the colors of the video character by character.
* **Style Presets**: One-click looks such as Classic, True Color, Matrix, Amber Terminal, Glitch and Shrek.
* **Sound Presets**: Ready-made lo-fi audio settings, from Clean to Destroyed.
* **Fonts**: Draw the characters in the default monospaced font or in the Shrek display font.
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
* **FFmpeg**: Downloaded automatically on first run to `%LOCALAPPDATA%\ASCIIV\ffmpeg`. To use your own build instead, place `ffmpeg.exe` and `ffprobe.exe` in an `ffmpeg` folder next to the application. Without FFmpeg, videos are converted without audio and MP4 export is unavailable.
* **Shrek font (optional)**: The Shrek preset and the "Shrek" entry in the Font list use the Shrek font by Kevin Wilson, which is not included in this repository. Download it from [fontmeme.com/fonts/shrek-kevin-wilson-font](https://fontmeme.com/fonts/shrek-kevin-wilson-font/) and save the `.ttf` file as `ASCIIV.Core/Fonts/Shrek.ttf` before building. Without it everything still works, but those videos are drawn in the default monospaced font.

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
Click the **"Load Video"** button to select a source file (supports `.mp4`, `.webm`, `.gif`, `.avi`, `.mov` and `.mkv`) from your computer.

### 2. Configure Output
* **Project Name**: Enter a custom name for your output files.
* **Crush Audio**: Check this to apply a bit-crushing effect to the video's audio track.
* **Preset**: Pick a ready-made style; it fills in the options below, which you can still adjust.
* **Match video colors**: Color every character like the video at that spot instead of using a single color.
* **Also Export**: Generate a playable `.mp4`, `.webm` or animated `.gif` alongside the custom `.asciiv` file. *Note: This process may take longer, and GIF files have no sound.*
* **Color**: Select the foreground color for the ASCII characters (e.g., "Lime" for a matrix style).

### 3. Convert
Click **"Convert & Save"**. The application will process the video frame-by-frame. A progress bar will indicate the status.

Finished files are saved to the `ASCIIV` folder inside your **Videos** folder. Click **"Open Output Folder"** to go there.

### 4. Play
Once finished, click **"Play Last Output"** to open the built-in player window and watch your creation immediately.

## 📄 License

This project is licensed under the **GNU General Public License v3.0**. See the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.