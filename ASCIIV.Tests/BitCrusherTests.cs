using ASCIIV.Converter;

using NAudio.Wave;

using Xunit;

namespace ASCIIV.Tests;

public class BitCrusherTests {
    private sealed class DummySampleProvider(float[] samples, WaveFormat? format = null) : ISampleProvider {
        private int _position;

        public WaveFormat WaveFormat { get; } = format ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        public int Read(float[] buffer, int offset, int count) {
            int available = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }

    [Fact]
    public void BitCrusher_ClampsBitDepthAndDownSample() {
        DummySampleProvider dummy = new([0.5f, 0.5f]);
        BitCrusher crusher = new(dummy);

        // Test low clamping
        crusher.SetBitDepth(0);
        Assert.Equal(1, crusher.BitDepth);

        // Test high clamping
        crusher.SetBitDepth(50);
        Assert.Equal(32, crusher.BitDepth);

        // Test downsample clamping
        crusher.SetDownSampleFactor(0);
        Assert.Equal(1, crusher.DownSampleFactor);
    }

    [Fact]
    public void BitCrusher_QuantizesSamplesAccurately() {
        // Provide known floats
        float[] input = [0.1f, 0.9f, -0.4f, 0.0f];
        DummySampleProvider dummy = new(input, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        BitCrusher crusher = new(dummy);
        crusher.SetBitDepth(4); // 2^4 = 16 steps
        crusher.SetDownSampleFactor(1); // No downsampling, just quantize

        float[] output = new float[4];
        int read = crusher.Read(output, 0, 4);

        Assert.Equal(4, read);
        float stepSize = 16.0f;
        for (int i = 0; i < 4; i++) {
            float expected = (float)(Math.Round(input[i] * stepSize) / stepSize);
            Assert.Equal(expected, output[i]);
        }
    }

    [Fact]
    public void BitCrusher_DownSamplesAudioHoldingValues() {
        // With downSampleFactor = 2, every odd frame holds the even frame's sample
        float[] input = [0.2f, 0.8f, 0.4f, 0.9f];
        DummySampleProvider dummy = new(input, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        BitCrusher crusher = new(dummy);
        crusher.SetBitDepth(16);
        crusher.SetDownSampleFactor(2);

        float[] output = new float[4];
        crusher.Read(output, 0, 4);

        // Frame 0 updates, Frame 1 holds Frame 0
        Assert.Equal(output[0], output[1]);
        // Frame 2 updates, Frame 3 holds Frame 2
        Assert.Equal(output[2], output[3]);
    }
}