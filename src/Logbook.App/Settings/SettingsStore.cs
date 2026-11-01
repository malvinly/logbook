using System.IO;
using System.Text.Json;
using Logbook.App.Diagnostics;
using Logbook.Core.Storage;

namespace Logbook.App.Settings;

/// <summary>
/// Finds the synced root, and loads and saves the settings file inside it (DESIGN.md sections 12 and 16).
/// The settings file is found by trying the default variables, since it cannot name its own location; the
/// <c>syncRootEnvVar</c> it holds then names the root the data file is relative to.
/// </summary>
public static class SettingsStore
{
    /// <summary>Tried in order to find an existing settings file, and offered as choices on first run.</summary>
    public static readonly IReadOnlyList<string> DefaultSyncRootEnvVars = ["OneDriveCommercial", "OneDrive", "USERPROFILE"];

    /// <summary>Variables that name a synced folder on this machine, whichever one the log lives in.</summary>
    public static readonly IReadOnlyList<string> KnownSyncEnvVars = ["OneDriveCommercial", "OneDriveConsumer", "OneDrive"];

    /// <summary>The folder a variable points at, or null when it is unset or not a folder.</summary>
    public static string? ResolveRoot(string envVar)
    {
        if (string.IsNullOrWhiteSpace(envVar))
        {
            return null;
        }

        string? value = Environment.GetEnvironmentVariable(envVar);
        return !string.IsNullOrWhiteSpace(value) && Directory.Exists(value) ? Path.GetFullPath(value) : null;
    }

    /// <summary>The default variable whose folder already holds a settings file, so an existing install is found first.</summary>
    public static string? FindConfiguredEnvVar()
    {
        foreach (string envVar in DefaultSyncRootEnvVars)
        {
            if (ResolveRoot(envVar) is { } root && File.Exists(Path.Combine(root, Settings.FileName)))
            {
                return envVar;
            }
        }

        return null;
    }

    public static string SettingsPath(string root) => Path.Combine(root, Settings.FileName);

    /// <summary>The root the data file path is relative to: the configured variable's folder, else the settings root.</summary>
    public static string DataRoot(string settingsRoot, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ResolveRoot(settings.SyncRootEnvVar) ?? settingsRoot;
    }

    /// <summary>
    /// The full data file path, or an empty string when the relative path escapes the root. The settings file is
    /// readable by others in the synced folder, so a path it names must stay inside the root.
    /// </summary>
    public static string DataFilePath(string settingsRoot, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string root = DataRoot(settingsRoot, settings);
        string path = Path.GetFullPath(Path.Combine(root, settings.DataFileRelativePath));
        return DataFolder.IsUnder(path, root) && !string.Equals(path, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ? path : "";
    }

    /// <summary>Null when the file is missing or unreadable; a broken settings file means the first-run flow.</summary>
    public static Settings? Load(string root)
    {
        string path = SettingsPath(root);
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
    public static void Save(string root, Settings settings)
    {
        string path = SettingsPath(root);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings));
        File.Move(temporary, path, overwrite: true);
    }
}
