using System.IO;
using System.Text;

namespace ASCIIV.Core;

/// <summary>
/// Locates and reads the text frames of an .asciiv file. Each frame is followed by a FRAME_END marker line.
/// </summary>
public static class FrameIndexer {
    public const string FrameMarker = "FRAME_END";

    // A tail after the last marker that is no longer than a line break is not a frame
    private const int MaxLineBreakLength = 2;

    private static readonly char[] LineBreakChars = ['\r', '\n'];

    /// <summary>
    /// Scans the stream from <paramref name="startPosition"/> and returns the byte offset where each frame starts.
    /// </summary>
    public static List<long> IndexFrames(Stream stream, long startPosition, int bufferSize = 64 * 1024) {
        ReadOnlySpan<byte> marker = "FRAME_END"u8;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bufferSize, marker.Length);

        List<long> offsets = [];
        byte[] buffer = new byte[bufferSize];

        // Chunks overlap by (marker length - 1) bytes so a marker straddling two chunks is never missed
        int overlap = marker.Length - 1;
        long chunkStart = startPosition;
        long frameStart = startPosition;
        stream.Position = startPosition;

        while (true) {
            int bytesRead = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (bytesRead < marker.Length) {
                break;
            }

            ReadOnlySpan<byte> chunk = buffer.AsSpan(0, bytesRead);
            int searchFrom = 0;
            while (true) {
                int hit = chunk[searchFrom..].IndexOf(marker);
                if (hit < 0) {
                    break;
                }

                offsets.Add(frameStart);
                frameStart = chunkStart + searchFrom + hit + marker.Length;
                searchFrom += hit + marker.Length;
            }

            if (bytesRead < buffer.Length) {
                break;
            }

            stream.Seek(-overlap, SeekOrigin.Current);
            chunkStart += bytesRead - overlap;
        }

        // A file cut off mid-frame still gets its last partial frame
        if ((stream.Length - frameStart) > MaxLineBreakLength) {
            offsets.Add(frameStart);
        }

        return offsets;
    }

    /// <summary>
    /// Reads one frame's text. Only the marker and the line breaks around the frame are removed,
    /// so rows made entirely of spaces keep every frame the same size.
    /// </summary>
    public static string ReadFrame(Stream stream, IReadOnlyList<long> frameOffsets, int index) {
        if ((index < 0) || (index >= frameOffsets.Count)) {
            return string.Empty;
        }

        long startPos = frameOffsets[index];
        long endPos = ((index + 1) < frameOffsets.Count) ? frameOffsets[index + 1] : stream.Length;
        int length = (int)(endPos - startPos);
        if (length <= 0) {
            return string.Empty;
        }

        byte[] frameBytes = new byte[length];
        stream.Seek(startPos, SeekOrigin.Begin);
        stream.ReadExactly(frameBytes, 0, length);

        string raw = Encoding.UTF8.GetString(frameBytes);
        int markerIndex = raw.LastIndexOf(FrameMarker, StringComparison.Ordinal);
        if (markerIndex >= 0) {
            raw = raw[..markerIndex];
        }

        return raw.Trim(LineBreakChars);
    }
}