namespace ASCIIV.Core;

/// <summary>
/// One frame of ASCII video: a grid of characters, optionally with a colour per cell.
/// </summary>
/// <param name="Width">Characters per row.</param>
/// <param name="Height">Number of rows.</param>
/// <param name="Chars">Width * Height characters, row by row from the top.</param>
/// <param name="Colors">Width * Height <see cref="AsciivColors"/> indices, or null when the frame has a single colour.</param>
public sealed record AsciiFrame(int Width, int Height, char[] Chars, byte[]? Colors = null) {
    private const string LineBreak = "\r\n";

    public static AsciiFrame Empty { get; } = new(0, 0, []);

    public bool IsEmpty => (Width == 0) || (Height == 0);

    /// <summary>
    /// Builds a frame from text with one row per line. Short rows are padded with spaces.
    /// </summary>
    public static AsciiFrame FromText(string text) {
        if (text.Length == 0) {
            return Empty;
        }

        string[] rows = text.Split('\n');
        int width = 0;
        for (int y = 0; y < rows.Length; y++) {
            rows[y] = rows[y].TrimEnd('\r');
            width = Math.Max(width, rows[y].Length);
        }

        if (width == 0) {
            return Empty;
        }

        char[] chars = new char[width * rows.Length];
        Array.Fill(chars, ' ');
        for (int y = 0; y < rows.Length; y++) {
            rows[y].CopyTo(chars.AsSpan(y * width));
        }

        return new AsciiFrame(width, rows.Length, chars);
    }

    /// <summary>
    /// Returns the frame as text: one character per cell and a line break between rows.
    /// </summary>
    public string ToText() {
        if (IsEmpty) {
            return string.Empty;
        }

        char[] text = new char[(Width * Height) + ((Height - 1) * LineBreak.Length)];
        int position = 0;
        for (int y = 0; y < Height; y++) {
            if (y > 0) {
                LineBreak.CopyTo(text.AsSpan(position));
                position += LineBreak.Length;
            }

            Chars.AsSpan(y * Width, Width).CopyTo(text.AsSpan(position));
            position += Width;
        }

        return new string(text);
    }
}