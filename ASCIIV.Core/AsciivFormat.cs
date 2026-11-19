namespace ASCIIV.Core;

/// <summary>
/// Constants of the .asciiv container.
/// <para>
/// Version 4 layout (little endian, strings as written by <see cref="System.IO.BinaryWriter"/>):
/// magic "ASCV", int version, double fps, string colour, int width, int height, int frame count,
/// string palette, byte colour mode, string font name, int audio length, audio bytes, then the frames.
/// </para>
/// <para>
/// A frame is width * height character cells, each one byte: an index into the palette. When the colour mode
/// is 1 the cells are followed by width * height colour bytes, each an <see cref="AsciivColors"/> index.
/// Rows are stored top to bottom without line breaks, so every frame has the same size and can be located
/// without scanning the file.
/// </para>
/// <para>
/// Version 3 is the same without the font name, and version 2 also lacks the colour mode byte. Both use the default font.
/// Files without the magic are legacy files: double fps, string colour, int audio length, audio bytes,
/// then UTF-8 text frames each followed by a FRAME_END line.
/// </para>
/// </summary>
public static class AsciivFormat {
    public const int CurrentVersion = 4;

    /// <summary>The first version with a colour mode byte.</summary>
    public const int CellColorsVersion = 3;

    /// <summary>The first version that names the font to draw with.</summary>
    public const int FontVersion = 4;

    /// <summary>The first version that has a magic number. It has no colour mode byte.</summary>
    public const int FirstVersion = 2;

    public const string FileExtension = ".asciiv";

    /// <summary>A cell is one byte, so a palette can hold at most this many characters.</summary>
    public const int MaxPaletteSize = 256;

    public const int MaxDimension = 4096;

    /// <summary>Colour mode: the whole video uses the header's colour.</summary>
    public const byte SingleColorMode = 0;

    /// <summary>Colour mode: every cell has its own colour.</summary>
    public const byte CellColorMode = 1;

    public static ReadOnlySpan<byte> Magic => "ASCV"u8;
}