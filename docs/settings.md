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
- No pointer (a new profile, or a pointer that is not one line holding a full path to a `logbook*.jsonl` file): the first-run picker runs.
- A pointer file that exists but cannot be read: the "Could not read where the log is" dialog names it and offers "Quit" (the default), to try again later, or "Choose a file", which runs the picker. A read failure is never taken for a first run.
- The pointer is written to a temporary file beside it and then swapped in, so a crash cannot leave half a path. A write that fails is never silent: at launch "Location not remembered" names the pointer file, the chosen log opens anyway, and the next start asks for the file again; from "Choose another…" "Could not switch" names it and the app keeps the current log.
- A pointer to a file that is not there: the "Log file not found" dialog names the path and offers "Choose a file", which runs the first-run picker, or "Quit", for when a sync client is still downloading the file.
- Which settings go with a newly chosen file, by the picker or "Choose another…": the `logbook.settings.json` beside it wins; an unreadable one is left alone and the current settings stay in memory; when there is none, the current settings are saved there. After "Log file not found" the current settings are the ones from the missing file's old folder, when it still has them, so a log moved without its settings file keeps the user's hotkeys and theme. So losing the profile costs one dialog, never data or settings.
- Choosing a different file in the conflict-copy sibling picker at launch updates the pointer; the sibling is in the same folder, so the settings stay.
- The Settings window's Files section shows the data file in a read-only box with "Choose another…", which runs the first-run picker again. An existing file is not unlocked there; the restart asks for its passphrase. A new file is created there, with a passphrase set twice as on first run.
- The settings rule above applies; the running app keeps its own settings until the restart, and the current settings file is left as it is.
- The pointer is then rewritten and the app offers to restart now or later. Restarting discards unsaved changes in the Settings window. Until it restarts, the app keeps using the current log, and the Settings window shows the chosen file with "Opens the next time Logbook starts."
- There is no built-in move. Moving the log is done in Explorer with Logbook quit (the file and its settings file together), then "Choose another…" or the "Log file not found" dialog points the app at it. Copying a file a sync client may be mid-upload on is not the app's job.
