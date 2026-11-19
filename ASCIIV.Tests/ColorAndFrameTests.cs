using ASCIIV.Core;

using System.IO;
using System.Text;

using Xunit;

namespace ASCIIV.Tests;

public class ColorAndFrameTests {
    private const string Palette = "@# ";

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(51, 102, 204)]
    public void Quantize_RoundTripsColorsThatAreInThePalette(byte red, byte green, byte blue) {
        byte index = AsciivColors.Quantize(red, green, blue);

        Assert.InRange(index, 0, AsciivColors.Count - 1);
        Assert.Equal((red, green, blue), AsciivColors.ToRgb(index));
    }

    [Fact]
    public void Quantize_PicksTheNearestPaletteColor() {
        // 60 is nearer to 51 than to 102, and 240 is nearer to 255 than to 204
        Assert.Equal(((byte)51, (byte)255, (byte)0), AsciivColors.ToRgb(AsciivColors.Quantize(60, 240, 20)));
    }

    [Fact]
    public void ToRgb_IndexOutsideThePaletteIsWhite() {
        Assert.Equal(((byte)255, (byte)255, (byte)255), AsciivColors.ToRgb(250));
    }

    [Fact]
    public void Brighten_KeepsHueAndLimitsTheGain() {
        // Bright enough to reach full brightness: the largest channel becomes 255
        Assert.Equal(((byte)255, (byte)64, (byte)0), AsciivColors.Brighten(200, 50, 0));

        // Very dark colours are only multiplied by the maximum gain
        Assert.Equal(((byte)25, (byte)50, (byte)0), AsciivColors.Brighten(10, 20, 0, maxGain: 2.5));

        Assert.Equal(((byte)0, (byte)0, (byte)0), AsciivColors.Brighten(0, 0, 0));
    }

    [Fact]
    public async Task RoundTrip_PreservesCellColors() {
        // One 2x2 frame: four character cells followed by four colour cells
        byte red = AsciivColors.Quantize(255, 0, 0);
        byte blue = AsciivColors.Quantize(0, 0, 255);
        byte[] frameBytes = [0, 1, 2, 0, red, blue, blue, red];
        AsciivHeader header = new(30.0, "White", Width: 2, Height: 2, FrameCount: 1, Palette, HasCellColors: true);

        MemoryStream file = new();
        using (MemoryStream frames = new(frameBytes)) {
            await AsciivWriter.WriteAsync(file, header, null, frames);
        }

        file.Position = 0;
        using AsciivReader reader = new(file);
        AsciiFrame frame = reader.GetFrame(0);

        Assert.True(reader.HasCellColors);
        Assert.Equal(1, reader.FrameCount);
        Assert.Equal("@#\r\n @", frame.ToText());
        Assert.Equal((byte[])[red, blue, blue, red], frame.Colors!);
    }

    [Fact]
    public async Task SingleColorFile_FramesHaveNoCellColors() {
        AsciivHeader header = new(30.0, "Lime", Width: 2, Height: 1, FrameCount: 1, Palette);

        MemoryStream file = new();
        using (MemoryStream frames = new([0, 1])) {
            await AsciivWriter.WriteAsync(file, header, null, frames);
        }

        file.Position = 0;
        using AsciivReader reader = new(file);

        Assert.False(reader.HasCellColors);
        Assert.Null(reader.GetFrame(0).Colors);
    }

    [Fact]
    public void Reader_ReadsVersion2FilesWhichHaveNoColorModeByte() {
        MemoryStream file = new();
        using (BinaryWriter writer = new(file, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(AsciivFormat.Magic);
            writer.Write(2);
            writer.Write(25.0);
            writer.Write("Cyan");
            writer.Write(3);
            writer.Write(1);
            writer.Write(2);
            writer.Write(Palette);
            writer.Write(0);
            writer.Write((byte[])[0, 1, 2, 2, 1, 0]);
        }

        file.Position = 0;
        using AsciivReader reader = new(file);

        Assert.False(reader.HasCellColors);
        Assert.Equal(2, reader.FrameCount);
        Assert.Equal("@# ", reader.ReadFrame(0));
        Assert.Equal(" #@", reader.ReadFrame(1));
    }

    [Fact]
    public async Task GetFrame_OutOfRangeIndexIsEmpty() {
        MemoryStream file = new();
        using (MemoryStream frames = new([0, 1])) {
            await AsciivWriter.WriteAsync(file, new AsciivHeader(30.0, "White", 2, 1, 1, Palette), null, frames);
        }

        file.Position = 0;
        using AsciivReader reader = new(file);

        Assert.True(reader.GetFrame(1).IsEmpty);
        Assert.True(reader.GetFrame(-1).IsEmpty);
    }

    [Fact]
    public void FromText_PadsShortRowsAndRoundTrips() {
        AsciiFrame frame = AsciiFrame.FromText("ab\r\nc\r\n   ");

        Assert.Equal(3, frame.Width);
        Assert.Equal(3, frame.Height);
        Assert.Null(frame.Colors);
        Assert.Equal("ab \r\nc  \r\n   ", frame.ToText());
    }

    [Fact]
    public void FromText_EmptyTextIsTheEmptyFrame() {
        Assert.True(AsciiFrame.FromText(string.Empty).IsEmpty);
        Assert.Equal(string.Empty, AsciiFrame.Empty.ToText());
    }

    [Fact]
    public void CreateFrame_RejectsMismatchedLengths() {
        Assert.Throws<ArgumentException>(static () => AsciivReader.CreateFrame([0, 1, 2], [], width: 2, height: 2, Palette));
        Assert.Throws<ArgumentException>(static () => AsciivReader.CreateFrame([0, 1], [5], width: 2, height: 1, Palette));
    }
}