using System.IO;
using System.Text.Json;
using Logbook.App.Diagnostics;

namespace Logbook.App.Settings;

/// <summary>Finds the synced root, and loads and saves the settings file inside it (DESIGN.md sections 12 and 16).</summary>
public static class SettingsStore
{
    /// <summary>Tried in order when no variable is configured yet.</summary>
    public static readonly IReadOnlyList<string> DefaultSyncRootEnvVars = ["OneDriveCommercial", "OneDrive", "USERPROFILE"];

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

    /// <summary>The first default variable whose folder exists. Null only on a machine with no profile at all.</summary>
    public static string? DetectRoot()
    {
        foreach (string envVar in DefaultSyncRootEnvVars)
        {
            if (ResolveRoot(envVar) is { } root)
            {
                return root;
            }
        }

        return null;
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

    public static string DataFilePath(string root, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Path.GetFullPath(Path.Combine(root, settings.DataFileRelativePath));
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

    public static void Save(string root, Settings settings)
    {
        string path = SettingsPath(root);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings));
    }
}
