# Settings, startup, and the moving user profile

- Settings are a plain JSON file **beside the data file**, wherever that is, not in `%APPDATA%`. They contain no secrets and do not name the data file ([Finding the data file](#finding-the-data-file)). Contents: hotkeys, autocomplete cap and ranking, `idleLockMinutes` (lock after that many minutes without input, [locking.md](locking.md); 0 never locks; default 60; edited in the Settings window as a whole number from 0 to 1440), `theme` (`system`, the default and what a file without the property gets, follows the Windows light or dark setting; `light` or `dark` forces one; edited in the Settings window as "Theme: Follow Windows / Light / Dark"; applied at startup and when Settings is saved, and open windows repaint at once, as does the tray icon; a forced theme ignores Windows changing mode, but the tray and window icons are redrawn on any Windows colour-set change so that a new accent reaches them too), export toggles, saved queries ([main-window.md](main-window.md)). The last export folder is remembered per Windows profile in `%LOCALAPPDATA%` instead, because it is an absolute local path that would point a reader of the synced file at the plaintext copy.
- On launch, if the app is not registered to start with Windows for the current user, it offers once to register (a shortcut in the Startup folder). A login change wipes that shortcut; the offer is how it comes back. Settings → Save re-registers the shortcut at the running exe when "Start with Windows" is on.
- Single instance: a named mutex. A second launch activates the first instance's main window and exits.
- `RegisterHotKey` failure is surfaced immediately with a balloon naming the hotkey and offering Settings. A silently dead hotkey means a week of nothing logged.
- Tray balloons queue: one shows at a time, the next waits until the current one has closed, and each keeps its own click action, so a hotkey failure at startup is not written over by the sync or reminder balloon that follows it. Windows does not always report the close, so the queue moves on by itself: after 15 seconds when Windows never reported the balloon shown at all, otherwise after a 60 second cap, since some accessibility settings keep a balloon up well past the default and replacing one still on screen is worse than a short wait.
- The Settings window shows the app's version in its footer, so a bug report can name the build.

## First run

1. Choose or create the data file with a plain file picker, in any folder. Where it lives is the user's choice; a folder a sync service backs up is recommended but not detected or enforced.
2. If creating: set a passphrase twice, read the no-recovery warning, confirm.
3. If opening an existing file: enter the passphrase.
4. Offer startup registration.
5. Show the hotkey and the grammar cheat sheet once.

## Finding the data file

- The absolute path of the chosen file is kept per Windows profile in `%LOCALAPPDATA%\Logbook\data-file`. It is the only thing that finds the file at launch; the settings file does not name it.
- No pointer (a new profile, or a pointer that is not a full path to a `logbook*.jsonl` file): the first-run picker runs.
- A pointer to a file that is not there: the "Log file not found" dialog names the path and offers "Choose a file", which runs the first-run picker, or "Quit", for when a sync client is still downloading the file.
- Browsing to an existing file uses the `logbook.settings.json` beside it. When there is none, the settings in use are kept and saved beside the file. So losing the profile costs one dialog, never data or settings.
- Choosing a different file in the conflict-copy sibling picker at launch updates the pointer; the sibling is in the same folder, so the settings stay.
