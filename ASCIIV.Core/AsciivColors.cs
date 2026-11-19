namespace ASCIIV.Core;

/// <summary>
/// The fixed colour palette used for per-cell colour: a 6 x 6 x 6 RGB cube, so a colour fits in one byte.
/// </summary>
public static class AsciivColors {
    public const int LevelsPerChannel = 6;
    public const int Count = LevelsPerChannel * LevelsPerChannel * LevelsPerChannel;

    private const int LevelStep = 255 / (LevelsPerChannel - 1);

    /// <summary>Returns the index of the palette colour closest to the given colour.</summary>
    public static byte Quantize(byte red, byte green, byte blue) {
        return (byte)((ToLevel(red) * LevelsPerChannel * LevelsPerChannel) + (ToLevel(green) * LevelsPerChannel) + ToLevel(blue));
    }

    /// <summary>Returns the colour of a palette index. Indices outside the palette give white.</summary>
    public static (byte Red, byte Green, byte Blue) ToRgb(byte index) {
        if (index >= Count) {
            return (255, 255, 255);
        }

        int red = index / (LevelsPerChannel * LevelsPerChannel);
        int green = (index / LevelsPerChannel) % LevelsPerChannel;
        int blue = index % LevelsPerChannel;
        return ((byte)(red * LevelStep), (byte)(green * LevelStep), (byte)(blue * LevelStep));
    }

    /// <summary>
    /// Brightens a colour while keeping its hue. Dark areas are already drawn with sparse characters,
    /// so without this they would be darkened twice and the picture would look murky.
    /// </summary>
    public static (byte Red, byte Green, byte Blue) Brighten(byte red, byte green, byte blue, double maxGain = 2.5) {
        int brightest = Math.Max(red, Math.Max(green, blue));
        if (brightest == 0) {
            return (0, 0, 0);
        }

        double gain = Math.Min(255.0 / brightest, maxGain);
        return (Scale(red, gain), Scale(green, gain), Scale(blue, gain));
    }

    private static byte Scale(byte value, double gain) {
        return (byte)Math.Min(255.0, Math.Round(value * gain));
    }

    private static int ToLevel(byte value) {
        return ((value * (LevelsPerChannel - 1)) + 127) / 255;
    }
}