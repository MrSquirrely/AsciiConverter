using System.IO;
using System.Text;

namespace ASCIIV.Core;

/// <summary>
/// Writes .asciiv files in the current format version.
/// </summary>
public static class AsciivWriter {
    /// <summary>
    /// Packs the header, the optional audio file and the raw frame cells into <paramref name="outputPath"/>.
    /// The file is written under a temporary name first, so a failed or cancelled write never destroys an existing file.
    /// </summary>
    public static async Task WriteAsync(
        string outputPath,
        AsciivHeader header,
        string? audioPath,
        string framesPath,
        CancellationToken cancellationToken = default) {

        string tempPath = outputPath + ".tmp";
        try {
            await using (FileStream output = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                await using FileStream frames = File.OpenRead(framesPath);
                if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath)) {
                    await using FileStream audio = File.OpenRead(audioPath);
                    await WriteAsync(output, header, audio, frames, cancellationToken);
                }
                else {
                    await WriteAsync(output, header, null, frames, cancellationToken);
                }
            }

            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Writes a complete file to <paramref name="output"/>. <paramref name="frames"/> must hold exactly
    /// <see cref="AsciivHeader.FrameCount"/> frames of <see cref="AsciivHeader.FrameSize"/> bytes each.
    /// </summary>
    public static async Task WriteAsync(
        Stream output,
        AsciivHeader header,
        Stream? audio,
        Stream frames,
        CancellationToken cancellationToken = default) {

        Validate(header);

        long audioLength = audio?.Length ?? 0;
        if (audioLength > int.MaxValue) {
            throw new InvalidDataException("The audio track is too large to store in an .asciiv file.");
        }

        long expectedFrameBytes = (long)header.FrameCount * header.FrameSize;
        if (frames.Length != expectedFrameBytes) {
            throw new InvalidDataException(
                $"Expected {expectedFrameBytes} bytes of frame data but found {frames.Length}.");
        }

        await using (BinaryWriter writer = new(output, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(AsciivFormat.Magic);
            writer.Write(AsciivFormat.CurrentVersion);
            writer.Write(header.FramesPerSecond);
            writer.Write(header.ColorName);
            writer.Write(header.Width);
            writer.Write(header.Height);
            writer.Write(header.FrameCount);
            writer.Write(header.Palette);
            writer.Write(header.HasCellColors ? AsciivFormat.CellColorMode : AsciivFormat.SingleColorMode);
            writer.Write(header.FontName);
            writer.Write((int)audioLength);
        }

        if (audio is not null) {
            await audio.CopyToAsync(output, cancellationToken);
        }

        await frames.CopyToAsync(output, cancellationToken);
    }

    private static void Validate(AsciivHeader header) {
        if (!double.IsFinite(header.FramesPerSecond) || (header.FramesPerSecond <= 0)) {
            throw new ArgumentException("The frame rate must be a positive number.", nameof(header));
        }

        if ((header.Width is < 1 or > AsciivFormat.MaxDimension) || (header.Height is < 1 or > AsciivFormat.MaxDimension)) {
            throw new ArgumentException($"Width and height must be between 1 and {AsciivFormat.MaxDimension}.", nameof(header));
        }

        if (header.FrameCount < 0) {
            throw new ArgumentException("The frame count cannot be negative.", nameof(header));
        }

        if (header.Palette.Length is < 1 or > AsciivFormat.MaxPaletteSize) {
            throw new ArgumentException($"The palette must hold between 1 and {AsciivFormat.MaxPaletteSize} characters.", nameof(header));
        }
    }
}