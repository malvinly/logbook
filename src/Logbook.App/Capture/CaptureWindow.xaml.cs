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

    public CaptureWindow(Rect workArea, Func<Suggestions> suggestions)
    {
        InitializeComponent();
        _workArea = workArea;
        SuggestionPopup.Attach(Line, suggestions);
        SuggestionPopup.Attach(ExtraTags, suggestions);
        SuggestionPopup.Attach(ExtraPeople, suggestions);
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
            Merge(line.Tags, ExtraTags.Text),
            Merge(line.People, ExtraPeople.Text),
            Lines(Refs.Text),
            false);

        Captured?.Invoke(entry);
        Close();
    }

    private static string? BlankToNull(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    /// <summary>Parsed names plus the extra box, normalised the same way and without duplicates.</summary>
    private static List<string> Merge(IReadOnlyList<string> parsed, string extra)
    {
        List<string> names = [.. parsed];
        foreach (string raw in extra.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = TokenNormalizer.Normalize(raw.TrimStart('#', '@'));
            if (name.Length > 0 && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyList<string> Lines(string text)
        => [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
