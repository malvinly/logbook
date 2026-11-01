using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Logbook.App.Diagnostics;
using Logbook.App.Hotkeys;
using Logbook.App.Startup;

namespace Logbook.App.Settings;

/// <summary>Edits the settings file in place (DESIGN.md section 12). The app re-registers hotkeys after a save.</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly Settings _settings;

    public SettingsWindow(Settings settings, string settingsPath, string dataFilePath)
    {
        InitializeComponent();
        _settings = settings;
        CaptureHotkey.Text = settings.CaptureHotkey;
        MainWindowHotkey.Text = settings.MainWindowHotkey;
        AutocompleteCap.Text = settings.AutocompleteCap.ToString(CultureInfo.InvariantCulture);
        AutocompleteRanking.SelectedIndex = string.Equals(settings.AutocompleteRanking, "frequent", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        StartWithWindows.IsChecked = StartupShortcut.IsRegistered();
        FileInfo.Text = $"Data file: {dataFilePath}\nSettings: {settingsPath}\nDiagnostics: {Diag.LogFolder}";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!Hotkey.TryParse(CaptureHotkey.Text, out Hotkey capture))
        {
            Problem.Text = "The new-entry hotkey needs at least one modifier and a key, like Ctrl+Alt+L.";
            return;
        }

        if (!Hotkey.TryParse(MainWindowHotkey.Text, out Hotkey main))
        {
            Problem.Text = "The main-window hotkey needs at least one modifier and a key, like Ctrl+Alt+Shift+L.";
            return;
        }

        if (capture == main)
        {
            Problem.Text = "The two hotkeys must differ.";
            return;
        }

        if (!int.TryParse(AutocompleteCap.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int cap) || cap < 1 || cap > 50)
        {
            Problem.Text = "Suggestions shown must be a number from 1 to 50.";
            return;
        }

        try
        {
            if (StartWithWindows.IsChecked == true)
            {
                StartupShortcut.Register();
            }
            else
            {
                StartupShortcut.Unregister();
            }
        }
        catch (Exception ex) when (ex is COMException or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Diag.Log("Could not change the startup shortcut", ex);
            Problem.Text = "Could not change the Startup shortcut: " + ex.Message;
            return;
        }

        _settings.CaptureHotkey = capture.ToString();
        _settings.MainWindowHotkey = main.ToString();
        _settings.AutocompleteCap = cap;
        _settings.AutocompleteRanking = (string)((ComboBoxItem)AutocompleteRanking.SelectedItem).Tag;
        DialogResult = true;
    }
}
