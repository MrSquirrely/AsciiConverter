using NAudio.Wave;

namespace ASCIIV.Converter;

/// <summary>
/// Lo-fi audio effect: reduces the bit depth (quantizing) and the effective sample rate (sample and hold).
/// </summary>
public class BitCrusher : ISampleProvider {
    private const int MinBitDepth = 1;
    private const int MaxBitDepth = 32;

    private readonly ISampleProvider _source;
    private readonly int _channels;

    // The last value kept for each channel while samples are being held
    private readonly float[] _heldSamples;

    // Cached so Math.Pow is not recomputed on every Read() call
    private float _stepSize;

    // Counts sample frames (one value per channel), not individual floats
    private int _frameCounter;

    public BitCrusher(ISampleProvider source) {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _heldSamples = new float[_channels];

        SetBitDepth(8);
        SetDownSampleFactor(5);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int BitDepth { get; private set; }

    public int DownSampleFactor { get; private set; }

    public void SetBitDepth(int bits) {
        BitDepth = Math.Clamp(bits, MinBitDepth, MaxBitDepth);
        _stepSize = (float)Math.Pow(2, BitDepth);
    }

    public void SetDownSampleFactor(int factor) {
        DownSampleFactor = Math.Max(1, factor);
    }

    public int Read(float[] buffer, int offset, int count) {
        int samplesRead = _source.Read(buffer, offset, count);

        // Walk the buffer one frame at a time (1 value for mono, 2 for stereo)
        for (int i = 0; i < samplesRead; i += _channels) {
            // Either take a new sample, or keep repeating the held one
            bool updateSample = (_frameCounter % DownSampleFactor) == 0;

            for (int channel = 0; channel < _channels; channel++) {
                int index = offset + i + channel;
                if (index >= buffer.Length) {
                    break;
                }

                if (updateSample) {
                    // Quantize to the reduced bit depth
                    _heldSamples[channel] = (float)(Math.Round(buffer[index] * _stepSize) / _stepSize);
                }

                buffer[index] = _heldSamples[channel];
            }

            // Wrapping keeps the counter from overflowing on long audio
            _frameCounter = (_frameCounter + 1) % DownSampleFactor;
        }

        return samplesRead;
    }
}