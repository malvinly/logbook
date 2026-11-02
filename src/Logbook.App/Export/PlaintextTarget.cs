using System.IO;
using System.Windows;
using Logbook.App.Diagnostics;
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

    /// <summary>The one-sentence reason a plaintext export defaults to a private folder under the Windows profile.</summary>
    public const string Why = "The export is unencrypted and contains notes about people and situations. Logbook cannot tell which folders are synced, so it defaults to a private folder under your Windows profile, outside the log's folder; if you choose another folder, choose one that is not synced.";

    /// <summary>
    /// The remembered folder, else <c>%LOCALAPPDATA%\Logbook\exports</c>: the first that is not inside the log's folder.
    /// The profile's local app data is not a folder sync services back up by default, which is the safest default the
    /// app can offer without detecting sync folders.
    /// </summary>
    public static string DefaultFolder(string logFolder)
    {
        List<string> existing = [];
        if (LocalMarker.Read(FolderMarker) is { Length: > 0 } remembered && Directory.Exists(remembered))
        {
            existing.Add(remembered);
        }

        if (PrivateExportsFolder() is { } exports)
        {
            existing.Add(exports);
        }

        // Null when every candidate is inside the log's folder; the warning before the write will say so.
        return PlaintextFolder.Choose(existing, logFolder) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <summary>Created on demand; null when it cannot be, in which case the dialog falls back to Documents.</summary>
    private static string? PrivateExportsFolder()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logbook", "exports");
        try
        {
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Could not create the exports folder", e);
            return null;
        }
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
