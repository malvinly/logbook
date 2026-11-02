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

    public static bool Exists(string name) => File.Exists(PathOf(name));

    /// <summary>The full path of the marker, for messages that tell the user which file failed.</summary>
    public static string PathOf(string name) => Path.Combine(Folder, name);

    /// <summary>The stored text, or null when there is none or it could not be read.</summary>
    public static string? Read(string name)
    {
        TryRead(name, out string? content);
        return content;
    }

    /// <summary>
    /// The stored text, null when the marker does not exist. False (content null) when it exists but could not be
    /// read, so a caller that must not mistake a read failure for "no marker" can tell the two apart.
    /// </summary>
    public static bool TryRead(string name, out string? content)
    {
        string path = PathOf(name);
        try
        {
            content = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log($"Could not read marker {name}", e);
            content = null;
            return false;
        }
    }

    /// <summary>
    /// Writes to a temporary file and then swaps it in, as the settings file is saved, so a crash mid-write cannot
    /// leave half a marker. False when the write failed; the failure is logged.
    /// </summary>
    public static bool Set(string name, string? content = null)
    {
        string path = PathOf(name);
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(temporary, content ?? DateTimeOffset.Now.ToString("O"));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log($"Could not write marker {name}", e);
            return false;
        }
    }
}
