using System.Windows;

namespace Logbook.App.Startup;

/// <summary>First-run step 2 (DESIGN.md section 12): the passphrase twice and the no-recovery warning.</summary>
public sealed partial class NewPassphraseWindow : Window
{
    public NewPassphraseWindow(string path)
    {
        InitializeComponent();
        FileLabel.Text = path;
    }

    public string Passphrase { get; private set; } = "";

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (First.Password.Length == 0)
        {
            Problem.Text = "Type a passphrase.";
            return;
        }

        if (First.Password != Second.Password)
        {
            Problem.Text = "The two passphrases differ.";
            Second.Clear();
            Second.Focus();
            return;
        }

        if (Understood.IsChecked != true)
        {
            Problem.Text = "Confirm that you have written the passphrase down.";
            return;
        }

        Passphrase = First.Password;
        DialogResult = true;
    }
}
