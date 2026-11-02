namespace Logbook.Core.Storage;

/// <summary>
/// The per-profile pointer that names the data file (docs/settings.md). Its content is the absolute path of the log;
/// anything else is treated as no pointer, so the app falls back to the first-run picker rather than guessing. This
/// class holds the parse rule; the app's DataFileLocation does the per-profile reading and writing.
/// </summary>
public static class DataFilePointer
{
    /// <summary>The path the pointer names, or null when it is empty, more than one line, relative, or not a log file name.</summary>
    public static string? Parse(string? content)
    {
        string path = content?.Trim() ?? "";
        if (path.Length == 0 || path.Contains('\r') || path.Contains('\n') || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        return DataFolder.IsLogFileName(Path.GetFileName(path)) ? path : null;
    }
}
