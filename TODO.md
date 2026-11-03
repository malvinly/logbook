# Roadmap

Logbook is feature complete for its purpose: capture with a hotkey, an encrypted append-only log in a folder of your choice, filtered timelines on demand. What is listed here is what is still planned, what is parked, and what is deliberately out of scope. The spec lives in [docs/](docs/).

## Before 1.0

- Publish a `v0.9.x` pre-release from the tag workflow and use it daily for a while, with the real log, so that the idle lock, the workstation lock, reminders and the balloon queue all happen in ordinary use. Fix what turns up.
- Tag `v1.0.0` when nothing new has turned up.

## Backlog

Not scheduled. Each is small, contrived or cosmetic; none affects data safety or ordinary use.

Grammar, all on contrived input:

- A typed word ending in an opener glues to the next word: `fixed "printer"#hw issue` stores `fixed "printer"issue`.
- Two openers can claim the same closing partner: `(#a,(#b c)` stores `((c)`.
- Leftovers stuck to the word before a token survive: `asked @j-doe,"@a-smith"` stores `asked,""`.
- The date-range picker deletes text when a keyword value swallowed more than the date: `last:7d,"x y"`.
- A keyword right after a separator is not recognised: `#printer,last:7d`.
- `@a,,#b` and `last:7d,,#b` parse differently.
- Letters outside the Basic Multilingual Plane (surrogate pairs) right after a sigil are not treated as letters.
- The "closing partner" of an opener is the next `)` or `"`, which the spec does not define.

Autocomplete:

- The popup has its own copy of the token grammar (where a token starts, what follows a separator, the keyword list), untested, and it can disagree with the scanner on longer chains such as `last:7d,#a,x,#pr`. A Core "token at caret" function with tests would end this.

Lock paths:

- The first-run open happens before the session notifications are registered, so a first run that completes under a locked session is not locked at once.
- A lock postponed by a sign-out that Windows then cancels is never cancelled.
- The Lock now refusal says "1 entry not on disk yet" during a header rewrite or a detail-pane save.
- "Idle lock postponed" is logged once a minute while a wait lasts.
- Windows sends the session-lock notification twice on a real Win+L; the handler is idempotent, so this only shows in the diagnostics log.
- The session-locked state machine could live in Core with tests.
- The one retry of the notification registration happens at the first unlock, which may be seconds after an early-boot failure.

Balloons:

- A clicked balloon brings no Closed event, so the next one waits the 60 s cap instead of 15 s; releasing on click when shown would be safe.
- "Reminder balloon shown" is logged when the balloon is queued, not when it is shown.

Exit paths:

- Exit, and the restart from Settings → "Choose another…", checks pending entries and writes in flight but not an open capture box, so typed, unsubmitted text is closed without a prompt. The capture code's own rule is that nothing typed is ever thrown away.
- The 30-second retry runs on a timer thread that the in-flight write counter does not see, so an exit or session end can stop the process during a queued write. The loss is one retry attempt, since each append is a single write-through call, but the exit check is meant to wait for every write.

Tests:

- Two leftover-punctuation cases now take the separator path and their names are stale.
- Missing cases: a capture of a line that is only a possessive token; `at:yesterday's,#a`; capture-side `said #a,"x"`; the 60 s cap test does not assert that the fallback held.

Build:

- Windows 10 is untested; the Mica and rounded-corner fallback has only been reasoned about.

Build tuning (Windows), all three measured on 2026-11-02 and none landed:

- Each change is measured alone on the published single-file exe (cold start to tray icon, first and second hotkey press to the capture box rendered, idle working set and private bytes, memory with the main window open, exe size), as the median of at least five runs. It is kept only if it is a net gain beyond run-to-run noise; otherwise it is reverted and its numbers are recorded here. The diagnostics log carries the timing lines ([docs/development.md](docs/development.md), Files).
- Baseline at e384886: tray icon ready 484 ms after process start (247 ms after `OnStartup` began), first hotkey press to capture box rendered 107 ms, second press 45 ms, idle 135 MB working set and 80 MB private bytes, main window open 175 MB and 122 MB, exe 173.1 MB. Run-to-run noise was about 7 ms on cold start, 8 ms on the presses and 1 to 3 MB on memory.
- Non-concurrent GC and `System.GC.ConserveMemory` at 5 in the runtime config: every metric within noise of the baseline (idle 135.0 MB and 80.6 MB), so there is nothing to gain; the heap is small and the working set is native and WPF memory.
- One throwaway `CaptureWindow` constructed at startup and closed without being shown: first press 107 to 77 ms, but 59 ms more startup work before the unlock prompt and idle memory up 5 to 6 MB (141 MB and 85 MB) for the life of the process. The remaining first-press cost is the first `Show`, the window handle, the DWM calls and the first render, which a construction cannot warm.
- ReadyToRun on the published exe: cold start 484 to 451 ms, but first press within noise (99 ms), every memory figure 3.5 to 4.3 MB worse, and the exe 18.5 MB larger (191.6 MB).

## After 1.0: macOS port

