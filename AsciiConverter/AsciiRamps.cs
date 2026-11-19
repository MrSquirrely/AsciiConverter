namespace ASCIIV.Converter;

/// <summary>
/// The built-in character ramps, by the name shown in the Text Style list.
/// A ramp lists characters from darkest to lightest.
/// </summary>
public static class AsciiRamps {
    public const string Standard = "Standard";
    public const string Binary = "Binary";
    public const string HighDetail = "High Detail";
    public const string Blocks = "Blocks";
    public const string Glitch = "Glitch";
    public const string Optimized = "Optimized";
    public const string Shrek = "Shrek";

    private static readonly Dictionary<string, string> Ramps = new() {
        [Standard] = "@%#*+=-:. ",
        [Binary] = "01",
        [HighDetail] = "$@B%8&WM#*oahkbdpqwmZO0QLCJUYXzcvunxrjft/\\|()1{}[]?-_+~<>i!lI;:,\"^`' ",
        [Blocks] = "█▓▒░ ",
        [Glitch] = "@%#*+=~-:,. ",
        [Optimized] = "@&%QWNM0gB$#DR8mHXKAUbGOpV4d9h6PkqwSE2]ayjxY5Zoen[ult13If}C{iF|(7J)vTLs?z/*cr!+<>;=^,_:'-. ",

        // Only characters the Shrek font has, ordered by how much ink they use in that font
        [Shrek] = "MWHRKSECThnrseji. "
    };

    /// <summary>The names of the built-in ramps, in display order.</summary>
    public static IReadOnlyList<string> Names { get; } = [Standard, Binary, HighDetail, Blocks, Glitch, Optimized, Shrek];

    /// <summary>
    /// Returns the ramp for a Text Style entry. Text that is not a built-in name is used as a custom ramp,
    /// and empty text gives the standard ramp.
    /// </summary>
    public static string Resolve(string? textStyle) {
        if (string.IsNullOrEmpty(textStyle)) {
            return Ramps[Standard];
        }

        return Ramps.TryGetValue(textStyle, out string? ramp) ? ramp : textStyle;
    }
}