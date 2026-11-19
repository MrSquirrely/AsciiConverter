using System.IO;

namespace ASCIIV.Converter;

/// <summary>
/// The folders the converter reads from and writes to.
/// </summary>
public static class AppPaths {
    private const string AppFolderName = "ASCIIV";

    /// <summary>Finished .asciiv files and exported videos: the ASCIIV folder inside the user's Videos folder.</summary>
    public static string OutputFolder { get; } = Path.Combine(GetUserMediaFolder(), AppFolderName);

    /// <summary>Downloaded tools, kept outside the install folder so the app also works from a read-only location.</summary>
    public static string AppDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    /// <summary>Intermediate files of a running conversion.</summary>
    public static string WorkFolder { get; } = Path.Combine(Path.GetTempPath(), AppFolderName);

    private static string GetUserMediaFolder() {
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return string.IsNullOrEmpty(videos)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : videos;
    }
}