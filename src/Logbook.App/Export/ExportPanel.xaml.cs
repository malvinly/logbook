using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Logbook.App.Diagnostics;
using Logbook.App.Settings;
using Logbook.Core;
using Logbook.Core.Export;

namespace Logbook.App.Export;

/// <summary>
/// The "Copy as timeline" panel (DESIGN.md section 11): the three toggles, a preview, and one button that puts the
/// plain text on the clipboard. Toggles are read from and written back to settings so they are remembered.
/// </summary>
public sealed partial class ExportPanel : Window
{
    private readonly string _filterText;
    private readonly IReadOnlyList<Entry> _entries;
    private readonly ExportSettings _settings;
    private readonly Action _saveSettings;
    private bool _loading = true;

    public ExportPanel(string filterText, IReadOnlyList<Entry> entries, ExportSettings settings, Action saveSettings)
    {
        InitializeComponent();
        _filterText = filterText;
        _entries = entries;
        _settings = settings;
        _saveSettings = saveSettings;
        ShowGaps.IsChecked = settings.ShowGaps;
        GroupByMonth.IsChecked = settings.GroupByMonth;
        Compact.IsChecked = settings.Compact;
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
        _saveSettings();
        Render();
    }

    private void Render()
    {
        ExportOptions options = new(_settings.ShowGaps, _settings.GroupByMonth, _settings.Compact);
        Preview.Text = TimelineExport.Render(_filterText, _entries, options);
        Copied.Text = "";
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        // Windows text on the clipboard is conventionally CRLF; the renderer itself stays LF for portability.
        DataObject data = new();
        data.SetText(Preview.Text.Replace("\n", "\r\n", StringComparison.Ordinal), TextDataFormat.UnicodeText);

        // Ask clipboard history and cross-device clipboard sync to leave this out: it is decrypted entry text.
        byte[] no = [0, 0, 0, 0];
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(no));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(no));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(no));

        try
        {
            Clipboard.SetDataObject(data, copy: true);
            Copied.Text = $"Copied {RangeText.Count(_entries.Count)}.";
        }
        catch (COMException ex)
        {
            // Another process holds the clipboard open; common with clipboard managers and remote desktops.
            Diag.Log("Clipboard was busy", ex);
            Copied.Text = "The clipboard is busy; try again in a moment.";
        }
    }
}
