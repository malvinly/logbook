using System.Windows;

namespace Logbook.App.Help;

/// <summary>The hotkeys and grammar on one page, shown once at first run and from Help (DESIGN.md section 12).</summary>
public sealed partial class CheatSheetWindow : Window
{
    public CheatSheetWindow(string captureHotkey, string mainWindowHotkey)
    {
        InitializeComponent();
        Hotkeys.Text = $"{captureHotkey,-18} new entry (Enter logs it, Esc cancels, Ctrl+Enter for more)\n{mainWindowHotkey,-18} open the main window";
    }
}
