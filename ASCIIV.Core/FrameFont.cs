using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ASCIIV.Core;

/// <summary>
/// A font ASCII frames can be drawn with. Every character is drawn in a cell of the same size,
/// so fonts that are not monospaced work too: narrow characters are centred in their cell.
/// </summary>
public sealed class FrameFont {
    /// <summary>The size, in device-independent pixels, the cell measurements are given for.</summary>
    public const double FontSize = 12;

    // Characters whose widest member decides the cell width
    private const string MeasuredCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private static readonly string[] MonospaceFamilies = ["Consolas", "Cascadia Mono", "Courier New", "Lucida Console"];

    private readonly Lazy<Metrics> _metrics;

    private FrameFont(string name, string displayName, string? fileName, bool isMonospace) {
        Name = name;
        DisplayName = displayName;
        FilePath = fileName is null ? null : Path.Combine(AppContext.BaseDirectory, "Fonts", fileName);
        IsMonospace = isMonospace;
        _metrics = new Lazy<Metrics>(LoadMetrics);
    }

    /// <summary>The system's bold monospaced font. Files that name no font, or an unknown one, use it.</summary>
    public static FrameFont Default { get; } = new(string.Empty, "Monospace", fileName: null, isMonospace: true);

    /// <summary>The chunky "Shrek" display font, shipped with the application.</summary>
    public static FrameFont Shrek { get; } = new("Shrek", "Shrek", "Shrek.ttf", isMonospace: false);

    public static IReadOnlyList<FrameFont> All { get; } = [Default, Shrek];

    /// <summary>The name stored in .asciiv files. Empty for the default font.</summary>
    public string Name { get; }

    /// <summary>The name shown to the user.</summary>
    public string DisplayName { get; }

    /// <summary>The font file, or null for a font installed on the system.</summary>
    public string? FilePath { get; }

    /// <summary>True when every character has the same width.</summary>
    public bool IsMonospace { get; }

    public bool IsDefault => ReferenceEquals(this, Default);

    public GlyphTypeface Glyphs => _metrics.Value.Glyphs;

    /// <summary>Width of one character cell at <see cref="FontSize"/>.</summary>
    public double CellWidth => _metrics.Value.CellWidth;

    /// <summary>Height of one character cell at <see cref="FontSize"/>.</summary>
    public double CellHeight => _metrics.Value.CellHeight;

    /// <summary>Distance from the top of a cell to the text baseline at <see cref="FontSize"/>.</summary>
    public double Baseline => _metrics.Value.Baseline;

    /// <summary>Cell width divided by cell height. Decides how many rows a picture of a given width needs.</summary>
    public double CellAspect => CellWidth / CellHeight;

    /// <summary>
    /// Returns the font with the given stored name, or the default font when the name is empty or unknown.
    /// </summary>
    public static FrameFont Get(string? name) {
        if (!string.IsNullOrEmpty(name)) {
            foreach (FrameFont font in All) {
                if (string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase)) {
                    return font;
                }
            }
        }

        return Default;
    }

    /// <summary>The width of a character at <see cref="FontSize"/>, or null when the font does not have it.</summary>
    public double? GetAdvance(char character) {
        return Glyphs.CharacterToGlyphMap.TryGetValue(character, out ushort glyph)
            ? Glyphs.AdvanceWidths[glyph] * FontSize
            : null;
    }

    private Metrics LoadMetrics() {
        GlyphTypeface glyphs = LoadGlyphTypeface();

        double widestAdvance = 0;
        foreach (char character in IsMonospace ? "M" : MeasuredCharacters) {
            if (glyphs.CharacterToGlyphMap.TryGetValue(character, out ushort glyph)) {
                widestAdvance = Math.Max(widestAdvance, glyphs.AdvanceWidths[glyph]);
            }
        }

        return new Metrics(glyphs, widestAdvance * FontSize, glyphs.Height * FontSize, glyphs.Baseline * FontSize);
    }

    private GlyphTypeface LoadGlyphTypeface() {
        if (FilePath is not null) {
            try {
                return new GlyphTypeface(new Uri(FilePath));
            }
            catch (Exception ex) when (ex is IOException or FileFormatException or UnauthorizedAccessException) {
                // A missing or damaged font file should not stop a video from playing
                System.Diagnostics.Debug.WriteLine($"Font '{DisplayName}' could not be loaded: {ex.Message}");
            }
        }

        foreach (string family in MonospaceFamilies) {
            Typeface typeface = new(new FontFamily(family), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            if (typeface.TryGetGlyphTypeface(out GlyphTypeface glyphTypeface)
                && glyphTypeface.CharacterToGlyphMap.ContainsKey('M')) {
                return glyphTypeface;
            }
        }

        throw new InvalidOperationException("No monospaced font is installed to draw ASCII frames with.");
    }

    private sealed record Metrics(GlyphTypeface Glyphs, double CellWidth, double CellHeight, double Baseline);
}