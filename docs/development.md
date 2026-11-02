# Development

How the code is laid out, the decisions that shape it, and how a build and a release are made. The other files in this folder are the spec: [grammar.md](grammar.md), [file-format.md](file-format.md), [locking.md](locking.md), [main-window.md](main-window.md), [export.md](export.md) and [settings.md](settings.md). The roadmap is [TODO.md](../TODO.md).

## Constraints

- .NET 10, WPF, Windows only. No third-party installs on the target machine, so the app is a self-contained build you drop in a folder.
- The data file should live in a folder managed by a file sync service, because that is the only backed-up location, but the app does not detect or enforce this: the user chooses the folder. Sync services sync files; they do not understand databases. The file format is designed to be sync-safe: append-only, one record per line, never rewritten in place (the single exception is line 1 on a passphrase change, written over itself at the same length; [file-format.md](file-format.md)).
- The Windows user account may change. Nothing may depend on the user profile surviving: no DPAPI, no per-user registry secrets, no absolute paths into `C:\Users\<login>`.
- The author must be able to understand every line. Prefer the boring implementation. Keep the core free of UI dependencies so it can be ported (likely to TypeScript) later.

## Solution structure

```
/README.md  /TODO.md  /CLAUDE.md  /LICENSE (MIT)  /Logbook.sln  /.gitignore  /.editorconfig
/docs/                 the spec and this file
/.github/workflows/    ci.yml (build and test on every push) and release.yml (a tagged release)
/src/Logbook.Core/     net10.0 class library, no UI references, no NuGet packages
/src/Logbook.App/      net10.0-windows WPF app (`UseWPF` plus `UseWindowsForms` for `NotifyIcon` only), references Core, no NuGet packages
/tests/Logbook.Tests/  xUnit; packages: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk
```

- `Logbook.Core` — record model, grammar parser, crypto, file store, query, export rendering. This is the part worth porting.
- `Logbook.App` — WPF tray app. Hotkey, capture popup, main window, settings. Thin; calls Core.
- `Logbook.Tests` — unit tests for Core. Every parser rule, every file-format rule and every export toggle has a test. A behaviour without a test is not done.

Namespaces follow folders: `Logbook.Core.Grammar`, `Logbook.Core.Storage`, `Logbook.Core.Crypto`, `Logbook.Core.Export`, `Logbook.App.Capture`, and so on.

A decision rule with no UI in it lives in Core with a test, even when only the App calls it: which lines the decrypted export writes for each of its options, whether the idle input tick count has wrapped, the order of the tray balloon queue.

## UI framework

WPF. The app must look polished, and the main window (grouped results, tag chips, a detail pane with changed-field highlighting, a fading toast) is where WinForms would turn into weeks of owner-drawn GDI+ work that still looks dated. WPF gives vector rendering, native DPI scaling, styles, templates, `ItemsControl` grouping and storyboard animations for free. The hard parts (global hotkey, tray, single instance) are Win32 P/Invoke in either framework. WinUI 3 was rejected because it needs the Windows App SDK runtime or MSIX, which fights single-exe deployment; Avalonia because it is a third-party dependency for a Windows-only app.

WPF rules for this project: plain code-behind plus a hand-written `INotifyPropertyChanged` base class; no MVVM framework, no behaviors library, no third-party control suite. WPF's built-in Fluent theme is the base (merged as a dictionary; the `Application.ThemeMode` API is still experimental on .NET 10 and is not used). Logbook's own theme values live in the `Theme` folder and nowhere else: `Theme.xaml` holds fonts, type scale, radii and the control styles, and `Theme.Light.xaml` and `Theme.Dark.xaml` hold the colours and brushes, so light and dark differ only in which of those two is merged ([settings.md](settings.md) says when). The merged token dictionary is loaded afresh, not only swapped when light and dark flip, on every Windows colour-set change and on Settings save: a brush whose colour is a Windows system colour, the accent brush, keeps the colour it had when the dictionary loaded, so a new accent reaches the views and the icons only through a fresh copy. Custom window chrome (`WindowChrome`) for the capture popup and toast; standard chrome for the main window. Every window carries the tray icon's "L" badge as its icon, so title bars and the taskbar do not show WPF's default. Mica and rounded corners on borderless windows need two DWM calls; they work on Windows 11 and degrade to a plain surface on older Windows rather than fail.

