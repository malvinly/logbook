using System.Windows;

namespace Logbook.App.Dialogs;

/// <summary>What a dialog is about, which picks its glyph and the glyph's color.</summary>
public enum DialogTone
{
    Information,
    Question,
    Warning,
    Error,
}

/// <summary>
/// The app's replacement for <see cref="MessageBox"/>, so a message looks like the rest of the app instead of falling back
/// to the Windows box. Two uses only: tell the user something, or ask a yes/no question. Enter presses the default button
/// and Escape always answers no, so a stray key can never confirm something destructive the user did not choose.
/// </summary>
public sealed partial class AppDialog : Window
{
    private AppDialog(Window? owner, string heading, string message, DialogTone tone)
    {
        InitializeComponent();
        Heading.Text = heading;
        Message.Text = message;
        (string glyph, string brush) = tone switch
        {
            DialogTone.Question => ("\uE9CE", "AccentBrush"),
            DialogTone.Warning => ("\uE7BA", "CautionBrush"),
            DialogTone.Error => ("\uEA39", "DangerBrush"),
            _ => ("\uE946", "AccentBrush"),
        };
        Glyph.Text = glyph;
        Glyph.SetResourceReference(ForegroundProperty, brush);

        // From the tray there is no window to sit on: stay on top and in the taskbar so the question cannot get lost.
        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ShowInTaskbar = true;
        }
    }

    /// <summary>A message with one button. Enter and Escape both close it.</summary>
    public static void Inform(Window? owner, string heading, string message, DialogTone tone)
    {
        AppDialog dialog = new(owner, heading, message, tone);
        dialog.Arrange(dialog.PrimaryButton, "OK", isDefault: true);
        dialog.PrimaryButton.IsCancel = true;
        dialog.SecondaryButton.Visibility = Visibility.Collapsed;
        dialog.ShowDialog();
    }

    /// <summary>
    /// A yes/no question. True only when the user chose <paramref name="confirm"/>. The default button (the one Enter
    /// presses) is the confirm button only when <paramref name="confirmIsDefault"/> is set; Escape always cancels.
    /// </summary>
    public static bool Confirm(Window? owner, string heading, string message, string confirm, string cancel, DialogTone tone, bool confirmIsDefault)
    {
        AppDialog dialog = new(owner, heading, message, tone);
        dialog.Arrange(dialog.PrimaryButton, confirm, isDefault: confirmIsDefault);
        dialog.Arrange(dialog.SecondaryButton, cancel, isDefault: !confirmIsDefault);
        return dialog.ShowDialog() == true;
    }

    // The default button is the accent one and has the focus, as in a Windows 11 dialog.
    private void Arrange(System.Windows.Controls.Button button, string text, bool isDefault)
    {
        button.Content = text;
        button.IsDefault = isDefault;
        button.Style = (Style)FindResource(isDefault ? "AccentButton" : "FooterButton");
        if (isDefault)
        {
            Loaded += (_, _) => button.Focus();
        }
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
