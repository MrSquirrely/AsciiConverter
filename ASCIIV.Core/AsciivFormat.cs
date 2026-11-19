namespace ASCIIV.Core;

/// <summary>
/// Constants of the .asciiv container.
/// <para>
/// Version 2 layout (little endian, strings as written by <see cref="System.IO.BinaryWriter"/>):
/// magic "ASCV", int version, double fps, string colour, int width, int height, int frame count,
/// string palette, int audio length, audio bytes, then frame count * (width * height) cells.
/// Each cell is one byte: an index into the palette. Rows are stored top to bottom without line breaks,
/// so every frame has the same size and can be located without scanning the file.
/// </para>
/// <para>
/// Files without the magic are legacy files: double fps, string colour, int audio length, audio bytes,
/// then UTF-8 text frames each followed by a FRAME_END line.
/// </para>
/// </summary>
public static class AsciivFormat {
    public const int CurrentVersion = 2;
    public const string FileExtension = ".asciiv";

    /// <summary>A cell is one byte, so a palette can hold at most this many characters.</summary>
    public const int MaxPaletteSize = 256;

    public const int MaxDimension = 4096;

    public static ReadOnlySpan<byte> Magic => "ASCV"u8;
}