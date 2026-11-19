using ASCIIV.Converter;
using ASCIIV.Core;

using System.IO;
using System.Text;

using Xunit;

namespace ASCIIV.Tests;

public class FrameFontTests {
    private const string Palette = "@# ";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NoSuchFont")]
    public void Get_EmptyOrUnknownNameGivesTheDefaultFont(string? name) {
        Assert.Same(FrameFont.Default, FrameFont.Get(name));
    }

    [Theory]
    [InlineData("Shrek")]
    [InlineData("shrek")]
    public void Get_FindsFontsByNameIgnoringCase(string name) {
        Assert.Same(FrameFont.Shrek, FrameFont.Get(name));
    }

    [Fact]
    public void DefaultFont_IsMonospacedWithUsableCells() {
        Assert.True(FrameFont.Default.IsDefault);
        Assert.True(FrameFont.Default.IsMonospace);
        Assert.True(FrameFont.Default.CellWidth > 0);
        Assert.True(FrameFont.Default.CellHeight > FrameFont.Default.CellWidth);
    }

    // Shrek.ttf is not part of the repository (see the README), so these two only check it where it was added
    private static bool HasShrekFontFile => File.Exists(FrameFont.Shrek.FilePath);

    [Fact]
    public void ShrekFont_LoadsFromItsFileWhenPresent() {
        if (!HasShrekFontFile) {
            return;
        }

        Assert.Contains("Shrek", FrameFont.Shrek.Glyphs.FamilyNames.Values);
        Assert.False(FrameFont.Shrek.IsMonospace);
    }

    [Fact]
    public void ShrekFont_FallsBackToAUsableFontWithoutItsFile() {
        // With or without the file, the font must give cells that can be drawn
        Assert.True(FrameFont.Shrek.CellWidth > 0);
        Assert.True(FrameFont.Shrek.CellHeight > 0);
        Assert.NotNull(FrameFont.Shrek.GetAdvance('M'));
    }

    [Fact]
    public void ShrekFont_CellIsAsWideAsItsWidestLetter() {
        if (!HasShrekFontFile) {
            return;
        }

        Assert.Equal(FrameFont.Shrek.GetAdvance('W'), FrameFont.Shrek.CellWidth);
        Assert.True(FrameFont.Shrek.GetAdvance('i') < FrameFont.Shrek.CellWidth);
    }

    [Fact]
    public void ShrekRamp_OnlyUsesCharactersTheFontHas() {
        string ramp = AsciiRamps.Resolve(AsciiRamps.Shrek);

        Assert.EndsWith(" ", ramp);
        Assert.All(ramp.Trim(), static character => Assert.NotNull(FrameFont.Shrek.GetAdvance(character)));
    }

    [Fact]
    public void ShrekPreset_UsesTheShrekFontAndSwampSound() {
        StylePreset preset = Assert.Single(StylePreset.All, static preset => preset.Name == "Shrek");

        Assert.Same(FrameFont.Shrek, FrameFont.Get(preset.FontName));
        Assert.Equal(AsciiRamps.Shrek, preset.TextStyle);
        Assert.Equal(SoundPreset.Swamp, preset.Sound);
        Assert.True(preset.Sound.BitCrush);
        Assert.Equal(4, preset.Sound.BitDepth);
        Assert.Equal(24, preset.Sound.DownsampleFactor);
    }

    [Fact]
    public void OtherPresets_UseTheDefaultFont() {
        Assert.All(StylePreset.All.Where(static preset => preset.Name != "Shrek"), static preset => Assert.Equal(string.Empty, preset.FontName));
        Assert.Equal(string.Empty, StylePreset.Default.FontName);
    }

    [Fact]
    public async Task RoundTrip_PreservesTheFontName() {
        AsciivHeader header = new(30.0, "White", Width: 2, Height: 1, FrameCount: 1, Palette, FontName: "Shrek");

        MemoryStream file = new();
        using (MemoryStream frames = new([0, 1])) {
            await AsciivWriter.WriteAsync(file, header, null, frames);
        }

        file.Position = 0;
        using AsciivReader reader = new(file);

        Assert.Equal("Shrek", reader.FontName);
        Assert.Equal("@#", reader.ReadFrame(0));
    }

    [Fact]
    public void Reader_Version3FilesHaveNoFontNameAndUseTheDefaultFont() {
        MemoryStream file = new();
        using (BinaryWriter writer = new(file, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(AsciivFormat.Magic);
            writer.Write(3);
            writer.Write(25.0);
            writer.Write("Cyan");
            writer.Write(2);
            writer.Write(1);
            writer.Write(1);
            writer.Write(Palette);
            writer.Write(AsciivFormat.SingleColorMode);
            writer.Write(0);
            writer.Write((byte[])[1, 0]);
        }

        file.Position = 0;
        using AsciivReader reader = new(file);

        Assert.Equal(string.Empty, reader.FontName);
        Assert.Equal(1, reader.FrameCount);
        Assert.Equal("#@", reader.ReadFrame(0));
    }
}