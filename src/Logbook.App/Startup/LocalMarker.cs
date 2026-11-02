using System.IO;
using Logbook.App.Diagnostics;

namespace Logbook.App.Startup;

/// <summary>
/// Small per-profile values in %LOCALAPPDATA%: the "already asked" marker for the startup offer, the last export
/// folder, and the pointer to the data file (<see cref="DataFileLocation"/>). They live with the profile on purpose.
/// When the login changes, the Startup shortcut vanishes and so does the marker, so the offer is made again; and an
/// absolute local path never lands in the synced settings file.
/// </summary>
internal static class LocalMarker
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logbook");

    public static bool Exists(string name) => File.Exists(Path.Combine(Folder, name));

    /// <summary>The stored text, or null when there is none.</summary>
    public static string? Read(string name)
    {
        try
        {
            string path = Path.Combine(Folder, name);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log($"Could not read marker {name}", e);
            return null;
        }
    }

    public static void Set(string name, string? content = null)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, name), content ?? DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log($"Could not write marker {name}", e);
        }
    }
}
