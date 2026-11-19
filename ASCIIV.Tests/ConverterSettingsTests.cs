using ASCIIV.Converter;

using Xunit;

namespace ASCIIV.Tests;

public class ConverterSettingsTests {
    // The ranges of the sliders in the converter window
    private const int MinWidth = 50;
    private const int MaxWidth = 400;
    private const double MinContrast = 0.5;
    private const double MaxContrast = 2.5;
    private const int MinBitDepth = 2;
    private const int MaxBitDepth = 16;
    private const int MinDownsample = 2;
    private const int MaxDownsample = 30;

    [Fact]
    public void Presets_HaveUniqueNames() {
        Assert.NotEmpty(StylePreset.All);
        Assert.Equal(StylePreset.All.Count, StylePreset.All.Select(static preset => preset.Name).Distinct().Count());
    }

    [Fact]
    public void Presets_UseBuiltInRampsAndValuesTheSlidersCanShow() {
        Assert.All(StylePreset.All, static preset => {
            Assert.Contains(preset.TextStyle, AsciiRamps.Names);
            Assert.InRange(preset.Width, MinWidth, MaxWidth);
            Assert.InRange(preset.Contrast, MinContrast, MaxContrast);
            Assert.Contains(preset.Sound, SoundPreset.All);
            Assert.False(string.IsNullOrWhiteSpace(preset.Description));
        });
    }

    [Fact]
    public void SoundPresets_HaveUniqueNamesAndValuesTheSlidersCanShow() {
        Assert.Equal(SoundPreset.All.Count, SoundPreset.All.Select(static preset => preset.Name).Distinct().Count());
        Assert.All(SoundPreset.All, static preset => {
            Assert.False(string.IsNullOrWhiteSpace(preset.Description));
            Assert.InRange(preset.BitDepth, MinBitDepth, MaxBitDepth);
            Assert.InRange(preset.DownsampleFactor, MinDownsample, MaxDownsample);
        });
    }

    [Fact]
    public void SoundPresets_OnlyCleanLeavesTheAudioUntouched() {
        Assert.False(SoundPreset.Clean.BitCrush);
        Assert.All(SoundPreset.All.Where(static preset => preset != SoundPreset.Clean), static preset => Assert.True(preset.BitCrush));
    }

    [Fact]
    public void DefaultPresets_AreWhatTheWindowStartsWith() {
        Assert.Contains(SoundPreset.Default, SoundPreset.All);
        Assert.Equal(SoundPreset.Default, StylePreset.Default.Sound);
        Assert.DoesNotContain(StylePreset.All, static preset => preset.Name == StylePreset.Default.Name);
        Assert.Equal((AsciiRamps.Standard, "White", false, 150, 1.0, false),
            (StylePreset.Default.TextStyle, StylePreset.Default.ColorName, StylePreset.Default.MatchColors, StylePreset.Default.Width, StylePreset.Default.Contrast, StylePreset.Default.Invert));
    }

    [Fact]
    public void Ramps_EveryBuiltInNameResolvesToItsOwnRamp() {
        Assert.All(AsciiRamps.Names, static name => {
            string ramp = AsciiRamps.Resolve(name);
            Assert.NotEqual(name, ramp);
            Assert.True(ramp.Length >= 2);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Ramps_EmptyTextGivesTheStandardRamp(string? text) {
        Assert.Equal(AsciiRamps.Resolve(AsciiRamps.Standard), AsciiRamps.Resolve(text));
    }

    [Fact]
    public void Ramps_OtherTextIsUsedAsACustomRamp() {
        Assert.Equal("#+. ", AsciiRamps.Resolve("#+. "));
    }

    [Theory]
    [InlineData(ExportFormat.Mp4, ".mp4", true)]
    [InlineData(ExportFormat.WebM, ".webm", true)]
    [InlineData(ExportFormat.Gif, ".gif", false)]
    public void ExportFormat_HasExtensionAndAudioSupport(ExportFormat format, string extension, bool supportsAudio) {
        Assert.Equal(extension, format.GetFileExtension());
        Assert.Equal(supportsAudio, format.SupportsAudio());
    }

    [Fact]
    public void ExportFormat_NoneHasNoExtension() {
        Assert.Throws<ArgumentOutOfRangeException>(static () => ExportFormat.None.GetFileExtension());
    }

    [Theory]
    [InlineData(ExportFormat.Mp4, "libx264", "aac")]
    [InlineData(ExportFormat.WebM, "libvpx-vp9", "libopus")]
    public void EncodeArguments_VideoWithAudioMapsBothInputs(ExportFormat format, string videoCodec, string audioCodec) {
        string arguments = AsciiRenderer.BuildEncodeArguments(@"C:\t\in.mp4", @"C:\t\a.wav", @"C:\out\my video" + format.GetFileExtension(), format);

        Assert.Contains("-i \"C:\\t\\in.mp4\" -i \"C:\\t\\a.wav\"", arguments);
        Assert.Contains("-map 0:v:0 -map 1:a:0", arguments);
        Assert.Contains("-c:v " + videoCodec, arguments);
        Assert.Contains("-c:a " + audioCodec, arguments);
        Assert.EndsWith("\"C:\\out\\my video" + format.GetFileExtension() + "\"", arguments);
    }

    [Theory]
    [InlineData(ExportFormat.Mp4)]
    [InlineData(ExportFormat.WebM)]
    public void EncodeArguments_WithoutAudioHasNoAudioOptions(ExportFormat format) {
        string arguments = AsciiRenderer.BuildEncodeArguments("in.mp4", null, "out" + format.GetFileExtension(), format);

        Assert.DoesNotContain("-c:a", arguments);
        Assert.DoesNotContain("-map 1", arguments);
    }

    [Fact]
    public void EncodeArguments_GifUsesAGeneratedPaletteAndLoops() {
        string arguments = AsciiRenderer.BuildEncodeArguments("in.mp4", null, "out.gif", ExportFormat.Gif);

        Assert.Contains("palettegen", arguments);
        Assert.Contains("paletteuse", arguments);
        Assert.Contains("-loop 0", arguments);
    }
}