using System.IO;
using Logbook.App.Diagnostics;
using Logbook.Core.Storage;

namespace Logbook.App.Startup;

/// <summary>The sync-service checks of docs/file-format.md: placeholder hydration and sibling log files.</summary>
internal static class SyncFile
{
    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS: the cloud filter keeps only a placeholder until the file is read.
    // ReparsePoint is deliberately not checked: sync clients leave it set on fully downloaded files too.
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>True when the file is a dehydrated placeholder. Reading it (which opening does) hydrates it.</summary>
    public static bool IsPlaceholder(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Offline) || attributes.HasFlag(RecallOnDataAccess);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Could not read file attributes", e);
            return false;
        }
    }

    /// <summary>Every log file beside the data file, the data file included, sorted by name. Decrypted exports are not logs.</summary>
    public static IReadOnlyList<string> Siblings(string dataFilePath)
    {
        string folder = Path.GetDirectoryName(dataFilePath) ?? ".";
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return
        [
            .. Directory.GetFiles(folder, "logbook*.jsonl")
                .Where(path => DataFolder.IsLogFileName(Path.GetFileName(path)))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }
}
