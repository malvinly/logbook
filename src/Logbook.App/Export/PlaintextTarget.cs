using System.IO;
using System.Windows;
using Logbook.App.Dialogs;
using Logbook.App.Startup;
using Logbook.Core.Storage;

namespace Logbook.App.Export;

/// <summary>
/// Where a plaintext export may go (docs/export.md), shared by the decrypted export and the CSV export: a default
/// folder outside every synced folder, a warning before writing into one, the guards that keep the encrypted log from
/// being overwritten, and the last folder remembered per Windows profile.
/// </summary>
internal static class PlaintextTarget
{
    // The folder is remembered per Windows profile, never in the synced settings file: it is an absolute local path
    // that would point a reader of that file at the one plaintext copy.
    private const string FolderMarker = "export-folder";

    /// <summary>The one-sentence reason a plaintext export defaults outside the synced root.</summary>
    public const string Why = "The export is unencrypted and contains notes about people and situations; synced storage is searchable by people who are not you, so it defaults to a folder outside the synced root.";

    /// <summary>The remembered folder, else Documents, else local app data: the first that is not inside any synced folder.</summary>
    public static string DefaultFolder(IReadOnlyList<string> syncRoots)
    {
        string?[] candidates =
        [
            LocalMarker.Read(FolderMarker),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ];
        foreach (string? folder in candidates)
        {
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder) && !IsSynced(folder, syncRoots))
            {
                return folder;
            }
        }

        // Everything is synced (USERPROFILE as the root does that); the warning before the write will say so.
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public static bool IsSynced(string path, IReadOnlyList<string> syncRoots) => syncRoots.Any(root => DataFolder.IsUnder(path, root));

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

    /// <summary>True when the path is outside every synced folder, or the user chose to write into one anyway.</summary>
    public static bool ConfirmIfSynced(Window owner, string fullPath, IReadOnlyList<string> syncRoots)
    {
        if (!IsSynced(fullPath, syncRoots))
        {
            return true;
        }

        return AppDialog.Confirm(owner, "Export into a synced folder?", "This folder is synced. The export is unencrypted and others may be able to search it. Write it there anyway?", "Write it there", "Cancel", DialogTone.Warning, confirmIsDefault: false);
    }

    public static void RememberFolder(string fullPath) => LocalMarker.Set(FolderMarker, Path.GetDirectoryName(fullPath));
}
