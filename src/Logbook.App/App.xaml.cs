using System.IO;
using System.Windows;
using System.Windows.Threading;
using Logbook.App.Capture;
using Logbook.App.Diagnostics;
using Logbook.App.Hotkeys;
using Logbook.App.Interop;
using Logbook.App.Lock;
using Logbook.App.Tray;
using Logbook.Core;
using Logbook.Core.Export;
using Logbook.Core.Storage;

namespace Logbook.App;

/// <summary>
/// Startup and the tray lifetime: single instance, settings, tray icon, hotkeys, lock state, and the 30-second
/// retry of pending writes (DESIGN.md sections 8, 9 and 12). Windows are created on demand and never own the process.
/// </summary>
public sealed partial class App : Application, IDisposable
{
    private SingleInstance? _instance;
    private MessageWindow? _messageWindow;
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private Timer? _retryTimer;
    private CaptureWindow? _capture;
    private Settings.Settings _settings = new();
    private string _root = "";
    private string _dataFilePath = "";

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

        if (!LoadSettings())
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

        // The app starts locked and asks once at launch; if dismissed, the next hotkey asks again.
        EnsureUnlocked();
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

    private bool LoadSettings()
    {
        // Until the first-run flow exists (build step 9), a missing settings or data file is reported and the app exits.
        string? envVar = Settings.SettingsStore.FindConfiguredEnvVar();
        string? root = envVar is null ? null : Settings.SettingsStore.ResolveRoot(envVar);
        Settings.Settings? settings = root is null ? null : Settings.SettingsStore.Load(root);
        if (root is null || settings is null)
        {
            MessageBox.Show("No logbook.settings.json was found in a synced folder. First-run setup is not built yet.", "Logbook", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        _root = root;
        _settings = settings;
        _dataFilePath = Settings.SettingsStore.DataFilePath(root, settings);
        if (!File.Exists(_dataFilePath))
        {
            MessageBox.Show($"The data file was not found:\n{_dataFilePath}", "Logbook", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
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

        _capture = new CaptureWindow(Monitors.WorkAreaAtCursor(_messageWindow!.TransformFromDevice));
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

        // Built in step 7.
        Diag.Log($"Main window requested, select {selectId ?? "nothing"}");
    }

    private void ShowSettings()
    {
        // Built in step 9.
        Diag.Log("Settings requested");
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
    }
}
