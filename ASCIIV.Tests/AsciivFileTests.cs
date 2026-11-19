using ASCIIV.Core;

using System.IO;
using System.Text;

using Xunit;

namespace ASCIIV.Tests;

public class AsciivFileTests {
    private const string Palette = "@# ";

    // Two 3x2 frames: cells are indices into Palette
    private static readonly byte[] FrameCells = [
        0, 1, 2,
        2, 2, 2,

        2, 2, 2,
        1, 0, 1
    ];

    private static readonly AsciivHeader Header = new(24.0, "Lime", Width: 3, Height: 2, FrameCount: 2, Palette);

    private static async Task<MemoryStream> WriteFileAsync(AsciivHeader header, byte[] frames, byte[]? audio = null) {
        MemoryStream output = new();
        using MemoryStream frameStream = new(frames);
        using MemoryStream? audioStream = audio is null ? null : new MemoryStream(audio);
        await AsciivWriter.WriteAsync(output, header, audioStream, frameStream);
        output.Position = 0;
        return output;
    }

    private static MemoryStream WriteLegacyFile(double fps, string color, byte[] audio, string text) {
        MemoryStream output = new();
        using (BinaryWriter writer = new(output, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(fps);
            writer.Write(color);
            writer.Write(audio.Length);
            writer.Write(audio);
            writer.Write(Encoding.UTF8.GetBytes(text));
        }

        output.Position = 0;
        return output;
    }

    [Fact]
    public async Task RoundTrip_PreservesHeaderFramesAndAudio() {
        byte[] audio = [.. "RIFF"u8, 1, 2, 3, 4];

        using AsciivReader reader = new(await WriteFileAsync(Header, FrameCells, audio));

        Assert.False(reader.IsLegacy);
        Assert.Equal(24.0, reader.FramesPerSecond);
        Assert.Equal("Lime", reader.ColorName);
        Assert.Equal(3, reader.Width);
        Assert.Equal(2, reader.Height);
        Assert.Equal(2, reader.FrameCount);
        Assert.True(reader.HasAudio);
        Assert.Equal(audio, reader.ReadAudio());
        Assert.Equal("@# \r\n   ", reader.ReadFrame(0));
        Assert.Equal("   \r\n#@#", reader.ReadFrame(1));
    }

    [Fact]
    public async Task RoundTrip_WithoutAudio() {
        using AsciivReader reader = new(await WriteFileAsync(Header, FrameCells));

        Assert.False(reader.HasAudio);
        Assert.Empty(reader.ReadAudio());
        Assert.Equal("   \r\n#@#", reader.ReadFrame(1));
    }

    [Fact]
    public async Task ReadFrame_CanReadFramesInAnyOrder() {
        using AsciivReader reader = new(await WriteFileAsync(Header, FrameCells));

        string second = reader.ReadFrame(1);
        string first = reader.ReadFrame(0);

        Assert.Equal("@# \r\n   ", first);
        Assert.Equal(second, reader.ReadFrame(1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task ReadFrame_OutOfRangeIndexReturnsEmpty(int index) {
        using AsciivReader reader = new(await WriteFileAsync(Header, FrameCells));

        Assert.Equal(string.Empty, reader.ReadFrame(index));
    }

    [Fact]
    public async Task Duration_IsFrameCountOverFrameRate() {
        using AsciivReader reader = new(await WriteFileAsync(Header with { FramesPerSecond = 4.0 }, FrameCells));

        Assert.Equal(TimeSpan.FromSeconds(0.5), reader.Duration);
    }

    [Fact]
    public async Task Reader_TruncatedFileKeepsCompleteFrames() {
        using MemoryStream complete = await WriteFileAsync(Header, FrameCells);
        byte[] truncated = complete.ToArray()[..^2];

        using AsciivReader reader = new(new MemoryStream(truncated));

        Assert.Equal(1, reader.FrameCount);
        Assert.Equal("@# \r\n   ", reader.ReadFrame(0));
    }

    [Fact]
    public async Task Reader_RejectsUnknownVersion() {
        using MemoryStream file = await WriteFileAsync(Header, FrameCells);
        byte[] bytes = file.ToArray();
        bytes[AsciivFormat.Magic.Length] = 99;

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => new AsciivReader(new MemoryStream(bytes)));
        Assert.Contains("version 99", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("This is just a text file, not a video.")]
    public void Reader_RejectsFilesThatAreNotAsciiv(string content) {
        Assert.Throws<InvalidDataException>(() => new AsciivReader(new MemoryStream(Encoding.UTF8.GetBytes(content))));
    }

    [Fact]
    public void Reader_ReadsLegacyFiles() {
        byte[] audio = [9, 8, 7];
        using AsciivReader reader = new(WriteLegacyFile(30.0, "#FF0000", audio, "ab\r\ncd\r\nFRAME_END\r\n  \r\nef\r\nFRAME_END\r\n"));

        Assert.True(reader.IsLegacy);
        Assert.Equal(30.0, reader.FramesPerSecond);
        Assert.Equal("#FF0000", reader.ColorName);
        Assert.Equal(2, reader.FrameCount);
        Assert.Equal(audio, reader.ReadAudio());
        Assert.Equal("ab\r\ncd", reader.ReadFrame(0));
        Assert.Equal("  \r\nef", reader.ReadFrame(1));
    }

    [Fact]
    public async Task Writer_RejectsFrameDataOfTheWrongLength() {
        using MemoryStream output = new();
        using MemoryStream frames = new(FrameCells[..^1]);

        await Assert.ThrowsAsync<InvalidDataException>(() => AsciivWriter.WriteAsync(output, Header, null, frames));
    }

    [Fact]
    public async Task Writer_RejectsInvalidHeader() {
        using MemoryStream output = new();
        using MemoryStream frames = new(FrameCells);

        await Assert.ThrowsAsync<ArgumentException>(() => AsciivWriter.WriteAsync(output, Header with { FramesPerSecond = 0 }, null, frames));
        await Assert.ThrowsAsync<ArgumentException>(() => AsciivWriter.WriteAsync(output, Header with { Palette = "" }, null, frames));
        await Assert.ThrowsAsync<ArgumentException>(() => AsciivWriter.WriteAsync(output, Header with { Width = 0 }, null, frames));
    }

    [Fact]
    public async Task Writer_FileOverloadReplacesExistingFileAndLeavesNoTempFile() {
        string folder = Path.Combine(Path.GetTempPath(), $"asciiv_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try {
            string framesPath = Path.Combine(folder, "frames.bin");
            string outputPath = Path.Combine(folder, "video.asciiv");
            await File.WriteAllBytesAsync(framesPath, FrameCells);
            await File.WriteAllTextAsync(outputPath, "old content");

            await AsciivWriter.WriteAsync(outputPath, Header, audioPath: null, framesPath);

            using (AsciivReader reader = AsciivReader.Open(outputPath)) {
                Assert.Equal(2, reader.FrameCount);
                Assert.Equal("@# \r\n   ", reader.ReadFrame(0));
            }

            Assert.Equal(["frames.bin", "video.asciiv"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        }
        finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void BuildIndexedLookup_IndicesPointAtTheSameCharactersAsBuildLookup() {
        const string ramp = "@%#*+=-:. ";

        char[] lookup = AsciiRamp.BuildLookup(ramp, contrast: 1.5, invert: true);
        (string palette, byte[] indices) = AsciiRamp.BuildIndexedLookup(ramp, contrast: 1.5, invert: true);

        Assert.Equal(256, indices.Length);
        Assert.Equal(palette.Length, palette.Distinct().Count());
        for (int i = 0; i < lookup.Length; i++) {
            Assert.Equal(lookup[i], palette[indices[i]]);
        }
    }

    [Fact]
    public void BuildIndexedLookup_RampWithRepeatedCharactersGetsACompactPalette() {
        (string palette, byte[] indices) = AsciiRamp.BuildIndexedLookup("0101");

        Assert.Equal(2, palette.Length);
        Assert.All(indices, static index => Assert.InRange(index, 0, 1));
    }

    [Fact]
    public void ComposeFrameText_UsesSpaceForIndicesOutsideThePalette() {
        string text = AsciivReader.ComposeFrameText([0, 200], width: 2, height: 1, "@");

        Assert.Equal("@ ", text);
    }
}