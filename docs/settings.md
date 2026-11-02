# Settings, startup, and the moving user profile

- Settings are a plain JSON file **beside the data file** in the synced folder, not in `%APPDATA%`. They contain no secrets. Contents: data file path (relative to the sync root), the environment variable to resolve the sync root from (configurable, since each sync client exposes its own), hotkeys, autocomplete cap and ranking, `idleLockMinutes` (lock after that many minutes without input, [locking.md](locking.md); 0 never locks; default 60; edited in the Settings window as a whole number from 0 to 1440), `theme` (`system`, the default and what a file without the property gets, follows the Windows light or dark setting; `light` or `dark` forces one; edited in the Settings window as "Theme: Follow Windows / Light / Dark"; applied at startup and when Settings is saved, and open windows repaint at once, as does the tray icon; a forced theme ignores Windows changing mode, but the tray and window icons are redrawn on any Windows colour-set change so that a new accent reaches them too), export toggles, saved queries ([main-window.md](main-window.md)). The last export folder is remembered per Windows profile in `%LOCALAPPDATA%` instead, because it is an absolute local path that would point a reader of the synced file at the plaintext copy.
- On launch, if the app is not registered to start with Windows for the current user, it offers once to register (a shortcut in the Startup folder). A login change wipes that shortcut; the offer is how it comes back. Settings → Save re-registers the shortcut at the running exe when "Start with Windows" is on.
- Single instance: a named mutex. A second launch activates the first instance's main window and exits.
- `RegisterHotKey` failure is surfaced immediately with a balloon naming the hotkey and offering Settings. A silently dead hotkey means a week of nothing logged.
- Tray balloons queue: one shows at a time, the next waits until the current one has closed, and each keeps its own click action, so a hotkey failure at startup is not written over by the sync or reminder balloon that follows it. Windows does not always report the close, so the queue moves on by itself: after 15 seconds when Windows never reported the balloon shown at all, otherwise after a 60 second cap, since some accessibility settings keep a balloon up well past the default and replacing one still on screen is worse than a short wait.
- The Settings window shows the app's version in its footer, so a bug report can name the build.

## First run

1. Choose or create the data file (file picker defaulting to the detected sync root).
2. If creating: set a passphrase twice, read the no-recovery warning, confirm.
3. If opening an existing file: enter the passphrase.
4. Offer startup registration.
5. Show the hotkey and the grammar cheat sheet once.
