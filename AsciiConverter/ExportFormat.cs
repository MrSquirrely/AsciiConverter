namespace ASCIIV.Converter;

/// <summary>
/// A standard video format the ASCII video can be rendered to, next to the .asciiv file.
/// </summary>
public enum ExportFormat {
    None,
    Mp4,
    WebM,
    Gif
}

public static class ExportFormatExtensions {
    /// <summary>The file extension of the format, including the dot.</summary>
    public static string GetFileExtension(this ExportFormat format) {
        return format switch {
            ExportFormat.Mp4 => ".mp4",
            ExportFormat.WebM => ".webm",
            ExportFormat.Gif => ".gif",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "This format has no file extension.")
        };
    }

    /// <summary>GIF files cannot carry sound.</summary>
    public static bool SupportsAudio(this ExportFormat format) {
        return format is ExportFormat.Mp4 or ExportFormat.WebM;
    }
}