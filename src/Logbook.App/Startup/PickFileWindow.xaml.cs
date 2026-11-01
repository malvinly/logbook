using System.IO;
using System.Windows;
using Logbook.App.Diagnostics;
using Logbook.Core.Storage;

namespace Logbook.App.Startup;

/// <summary>One candidate file in the picker: its name, header creation date, and line count.</summary>
public sealed class FileCandidate
{
    public FileCandidate(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        try
        {
            FileHeader header = LogFile.ReadHeader(path);
            int lines = LogFile.CountRecordLines(path);
            Detail = FormattableString.Invariant($"created {Format.DateTime(header.CreatedAt)} · {lines} record lines · modified {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm}");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Diag.Log($"Could not describe {Name}", e);
            Detail = "not a readable Logbook file: " + e.Message;
        }
    }

    public string Path { get; }

    public string Name { get; }

    public string Detail { get; }
}

/// <summary>Shown when several <c>logbook*.jsonl</c> files sit beside each other (DESIGN.md section 8).</summary>
public sealed partial class PickFileWindow : Window
{
    public PickFileWindow(IEnumerable<string> paths)
    {
        InitializeComponent();
        Files.ItemsSource = paths.Select(p => new FileCandidate(p)).ToList();
        Files.SelectedIndex = 0;
    }

    public string? ChosenPath { get; private set; }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (Files.SelectedItem is FileCandidate candidate)
        {
            ChosenPath = candidate.Path;
            DialogResult = true;
        }
    }
}