A larger item, not started, approved in outline on 2026-11-02. It is one piece of work with its own phases, done after `v1.0.0` ships and the Windows app has settled.

The shape:

- `Logbook.Core` stays as it is. It targets plain `net10.0` and uses only the BCL, so it already runs on macOS; the parser, file format, crypto, query and export rules and their tests are reused, not ported. Two details to confirm on a real Mac: `AesGcm` there is backed by CryptoKit and supports only 128-bit tags, which is what [docs/file-format.md](docs/file-format.md) specifies; and `ClipboardHtml` produces the Windows CF_HTML framing, which the Mac clipboard path does not use.
- `Logbook.App` is rewritten once in Avalonia and replaces the WPF app on Windows too, so there is one UI codebase. It is a rewrite, not a port: Avalonia's styling and window chrome differ enough that the XAML is re-authored. The rules stay: plain code-behind, the hand-written `INotifyPropertyChanged` base, no MVVM framework, theme values only in the `Theme` dictionaries. Avalonia's built-in `TrayIcon` replaces `NotifyIcon`, so `UseWindowsForms` goes.
- The behaviours that call the OS directly get a Windows and a macOS implementation each, chosen once at startup by an `OperatingSystem` check: global hotkey (Carbon `RegisterEventHotKey` on the Mac, which needs no accessibility permission), single instance, idle time for the auto-lock, screen lock and sign-out notifications, start at login (a LaunchAgent or `SMAppService`), light or dark detection, clipboard HTML, the per-profile folder (`LocalApplicationData` resolves to a Unix-style dotfolder on macOS, so the pointer location is chosen per OS explicitly), and opening a folder in the shell. The hotkey model and the Settings picker stop being built on Windows virtual-key codes. Mica, rounded corners and the dark title bar have no Mac counterpart and go.
- Footprint and responsiveness, in this order: publish with NativeAOT (Avalonia 11 supports it on both OSes), which needs the `DllImport` declarations to become `LibraryImport` and the `IShellLink` COM import replaced, since COM imports are not supported under NativeAOT; trimming comes with it, once the source-generated JSON context is proven to cover every serialised type. Trimming cannot land on the WPF app: the SDK refuses `PublishTrimmed` and `PublishAot` outright (NETSDK1175, because of the WinForms tray icon), and WPF itself is not trim-compatible, so this is the first build where it is possible. ReadyToRun is the fallback if AOT proves too strict. Non-concurrent GC and `GCConserveMemory` in the runtimeconfig. The capture window is created once at startup and kept hidden, rendered once so its first real show has paid for layout and glyph shaping; the hotkey path shows it and returns, everything else runs after it is visible. The main window is created lazily and disposed on close. Compiled bindings only, no runtime XAML loading. Target: idle in the tray in the 30 to 50 MB range, and hotkey to popup indistinguishable from instant.
- The macOS build is a self-contained `.app` bundle made by a script in the release workflow, unsigned and not notarized at first, so Gatekeeper warns the way SmartScreen does today; the README says so and points at the hash.

What changes in the docs when this starts, in the same commits as the code: the "WPF, Windows only" constraint and the Avalonia rejection in [docs/development.md](docs/development.md), the no-packages rule for the App in `CLAUDE.md` (Avalonia becomes the one allowed package in the App), the Startup shortcut and `%LOCALAPPDATA%` paths in [docs/settings.md](docs/settings.md) and [docs/export.md](docs/export.md), and a note in [docs/file-format.md](docs/file-format.md) that iCloud Drive can evict a file to the cloud and bring it back on access, which the append-only design copes with.

The cheap first step, with no app changes: run the Core tests on a Mac with `dotnet test tests/Logbook.Tests` (the root solution does not build there because the App targets `net10.0-windows`) and add a macOS job for them to `ci.yml`.

Rejected for this: keeping WPF on Windows and writing a native Swift app for the Mac, because Swift cannot use Core and the grammar and file format would need a second implementation kept in lockstep; Electron, because it rewrites Core in TypeScript and keeps a resident browser for a tray app; Tauri, because the append-only storage layer would have to move to Rust.

## Out of scope

- AI auto-tagging. Sending names and incidents to a third-party API is a policy problem, and recency-ranked autocomplete over a small tag set solves the real need.
- Data-key rotation on passphrase change. A change re-wraps the same data key, so a copy of the file made before it still opens with the old passphrase and decrypts every later record. Rotating would mean re-encrypting every record into a new file under a new key, a whole-file rewrite that contradicts the append-only rule and would have to be reconciled with the sync folder. [docs/file-format.md](docs/file-format.md) tells the user about old copies instead.
- Hash chains or any tamper evidence. You are the only person who could forge the log.
- SQLite, a persisted index, or full-text ranking. A few thousand records in a list is the design.
- Prefix tag matching. Exact plus autocomplete is strictly better.
- Sharing, multi-user, sync conflict merging, permissions. One user, one machine.
- CLI or script input, integrations writing entries. Capture is human and manual.
- Rich text, attachments, duration or numeric value fields, file compaction, passphrase recovery.
