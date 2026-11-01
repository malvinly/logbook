using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using Logbook.App.Diagnostics;
using Logbook.Core.Storage;

namespace Logbook.App.Lock;

/// <summary>
/// The passphrase prompt (DESIGN.md section 9). Key derivation takes a noticeable moment, so the file is opened
/// on a worker and the window shows a busy state rather than freezing. A wrong passphrase keeps the window open.
/// </summary>
public sealed partial class UnlockWindow : Window
{
    private readonly string _path;
    private bool _busy;

    public UnlockWindow(string path)
    {
        InitializeComponent();
        _path = path;
        FileLabel.Text = path;
    }

    /// <summary>The opened file when the dialog result is true.</summary>
    public LogFile? Opened { get; private set; }

    private void OnPassphraseKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = TryUnlockAsync();
        }
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e) => _ = TryUnlockAsync();

    private async Task TryUnlockAsync()
    {
        if (_busy)
        {
            return;
        }

        string passphrase = Passphrase.Password;
        if (passphrase.Length == 0)
        {
            Status.Text = "Type the passphrase first.";
            return;
        }

        SetBusy(true);
        Status.Text = "Checking…";
        Status.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        try
        {
            Opened = await Task.Run(() => LogFile.Open(_path, passphrase)).ConfigureAwait(true);
            DialogResult = true;
        }
        catch (CryptographicException)
        {
            ShowError("Wrong passphrase. There is no recovery if it is lost.");
            Passphrase.Clear();
            Passphrase.Focus();
        }
        catch (InvalidDataException e)
        {
            Diag.Log("Refused to open the data file", e);
            ShowError("This is not a Logbook file, or it is from a newer version: " + e.Message);
        }
        catch (IOException e)
        {
            Diag.Log("Could not read the data file", e);
            ShowError("Could not read the file: " + e.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowError(string message)
    {
        Status.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        Status.Text = message;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Passphrase.IsEnabled = !busy;
        UnlockButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : null;
    }
}
