using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Logbook.App.Autocomplete;
using Logbook.App.Diagnostics;
using Logbook.Core;
using Logbook.Core.Export;
using Logbook.Core.Grammar;
using Logbook.Core.Query;
using Logbook.Core.Storage;

namespace Logbook.App.Main;

/// <summary>
/// Filter bar, grouped results, detail pane with revisions, and the export entry points (DESIGN.md section 10).
/// The window reads the live set from the log on every refresh; the log is the only state.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly LogFile _log;
    private readonly Func<Suggestions> _suggestions;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private IReadOnlyList<Entry> _filtered = [];
    private EntryEditor? _editor;
    private bool _updatingRange;

    public MainWindow(LogFile log, Func<Suggestions> suggestions)
    {
        InitializeComponent();
        _log = log;
        _suggestions = suggestions;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
        SuggestionPopup.Attach(Filter, suggestions);
        SuggestionPopup.Attach(TagsBox, suggestions);
        SuggestionPopup.Attach(PeopleBox, suggestions);
        Loaded += (_, _) => Filter.Focus();
        Refresh();
    }

    /// <summary>Raised when the user asks for the settings window.</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the user asks to copy the current set as a timeline; the app owns the export panel.</summary>
    public event Action<string, IReadOnlyList<Entry>>? CopyTimelineRequested;

    /// <summary>Raised for Tools → Export decrypted.</summary>
    public event Action? ExportDecryptedRequested;

    /// <summary>Raised for Help → Grammar cheat sheet.</summary>
    public event Action? CheatSheetRequested;

    /// <summary>Raised after an edit or delete was appended, so caches built from the live set can be dropped.</summary>
    public event Action? Changed;

    /// <summary>Re-runs the filter against the log, keeping the selection where possible.</summary>
    public void Refresh()
    {
        string? selectedId = (Results.SelectedItem as EntryRow)?.Id;
        FilterPlaceholder.Visibility = Filter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        QueryLine query = LineParser.ParseQuery(Filter.Text, DateOnly.FromDateTime(DateTime.Now));
        SyncRangePickers(query);
        _filtered = EntryQuery.Filter(_log.Live, query);

        ListCollectionView view = new(_filtered.Select(e => new EntryRow(e)).ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EntryRow.DayHeader)));
        Results.ItemsSource = view;

        // "How many times" is a driving question, so the count is always in view (DESIGN.md section 10).
        string range = RangeText.Range(_filtered);
        Header.Text = range.Length == 0 ? RangeText.Count(_filtered.Count) : $"{RangeText.Count(_filtered.Count)} · {range}";
        ShowWarnings();

        // Keep the same entry selected if the new filter still shows it; otherwise the pane simply closes.
        Results.SelectedItem = selectedId is null ? null : RowFor(selectedId);
    }

    /// <summary>Selects the entry with this id, clearing the filter if the filter hides it (the toast's click).</summary>
    public void Select(string id)
    {
        EntryRow? row = RowFor(id);
        if (row is null && Filter.Text.Length > 0)
        {
            Filter.Text = "";
            Refresh();
            row = RowFor(id);
        }

        Results.SelectedItem = row;
        if (row is not null)
        {
            Results.ScrollIntoView(row);
        }
    }

    private EntryRow? RowFor(string id) => Results.Items.OfType<EntryRow>().FirstOrDefault(r => r.Id == id);

    private void ShowWarnings()
    {
        List<string> notes = [];
        if (_log.PendingCount > 0)
        {
            notes.Add(FormattableString.Invariant($"{_log.PendingCount} unsaved, retrying"));
        }

        if (_log.UnreadableLineCount > 0)
        {
            notes.Add(FormattableString.Invariant($"{_log.UnreadableLineCount} unreadable lines in the file"));
        }

        if (_log.TrailingLineTruncated)
        {
            notes.Add("last line of the file was cut off");
        }

        Warnings.Text = string.Join(" · ", notes);
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    // The pickers mirror the since:/until: tokens; changing a picker rewrites the tokens in the text, not a hidden filter.
    private void SyncRangePickers(QueryLine query)
    {
        _updatingRange = true;
        try
        {
            FromDate.SelectedDate = query.Since?.ToDateTime(TimeOnly.MinValue);
            ToDate.SelectedDate = query.Until?.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            _updatingRange = false;
        }
    }

    private void OnRangeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingRange)
        {
            return;
        }

        // The grammar does the rewrite, so phrases survive and a last: token is replaced by the fixed date it stood for.
        DateOnly? since = FromDate.SelectedDate is { } from ? DateOnly.FromDateTime(from) : null;
        DateOnly? until = ToDate.SelectedDate is { } to ? DateOnly.FromDateTime(to) : null;
        Filter.Text = LineParser.WithDateRange(Filter.Text, since, until);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem is not EntryRow row)
        {
            _editor = null;
            Detail.DataContext = null;
            Detail.Visibility = Visibility.Collapsed;
            NoSelection.Visibility = Visibility.Visible;
            return;
        }

        // Every revision but the live one is history; the one just before the live one is "previous" for highlighting.
        List<Entry> history = [.. _log.RevisionsOf(row.Id).Where(r => !ReferenceEquals(r, row.Entry))];
        Entry? previous = history.Count > 0 ? history[^1] : null;
        _editor = new EntryEditor(row.Entry, previous);
        Detail.DataContext = _editor;
        Revisions.ItemsSource = history.AsEnumerable().Reverse().Select(r => new RevisionRow(r)).ToList();
        NoRevisions.Visibility = history.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        Detail.Visibility = Visibility.Visible;
        NoSelection.Visibility = Visibility.Collapsed;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_editor?.ToRevision(DateTimeOffset.Now) is not { } revision)
        {
            return;
        }

        await AppendAsync(revision).ConfigureAwait(true);
        Refresh();
        Select(revision.Id);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_editor is null)
        {
            return;
        }

        MessageBoxResult answer = MessageBox.Show(this, "Delete this entry? It stays in the file's history but disappears from every list and export.", "Delete entry", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await AppendAsync(_editor.ToTombstone(DateTimeOffset.Now)).ConfigureAwait(true);
        Results.SelectedItem = null;
        Refresh();
    }

    // The buttons are disabled while the write runs so a second click cannot append a second revision with the same number.
    private async Task AppendAsync(Entry revision)
    {
        LogFile log = _log;
        SaveButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        bool saved;
        try
        {
            saved = await Task.Run(() => log.Append(revision)).ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Diag.Log("Revision write failed unexpectedly", e);
            saved = false;
        }
        finally
        {
            SaveButton.IsEnabled = true;
            DeleteButton.IsEnabled = true;
        }

        Changed?.Invoke();
        if (!saved)
        {
            Diag.Log("Revision could not be written; it is pending");
            MessageBox.Show(this, "The change is kept in memory but could not be written to the file yet. It will be retried every 30 seconds.", "Not saved yet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCopyTimelineClick(object sender, RoutedEventArgs e) => CopyTimelineRequested?.Invoke(Filter.Text, _filtered);

    private void OnExportDecryptedClick(object sender, RoutedEventArgs e) => ExportDecryptedRequested?.Invoke();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnCheatSheetClick(object sender, RoutedEventArgs e) => CheatSheetRequested?.Invoke();

    private void OnDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Diag.LogFolder) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            Diag.Log("Could not open the diagnostics folder", ex);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The tray app lives on; the window just hides so it reopens instantly with its filter intact.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
