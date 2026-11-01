using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Logbook.App.Diagnostics;
using Logbook.App.Dialogs;
using Logbook.App.Startup;
using Logbook.Core;
using Logbook.Core.Storage;
using Microsoft.Win32;

namespace Logbook.App.Export;

/// <summary>
/// Tools → Export decrypted (DESIGN.md section 11): a plaintext JSONL file, latest revision per live id, or the
/// whole history. The save location defaults outside every synced folder and is remembered.
/// </summary>
public sealed partial class ExportDecryptedWindow : Window
{
    private readonly LogFile _log;
    private readonly string _dataFilePath;
    private readonly IReadOnlyList<string> _syncRoots;
    // The folder is remembered per Windows profile, never in the synced settings file: it is an absolute local path
    // that would point a reader of that file at the one plaintext copy.
    private const string FolderMarker = "export-folder";

    public ExportDecryptedWindow(LogFile log, string dataFilePath, IReadOnlyList<string> syncRoots)
    {
        InitializeComponent();
        _log = log;
        _dataFilePath = dataFilePath;
        _syncRoots = syncRoots;
        PathBox.Text = Path.Combine(DefaultFolder(), FileName());
    }

    private static string FileName() => DateTime.Now.ToString("'logbook-export-'yyyy-MM-dd'.jsonl'", CultureInfo.InvariantCulture);

    // The remembered folder, else Documents, else local app data: the first that is not inside any synced folder.
    private string DefaultFolder()
    {
        string?[] candidates =
        [
            LocalMarker.Read(FolderMarker),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ];
        foreach (string? folder in candidates)
        {
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder) && !IsSynced(folder))
            {
                return folder;
            }
        }

        // Everything is synced (USERPROFILE as the root does that); the warning after the write will say so.
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private bool IsSynced(string path) => _syncRoots.Any(root => DataFolder.IsUnder(path, root));

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = "Export decrypted log",
            FileName = Path.GetFileName(PathBox.Text),
            InitialDirectory = Path.GetDirectoryName(PathBox.Text) ?? DefaultFolder(),
            Filter = "JSON Lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
            DefaultExt = ".jsonl",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        string path = PathBox.Text.Trim();
        if (path.Length == 0)
        {
            Report("Choose where to save the file.", danger: true);
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Report("That is not a valid file path.", danger: true);
            return;
        }

        // Never the encrypted log itself, nor a name the launch check would mistake for one.
        if (string.Equals(fullPath, Path.GetFullPath(_dataFilePath), StringComparison.OrdinalIgnoreCase))
        {
            Report("That is the encrypted log file. Exporting over it would destroy it.", danger: true);
            return;
        }

        if (DataFolder.IsLogFileName(Path.GetFileName(fullPath)))
        {
            Report("A name like logbook*.jsonl would be taken for a log at the next launch. Keep the logbook-export- prefix.", danger: true);
            return;
        }

        if (IsSynced(fullPath))
        {
            bool writeThere = AppDialog.Confirm(this, "Export into a synced folder?", "This folder is synced. The export is unencrypted and others may be able to search it. Write it there anyway?", "Write it there", "Cancel", DialogTone.Warning, confirmIsDefault: false);
            if (!writeThere)
            {
                return;
            }
        }

        bool history = IncludeHistory.IsChecked == true;
        IEnumerable<Entry> records = history
            ? _log.Revisions
            : _log.Live.OrderBy(entry => entry.Id, StringComparer.Ordinal);
        try
        {
            StringBuilder text = new();
            int count = 0;
            foreach (Entry record in records)
            {
                text.Append(LogbookJson.Serialize(record)).Append('\n');
                count++;
            }

            File.WriteAllText(fullPath, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            LocalMarker.Set(FolderMarker, Path.GetDirectoryName(fullPath));
            Diag.Log($"Exported {count} records decrypted (history: {history})");
            Report(FormattableString.Invariant($"Wrote {count} records to {fullPath}. Delete it when you are done with it."), danger: false);
            ExportButton.IsEnabled = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Decrypted export failed", ex);
            Report("Could not write the file: " + ex.Message, danger: true);
        }
    }

    private void Report(string message, bool danger)
    {
        Status.Foreground = (Brush)FindResource(danger ? "DangerBrush" : "TextMutedBrush");
        Status.Text = message;
    }
}
