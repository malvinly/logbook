using Logbook.Core.Storage;

namespace Logbook.App.Startup;

/// <summary>
/// The per-profile pointer to the data file, <c>%LOCALAPPDATA%\Logbook\data-file</c> (docs/settings.md). It is the only
/// thing that finds the log at launch; the settings file beside the log does not name it. Core's
/// <see cref="DataFilePointer"/> holds the parse rule; this class does the per-profile reading and writing.
/// </summary>
internal static class DataFileLocation
{
    private const string MarkerName = "data-file";

    /// <summary>The pointer file's own path, for messages that name it.</summary>
    public static string Path => LocalMarker.PathOf(MarkerName);

    /// <summary>
    /// False when the pointer file exists but could not be read. Otherwise true, with the absolute path of the log, or
    /// null when there is no usable pointer and first run must ask.
    /// </summary>
    public static bool TryRead(out string? path)
    {
        bool read = LocalMarker.TryRead(MarkerName, out string? content);
        path = DataFilePointer.Parse(content);
        return read;
    }

    /// <summary>False when the pointer could not be written; the failure is logged.</summary>
    public static bool Write(string path) => LocalMarker.Set(MarkerName, path);
}
