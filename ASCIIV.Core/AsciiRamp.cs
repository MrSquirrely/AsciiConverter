namespace ASCIIV.Core;

/// <summary>
/// Maps grayscale pixel values to characters of an ASCII ramp (darkest character first).
/// </summary>
public static class AsciiRamp {
    /// <summary>
    /// Builds a 256-entry table so each pixel is converted with a single array lookup.
    /// </summary>
    public static char[] BuildLookup(ReadOnlySpan<char> ramp, double contrast = 1.0, bool invert = false) {
        if (ramp.IsEmpty) {
            throw new ArgumentException("The ramp must contain at least one character.", nameof(ramp));
        }

        char[] lookup = new char[256];
        int maxIndex = ramp.Length - 1;
        for (int i = 0; i < lookup.Length; i++) {
            // Contrast is applied around the midpoint (128)
            double adjusted = Math.Clamp(((i - 128.0) * contrast) + 128.0, 0.0, 255.0);
            if (invert) {
                adjusted = 255.0 - adjusted;
            }

            lookup[i] = ramp[(int)((adjusted / 255.0) * maxIndex)];
        }

        return lookup;
    }

    /// <summary>
    /// Builds the lookup in the form .asciiv files store: a palette of the distinct characters in use,
    /// and a 256-entry table giving the palette index for each pixel value.
    /// </summary>
    public static (string Palette, byte[] Indices) BuildIndexedLookup(ReadOnlySpan<char> ramp, double contrast = 1.0, bool invert = false) {
        char[] lookup = BuildLookup(ramp, contrast, invert);

        // At most 256 distinct characters can appear, so an index always fits in a byte
        List<char> palette = [];
        byte[] indices = new byte[lookup.Length];
        for (int i = 0; i < lookup.Length; i++) {
            int index = palette.IndexOf(lookup[i]);
            if (index < 0) {
                index = palette.Count;
                palette.Add(lookup[i]);
            }

            indices[i] = (byte)index;
        }

        return (new string([.. palette]), indices);
    }
}