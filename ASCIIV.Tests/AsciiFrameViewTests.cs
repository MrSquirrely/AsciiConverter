using ASCIIV.Core;
using ASCIIV.Core.Controls;

using System.Windows.Media;
using System.Windows.Media.Imaging;

using Xunit;

namespace ASCIIV.Tests;

public class AsciiFrameViewTests {
    // WPF drawing needs a single-threaded apartment, which test threads are not
    private static T RunOnStaThread<T>(Func<T> action) {
        T? result = default;
        Exception? error = null;
        Thread thread = new(() => {
            try {
                result = action();
            }
            catch (Exception ex) {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return error is null ? result! : throw new InvalidOperationException("Rendering failed: " + error.Message, error);
    }

    private static AsciiFrame CreateFilledFrame(int width, int height, bool withColors) {
        char[] chars = new char[width * height];
        Array.Fill(chars, 'M');
        byte[]? colors = withColors ? new byte[chars.Length] : null;
        return new AsciiFrame(width, height, chars, colors);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("Shrek", false)]
    [InlineData("Shrek", true)]
    public void RenderToBitmap_HandlesFramesWithMoreCharactersThanOneGlyphRunHolds(string fontName, bool withColors) {
        // 400 x 300 is the widest setting with a tall picture: 120,000 characters, all the same colour
        AsciiFrame frame = CreateFilledFrame(400, 300, withColors);

        BitmapSource bitmap = RunOnStaThread(() => AsciiFrameView.RenderToBitmap(frame, Brushes.White, fontName, scale: 0.5, padding: 0));

        Assert.True(bitmap.PixelWidth > 0);
        Assert.True(bitmap.PixelHeight > 0);
    }

    [Fact]
    public void RenderToBitmap_SizeDependsOnTheGridNotTheContent() {
        AsciiFrame full = CreateFilledFrame(40, 10, withColors: false);
        AsciiFrame blank = new(40, 10, [.. new string(' ', 400)]);

        (int Width, int Height) fullSize = RunOnStaThread(() => {
            BitmapSource bitmap = AsciiFrameView.RenderToBitmap(full, Brushes.White);
            return (bitmap.PixelWidth, bitmap.PixelHeight);
        });
        (int Width, int Height) blankSize = RunOnStaThread(() => {
            BitmapSource bitmap = AsciiFrameView.RenderToBitmap(blank, Brushes.White);
            return (bitmap.PixelWidth, bitmap.PixelHeight);
        });

        Assert.Equal(fullSize, blankSize);
    }

    [Fact]
    public void RenderToBitmap_EmptyFrameDoesNotThrow() {
        BitmapSource bitmap = RunOnStaThread(static () => AsciiFrameView.RenderToBitmap(AsciiFrame.Empty, Brushes.White));

        Assert.True(bitmap.PixelWidth > 0);
    }
}