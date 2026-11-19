namespace ASCIIV.Core;

/// <summary>
/// Describes the contents of an .asciiv file.
/// </summary>
/// <param name="FramesPerSecond">Playback rate of the frames.</param>
/// <param name="ColorName">Named colour or #RRGGBB value the text is drawn with when cells have no colour of their own.</param>
/// <param name="Width">Characters per row.</param>
/// <param name="Height">Rows per frame.</param>
/// <param name="FrameCount">Number of frames stored in the file.</param>
/// <param name="Palette">The characters a frame cell can refer to. A cell stores an index into this string.</param>
/// <param name="HasCellColors">True when every cell also stores an <see cref="AsciivColors"/> index.</param>
/// <param name="FontName">Name of the <see cref="FrameFont"/> to draw with. Empty for the default font.</param>
public sealed record AsciivHeader(
    double FramesPerSecond,
    string ColorName,
    int Width,
    int Height,
    int FrameCount,
    string Palette,
    bool HasCellColors = false,
    string FontName = "") {

    /// <summary>Number of cells in one frame.</summary>
    public int CellCount => Width * Height;

    /// <summary>Number of bytes one stored frame takes.</summary>
    public int FrameSize => HasCellColors ? (CellCount * 2) : CellCount;
}