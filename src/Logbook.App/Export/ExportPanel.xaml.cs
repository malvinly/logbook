using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Logbook.App.Diagnostics;
using Logbook.App.Settings;
using Logbook.Core;
using Logbook.Core.Export;
using Microsoft.Win32;

namespace Logbook.App.Export;

/// <summary>
/// The "Copy as timeline" panel (DESIGN.md section 11): the four toggles, a preview, one button that puts the plain
/// text and the same timeline as HTML on the clipboard, and one that saves the same entries as a CSV file. Toggles are
/// read from and written back to settings so they are remembered.
/// </summary>
public sealed partial class ExportPanel : Window
{
    private readonly string _title; // the filter as typed, or a saved query's name (ExportTitle)
    private readonly IReadOnlyList<Entry> _entries;
    private readonly ExportSettings _settings;
    private readonly Action _saveSettings;
    private readonly string _dataFilePath;
    private readonly IReadOnlyList<string> _syncRoots;
    private bool _loading = true;

    public ExportPanel(string title, IReadOnlyList<Entry> entries, ExportSettings settings, Action saveSettings, string dataFilePath, IReadOnlyList<string> syncRoots)
    {
        InitializeComponent();
        _title = title;
        _entries = entries;
        _settings = settings;
        _saveSettings = saveSettings;
        _dataFilePath = dataFilePath;
        _syncRoots = syncRoots;
        CsvWhy.Text = "Save as CSV writes the entries listed here with every field, except that a text cell starting with =, +, - or @ gets a leading apostrophe so spreadsheets do not run it as a formula. " + PlaintextTarget.Why;
        ShowGaps.IsChecked = settings.ShowGaps;
        GroupByMonth.IsChecked = settings.GroupByMonth;
        Compact.IsChecked = settings.Compact;
        TagsAndPeople.IsChecked = settings.TagsAndPeople;
        _loading = false;
        Render();
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _settings.ShowGaps = ShowGaps.IsChecked == true;
        _settings.GroupByMonth = GroupByMonth.IsChecked == true;
        _settings.Compact = Compact.IsChecked == true;
        _settings.TagsAndPeople = TagsAndPeople.IsChecked == true;
        _saveSettings();
        Render();
    }

    private ExportOptions Options => new(_settings.ShowGaps, _settings.GroupByMonth, _settings.Compact, _settings.TagsAndPeople);

    private void Render()
    {
        Preview.Text = TimelineExport.Render(_title, _entries, Options);
        Report("", danger: false);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        // Windows text on the clipboard is conventionally CRLF; the renderer itself stays LF for portability.
        DataObject data = new();
        data.SetText(Preview.Text.Replace("\n", "\r\n", StringComparison.Ordinal), TextDataFormat.UnicodeText);

        // The same timeline as HTML beside the text, so mail clients paste bold dates and a real list; plain-text
        // targets keep taking the text. WPF puts an HTML string on the clipboard as UTF-8, which the offsets count.
        data.SetData(DataFormats.Html, ClipboardHtml.Wrap(TimelineHtml.Render(_title, _entries, Options)));

        // Ask clipboard history and cross-device clipboard sync to leave this out: it is decrypted entry text.
        byte[] no = [0, 0, 0, 0];
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(no));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(no));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(no));

        try
        {
            Clipboard.SetDataObject(data, copy: true);
            Report($"Copied {RangeText.Count(_entries.Count)}.", danger: false);
        }
        catch (COMException ex)
        {
            // Another process holds the clipboard open; common with clipboard managers and remote desktops.
            Diag.Log("Clipboard was busy", ex);
            Report("The clipboard is busy; try again in a moment.", danger: false);
        }
    }

    // The same rules as Export decrypted, since this is a plaintext copy too: a default folder outside every synced
    // folder, a warning before writing into one, never over the log, and the folder remembered on this profile only.
    private void OnSaveCsvClick(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = "Save as CSV",
            FileName = DateTime.Now.ToString("'logbook-export-'yyyy-MM-dd'.csv'", CultureInfo.InvariantCulture),
            InitialDirectory = PlaintextTarget.DefaultFolder(_syncRoots),
            Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            OverwritePrompt = true,
        };
        // Lock now and the idle lock close this panel even while the save dialog is up; a closed panel writes nothing.
        if (dialog.ShowDialog(this) != true || !IsLoaded)
        {
            return;
        }

        string fullPath = Path.GetFullPath(dialog.FileName);
        if (PlaintextTarget.Problem(fullPath, _dataFilePath) is { } problem)
        {
            Report(problem, danger: true);
            return;
        }

        if (!PlaintextTarget.ConfirmIfSynced(this, fullPath, _syncRoots) || !IsLoaded)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(fullPath, CsvExport.FileBytes(_entries));
            PlaintextTarget.RememberFolder(fullPath);
            Diag.Log($"Exported {_entries.Count} entries as CSV");
            Report($"Wrote {RangeText.Count(_entries.Count)} to {fullPath}. Delete it when you are done with it.", danger: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Log("CSV export failed", ex);
            Report("Could not write the file: " + ex.Message, danger: true);
        }
    }

    private void Report(string message, bool danger)
    {
        // A resource reference rather than the brush itself, so the text repaints if the theme changes while it shows.
        if (danger)
        {
            Copied.SetResourceReference(ForegroundProperty, "DangerBrush");
        }
        else
        {
            Copied.ClearValue(ForegroundProperty);
        }

        Copied.Text = message;
    }
}
