using ASCIIV.Core;

using System.IO;
using System.Text;

using Xunit;

namespace ASCIIV.Tests;

public class FrameBoundaryTests {
    // Builds the text section exactly the way the converter writes it: rows, then a marker line
    private static MemoryStream BuildStream(string prefix, params string[][] frames) {
        StringBuilder sb = new(prefix);
        foreach (string[] rows in frames) {
            foreach (string row in rows) {
                sb.Append(row).Append("\r\n");
            }
            sb.Append(FrameIndexer.FrameMarker).Append("\r\n");
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    [Fact]
    public void IndexFrames_CountsOneFramePerMarker() {
        using MemoryStream stream = BuildStream("", ["ab", "cd"], ["ef", "gh"], ["ij", "kl"]);

        List<long> offsets = FrameIndexer.IndexFrames(stream, 0);

        // The line break after the last marker must not become an extra, empty frame
        Assert.Equal(3, offsets.Count);
        Assert.Equal(0, offsets[0]);
    }

    [Fact]
    public void IndexFrames_StartsAfterTheHeader() {
        const string header = "HEADER";
        using MemoryStream stream = BuildStream(header, ["ab"], ["cd"]);

        List<long> offsets = FrameIndexer.IndexFrames(stream, header.Length);

        Assert.Equal(2, offsets.Count);
        Assert.Equal(header.Length, offsets[0]);
        Assert.Equal("ab", FrameIndexer.ReadFrame(stream, offsets, 0));
        Assert.Equal("cd", FrameIndexer.ReadFrame(stream, offsets, 1));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(64)]
    public void IndexFrames_FindsMarkersStraddlingChunkBoundaries(int bufferSize) {
        string[][] frames = [.. Enumerable.Range(0, 25).Select(static i => new[] { $"row{i}a", $"row{i}b" })];
        using MemoryStream stream = BuildStream("", frames);

        List<long> offsets = FrameIndexer.IndexFrames(stream, 0, bufferSize);

        Assert.Equal(frames.Length, offsets.Count);
        for (int i = 0; i < frames.Length; i++) {
            Assert.Equal($"row{i}a\r\nrow{i}b", FrameIndexer.ReadFrame(stream, offsets, i));
        }
    }

    [Fact]
    public void IndexFrames_KeepsPartialFrameOfTruncatedFile() {
        byte[] bytes = Encoding.UTF8.GetBytes($"ab\r\n{FrameIndexer.FrameMarker}\r\ncd\r\nef");
        using MemoryStream stream = new(bytes);

        List<long> offsets = FrameIndexer.IndexFrames(stream, 0);

        Assert.Equal(2, offsets.Count);
        Assert.Equal("cd\r\nef", FrameIndexer.ReadFrame(stream, offsets, 1));
    }

    [Fact]
    public void IndexFrames_EmptyTextSectionHasNoFrames() {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("HEADER"));

        Assert.Empty(FrameIndexer.IndexFrames(stream, 6));
    }

    [Fact]
    public void ReadFrame_KeepsBlankRowsSoEveryFrameHasTheSameSize() {
        using MemoryStream stream = BuildStream("", ["@@@", "@@@"], ["   ", "@  "], ["@@@", "   "], ["   ", "   "]);
        List<long> offsets = FrameIndexer.IndexFrames(stream, 0);

        Assert.Equal("@@@\r\n@@@", FrameIndexer.ReadFrame(stream, offsets, 0));
        Assert.Equal("   \r\n@  ", FrameIndexer.ReadFrame(stream, offsets, 1));
        Assert.Equal("@@@\r\n   ", FrameIndexer.ReadFrame(stream, offsets, 2));
        Assert.Equal("   \r\n   ", FrameIndexer.ReadFrame(stream, offsets, 3));
    }

    [Fact]
    public void ReadFrame_HandlesMultiByteCharacters() {
        using MemoryStream stream = BuildStream("", ["█▓", "▒░"], ["░ ", " █"]);
        List<long> offsets = FrameIndexer.IndexFrames(stream, 0);

        Assert.Equal(2, offsets.Count);
        Assert.Equal("█▓\r\n▒░", FrameIndexer.ReadFrame(stream, offsets, 0));
        Assert.Equal("░ \r\n █", FrameIndexer.ReadFrame(stream, offsets, 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void ReadFrame_OutOfRangeIndexReturnsEmpty(int index) {
        using MemoryStream stream = BuildStream("", ["ab"]);
        List<long> offsets = FrameIndexer.IndexFrames(stream, 0);

        Assert.Equal(string.Empty, FrameIndexer.ReadFrame(stream, offsets, index));
    }
}