## Language and style

- C# 14, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, file-scoped namespaces.
- Classes are `sealed` unless designed for inheritance. The model is immutable `record` types; a revision is a new record, never a mutated one.
- No reflection-based magic, no DI container, no MediatR-style indirection. Constructors take what they need.
- A non-obvious block (P/Invoke, crypto framing, parser edge cases) gets a short comment saying why, not what.

## Libraries, all from the BCL

- ULID: hand-rolled in Core (~40 lines). 48-bit millisecond timestamp + 80 random bits, Crockford base32, 26 characters. No monotonic counter; two captures in the same millisecond is not a real case.
- JSON: `System.Text.Json` with a source-generated `JsonSerializerContext` for the record and header types, so the app can be published trimmed and single-file without reflection warnings.
- Crypto: `AesGcm`, `Rfc2898DeriveBytes.Pbkdf2`, `RandomNumberGenerator`. Nothing else.
- Hotkey: `RegisterHotKey` / `UnregisterHotKey` via P/Invoke, with `WM_HOTKEY` handled through an `HwndSource` hook on a hidden message window.
- Tray: `System.Windows.Forms.NotifyIcon`, the one reason `UseWindowsForms` is enabled; nothing else from that assembly is used. Single instance: a named `Mutex`; the second instance posts a custom window message to the first and exits.
- Toast: a borderless, topmost, non-activating WPF `Window` (`ShowActivated=false`, `WindowChrome`) at the bottom-right of the active monitor, opacity storyboard fades it out after 5 s, click opens the entry. Not the Windows notification API.

## Time and formatting

- Capture uses `DateTimeOffset.Now`. Storage is ISO-8601 with the offset as `System.Text.Json` writes a `DateTimeOffset`: `2026-10-06T10:14:03-05:00`, with fractional seconds only when they are not zero. Only the CSV export uses the fixed-width round-trip form `"O"` ([export.md](export.md)).
- Export renders with `CultureInfo.InvariantCulture` and fixed patterns: `ddd MMM d, yyyy h:mm tt` for full, `MMM d, yyyy` for compact. Output is intentionally US-style regardless of machine locale, because the format is part of the spec.
- `since:`/`until:` are inclusive calendar dates, compared against the date each entry carries in its own stored offset, never against a converted instant: an entry written at 00:30 in Tokyo is a Tokyo-date entry wherever the log is read. `last:Nd` = N days, `last:Nw` = 7N days, `last:Nm` = `AddMonths(-N)`, all counted back from today's date.
- `at:` stores the local offset in force at the date and time it names, not today's: `at:yesterday,3pm` typed the day after a clock change carries yesterday's offset, as the reminder field does. A date with no time stores 12:00:00 in that offset.

## Files

- Data file: `logbook.jsonl`, wherever the user put it. Settings file: `logbook.settings.json`, beside it.
- The data file is found through the per-profile pointer `%LOCALAPPDATA%\Logbook\data-file` ([settings.md](settings.md)). If the file it names is missing at launch, the app asks rather than creating a new file silently.
- Diagnostics (not entries, never entry contents) go to a rolling text log in `%LOCALAPPDATA%\Logbook\logs\`, one file per day, two weeks kept. Losing it on a profile change is fine.

## Build, test and release

- `dotnet build` and `dotnet test` from the repo root. Warnings are errors. The `ci.yml` workflow runs both on every push and pull request.
- Release build: `dotnet publish src/Logbook.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false`. One `Logbook.exe` dropped in a folder is the whole install. Trimming stays off until the source-generated JSON context is proven.
- A release is a tag. Pushing `vX.Y.Z` runs `release.yml`: build, test, publish with `-p:Version=X.Y.Z`, and a GitHub Release holding `Logbook.exe` and its SHA-256. A `0.x` tag is marked a pre-release. The version in `Logbook.App.csproj` is the fallback for local builds and is what the Settings footer shows.
- The exe is not code-signed, so SmartScreen warns on the first run of a downloaded copy; the README says so and points at the hash.
- Smoke tests that save Settings from a test build must back up `Startup\Logbook.lnk` first and restore it afterwards, and remove any `%LOCALAPPDATA%\Logbook\export-folder` marker a smoke export created, or the real installation ends up pointing at the test exe.
