using Logbook.Core.Storage;

namespace Logbook.App.Startup;

/// <summary>
/// The per-profile pointer to the data file, <c>%LOCALAPPDATA%\Logbook\data-file</c> (docs/settings.md). It is the only
/// thing that finds the log at launch; the settings file beside the log does not name it.
/// </summary>
internal static class DataFileLocation
{
    private const string MarkerName = "data-file";

    /// <summary>The absolute path of the log, or null when there is no usable pointer and first run must ask.</summary>
    public static string? Read() => DataFilePointer.Parse(LocalMarker.Read(MarkerName));

    public static void Write(string path) => LocalMarker.Set(MarkerName, path);
}
