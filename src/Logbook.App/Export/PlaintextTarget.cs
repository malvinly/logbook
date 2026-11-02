using System.IO;
using System.Windows;
using Logbook.App.Dialogs;
using Logbook.App.Startup;
using Logbook.Core.Export;
using Logbook.Core.Storage;

namespace Logbook.App.Export;

/// <summary>
/// Where a plaintext export may go (docs/export.md), shared by the decrypted export and the CSV export: a default
/// folder outside the log's folder (which may be synced), a warning before writing into it, the guards that keep the
/// encrypted log from being overwritten, and the last folder remembered per Windows profile.
/// </summary>
internal static class PlaintextTarget
{
    // The folder is remembered per Windows profile in %LOCALAPPDATA%, never in the settings file beside the log, which
    // may be synced: it is an absolute local path that would point a reader of that file at the one plaintext copy.
    private const string FolderMarker = "export-folder";

    /// <summary>The one-sentence reason a plaintext export defaults outside the log's folder.</summary>
    public const string Why = "The export is unencrypted and contains notes about people and situations. Logbook cannot tell which folders are synced, so it only keeps the default out of the log's folder; choose a folder that is not synced.";

    /// <summary>The remembered folder, else Documents, else local app data: the first that is not inside the log's folder.</summary>
    public static string DefaultFolder(string logFolder)
    {
        string?[] candidates =
        [
            LocalMarker.Read(FolderMarker),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ];
        List<string> existing = [];
        foreach (string? folder in candidates)
        {
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                existing.Add(folder);
            }
        }

        // Null when every candidate is inside the log's folder; the warning before the write will say so.
        return PlaintextFolder.Choose(existing, logFolder) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public static bool IsInLogFolder(string path, string logFolder) => DataFolder.IsUnder(path, logFolder);

    /// <summary>Why the path must not be written, or null when it may be.</summary>
    public static string? Problem(string fullPath, string dataFilePath)
    {
        // Never the encrypted log itself, nor a name the launch check would mistake for one.
        if (string.Equals(fullPath, Path.GetFullPath(dataFilePath), StringComparison.OrdinalIgnoreCase))
        {
            return "That is the encrypted log file. Exporting over it would destroy it.";
        }

        if (DataFolder.IsLogFileName(Path.GetFileName(fullPath)))
        {
            return "A name like logbook*.jsonl would be taken for a log at the next launch. Keep the logbook-export- prefix.";
        }

        return null;
    }

    /// <summary>True when the path is outside the log's folder, or the user chose to write into it anyway.</summary>
    public static bool ConfirmIfInLogFolder(Window owner, string fullPath, string logFolder)
    {
        if (!IsInLogFolder(fullPath, logFolder))
        {
            return true;
        }

        return AppDialog.Confirm(owner, "Export into the log's folder?", "This folder holds the log and may be synced. The export is unencrypted and others may be able to search synced storage. Write it there anyway?", "Write it there", "Cancel", DialogTone.Warning, confirmIsDefault: false);
    }

    public static void RememberFolder(string fullPath) => LocalMarker.Set(FolderMarker, Path.GetDirectoryName(fullPath));
}
