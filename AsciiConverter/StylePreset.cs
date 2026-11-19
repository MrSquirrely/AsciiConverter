namespace ASCIIV.Converter;

/// <summary>
/// A named combination of look and sound settings that can be applied in one click.
/// </summary>
/// <param name="Name">Shown in the preset list.</param>
/// <param name="Description">One line on what it looks like, shown as a tooltip.</param>
/// <param name="TextStyle">Name of a built-in character ramp.</param>
/// <param name="ColorName">Named colour or #RRGGBB value of the text.</param>
/// <param name="MatchColors">Colour every character like the video instead of using <paramref name="ColorName"/>.</param>
/// <param name="Width">Characters per row.</param>
/// <param name="Contrast">Contrast multiplier; 1.0 leaves the video unchanged.</param>
/// <param name="Invert">Swap dark and light characters.</param>
/// <param name="Sound">The sound preset that goes with the look.</param>
/// <param name="FontName">Name of the font the characters are drawn with. Empty for the default monospaced font.</param>
public sealed record StylePreset(
    string Name,
    string Description,
    string TextStyle,
    string ColorName,
    bool MatchColors,
    int Width,
    double Contrast,
    bool Invert,
    SoundPreset Sound,
    string FontName = "") {

    private const string ShrekGreen = "#B0C400";

    /// <summary>The settings the application starts with, and returns to on Reset.</summary>
    public static StylePreset Default { get; } = new(
        "Default", "The settings the app starts with",
        AsciiRamps.Standard, "White", MatchColors: false, Width: 150, Contrast: 1.0, Invert: false, SoundPreset.Default);

    public static IReadOnlyList<StylePreset> All { get; } = [
        new("Classic", "Plain white ASCII art with clean sound",
            AsciiRamps.Standard, "White", MatchColors: false, Width: 150, Contrast: 1.0, Invert: false, SoundPreset.Clean),
        new("True Color", "Fine detail, colored like the video",
            AsciiRamps.Optimized, "White", MatchColors: true, Width: 200, Contrast: 1.1, Invert: false, SoundPreset.Clean),
        new("Color Blocks", "Chunky colored blocks, like a mosaic",
            AsciiRamps.Blocks, "White", MatchColors: true, Width: 120, Contrast: 1.0, Invert: false, SoundPreset.Clean),
        new("Matrix", "Dense green characters on black",
            AsciiRamps.HighDetail, "Lime", MatchColors: false, Width: 180, Contrast: 1.4, Invert: false, SoundPreset.Clean),
        new("Amber Terminal", "Old amber monitor with console sound",
            AsciiRamps.Standard, "#FFB000", MatchColors: false, Width: 120, Contrast: 1.3, Invert: false, SoundPreset.RetroConsole),
        new("Retro 8-Bit", "Low-resolution cyan blocks with arcade sound",
            AsciiRamps.Blocks, "Cyan", MatchColors: false, Width: 80, Contrast: 1.5, Invert: false, SoundPreset.Arcade),
        new("Glitch", "Harsh red high-contrast picture with crunched sound",
            AsciiRamps.Glitch, "Red", MatchColors: false, Width: 150, Contrast: 2.0, Invert: false, SoundPreset.Crunch),
        new("Negative", "Dark and light swapped, like a photo negative",
            AsciiRamps.HighDetail, "White", MatchColors: false, Width: 150, Contrast: 1.2, Invert: true, SoundPreset.Clean),
        new("Shrek", "Ogre-green letters in the Shrek font, with swampy sound",
            AsciiRamps.Shrek, ShrekGreen, MatchColors: false, Width: 70, Contrast: 1.3, Invert: false, SoundPreset.Swamp, "Shrek")
    ];
}