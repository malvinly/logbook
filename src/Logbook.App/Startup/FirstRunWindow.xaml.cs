using System.IO;
using System.Windows;
using System.Windows.Controls;
using Logbook.Core.Storage;
using Microsoft.Win32;

namespace Logbook.App.Startup;

/// <summary>
/// First-run step 1 (docs/settings.md): choose or create the data file, in any folder. The result is the file's full
/// path and whether it still has to be created.
/// </summary>
public sealed partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
    }

    public string DataFilePath { get; private set; } = "";

    /// <summary>True when the chosen path does not exist yet and must be created with a new passphrase.</summary>
    public bool CreateNew { get; private set; }

    private void OnFileChanged(object sender, TextChangedEventArgs e)
    {
        string path = FilePath.Text.Trim();
        Problem.Text = "";
        if (path.Length == 0)
        {
            FileNote.Text = "";
        }
        else if (File.Exists(path))
        {
            FileNote.Text = "This file exists. You will be asked for its passphrase, and the settings beside it will be used.";
        }
        else
        {
            FileNote.Text = "This file will be created. You will choose a passphrase next.";
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        string? typedFolder = Path.GetDirectoryName(FilePath.Text.Trim());
        OpenFileDialog dialog = new()
        {
            Title = "Choose or name the log file",
            Filter = "Logbook files (logbook*.jsonl)|logbook*.jsonl|All files (*.*)|*.*",
            InitialDirectory = !string.IsNullOrEmpty(typedFolder) && Directory.Exists(typedFolder)
                ? typedFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = Path.GetFileName(FilePath.Text.Trim()),
            CheckFileExists = false,
            CheckPathExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            FilePath.Text = dialog.FileName;
        }
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        string path = FilePath.Text.Trim();
        // A relative path would resolve against wherever the exe was started from, so only a full path is accepted.
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(Path.GetDirectoryName(path)))
        {
            Problem.Text = "Choose a file inside an existing folder.";
            return;
        }

        if (!DataFolder.IsLogFileName(Path.GetFileName(path)))
        {
            Problem.Text = "The file name should look like logbook.jsonl so the launch checks can find it (and not logbook-export-…, which is reserved for exports).";
            return;
        }

        DataFilePath = Path.GetFullPath(path);
        CreateNew = !File.Exists(DataFilePath);
        DialogResult = true;
    }
}
