using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Logbook.App.Autocomplete;
using Logbook.App.Capture;
using Logbook.App.Diagnostics;
using Logbook.App.Export;
using Logbook.App.Help;
using Logbook.App.Hotkeys;
using Logbook.App.Interop;
using Logbook.App.Lock;
using Logbook.App.Main;
using Logbook.App.Settings;
using Logbook.App.Startup;
using Logbook.App.Tray;
using Logbook.Core;
using Logbook.Core.Export;
using Logbook.Core.Storage;

namespace Logbook.App;

/// <summary>
/// Startup and the tray lifetime: single instance, settings and first run, tray icon, hotkeys, lock state, and the
/// 30-second retry of pending writes (DESIGN.md sections 8, 9 and 12). Windows are created on demand and never own the process.
/// </summary>
public sealed partial class App : Application, IDisposable
{
    private const string StartupOfferMarker = "startup-offered";

    private SingleInstance? _instance;
    private MessageWindow? _messageWindow;
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private Timer? _retryTimer;
    private CaptureWindow? _capture;
    private MainWindow? _main;
    private Suggestions? _suggestions;
    private LogFile? _suggestionsFor;
    private Settings.Settings _settings = new();
    private string _root = "";
    private string _dataFilePath = "";
    private bool _firstRun;
    private bool _placeholderSeen;

    /// <summary>Null while locked. The data key lives inside for the life of the process.</summary>
    public LogFile? Log { get; private set; }

