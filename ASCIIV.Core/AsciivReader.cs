using System.IO;
using System.Text;

namespace ASCIIV.Core;

/// <summary>
/// Reads .asciiv files: the current indexed format and the legacy text format.
/// Not thread safe: use one reader per thread.
/// </summary>
public sealed class AsciivReader : IDisposable {
    private const double MaxFramesPerSecond = 1000;

    private readonly Stream _stream;
    private readonly long _audioStart;
    private readonly int _audioLength;
    private readonly long _framesStart;

    // Indexed format only
    private readonly char[] _palette = [];
    private readonly byte[] _frameBuffer = [];

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
                if (version is < AsciivFormat.FirstVersion or > AsciivFormat.CurrentVersion) {
                    throw new InvalidDataException(
                        $"This file uses format version {version}, but only versions up to {AsciivFormat.CurrentVersion} are supported.");
                }

                FramesPerSecond = ReadFramesPerSecond(reader);
                ColorName = reader.ReadString();
                Width = reader.ReadInt32();
                Height = reader.ReadInt32();
                int declaredFrameCount = reader.ReadInt32();
                _palette = reader.ReadString().ToCharArray();
                byte colorMode = (version >= AsciivFormat.CellColorsVersion) ? reader.ReadByte() : AsciivFormat.SingleColorMode;
                FontName = (version >= AsciivFormat.FontVersion) ? reader.ReadString() : string.Empty;

                if ((Width is < 1 or > AsciivFormat.MaxDimension)
                    || (Height is < 1 or > AsciivFormat.MaxDimension)
                    || (declaredFrameCount < 0)
                    || (_palette.Length is < 1 or > AsciivFormat.MaxPaletteSize)
                    || (colorMode > AsciivFormat.CellColorMode)) {
                    throw new InvalidDataException("The file header is corrupt.");
                }

                HasCellColors = colorMode == AsciivFormat.CellColorMode;
                (_audioStart, _audioLength) = ReadAudioSection(reader);
                _framesStart = _audioStart + _audioLength;
                _frameBuffer = new byte[Width * Height * (HasCellColors ? 2 : 1)];

                // A truncated file still plays the frames that are complete
                long availableFrames = (stream.Length - _framesStart) / _frameBuffer.Length;
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

    /// <summary>True when every cell has its own colour instead of the whole video using <see cref="ColorName"/>.</summary>
    public bool HasCellColors { get; }

    /// <summary>Name of the <see cref="FrameFont"/> to draw with. Empty for the default font.</summary>
    public string FontName { get; } = string.Empty;

    public bool HasAudio => _audioLength > 0;

    /// <summary>True when the file was written before the versioned format existed.</summary>
    public bool IsLegacy { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / FramesPerSecond);

    public static AsciivReader Open(string path) {
        return new AsciivReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    /// <summary>
    /// Returns one frame. An index outside the file returns <see cref="AsciiFrame.Empty"/>.
    /// </summary>
    public AsciiFrame GetFrame(int index) {
        if ((index < 0) || (index >= FrameCount)) {
            return AsciiFrame.Empty;
        }

        if (_legacyOffsets is not null) {
            return AsciiFrame.FromText(FrameIndexer.ReadFrame(_stream, _legacyOffsets, index));
        }

        _stream.Position = _framesStart + ((long)index * _frameBuffer.Length);
        _stream.ReadExactly(_frameBuffer);

        int cellCount = Width * Height;
        ReadOnlySpan<byte> cells = _frameBuffer.AsSpan(0, cellCount);
        return CreateFrame(cells, HasCellColors ? _frameBuffer.AsSpan(cellCount) : [], Width, Height, _palette);
    }

    /// <summary>
    /// Returns the text of one frame, rows separated by line breaks. An index outside the file returns an empty string.
    /// </summary>
    public string ReadFrame(int index) {
        return GetFrame(index).ToText();
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
    /// Turns stored cells into a frame. <paramref name="colors"/> is empty for a single-colour frame.
    /// </summary>
    public static AsciiFrame CreateFrame(ReadOnlySpan<byte> cells, ReadOnlySpan<byte> colors, int width, int height, ReadOnlySpan<char> palette) {
        if (cells.Length != (width * height)) {
            throw new ArgumentException("The cell count does not match the frame dimensions.", nameof(cells));
        }

        if (!colors.IsEmpty && (colors.Length != cells.Length)) {
            throw new ArgumentException("The colour count does not match the cell count.", nameof(colors));
        }

        char[] chars = new char[cells.Length];
        for (int i = 0; i < cells.Length; i++) {
            // An index outside the palette can only come from a damaged file
            chars[i] = (cells[i] < palette.Length) ? palette[cells[i]] : ' ';
        }

        return new AsciiFrame(width, height, chars, colors.IsEmpty ? null : colors.ToArray());
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