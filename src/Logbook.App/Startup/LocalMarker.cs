using System.IO;
using Logbook.App.Diagnostics;

namespace Logbook.App.Startup;

/// <summary>
/// A per-profile "already asked" marker in %LOCALAPPDATA%. It lives with the profile on purpose: when the login
/// changes and the Startup shortcut vanishes, the marker vanishes with it and the offer is made again.
/// </summary>
internal static class LocalMarker
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logbook");

    public static bool Exists(string name) => File.Exists(Path.Combine(Folder, name));

    public static void Set(string name)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, name), DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log($"Could not write marker {name}", e);
        }
    }
}
