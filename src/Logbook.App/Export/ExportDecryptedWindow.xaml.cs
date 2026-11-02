using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Logbook.App.Diagnostics;
using Logbook.Core;
using Logbook.Core.Export;
using Logbook.Core.Storage;
using Microsoft.Win32;

namespace Logbook.App.Export;

/// <summary>
/// Tools → Export decrypted (docs/export.md): a plaintext JSONL file, latest revision per live id, or the
/// whole history. The save location defaults outside the log's folder and is remembered.
/// </summary>
public sealed partial class ExportDecryptedWindow : Window
{
    private readonly LogFile _log;
    private readonly string _dataFilePath;
    private readonly string _logFolder;

    public ExportDecryptedWindow(LogFile log, string dataFilePath, string logFolder)
    {
        InitializeComponent();
        _log = log;
        _dataFilePath = dataFilePath;
        _logFolder = logFolder;
        PathBox.Text = Path.Combine(DefaultFolder(), FileName());
    }

    private static string FileName() => DateTime.Now.ToString("'logbook-export-'yyyy-MM-dd'.jsonl'", CultureInfo.InvariantCulture);

    private string DefaultFolder() => PlaintextTarget.DefaultFolder(_logFolder);

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
        // Lock now and the idle lock close this window even while the save dialog is up.
        if (dialog.ShowDialog(this) == true && IsLoaded)
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

        if (PlaintextTarget.Problem(fullPath, _dataFilePath) is { } problem)
        {
            Report(problem, danger: true);
            return;
        }

        // A lock while the question was up closed this window and emptied the log; there is nothing to write then.
        if (!PlaintextTarget.ConfirmIfInLogFolder(this, fullPath, _logFolder) || !IsLoaded)
        {
            return;
        }

        bool history = IncludeHistory.IsChecked == true;
        IReadOnlyList<Entry> records = DecryptedExport.Select(_log.Revisions, _log.Live, history);
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
            PlaintextTarget.RememberFolder(fullPath);
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
