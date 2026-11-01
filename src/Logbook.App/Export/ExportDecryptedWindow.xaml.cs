using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Logbook.App.Diagnostics;
using Logbook.Core;
using Logbook.Core.Storage;
using Microsoft.Win32;

namespace Logbook.App.Export;

/// <summary>
/// Tools → Export decrypted (DESIGN.md section 11): a plaintext JSONL file, latest revision per live id, or the
/// whole history. The save location defaults outside the synced root and is remembered.
/// </summary>
public sealed partial class ExportDecryptedWindow : Window
{
    private readonly LogFile _log;
    private readonly string _syncRoot;
    private readonly Settings.Settings _settings;
    private readonly Action _saveSettings;

    public ExportDecryptedWindow(LogFile log, string syncRoot, Settings.Settings settings, Action saveSettings)
    {
        InitializeComponent();
        _log = log;
        _syncRoot = syncRoot;
        _settings = settings;
        _saveSettings = saveSettings;
        PathBox.Text = Path.Combine(DefaultFolder(), FileName());
    }

    private static string FileName() => DateTime.Now.ToString("'logbook-export-'yyyy-MM-dd'.jsonl'", CultureInfo.InvariantCulture);

    // Documents unless it is itself under the synced root, then the local app data folder; never the synced root.
    private string DefaultFolder()
    {
        if (!string.IsNullOrEmpty(_settings.LastExportFolder) && Directory.Exists(_settings.LastExportFolder) && !IsUnderSyncRoot(_settings.LastExportFolder))
        {
            return _settings.LastExportFolder;
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (documents.Length > 0 && !IsUnderSyncRoot(documents))
        {
            return documents;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private bool IsUnderSyncRoot(string folder)
    {
        string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(_syncRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

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

            File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _settings.LastExportFolder = Path.GetDirectoryName(path);
            _saveSettings();
            Diag.Log($"Exported {count} records decrypted (history: {history})");
            string warning = IsUnderSyncRoot(path) ? " This file is inside the synced folder." : "";
            Report(FormattableString.Invariant($"Wrote {count} records to {path}.{warning}"), danger: warning.Length > 0);
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
