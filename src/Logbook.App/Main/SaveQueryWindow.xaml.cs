using System.Windows;

namespace Logbook.App.Main;

/// <summary>Asks for the name a filter is saved under (docs/main-window.md, saved queries).</summary>
public sealed partial class SaveQueryWindow : Window
{
    public SaveQueryWindow(string filter)
    {
        InitializeComponent();
        FilterLabel.Text = filter;

        // The filter itself is the obvious first name; it is selected so typing replaces it.
        NameBox.Text = filter;
        NameBox.SelectAll();
    }

    public string QueryName { get; private set; } = "";

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            Problem.Text = "Type a name for the button.";
            return;
        }

        QueryName = name;
        DialogResult = true;
    }
}
