using System.IO;
using System.Text.Json;
using Logbook.App.Diagnostics;

namespace Logbook.App.Settings;

/// <summary>
/// Loads and saves the settings file, which lives in the data file's folder (docs/settings.md). The settings do not
/// name the data file; the per-profile pointer does.
/// </summary>
public static class SettingsStore
{
    public static string SettingsPath(string folder) => Path.Combine(folder, Settings.FileName);

    /// <summary>Null when the file is missing or unreadable; the caller then uses the settings it already has.</summary>
    public static Settings? Load(string folder)
    {
        string path = SettingsPath(folder);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.Settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Diag.Log($"Could not read settings at {path}", e);
            return null;
        }
    }

    /// <summary>Writes to a temporary file and then swaps it in, so a crash mid-write cannot leave a half settings file.</summary>
    public static void Save(string folder, Settings settings)
    {
        string path = SettingsPath(folder);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings));
        File.Move(temporary, path, overwrite: true);
    }
}
