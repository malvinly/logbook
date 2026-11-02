using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using Logbook.App.Diagnostics;
using Logbook.Core.Storage;

namespace Logbook.App.Lock;

/// <summary>
/// Tools → Change passphrase (DESIGN.md section 8): the current passphrase, the new one twice, and the no-recovery
/// warning again. The change re-wraps the same data key in line 1 only, so the open log stays open and unlocked.
/// Key derivation takes a noticeable moment, so the change runs on a worker and the window shows a busy state.
/// Nothing typed here is logged; the boxes are cleared once the passphrases have been handed over.
/// </summary>
public sealed partial class ChangePassphraseWindow : Window
{
    // LogFile.ChangePassphrase throws CryptographicException both for a wrong current passphrase and, with exactly this
    // message, when the new header fails its in-memory check; only the message tells them apart. Keep it in step with Core.
    private const string NewHeaderDidNotVerify = "The new header did not verify. The passphrase is unchanged.";

    private readonly LogFile _log;
    private readonly Func<bool> _writeInProgress;

    /// <param name="log">The open log; its data key stays the same.</param>
    /// <param name="path">The data file, shown under the title.</param>
    /// <param name="writeInProgress">True while a capture is pending or being written; the change must not race it.</param>
    public ChangePassphraseWindow(LogFile log, string path, Func<bool> writeInProgress)
    {
        InitializeComponent();
        _log = log;
        _writeInProgress = writeInProgress;
        FileLabel.Text = path;
    }

    /// <summary>True while the header is being rewritten; locking waits for it.</summary>
    public bool IsChanging { get; private set; }

    /// <summary>
    /// True once an attempt has run and its outcome is on screen (success closes the window, so in practice a failure
    /// such as a wrong current passphrase). Locking waits for it, so the user learns which passphrase the log now has.
    /// </summary>
    public bool HasOutcome { get; private set; }

    private void OnChangeClick(object sender, RoutedEventArgs e) => _ = TryChangeAsync();

    private async Task TryChangeAsync()
    {
        if (IsChanging)
        {
            return;
        }

        if (Current.Password.Length == 0)
        {
            ShowProblem("Type the current passphrase.");
            Current.Focus();
            return;
        }

        if (First.Password.Length == 0)
        {
            ShowProblem("Type a new passphrase.");
            First.Focus();
            return;
        }

        if (First.Password != Second.Password)
        {
            ShowProblem("The two new passphrases differ.");
            Second.Clear();
            Second.Focus();
            return;
        }

        if (Understood.IsChecked != true)
        {
            ShowProblem("Confirm that you have written the new passphrase down.");
            return;
        }

        if (_writeInProgress())
        {
            ShowProblem("An entry is still being written. Try again once the tray icon no longer shows unsaved entries.");
            return;
        }

        string current = Current.Password;
        string next = First.Password;
        bool changed = false;
        SetBusy(true);
        Problem.Text = "Changing…";
        Problem.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        try
        {
            await Task.Run(() => _log.ChangePassphrase(current, next)).ConfigureAwait(true);
            Diag.Log("Passphrase changed; the header was rewritten in place");
            changed = true;
        }
        catch (CryptographicException ex) when (ex.Message == NewHeaderDidNotVerify)
        {
            // Not the user's typing: the header built for the new passphrase failed its own check, and nothing was written.
            Diag.Log("The new header did not verify", ex);
            ShowProblem("Unexpected error; see the diagnostics log. " + ex.Message);
        }
        catch (CryptographicException)
        {
            // Every outcome goes to the diagnostics log (DESIGN.md section 9), never what was typed.
            Diag.Log("Passphrase change refused: the current passphrase is wrong; nothing was changed");
            ShowProblem("The current passphrase is wrong. Nothing was changed.");
            Current.Clear();
            Current.Focus();
        }
        catch (InvalidOperationException)
        {
            Diag.Log("Passphrase change refused: an entry is still waiting to be written; nothing was changed");
            ShowProblem("An entry is still waiting to be written. Try again once the tray icon no longer shows unsaved entries.");
        }
        catch (InvalidDataException ex)
        {
            Diag.Log("Refused to change the passphrase", ex);
            ShowProblem(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Could not change the passphrase", ex);
            ShowProblem("Could not write the file: " + ex.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The task is not awaited by anyone else, so an unexpected failure must land here or vanish.
            Diag.Log("Unexpected failure while changing the passphrase", ex);
            ShowProblem("Unexpected error; see the diagnostics log. " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            HasOutcome = true;
        }

        if (changed)
        {
            DialogResult = true; // after SetBusy(false), since closing is refused while busy
        }
    }

    // Cancel and the close button are refused while the header is being written, so the result is never lost.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (IsChanging)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Current.Clear();
        First.Clear();
        Second.Clear();
        base.OnClosed(e);
    }

    private void ShowProblem(string message)
    {
        Problem.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        Problem.Text = message;
    }

    private void SetBusy(bool busy)
    {
        IsChanging = busy;
        Current.IsEnabled = !busy;
        First.IsEnabled = !busy;
        Second.IsEnabled = !busy;
        ChangeButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : null;
    }
}
