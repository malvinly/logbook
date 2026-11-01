using System.Windows;
using System.Windows.Input;
using Logbook.App.Autocomplete;
using Logbook.App.Chips;
using Logbook.Core;
using Logbook.Core.Grammar;

namespace Logbook.App.Capture;

/// <summary>
/// The launcher-style capture box (DESIGN.md section 5). The line is parsed on every keystroke and the recognised
/// tokens show as chips, so what will be stored is visible before Enter. The window remembers nothing.
/// </summary>
public sealed partial class CaptureWindow : Window
{
    private readonly Rect _workArea;
    private readonly SuggestionPopup[] _popups;

    public CaptureWindow(Rect workArea, Func<Suggestions> suggestions)
    {
        InitializeComponent();
        _workArea = workArea;
        _popups = [SuggestionPopup.Attach(Line, suggestions), SuggestionPopup.Attach(ExtraTags, suggestions), SuggestionPopup.Attach(ExtraPeople, suggestions)];
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + workArea.Height * 0.3;
        Loaded += (_, _) => Line.Focus();
    }

    /// <summary>The work area the box was shown on, so the toast can land on the same monitor.</summary>
    public Rect WorkArea => _workArea;

    /// <summary>Raised once with the new entry when the user confirms. The window closes itself afterwards.</summary>
    public event Action<Entry>? Captured;

    private void OnLineChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Placeholder.Visibility = Line.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Chips.ItemsSource = Chip.For(LineParser.ParseCapture(Line.Text, DateTimeOffset.Now));
        Problem.Visibility = Visibility.Collapsed;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Preview events reach the window before the text box. While a suggestion list is open, Enter accepts a
        // name and Escape closes the list; the window must not submit or close on top of that.
        if (_popups.Any(p => p.IsOpen))
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ToggleMore();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && ReferenceEquals(e.OriginalSource, Line))
        {
            e.Handled = true;
            Submit();
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e) => ToggleMore();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnLogClick(object sender, RoutedEventArgs e) => Submit();

    private void ToggleMore()
    {
        bool open = More.Visibility != Visibility.Visible;
        More.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.Content = open ? "Less  ˄" : "More  ˅";
        if (open)
        {
            Reason.Focus();
        }
        else
        {
            Line.Focus();
        }
    }

    private void Submit()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        CaptureLine line = LineParser.ParseCapture(Line.Text, now);
        if (line.Summary.Length == 0)
        {
            Problem.Text = "Type what happened; tags and people alone are not an entry.";
            Problem.Visibility = Visibility.Visible;
            Line.Focus();
            return;
        }

        Entry entry = new(
            Ulid.Generate(now),
            1,
            line.OccurredAt ?? now,
            now,
            line.Summary,
            BlankToNull(Reason.Text),
            TokenNormalizer.NormalizeList(ExtraTags.Text, existing: line.Tags),
            TokenNormalizer.NormalizeList(ExtraPeople.Text, existing: line.People),
            Lines(Refs.Text),
            false);

        Captured?.Invoke(entry);
        Close();
    }

    private static string? BlankToNull(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static IReadOnlyList<string> Lines(string text)
        => [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
