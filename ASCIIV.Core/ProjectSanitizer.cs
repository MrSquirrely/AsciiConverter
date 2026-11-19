using System.IO;

namespace ASCIIV.Core;

/// <summary>
/// Centralized sanitization utilities for safe project naming and file system operations.
/// </summary>
public static class ProjectSanitizer {
    // SECURITY & ARCHITECTURE: Centralized path and filename sanitizer to prevent path traversal
    public static string SanitizeProjectName(string? rawName, string defaultName = "OutputVideo") {
        if (string.IsNullOrWhiteSpace(rawName)) {
            return defaultName;
        }

        // Remove invalid filename and path characters (\, /, :, *, ?, ", <, >, |)
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string clean = string.Join("_", rawName.Trim().Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Strip directory navigation tokens and clean up leading/trailing separators
        clean = clean.Replace("..", "").Trim('_', ' ');

        return string.IsNullOrWhiteSpace(clean) ? defaultName : clean;
    }
}