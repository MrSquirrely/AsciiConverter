using NAudio.Wave;

namespace AsciiConverter {
    public class BitCrusher : ISampleProvider {
        private readonly ISampleProvider _source;
        private readonly int _channels;

        // Parameters
        private int _bitDepth;
        private int _downSampleFactor;

        // State for downsampling (Holding values)
        // We need an array to hold the last value for EACH channel
        private readonly float[] _lastSampleValues;
        private int _sampleFrameCount; // Counts "Pairs" of samples, not individual floats
        public BitCrusher(ISampleProvider source) {
            _source = source;
            _channels = source.WaveFormat.Channels;

            // Initialize the "Hold" buffer (Size 1 for Mono, Size 2 for Stereo)
            _lastSampleValues = new float[_channels];

            // Defaults
            _bitDepth = 8;
            _downSampleFactor = 5;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public void SetBitDepth(int bits) {
            if (bits < 1) bits = 1;
            if (bits > 32) bits = 32;
            _bitDepth = bits;
        }

        public void SetDownSampleFactor(int factor) {
            if (factor < 1) factor = 1;
            _downSampleFactor = factor;
        }

        public int Read(float[] buffer, int offset, int count) {
            int samplesRead = _source.Read(buffer, offset, count);

            // Calculate Bit-Depth Step Size
            float stepSize = (float)Math.Pow(2, _bitDepth);

            // LOOP through the buffer by "Frames" (Steps of 1 for Mono, 2 for Stereo)
            for (int i = 0; i < samplesRead; i += _channels) {
                // Deciding: Do we update the sound, or hold the old sound?
                bool updateSample = (_sampleFrameCount % _downSampleFactor) == 0;

                // Process every channel in this frame (Left, then Right)
                for (int channel = 0; channel < _channels; channel++) {
                    int index = offset + i + channel;

                    // Safety check to ensure we don't go out of bounds
                    if (index >= buffer.Length) break;

                    if (updateSample) {
                        // 1. Get the real value
                        float rawSample = buffer[index];

                        // 2. Crush it (Quantize)
                        float crushedSample = (float)(Math.Round(rawSample * stepSize) / stepSize);

                        // 3. Save it to the buffer AND our history
                        buffer[index] = crushedSample;
                        _lastSampleValues[channel] = crushedSample;
                    }
                    else {
                        // HOLD: Overwrite the current sample with the OLD value
                        buffer[index] = _lastSampleValues[channel];
                    }
                }

                // Only increment the frame counter after processing the full L+R pair
                _sampleFrameCount++;
            }

            return samplesRead;
        }
    }
}