    public bool IsLocked => Log is null;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Diag.Prune();
        Diag.Log("Starting");
        DispatcherUnhandledException += OnUnhandledException;

        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            Diag.Log("Another instance is running; handing over and exiting");
            SingleInstance.NotifyFirstInstance();
            Shutdown();
            return;
        }

        if (!PrepareDataFile())
        {
            Shutdown();
            return;
        }

        _messageWindow = new MessageWindow();
        _messageWindow.AddHook(OnMessage);
        _hotkeys = new HotkeyManager(_messageWindow);
        _tray = new TrayIcon(_messageWindow.Handle);
        _tray.OpenRequested += ShowMainWindow;
        _tray.CaptureRequested += ShowCapture;
        _tray.SettingsRequested += ShowSettings;
        _tray.ExitRequested += Shutdown;
        _tray.BalloonClicked += ShowSettings;
        RegisterHotkeys();

        _retryTimer = new Timer(_ => RetryPending(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        if (_placeholderSeen)
        {
            _tray.ShowBalloon("Downloaded from sync", "The log was a cloud placeholder and had to be downloaded. Mark its folder \"Always keep on this device\" so this does not block a capture.", warning: false);
        }

        // The app starts locked and asks once at launch; if dismissed, the next hotkey asks again.
        // First run has already opened or created the file.
        if (Log is not null)
        {
            _tray.SetLocked(false);
        }
        else
        {
            EnsureUnlocked();
        }

        OfferStartupRegistration();
        if (_firstRun)
        {
            ShowCheatSheet();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Diag.Log("Exiting");
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        _retryTimer?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _messageWindow?.Dispose();
        _instance?.Dispose();
        _retryTimer = null;
        _hotkeys = null;
        _tray = null;
        _messageWindow = null;
        _instance = null;
    }

    /// <summary>Finds the settings and data file, running first-run setup when there are none. False means quit.</summary>
    private bool PrepareDataFile()
    {
        string? envVar = SettingsStore.FindConfiguredEnvVar();
        string? root = envVar is null ? null : SettingsStore.ResolveRoot(envVar);
        Settings.Settings? settings = root is null ? null : SettingsStore.Load(root);
        if (root is null || settings is null)
        {
            return RunFirstRun();
        }

        _root = root;
        _settings = settings;
        _dataFilePath = SettingsStore.DataFilePath(root, settings);

        // Several logbook*.jsonl files beside each other: ask which is real, never guess (DESIGN.md section 8).
        IReadOnlyList<string> siblings = SyncFile.Siblings(_dataFilePath);
        if (siblings.Count > 1)
        {
            PickFileWindow picker = new(siblings);
            if (picker.ShowDialog() != true || picker.ChosenPath is null)
            {
                return false;
            }

            if (!string.Equals(picker.ChosenPath, _dataFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _dataFilePath = picker.ChosenPath;
                _settings.DataFileRelativePath = Path.GetRelativePath(_root, _dataFilePath);
                SaveSettings();
            }
        }
        else if (!File.Exists(_dataFilePath))
        {
            // The configured file is gone: run the picker rather than create a new file silently (DESIGN.md section 16).
            Diag.Log($"Configured data file is missing: {_dataFilePath}");
            return RunFirstRun();
        }

        if (SyncFile.IsPlaceholder(_dataFilePath))
        {
            Diag.Log("The data file is a sync placeholder; reading it will download it");
            _placeholderSeen = true;
        }

        return true;
    }

    // DESIGN.md section 12, first run steps 1 to 3. Cancelling a later step returns to the first.
    private bool RunFirstRun()
    {
        while (true)
        {
            FirstRunWindow welcome = new();
            if (welcome.ShowDialog() != true)
            {
                return false;
            }

            LogFile opened;
            if (welcome.CreateNew)
            {
                NewPassphraseWindow passphrase = new(welcome.DataFilePath);
                if (passphrase.ShowDialog() != true)
                {
                    continue;
                }

                try
                {
                    opened = LogFile.Create(welcome.DataFilePath, passphrase.Passphrase, DateTimeOffset.Now);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Diag.Log("Could not create the data file", e);
                    MessageBox.Show("The file could not be created: " + e.Message, "Logbook", MessageBoxButton.OK, MessageBoxImage.Error);
                    continue;
                }
            }
            else
            {
                UnlockWindow unlock = new(welcome.DataFilePath);
                if (unlock.ShowDialog() != true || unlock.Opened is null)
                {
                    continue;
                }

                opened = unlock.Opened;
            }

            _root = welcome.Root;
            _settings = welcome.Settings;
            _dataFilePath = welcome.DataFilePath;
            Log = opened;
            _firstRun = true;
            SaveSettings();
            Diag.Log("First run complete");
            return true;
        }
    }

    // Offered once per Windows profile; a login change wipes the shortcut and the marker together (DESIGN.md section 12).
    private void OfferStartupRegistration()
    {
        if (StartupShortcut.IsRegistered() || LocalMarker.Exists(StartupOfferMarker))
        {
            return;
        }

        LocalMarker.Set(StartupOfferMarker);
        MessageBoxResult answer = MessageBox.Show(
            "Start Logbook when you sign in? A hotkey that is not running logs nothing. This adds a shortcut to your Startup folder; you can change it later in Settings.",
            "Logbook",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            StartupShortcut.Register();
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Diag.Log("Could not create the startup shortcut", e);
            _tray?.ShowBalloon("Startup shortcut not created", e.Message, warning: true);
        }
    }

    private void RegisterHotkeys()
    {
        RegisterHotkey(_settings.CaptureHotkey, "capture", ShowCapture);
        RegisterHotkey(_settings.MainWindowHotkey, "main window", ShowMainWindow);
    }

    // A silently dead hotkey means a week of nothing logged, so failure is surfaced at once (DESIGN.md section 12).
    private void RegisterHotkey(string text, string purpose, Action action)
    {
        if (!Hotkey.TryParse(text, out Hotkey hotkey))
        {
            Diag.Log($"The {purpose} hotkey '{text}' is not a valid chord");
            _tray?.ShowBalloon("Hotkey not set", $"'{text}' is not a valid {purpose} hotkey. Click to open Settings.", warning: true);
            return;
        }

        if (!_hotkeys!.TryRegister(hotkey, action))
        {
            Diag.Log($"Windows refused the {purpose} hotkey {hotkey}");
            _tray?.ShowBalloon("Hotkey in use", $"{hotkey} could not be registered for {purpose}; another app may own it. Click to open Settings.", warning: true);
        }
    }

    /// <summary>Prompts for the passphrase if locked. Returns true when the log is open afterwards.</summary>
    private bool EnsureUnlocked()
    {
        if (!IsLocked)
        {
            return true;
        }

        UnlockWindow prompt = new(_dataFilePath);
        if (prompt.ShowDialog() == true && prompt.Opened is { } opened)
        {
            Log = opened;
            _tray?.SetLocked(false);
            Diag.Log($"Unlocked; {opened.Live.Count} live entries, {opened.UnreadableLineCount} unreadable lines, trailing truncated: {opened.TrailingLineTruncated}");
            return true;
        }

        return false;
    }

    private void ShowCapture()
    {
        if (!EnsureUnlocked())
        {
            return;
        }

        if (_capture is { } open)
        {
            open.Activate();
            return;
        }

        _capture = new CaptureWindow(Monitors.WorkAreaAtCursor(_messageWindow!.TransformFromDevice), BuildSuggestions);
        _capture.Captured += entry => _ = SaveCapturedAsync(entry, _capture.WorkArea);
        _capture.Closed += (_, _) => _capture = null;
        _capture.Show();
        _capture.Activate();
    }

    // The write runs off the UI thread: it can retry for a while if the sync client holds the file.
    private async Task SaveCapturedAsync(Entry entry, Rect workArea)
    {
        LogFile log = Log!;
        bool saved = await Task.Run(() => log.Append(entry)).ConfigureAwait(true);
        _suggestions = null; // the new entry may introduce a tag or person
        if (_main is { IsVisible: true })
        {
            _main.Refresh();
        }

        if (saved)
        {
            ToastWindow toast = new(workArea, "Logged — click to edit", entry.Summary, danger: false, TimeSpan.FromSeconds(5));
            toast.Clicked += () => ShowMainWindow(entry.Id);
            toast.Show();
        }
        else
        {
            Diag.Log("Capture could not be written; it is pending");
            ToastWindow toast = new(workArea, "Not saved yet", $"{RangeText.Count(log.PendingCount)} waiting for the file. Retrying every 30 seconds.", danger: true, TimeSpan.FromSeconds(15));
            toast.Clicked += () => ShowMainWindow(entry.Id);
            toast.Show();
        }
    }

    private void ShowMainWindow() => ShowMainWindow(null);

    private void ShowMainWindow(string? selectId)
    {
        if (!EnsureUnlocked())
        {
            return;
        }

        if (_main is null)
        {
            _main = new MainWindow(Log!, BuildSuggestions);
            _main.SettingsRequested += ShowSettings;
            _main.CopyTimelineRequested += ShowExportPanel;
            _main.ExportDecryptedRequested += ShowExportDecrypted;
            _main.CheatSheetRequested += ShowCheatSheet;
        }
        else
        {
            _main.Refresh();
        }

        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }

        _main.Show();
        _main.Activate();
        if (selectId is not null)
        {
            _main.Select(selectId);
        }
    }

    private Suggestions BuildSuggestions()
    {
        // Rebuilt on demand; a few thousand entries make this trivial.
        if (_suggestionsFor != Log || _suggestions is null)
        {
            _suggestions = new Suggestions(Log?.Live ?? [], _settings.AutocompleteCap, _settings.AutocompleteRanking);
            _suggestionsFor = Log;
        }

        return _suggestions;
    }

    private Window? VisibleMain => _main is { IsVisible: true } ? _main : null;

    private void ShowExportPanel(string filterText, IReadOnlyList<Entry> entries)
    {
        ExportPanel panel = new(filterText, entries, _settings.Export, SaveSettings) { Owner = VisibleMain };
        panel.ShowDialog();
    }

    private void ShowExportDecrypted()
    {
        ExportDecryptedWindow window = new(Log!, _root, _settings, SaveSettings) { Owner = VisibleMain };
        window.ShowDialog();
    }

    private void ShowCheatSheet()
    {
        CheatSheetWindow sheet = new(_settings.CaptureHotkey, _settings.MainWindowHotkey) { Owner = VisibleMain };
        sheet.ShowDialog();
    }

    private void ShowSettings()
    {
        SettingsWindow window = new(_settings, SettingsStore.SettingsPath(_root), _dataFilePath) { Owner = VisibleMain };
        if (window.ShowDialog() != true)
        {
            return;
        }

        SaveSettings();
        _hotkeys?.UnregisterAll();
        RegisterHotkeys();
        _suggestions = null;
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(_root, _settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Could not save settings", e);
            _tray?.ShowBalloon("Settings not saved", "The settings file could not be written: " + e.Message, warning: true);
        }
    }

    private void RetryPending()
    {
        LogFile? log = Log;
        if (log is null || log.PendingCount == 0)
        {
            return;
        }

        bool drained = log.RetryPending();
        Diag.Log(drained ? "Pending writes flushed" : $"Pending writes still failing: {log.PendingCount} waiting");
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == SingleInstance.ShowMessage)
        {
            handled = true;
            Dispatcher.BeginInvoke(ShowMainWindow);
        }

        return IntPtr.Zero;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Diag.Log("Unhandled exception", e.Exception);
        MessageBox.Show("Something went wrong. Details were written to the diagnostics log:\n" + Diag.LogFolder, "Logbook", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        if (_tray is null)
        {
            // Startup never finished, so there is no tray icon to exit from; a silent zombie process would be worse.
            Shutdown();
        }
    }
}
