using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Logbook.App.Autocomplete;
using Logbook.App.Capture;
using Logbook.App.Diagnostics;
using Logbook.App.Dialogs;
using Logbook.App.Export;
using Logbook.App.Help;
using Logbook.App.Hotkeys;
using Logbook.App.Interop;
using Logbook.App.Lock;
using Logbook.App.Main;
using Logbook.App.Settings;
using Logbook.App.Startup;
using Logbook.App.Theme;
using Logbook.App.Tray;
using Logbook.Core;
using Logbook.Core.Export;
using Logbook.Core.Locking;
using Logbook.Core.Query;
using Logbook.Core.Storage;

namespace Logbook.App;

/// <summary>
/// Startup and the tray lifetime: single instance, settings and first run, tray icon, hotkeys, lock state, and the
/// 30-second retry of pending writes, the reminder check and idle auto-lock (DESIGN.md sections 8, 9, 10 and 12). Windows are created on demand and never own the process.
/// </summary>
public sealed partial class App : Application, IDisposable
{
    private const string StartupOfferMarker = "startup-offered";

    private SingleInstance? _instance;
    private MessageWindow? _messageWindow;
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private Timer? _retryTimer;
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly HashSet<Reminders.Shown> _remindersShown = []; // since the last unlock; dropped on lock with the log
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMinutes(1) }; // runs only while unlocked

    // Runs only while a workstation lock or sign-out found a reason to wait (LockWaitReason); takes that lock once none is
    // left, or is cancelled if the session unlocks first.
    private readonly DispatcherTimer _postponedLockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CaptureWindow? _capture;
    private MainWindow? _main;
    private UnlockWindow? _unlockPrompt;
    private SettingsWindow? _settingsWindow;
    private Suggestions? _suggestions;
    private LogFile? _suggestionsFor;
    private Settings.Settings _settings = new();
    private string _root = "";
    private string _dataFilePath = "";
    private bool _firstRun;
    private bool _placeholderSeen;
    private bool _exiting;
    private int _writesInFlight; // captures handed to a worker and not yet back; UI thread only
    private bool _passphraseNoticeOpen; // the "Passphrase changed" notice is on screen; locking waits for it
    private bool _sessionLocked; // Windows locked the session and has not reported an unlock since
    private bool _retrySessionNotifications; // registering at startup failed; retried once at the first unlock

    /// <summary>Null while locked. The data key lives inside until Lock now, the idle lock or exit.</summary>
    public LogFile? Log { get; private set; }

    public bool IsLocked => Log is null;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppTheme.FollowWindows(this);
        UpdateWindowIcon(); // before any window, the first-run ones included
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

        // Before the unlock prompt, so every window opens in the chosen theme; following Windows this changes nothing.
        AppTheme.Apply(this, _settings.Theme);
        UpdateWindowIcon();

        _messageWindow = new MessageWindow();
        _messageWindow.AddHook(OnMessage);
        if (_messageWindow.RegisterForSessionNotifications())
        {
            Diag.Log("Registered for session lock and sign-out notifications");
        }
        else
        {
            Diag.Log($"Could not register for session notifications (error {Marshal.GetLastWin32Error()}); a workstation lock will not lock the log");
            _retrySessionNotifications = true;
        }

        _hotkeys = new HotkeyManager(_messageWindow);
        _tray = new TrayIcon(_messageWindow.Handle, ThemeColor("AccentBrush"), ThemeColor("TrayLockedBrush"), ThemeColor("TrayPendingBrush"));
        _tray.OpenRequested += ShowMainWindow;
        _tray.CaptureRequested += ShowCapture;
        _tray.SettingsRequested += ShowSettings;
        _tray.LockRequested += LockNow;
        _tray.ExitRequested += RequestExit;
        RegisterHotkeys();

        _retryTimer = new Timer(_ => RetryPending(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        _reminderTimer.Tick += (_, _) => ShowDueReminder();
        _idleTimer.Tick += (_, _) => LockIfIdle();
        _postponedLockTimer.Tick += (_, _) => TakePostponedLock();

        if (_placeholderSeen)
        {
            _tray.ShowBalloon("Downloaded from sync", "The log was only a cloud placeholder until now. Mark its folder \"Always keep on this device\" so a download never delays a capture.", warning: false);
        }

        // The app starts locked and asks once at launch; if dismissed, the next hotkey asks again.
        // First run has already opened or created the file.
        if (Log is not null)
        {
            _tray.SetLocked(false);
            StartReminders();
            _idleTimer.Start();
            RetrySessionNotifications();
        }
        else
        {
            EnsureUnlocked();
        }

        // Exit was chosen from the tray while the unlock prompt was up, so the startup offer is not made while the app
        // shuts down; it comes back next launch.
        if (_exiting)
        {
            return;
        }

        OfferStartupRegistration();
        if (_firstRun)
        {
            ShowCheatSheet();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // A sign-out or shutdown would take the in-memory queue with it; one more try, then refuse rather than lose it.
        // A write still running would be killed halfway, so that is refused too; it ends within seconds.
        if (WriteInFlight())
        {
            e.Cancel = true;
            Diag.Log("Refused session end while a write was in flight");
            AppDialog.Inform(null, "Still writing", "Logbook is writing to the file right now. Wait a moment, then sign out or shut down again.", DialogTone.Warning);
            base.OnSessionEnding(e);
            return;
        }

        int pending = FlushPendingNow();
        if (pending > 0)
        {
            e.Cancel = true;
            Diag.Log($"Refused session end with {pending} pending");
            AppDialog.Inform(null, "Entries not saved yet", $"Logbook still has {RangeText.Count(pending)} that could not be written to the file. Fix the file (is the sync client holding it?) and try again, or exit Logbook from the tray and accept the loss.", DialogTone.Warning);
        }

        base.OnSessionEnding(e);
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

    /// <summary>
    /// Every window's XAML sets <c>Icon="{DynamicResource WindowIcon}"</c> (DESIGN.md section 16), so replacing this
    /// resource gives open windows the new icon at once, as a brush change repaints them. Drawn in the tray's unlocked
    /// color, which comes from the theme, so it is redrawn whenever the theme is applied.
    /// </summary>
    private void UpdateWindowIcon()
    {
        using System.Drawing.Icon icon = TrayIcon.DrawWindowIcon(ThemeColor("AccentBrush"));

        // The bitmap copies the icon's pixels, so the icon can go; frozen because it is shared by every window.
        BitmapSource image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        Resources["WindowIcon"] = image;
    }

    // The tray icon and the window icon are drawn from theme colors, not brushes, so a theme change redraws them.
    private void RecolorIcons()
    {
        _tray?.Recolor(ThemeColor("AccentBrush"), ThemeColor("TrayLockedBrush"), ThemeColor("TrayPendingBrush"));
        UpdateWindowIcon();
    }

    private System.Drawing.Color ThemeColor(string brushKey)
    {
        System.Windows.Media.Color c = ((System.Windows.Media.SolidColorBrush)FindResource(brushKey)).Color;
        return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    // Tray → Exit. A write still running is waited for, not killed halfway; unsaved captures get one more write and
    // then a question, and the answer must be explicit. The answer covers only the entries it named: a capture made
    // while the question was open and not written either asks again.
    private void RequestExit()
    {
        if (_exiting)
        {
            return;
        }

        int accepted = 0; // how many unsaved entries the user has agreed to lose
        while (true)
        {
            if (WriteInFlight())
            {
                AppDialog.Inform(VisibleMain, "Still writing", "Logbook is writing to the file right now. Wait a moment, then exit again.", DialogTone.Warning);
                return;
            }

            int pending = FlushPendingNow();
            if (pending <= accepted)
            {
                break;
            }

            bool exitAnyway = AppDialog.Confirm(
                null,
                "Unsaved entries",
                $"{RangeText.Count(pending)} could not be written to the file and will be lost if Logbook exits now. Exit anyway?",
                "Exit anyway",
                "Keep running",
                DialogTone.Warning,
                confirmIsDefault: false);
            if (!exitAnyway)
            {
                return;
            }

            accepted = pending;
        }

        if (accepted > 0)
        {
            Diag.Log($"Exiting with {accepted} pending at the user's request");
        }

        _exiting = true;
        Shutdown();
    }

    /// <summary>One synchronous retry of the queue. Returns what is still pending afterwards.</summary>
    private int FlushPendingNow()
    {
        LogFile? log = Log;
        if (log is null || log.PendingCount == 0)
        {
            return 0;
        }

        try
        {
            log.RetryPending();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Diag.Log("Final retry failed", e);
        }

        return log.PendingCount;
    }

    /// <summary>Finds the settings and data file, running first-run setup when there are none. False means quit.</summary>
    private bool PrepareDataFile()
    {
        string? envVar = SettingsStore.FindConfiguredEnvVar();
        string? root = envVar is null ? null : SettingsStore.ResolveRoot(envVar);
        Settings.Settings? settings = root is null ? null : SettingsStore.Load(root);
        if (root is null || settings is null)
        {
            return RunFirstRun(null);
        }

        _root = root;
        _settings = settings;
        _dataFilePath = SettingsStore.DataFilePath(root, settings);
        if (_dataFilePath.Length == 0)
        {
            Diag.Log($"The configured data file path is not inside the synced root: {settings.DataFileRelativePath}");
            AppDialog.Inform(null, "Data file outside the synced folder", "The settings point at a data file outside the synced folder, which Logbook does not allow. Choose the file again.", DialogTone.Warning);
            return RunFirstRun(settings);
        }

        // Several logbook*.jsonl files beside each other: ask which is real, never guess (DESIGN.md section 8).
        IReadOnlyList<string> siblings = SyncFile.Siblings(_dataFilePath);
        if (siblings.Count > 1)
        {
            PickFileWindow picker = new(siblings, _dataFilePath);
            if (picker.ShowDialog() != true || picker.ChosenPath is null)
            {
                return false;
            }

            if (!string.Equals(picker.ChosenPath, _dataFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _dataFilePath = picker.ChosenPath;
                _settings.DataFileRelativePath = Path.GetRelativePath(SettingsStore.DataRoot(_root, _settings), _dataFilePath);
                SaveSettings();
            }
        }
        else if (!File.Exists(_dataFilePath))
        {
            // The configured file is gone: run the picker rather than create a new file silently (DESIGN.md section 16).
            Diag.Log($"Configured data file is missing: {_dataFilePath}");
            AppDialog.Inform(
                null,
                "Log file not found",
                $"The log file is not where the settings say:\n{_dataFilePath}\n\nIf the sync client is still downloading, quit and wait for it; creating a new log here would split your entries across two files. Otherwise choose or create the file now.",
                DialogTone.Warning);
            return RunFirstRun(settings);
        }

        if (SyncFile.IsPlaceholder(_dataFilePath))
        {
            Diag.Log("The data file is a sync placeholder; reading it will download it");
            _placeholderSeen = true;
        }

        return true;
    }

    // DESIGN.md section 12, first run steps 1 to 3. Cancelling a later step returns to the first.
    // Existing settings (hotkeys, toggles) are kept; only the file location changes.
    private bool RunFirstRun(Settings.Settings? existing)
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
                    AppDialog.Inform(null, "Could not create the file", "The file could not be created: " + e.Message, DialogTone.Error);
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

            Settings.Settings settings = existing ?? new Settings.Settings();
            settings.SyncRootEnvVar = welcome.SyncRootEnvVar;
            settings.DataFileRelativePath = welcome.DataFileRelativePath;
            _root = welcome.Root;
            _settings = settings;
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

        bool wasLocked = IsLocked;
        bool register = AppDialog.Confirm(
            null,
            "Start Logbook when you sign in?",
            "A hotkey that is not running logs nothing. This adds a shortcut to your Startup folder; you can change it later in Settings.",
            "Start with Windows",
            "Not now",
            DialogTone.Question,
            confirmIsDefault: true);

        // A workstation or idle lock closes the dialog unanswered, and Confirm then returns false as if for "Not now",
        // so the marker is set only after the dialog and only when no lock closed it; otherwise the offer comes back.
        if (!wasLocked && IsLocked)
        {
            return;
        }

        LocalMarker.Set(StartupOfferMarker);
        if (!register)
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
            _tray?.ShowBalloon("Startup shortcut not created", e.Message, warning: true, ShowSettings);
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
            _tray?.ShowBalloon("Hotkey not set", $"'{text}' is not a valid {purpose} hotkey. Click to open Settings.", warning: true, ShowSettings);
            return;
        }

        if (!_hotkeys!.TryRegister(hotkey, action))
        {
            Diag.Log($"Windows refused the {purpose} hotkey {hotkey}");
            _tray?.ShowBalloon("Hotkey in use", $"{hotkey} could not be registered for {purpose}; another app may own it. Click to open Settings.", warning: true, ShowSettings);
        }
    }

    /// <summary>Prompts for the passphrase if locked. Returns true when the log is open afterwards.</summary>
    private bool EnsureUnlocked()
    {
        if (!IsLocked)
        {
            return true;
        }

        // A hotkey pressed while the prompt is already up must not stack a second prompt and a second LogFile.
        if (_unlockPrompt is { } open)
        {
            open.Activate();
            return false;
        }

        _unlockPrompt = new UnlockWindow(_dataFilePath);
        try
        {
            if (_unlockPrompt.ShowDialog() == true && _unlockPrompt.Opened is { } opened)
            {
                Log = opened;
                Diag.Log($"Unlocked; {opened.Live.Count} live entries, {opened.UnreadableLineCount} unreadable lines, trailing truncated: {opened.TrailingLineTruncated}");

                // The key finished deriving after Windows locked the session (DESIGN.md section 9): locking now, before
                // any reminder or timer starts, keeps the log from sitting open behind the lock screen until the idle limit.
                if (_sessionLocked)
                {
                    Lock("The log opened while Windows had the session locked; locked at once");
                    return false;
                }

                _tray?.SetLocked(false);
                StartReminders();
                _idleTimer.Start();
                RetrySessionNotifications();
                return true;
            }

            return false;
        }
        finally
        {
            _unlockPrompt = null;
        }
    }

    // A silent failure here leaves the log open on an unattended machine (DESIGN.md section 9), so a registration that
    // failed at startup is tried once more at the first unlock, and if Windows refuses again the user is told.
    private void RetrySessionNotifications()
    {
        if (!_retrySessionNotifications)
        {
            return;
        }

        _retrySessionNotifications = false;
        if (_messageWindow!.RegisterForSessionNotifications())
        {
            Diag.Log("Registered for session lock and sign-out notifications on the retry at unlock");
            return;
        }

        Diag.Log($"Could not register for session notifications on the retry either (error {Marshal.GetLastWin32Error()}); a workstation lock will not lock the log");
        _tray?.ShowBalloon("Workstation lock not followed", "Windows refused to report workstation locks to Logbook, so locking the workstation will not lock the log. Use Lock now in the tray menu before you walk away.", warning: true);
    }

    /// <summary>
    /// Tray → Lock now: back to the state the app starts in (DESIGN.md section 9). The open log, and with it the data
    /// key and every decrypted entry, is dropped; the next hotkey prompts for the passphrase and opens the file afresh.
    /// Unsaved captures live only inside the open log, so locking is refused until they are on disk; it is refused too
    /// while the result of a passphrase change is on screen, the only place the user learns which passphrase now opens the log.
    /// </summary>
    private void LockNow()
    {
        if (Log is not { } log)
        {
            return;
        }

        if (WriteInProgress(log))
        {
            int waiting = Math.Max(log.PendingCount, 1);
            AppDialog.Inform(
                VisibleMain,
                "Not locked yet",
                $"{RangeText.Count(waiting)} not on disk yet, and locking would lose it. Logbook keeps retrying every 30 seconds; lock again once the tray icon no longer shows unsaved entries.",
                DialogTone.Warning);
            return;
        }

        if (PassphraseResultOnScreen())
        {
            AppDialog.Inform(VisibleMain, "Not locked yet", "The result of the passphrase change is still on screen. Read it and close it, then lock again.", DialogTone.Warning);
            return;
        }

        Lock("Locked from the tray");
    }

    /// <summary>
    /// Idle auto-lock (DESIGN.md section 9), checked once a minute while unlocked. "Idle" is no input to the Windows
    /// session at all, so typing in other apps keeps the log open and the hotkey stays one keystroke away. When due it
    /// takes the same path as Lock now. A tick never shows a dialog: while anything is unsaved it skips and tries again
    /// next minute (the tray icon already shows the pending count), as it does while the result of a passphrase change
    /// is on screen, and while one of the app's modal dialogs is open it
    /// waits too, so a half-made decision there is not closed under the user, until twice the limit has passed; then
    /// the lock closes the dialog along with every other window.
    /// </summary>
    private void LockIfIdle()
    {
        if (Log is not { } log)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        if (LastInput.Before(now) is not { } lastInput)
        {
            return;
        }

        bool dialogOpen = ComponentDispatcher.IsThreadModal;
        if (IdleLock.Decide(lastInput, now, _settings.IdleLockMinutes, dialogOpen) != IdleDecision.Lock)
        {
            return;
        }

        // Unsaved captures and the result of a passphrase change win over everything, the twice-the-limit rule included:
        // a lock never drops the one or takes away the other.
        if (LockWaitReason(log) is { } reason)
        {
            Diag.Log($"Idle lock postponed: {reason}");
            return;
        }

        Lock(dialogOpen
            ? $"Locked after twice the {_settings.IdleLockMinutes}-minute idle limit without input; the open dialog was closed"
            : $"Locked after {_settings.IdleLockMinutes} minutes without input");
    }

    /// <summary>
    /// Windows locked the session or is signing the user out (DESIGN.md section 9): lock the log as Lock now does, since
    /// walking away is usually what locking the workstation means. While an entry is not on disk yet, a write is in
    /// flight, a passphrase change is rewriting the header or its result is on screen, the lock is postponed and taken
    /// as soon as that is done. A sign-out with unsaved entries is refused separately (OnSessionEnding); a postponed
    /// lock cannot hide them, because it is only taken once nothing is unsaved.
    /// </summary>
    private void LockForSession(string what)
    {
        if (Log is not { } log)
        {
            return;
        }

        if (LockWaitReason(log) is { } reason)
        {
            Diag.Log($"{what}; lock postponed: {reason}");
            _postponedLockTimer.Start();
            return;
        }

        Lock($"{what}; locked");
    }

    // A postponed lock still waiting when the user is back protects nothing, and taking it would close their typed text
    // (DESIGN.md section 9), so it is cancelled.
    private void SessionUnlocked()
    {
        _sessionLocked = false;
        if (_postponedLockTimer.IsEnabled)
        {
            _postponedLockTimer.Stop();
            Diag.Log("Windows unlocked the session before the postponed lock was taken; the lock was cancelled");
        }
    }

    // Polled rather than hooked into every place a write can finish (a capture, a detail-pane save, the 30-second
    // retry, a passphrase change) or a passphrase result can be closed, so no settling path can be missed.
    private void TakePostponedLock()
    {
        if (Log is not { } log)
        {
            _postponedLockTimer.Stop();
            return;
        }

        if (LockWaitReason(log) is not null)
        {
            return;
        }

        Lock("Took the lock postponed from a workstation lock or sign-out");
    }

    // Drops the open log and everything that shows it. Callers have already checked that nothing is unsaved.
    private void Lock(string reason)
    {
        LogFile? log = Log;
        try
        {
            // Every window closes, toasts included: the "Logged" toast shows a decrypted summary and stays up while the
            // mouse is over it. A toast's click already goes through the unlock prompt, so closing one loses nothing.
            // Newest first, so a dialog closes before the window that owns it; closing an owner closes its dialogs, so a
            // window may be gone by the time the loop reaches it.
            foreach (Window window in Windows.OfType<Window>().Reverse().ToList())
            {
                if (!Windows.OfType<Window>().Contains(window))
                {
                    continue;
                }

                try
                {
                    if (window is MainWindow main)
                    {
                        main.CloseForLock();
                    }
                    else if (window.IsLoaded)
                    {
                        window.Close();
                    }
                }
                catch (InvalidOperationException e)
                {
                    // WPF refuses Close on a window that is already closing; that window is on its way out anyway.
                    Diag.Log("A window could not be closed for the lock", e);
                }
            }
        }
        finally
        {
            // Whatever happened above, the app must end up locked: a half-done lock would keep the key and the entries.
            _main = null;
            _capture = null;
            _suggestions = null;
            _suggestionsFor = null;
            Log = null;
            _reminderTimer.Stop();
            _idleTimer.Stop();
            _postponedLockTimer.Stop();
            _remindersShown.Clear();
            _tray?.DropWaitingReminders();
            _tray?.SetLocked(true);
            log?.Close(); // zeroes the data key and empties the entries; callers checked nothing is pending
            Diag.Log(reason);
        }
    }

    /// <summary>
    /// True while anything is pending, being written, or rewriting the header. Locking would drop unsaved captures, and
    /// a passphrase change must not race an append, so both wait for this to clear.
    /// </summary>
    private bool WriteInProgress(LogFile log) => log.PendingCount > 0 || WriteInFlight();

    /// <summary>
    /// Why a lock must wait right now, for the diagnostics log; null when it may go ahead. A write in progress, or the
    /// result of a passphrase change still on screen (DESIGN.md section 9): that notice is the only place the user
    /// learns which passphrase the log now has. A change window with nothing attempted yet may still be closed.
    /// </summary>
    private string? LockWaitReason(LogFile log)
    {
        if (WriteInProgress(log))
        {
            return "an entry is not on disk yet";
        }

        if (PassphraseResultOnScreen())
        {
            return "the result of a passphrase change is on screen";
        }

        return null;
    }

    private bool PassphraseResultOnScreen()
        => _passphraseNoticeOpen || Windows.OfType<ChangePassphraseWindow>().Any(w => w.HasOutcome);

    /// <summary>True while a capture, a detail-pane save or a passphrase change is being written right now.</summary>
    private bool WriteInFlight()
        => _writesInFlight > 0
            || _main is { IsSaving: true }
            || Windows.OfType<ChangePassphraseWindow>().Any(w => w.IsChanging);

    private void ShowCapture() => ShowCapture(null);

    /// <param name="followUpOf">The entry the main window's Follow up button was pressed on; null for a plain capture.</param>
    private void ShowCapture(Entry? followUpOf)
    {
        if (!EnsureUnlocked())
        {
            return;
        }

        // A box that is already open keeps what was typed in it, follow-up or not; nothing typed is ever thrown away.
        if (_capture is { } open)
        {
            open.Activate();
            return;
        }

        CaptureWindow capture = new(Monitors.WorkAreaAtCursor(_messageWindow!.TransformFromDevice), BuildSuggestions, followUpOf);
        capture.Captured += entry => _ = SaveCapturedAsync(entry, capture.WorkArea);
        capture.Closed += (_, _) => _capture = null;
        _capture = capture;
        capture.Show();
        capture.Activate();
    }

    // The write runs off the UI thread: it can retry for a few seconds if the sync client holds the file.
    private async Task SaveCapturedAsync(Entry entry, Rect workArea)
    {
        LogFile log = Log!;
        bool saved;
        _writesInFlight++;
        try
        {
            saved = await Task.Run(() => log.Append(entry)).ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Not expected, but this path must never fail quietly: the entry is in memory and in the queue.
            Diag.Log("Capture write failed unexpectedly", e);
            saved = false;
        }
        finally
        {
            _writesInFlight--;
        }

        if (!saved)
        {
            Diag.Log("Capture could not be written; it is pending");
        }

        // Nothing awaits this task, so an exception from here on would vanish, and with it the "Not saved yet" toast.
        try
        {
            _suggestions = null; // the new entry may introduce a tag or person
            _tray?.SetPending(log.PendingCount);
            if (_main is { IsVisible: true })
            {
                _main.Refresh();
            }

            ToastWindow toast = saved
                ? new ToastWindow(workArea, "Logged — click to edit", entry.Summary, danger: false, TimeSpan.FromSeconds(5))
                : new ToastWindow(workArea, "Not saved yet", $"{RangeText.Count(log.PendingCount)} waiting for the file. Retrying every 30 seconds; the tray icon shows the count.", danger: true, TimeSpan.FromSeconds(15));
            toast.Clicked += () => ShowMainWindow(entry.Id);
            toast.Show();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Diag.Log("Could not finish showing a capture's result", e);
            if (!saved)
            {
                _tray?.ShowBalloon("Not saved yet", $"{RangeText.Count(log.PendingCount)} waiting for the file. Retrying every 30 seconds; the tray icon shows the count.", warning: true);
            }
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
            _main = new MainWindow(Log!, BuildSuggestions, _settings.SavedQueries, SaveSettings);
            _main.SettingsRequested += ShowSettings;
            _main.CopyTimelineRequested += ShowExportPanel;
            _main.ExportDecryptedRequested += ShowExportDecrypted;
            _main.CheatSheetRequested += ShowCheatSheet;
            _main.ChangePassphraseRequested += ShowChangePassphrase;
            _main.FollowUpRequested += ShowCapture;
            _main.Changed += () =>
            {
                _suggestions = null;
                _tray?.SetPending(Log?.PendingCount ?? 0);
            };
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
        // Cached until a write clears it; a few thousand entries make the rebuild trivial.
        if (_suggestionsFor != Log || _suggestions is null)
        {
            _suggestions = new Suggestions(Log?.Live ?? [], _settings.AutocompleteCap, _settings.AutocompleteRanking);
            _suggestionsFor = Log;
        }

        return _suggestions;
    }

    private Window? VisibleMain => _main is { IsVisible: true } ? _main : null;

    private void ShowExportPanel(string title, IReadOnlyList<Entry> entries)
    {
        ExportPanel panel = new(title, entries, _settings.Export, SaveSettings, _dataFilePath, SyncRoots()) { Owner = VisibleMain };
        panel.ShowDialog();
    }

    private void ShowExportDecrypted()
    {
        ExportDecryptedWindow window = new(Log!, _dataFilePath, SyncRoots()) { Owner = VisibleMain };
        window.ShowDialog();
    }

    // Every synced folder on the machine, not only the configured one: a plaintext export must not land in any of them.
    private List<string> SyncRoots()
    {
        List<string> roots = [_root];
        foreach (string envVar in SettingsStore.KnownSyncEnvVars)
        {
            if (SettingsStore.ResolveRoot(envVar) is { } folder && !roots.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(folder);
            }
        }

        return roots;
    }

    // Only reachable from the main window, which exists only while unlocked.
    private void ShowChangePassphrase()
    {
        if (Log is not { } log)
        {
            return;
        }

        if (WriteInProgress(log))
        {
            AppDialog.Inform(VisibleMain, "Not yet", "An entry is still being written to the file. Change the passphrase once the tray icon no longer shows unsaved entries.", DialogTone.Warning);
            return;
        }

        ChangePassphraseWindow window = new(log, _dataFilePath, () => WriteInProgress(log)) { Owner = VisibleMain };
        if (window.ShowDialog() == true)
        {
            _passphraseNoticeOpen = true;
            try
            {
                AppDialog.Inform(VisibleMain, "Passphrase changed", "Use the new passphrase from now on; this file no longer opens with the old one. Copies made before the change (sync version history, backups) still do, because the entries themselves are not re-encrypted. Logbook stays unlocked, and every entry is unchanged.", DialogTone.Information);
            }
            finally
            {
                _passphraseNoticeOpen = false;
            }
        }
    }

    private void ShowCheatSheet()
    {
        CheatSheetWindow sheet = new(_settings.CaptureHotkey, _settings.MainWindowHotkey) { Owner = VisibleMain };
        sheet.ShowDialog();
    }

    private void ShowSettings()
    {
        // The main window's menu and the tray menu both open Settings, and the tray still works while a dialog is up;
        // two windows editing the same settings would each save over the other.
        if (_settingsWindow is { } open)
        {
            open.Activate();
            return;
        }

        SettingsWindow window = new(_settings, SettingsStore.SettingsPath(_root), _dataFilePath) { Owner = VisibleMain };
        _settingsWindow = window;
        try
        {
            if (window.ShowDialog() != true)
            {
                return;
            }
        }
        finally
        {
            _settingsWindow = null;
        }

        SaveSettings();
        _hotkeys?.UnregisterAll();
        RegisterHotkeys();
        _suggestions = null;

        // Open windows repaint through their DynamicResource brushes; the capture box's acrylic tint is a window
        // attribute, so an open one is told separately. A box opened later reads the theme when it is created.
        AppTheme.Apply(this, _settings.Theme);
        RecolorIcons();
        if (_capture is { } capture)
        {
            Backdrop.RefreshTint(capture);
        }
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

    // Reminders can only be read while unlocked, since the entries are encrypted at rest: checked at unlock and every
    // 30 seconds after, stopped on lock. The set of shown reminders starts empty at each unlock, so a passed reminder
    // that was never cleared shows once more after an unlock or a restart; clearing it (a new revision) ends it.
    private void StartReminders()
    {
        _remindersShown.Clear();
        _reminderTimer.Start();
        ShowDueReminder();
    }

    // One balloon per check, earliest first, so several due at once arrive 30 seconds apart and each click opens its own entry.
    private void ShowDueReminder()
    {
        if (Log is not { } log || _tray is null)
        {
            return;
        }

        if (Reminders.NextDue(log.Live, DateTimeOffset.Now, _remindersShown) is not { } due)
        {
            return;
        }

        _remindersShown.Add(Reminders.Shown.Of(due));
        string id = due.Id;
        // Never the summary: Windows keeps balloon text in its notification history long after the app has locked.
        _tray.ShowBalloon("Reminder", $"A reminder is due; it was due {Format.DateTime(due.RemindAt!.Value)}. Click to open its entry; clear the reminder there to stop it.", warning: false, () => ShowMainWindow(id), reminder: true);
        Diag.Log("Reminder balloon shown");
    }

    // Timer thread. Nothing here may throw: an exception on a thread-pool thread would end the process and its queue.
    private void RetryPending()
    {
        LogFile? log = Log;
        if (log is null || log.PendingCount == 0)
        {
            return;
        }

        try
        {
            // A capture or save being written right now writes the queue first and reports its own outcome; retrying
            // alongside it would only wait for that write and then announce it as this retry's work.
            if (Dispatcher.Invoke(WriteInFlight))
            {
                return;
            }

            // Announce only what this call wrote: the queue can also empty under it by a capture started meanwhile.
            int before = log.PendingCount;
            bool drained = log.RetryPending();
            bool wroteHere = drained && log.PendingCount < before;
            Diag.Log(drained ? "Pending writes flushed" : $"Pending writes still failing: {log.PendingCount} waiting");
            Dispatcher.BeginInvoke(() =>
            {
                _tray?.SetPending(log.PendingCount);
                if (wroteHere)
                {
                    _tray?.ShowBalloon("Saved", "The entries that were waiting have been written to the file.", warning: false);
                    if (_main is { IsVisible: true })
                    {
                        _main.Refresh();
                    }
                }
            });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Diag.Log("Retry of pending writes failed", e);
        }
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == SingleInstance.ShowMessage)
        {
            handled = true;
            Dispatcher.BeginInvoke(ShowMainWindow);
        }
        else if (msg == NativeMethods.WmWtsSessionChange && wParam.ToInt32() is NativeMethods.WtsSessionLock or NativeMethods.WtsSessionLogoff)
        {
            string what = wParam.ToInt32() == NativeMethods.WtsSessionLock ? "Windows locked the session" : "Windows is signing the user out";

            // Not inside the message hook: the lock closes windows.
            // Only a lock sets the flag: a sign-out ends the process anyway, and one Windows later cancels would leave it set.
            bool locked = wParam.ToInt32() == NativeMethods.WtsSessionLock;
            Dispatcher.BeginInvoke(() =>
            {
                _sessionLocked |= locked;
                LockForSession(what);
            });
        }
        else if (msg == NativeMethods.WmWtsSessionChange && wParam.ToInt32() == NativeMethods.WtsSessionUnlock)
        {
            Dispatcher.BeginInvoke(SessionUnlocked);
        }
        else if (msg == NativeMethods.WmSettingChange && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
        {
            // Windows broadcasts this when light or dark mode (or the accent) changes; follow it without a restart.
            // WPF handles the same message to swap its Fluent resources, so the tokens follow once that work is done.
            // An open capture box's acrylic tint is a window attribute, not a brush, so it is told as Settings save does,
            // and the icons are redrawn as there. A forced theme ignores Windows changing mode, its icons included.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                AppTheme.FollowWindows(this);
                if (AppTheme.FollowsWindows)
                {
                    RecolorIcons();
                }

                if (_capture is { } capture)
                {
                    Backdrop.RefreshTint(capture);
                }
            });
        }

        return IntPtr.Zero;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Diag.Log("Unhandled exception", e.Exception);
        e.Handled = true;
        if (_exiting)
        {
            // The app is shutting down and can no longer load a window; the log has the details.
            return;
        }

        AppDialog.Inform(null, "Something went wrong", "Details were written to the diagnostics log:\n" + Diag.LogFolder, DialogTone.Error);
        if (_tray is null)
        {
            // Startup never finished, so there is no tray icon to exit from; a silent zombie process would be worse.
            Shutdown();
        }
    }
}
