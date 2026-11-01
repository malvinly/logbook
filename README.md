# Logbook

Logbook is a personal Windows tray app for recording decisions and events as they happen: press a hotkey, type one line, press Enter. Everything is stored in a single encrypted, append-only file that lives in a synced folder, and the app can turn any filtered set of entries into a dated timeline you can paste into an email or chat. It exists to answer "when did that happen, how many times, and did I tell anyone" with evidence instead of memory.

## What it does

- **Capture.** A global hotkey (default `Ctrl+Alt+L`) opens a launcher-style box. One line is parsed as you type: `#tag` and `@person` become chips, `at:yesterday,3pm` sets when it happened, the rest is the summary. A "more" section takes a reason, extra tags and people, and refs such as ticket numbers. A toast confirms the write and opens the entry on click.
- **Storage.** One `logbook.jsonl` file: a plaintext header, then one AES-256-GCM encrypted record per line under a data key that a passphrase unwraps. The file is only ever appended to; edits and deletes are new revisions. It is built to live in a sync folder, and nothing depends on the Windows user profile surviving.
- **Main window.** Filter with the same grammar (`#asked @j-doe last:30d "exact phrase"`), save a filter as a button, read entries grouped by day, edit any field as a new revision, see every prior revision with changed fields highlighted, follow up on an entry with the thread recorded, and set a reminder that shows as a tray balloon.
- **Export.** Copy the filtered set as a plain-text timeline (with HTML alongside for mail clients), with optional gaps between entries, month counts, a compact form and tags and people per line. Save it as CSV, or export the whole log as decrypted JSONL, both steered away from synced folders.
- **Lock.** The app starts locked and prompts for the passphrase at the first hotkey. Lock now in the tray menu, idle auto-lock, and Tools → Change passphrase, which rewrites only the header.
- **Look.** Windows 11 Fluent, following the Windows light or dark mode or forced from Settings.

## Running it

Windows 10 or 11. The release build is one self-contained `Logbook.exe`; drop it in a folder and run it. The first run asks where the data file should live, sets the passphrase (there is no recovery; write it down somewhere physical) and offers to start with Windows.

```
dotnet build
dotnet test
dotnet publish src/Logbook.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false
```

## Where things are

- [DESIGN.md](DESIGN.md) is the source of truth: grammar, file format, key hierarchy, the main window, exports, settings, the phase list in section 14 and the implementation decisions in section 16.
- [CLAUDE.md](CLAUDE.md) holds the conventions for working in this repo.
- `src/Logbook.Core` is the UI-free library (model, parser, crypto, file store, query, export); `src/Logbook.App` is the WPF tray app; `tests/Logbook.Tests` holds the Core tests.

Status: Phases 1 to 6 of DESIGN.md are built. Phase 7, the list of decisions from the review of those phases, is written into the design and next to build.
