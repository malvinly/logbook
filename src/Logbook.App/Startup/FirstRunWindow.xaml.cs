using System.IO;
using System.Windows;
using System.Windows.Controls;
using Logbook.App.Settings;
using Microsoft.Win32;

namespace Logbook.App.Startup;

/// <summary>
/// First-run step 1 (DESIGN.md section 12): pick the synced root and choose or create the data file.
/// The result is the root, the settings to save there, and whether the file still has to be created.
/// </summary>
public sealed partial class FirstRunWindow : Window
{
    private sealed record RootChoice(string EnvVar, string Path)
    {
        public override string ToString() => $"{EnvVar}  —  {Path}";
    }

    public FirstRunWindow()
    {
        InitializeComponent();
        List<RootChoice> choices = [];
        foreach (string envVar in SettingsStore.DefaultSyncRootEnvVars)
        {
            if (SettingsStore.ResolveRoot(envVar) is { } path)
            {
                choices.Add(new RootChoice(envVar, path));
            }
        }

        RootVar.ItemsSource = choices;
        RootVar.SelectedIndex = 0;
    }

    public string Root { get; private set; } = "";

    public Settings.Settings Settings { get; private set; } = new();

    public string DataFilePath { get; private set; } = "";

    /// <summary>True when the chosen path does not exist yet and must be created with a new passphrase.</summary>
    public bool CreateNew { get; private set; }

    private RootChoice? SelectedRoot => RootVar.SelectedItem as RootChoice;

    private void OnRootChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedRoot is { } root)
        {
            RootPath.Text = root.Path;
            FilePath.Text = Path.Combine(root.Path, "logbook.jsonl");
        }
    }

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
            FileNote.Text = "This file exists. You will be asked for its passphrase.";
        }
        else
        {
            FileNote.Text = "This file will be created. You will choose a passphrase next.";
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Choose or name the log file",
            Filter = "Logbook files (logbook*.jsonl)|logbook*.jsonl|All files (*.*)|*.*",
            InitialDirectory = SelectedRoot?.Path,
            FileName = Path.GetFileName(FilePath.Text),
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
        if (SelectedRoot is not { } root)
        {
            Problem.Text = "No synced folder was found. Set OneDrive or OneDriveCommercial in the environment first.";
            return;
        }

        string path = FilePath.Text.Trim();
        if (path.Length == 0 || !Directory.Exists(Path.GetDirectoryName(path)))
        {
            Problem.Text = "Choose a file inside an existing folder.";
            return;
        }

        if (!Path.GetFileName(path).StartsWith("logbook", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            Problem.Text = "The file name should look like logbook.jsonl so the launch checks can find it.";
            return;
        }

        Root = root.Path;
        DataFilePath = Path.GetFullPath(path);
        CreateNew = !File.Exists(DataFilePath);
        Settings = new Settings.Settings
        {
            SyncRootEnvVar = root.EnvVar,
            DataFileRelativePath = Path.GetRelativePath(root.Path, DataFilePath),
        };
        DialogResult = true;
    }
}
