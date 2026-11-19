using System.IO;
using System.Text;

namespace ASCIIV.Core;

/// <summary>
/// Reads .asciiv files: the current indexed format and the legacy text format.
/// Not thread safe: use one reader per thread.
/// </summary>
public sealed class AsciivReader : IDisposable {
    private const double MaxFramesPerSecond = 1000;
    private const string LineBreak = "\r\n";

    private readonly Stream _stream;
    private readonly long _audioStart;
    private readonly int _audioLength;
    private readonly long _framesStart;

    // Version 2 only
    private readonly char[] _palette = [];
    private readonly byte[] _cellBuffer = [];

    // Legacy only: byte offset of each text frame
    private readonly List<long>? _legacyOffsets;

    /// <summary>
    /// Takes ownership of <paramref name="stream"/>, which must be readable and seekable.
    /// </summary>
    /// <exception cref="InvalidDataException">The stream is not a readable .asciiv file.</exception>
    public AsciivReader(Stream stream) {
        _stream = stream;
        try {
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

            Span<byte> magic = stackalloc byte[AsciivFormat.Magic.Length];
            IsLegacy = (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) < magic.Length)
                || !magic.SequenceEqual(AsciivFormat.Magic);

            if (IsLegacy) {
                stream.Position = 0;
                FramesPerSecond = ReadFramesPerSecond(reader);
                ColorName = reader.ReadString();
                (_audioStart, _audioLength) = ReadAudioSection(reader);

                _framesStart = _audioStart + _audioLength;
                _legacyOffsets = FrameIndexer.IndexFrames(stream, _framesStart);
                FrameCount = _legacyOffsets.Count;
            }
            else {
                int version = reader.ReadInt32();
                if (version != AsciivFormat.CurrentVersion) {
                    throw new InvalidDataException(
                        $"This file uses format version {version}, but only version {AsciivFormat.CurrentVersion} is supported.");
                }

                FramesPerSecond = ReadFramesPerSecond(reader);
                ColorName = reader.ReadString();
                Width = reader.ReadInt32();
                Height = reader.ReadInt32();
                int declaredFrameCount = reader.ReadInt32();
                _palette = reader.ReadString().ToCharArray();

                if ((Width is < 1 or > AsciivFormat.MaxDimension)
                    || (Height is < 1 or > AsciivFormat.MaxDimension)
                    || (declaredFrameCount < 0)
                    || (_palette.Length is < 1 or > AsciivFormat.MaxPaletteSize)) {
                    throw new InvalidDataException("The file header is corrupt.");
                }

                (_audioStart, _audioLength) = ReadAudioSection(reader);
                _framesStart = _audioStart + _audioLength;
                _cellBuffer = new byte[Width * Height];

                // A truncated file still plays the frames that are complete
                long availableFrames = (stream.Length - _framesStart) / _cellBuffer.Length;
                FrameCount = (int)Math.Min(declaredFrameCount, availableFrames);
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or FormatException or ArgumentException or InvalidDataException) {
            stream.Dispose();
            throw ex as InvalidDataException ?? new InvalidDataException("This is not a valid .asciiv file.", ex);
        }
        catch {
            stream.Dispose();
            throw;
        }
    }

    public double FramesPerSecond { get; }

    public string ColorName { get; }

    /// <summary>Characters per row. Zero for legacy files, which do not record it.</summary>
    public int Width { get; }

    /// <summary>Rows per frame. Zero for legacy files, which do not record it.</summary>
    public int Height { get; }

    public int FrameCount { get; }

    public bool HasAudio => _audioLength > 0;

    /// <summary>True when the file was written before the versioned format existed.</summary>
    public bool IsLegacy { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / FramesPerSecond);

    public static AsciivReader Open(string path) {
        return new AsciivReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    /// <summary>
    /// Returns the text of one frame, rows separated by line breaks. An index outside the file returns an empty string.
    /// </summary>
    public string ReadFrame(int index) {
        if ((index < 0) || (index >= FrameCount)) {
            return string.Empty;
        }

        if (_legacyOffsets is not null) {
            return FrameIndexer.ReadFrame(_stream, _legacyOffsets, index);
        }

        _stream.Position = _framesStart + ((long)index * _cellBuffer.Length);
        _stream.ReadExactly(_cellBuffer);
        return ComposeFrameText(_cellBuffer, Width, Height, _palette);
    }

    /// <summary>
    /// Returns the embedded audio file (MP3 or WAV), or an empty array when there is none.
    /// </summary>
    public byte[] ReadAudio() {
        if (_audioLength == 0) {
            return [];
        }

        byte[] audio = new byte[_audioLength];
        _stream.Position = _audioStart;
        _stream.ReadExactly(audio);
        return audio;
    }

    /// <summary>
    /// Turns the cells of one frame into text: one character per cell and a line break between rows.
    /// </summary>
    public static string ComposeFrameText(ReadOnlySpan<byte> cells, int width, int height, ReadOnlySpan<char> palette) {
        if (cells.Length != (width * height)) {
            throw new ArgumentException("The cell count does not match the frame dimensions.", nameof(cells));
        }

        if ((width == 0) || (height == 0)) {
            return string.Empty;
        }

        char[] text = new char[(width * height) + ((height - 1) * LineBreak.Length)];
        int position = 0;
        for (int y = 0; y < height; y++) {
            if (y > 0) {
                LineBreak.CopyTo(text.AsSpan(position));
                position += LineBreak.Length;
            }

            ReadOnlySpan<byte> row = cells.Slice(y * width, width);
            foreach (byte cell in row) {
                // An index outside the palette can only come from a damaged file
                text[position++] = (cell < palette.Length) ? palette[cell] : ' ';
            }
        }

        return new string(text);
    }

    public void Dispose() {
        _stream.Dispose();
    }

    private static double ReadFramesPerSecond(BinaryReader reader) {
        double framesPerSecond = reader.ReadDouble();
        return (double.IsFinite(framesPerSecond) && (framesPerSecond > 0) && (framesPerSecond <= MaxFramesPerSecond))
            ? framesPerSecond
            : throw new InvalidDataException("This is not a valid .asciiv file.");
    }

    private static (long Start, int Length) ReadAudioSection(BinaryReader reader) {
        int length = reader.ReadInt32();
        long start = reader.BaseStream.Position;
        return ((length >= 0) && (length <= (reader.BaseStream.Length - start)))
            ? (start, length)
            : throw new InvalidDataException("The audio section of the file is corrupt.");
    }
}