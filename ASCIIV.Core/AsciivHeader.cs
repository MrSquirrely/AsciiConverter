namespace ASCIIV.Core;

/// <summary>
/// Describes the contents of an .asciiv file.
/// </summary>
/// <param name="FramesPerSecond">Playback rate of the frames.</param>
/// <param name="ColorName">Named colour or #RRGGBB value the text is drawn with.</param>
/// <param name="Width">Characters per row.</param>
/// <param name="Height">Rows per frame.</param>
/// <param name="FrameCount">Number of frames stored in the file.</param>
/// <param name="Palette">The characters a frame cell can refer to. A cell stores an index into this string.</param>
public sealed record AsciivHeader(
    double FramesPerSecond,
    string ColorName,
    int Width,
    int Height,
    int FrameCount,
    string Palette) {

    /// <summary>Number of cells, and therefore bytes, in one stored frame.</summary>
    public int FrameSize => Width * Height;
}