using NAudio.Wave;

namespace ASCIIV.Converter {
    public class BitCrusher : ISampleProvider {
        private readonly ISampleProvider source;
        private readonly int channels;

        // Parameters
        private int bitDepth;
        private int downSampleFactor;

        // State for downsampling (Holding values)
        // We need an array to hold the last value for EACH channel
        private readonly float[] lastSampleValues;
        private int sampleFrameCount; // Counts "Pairs" of samples, not individual floats
        public BitCrusher(ISampleProvider source) {
            this.source = source;
            channels = source.WaveFormat.Channels;

            // Initialize the "Hold" buffer (Size 1 for Mono, Size 2 for Stereo)
            lastSampleValues = new float[channels];

            // Defaults
            bitDepth = 8;
            downSampleFactor = 5;
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        public void SetBitDepth(int bits) {
            if (bits < 1) bits = 1;
            if (bits > 32) bits = 32;
            bitDepth = bits;
        }

        public void SetDownSampleFactor(int factor) {
            if (factor < 1) factor = 1;
            downSampleFactor = factor;
        }

        public int Read(float[] buffer, int offset, int count) {
            int samplesRead = source.Read(buffer, offset, count);

            // Calculate Bit-Depth Step Size
            float stepSize = (float)Math.Pow(2, bitDepth);

            // LOOP through the buffer by "Frames" (Steps of 1 for Mono, 2 for Stereo)
            for (int i = 0; i < samplesRead; i += channels) {
                // Deciding: Do we update the sound, or hold the old sound?
                bool updateSample = (sampleFrameCount % downSampleFactor) == 0;

                // Process every channel in this frame (Left, then Right)
                for (int channel = 0; channel < channels; channel++) {
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
                        lastSampleValues[channel] = crushedSample;
                    }
                    else {
                        // HOLD: Overwrite the current sample with the OLD value
                        buffer[index] = lastSampleValues[channel];
                    }
                }

                // Only increment the frame counter after processing the full L+R pair
                sampleFrameCount++;
            }

            return samplesRead;
        }
    }
}