# Logbook

Logbook is a personal Windows tray app for recording decisions and events as they happen: press a hotkey, type one line, press Enter. Everything is stored in a single encrypted, append-only file that lives in a folder you choose, typically one a sync service backs up, and the app can turn any filtered set of entries into a dated timeline you can paste into an email or chat. It exists to answer "when did that happen, how many times, and did I tell anyone" with evidence instead of memory.

## What it does

- **Capture.** A global hotkey (default `Ctrl+Alt+L`) opens a launcher-style box. One line is parsed as you type: `#tag` and `@person` become chips, `at:yesterday,3pm` sets when it happened, the rest is the summary. A "more" section takes a reason, extra tags and people, and refs such as ticket numbers. A toast confirms the write and opens the entry on click.
- **Storage.** One `logbook.jsonl` file: a plaintext header, then one AES-256-GCM encrypted record per line under a data key that a passphrase unwraps. The file is only ever appended to; edits and deletes are new revisions. It is built to survive living in a sync folder, and nothing depends on the Windows user profile surviving.
- **Main window.** Filter with the same grammar (`#asked @j-doe last:30d "exact phrase"`), save a filter as a button, read entries grouped by day, edit any field as a new revision, see every prior revision with changed fields highlighted, follow up on an entry with the thread recorded (the original lists its follow-ups as "Followed up by" lines), and set a reminder that shows as a tray balloon.
- **Export.** Copy the filtered set as a plain-text timeline (with HTML alongside for mail clients), with optional gaps between entries, month counts, a compact form and tags and people per line. A timeline from a saved-query button is headed by the button's name. Save it as CSV, where a cell that would read as a spreadsheet formula gets a leading apostrophe, or export the whole log as decrypted JSONL, both steered away from synced folders.
- **Lock.** The app starts locked and prompts for the passphrase at the first hotkey. Lock now in the tray menu, idle auto-lock (60 minutes by default; an open dialog holds it off for up to twice that), and locking when Windows locks the session or signs out, which, like the idle lock, waits until no entry is still being saved and no passphrase change is under way; a lock postponed that way is dropped again if you unlock the session first. Tools → Change passphrase rewrites only the header.
- **Look.** Windows 11 Fluent, following the Windows light or dark mode or forced from Settings.

## Running it

Built and tested on Windows 11; Windows 10 should work but is untested. The release build is one self-contained `Logbook.exe`: download it from the [Releases](https://github.com/malvinly/logbook/releases) page, drop it in a folder and run it. The first run asks where the data file should live, sets the passphrase (there is no recovery; write it down somewhere physical) and offers to start with Windows.

The exe is not code-signed, so Windows SmartScreen warns the first time you run a downloaded copy. Each release lists the SHA-256 of its exe; check it, then choose "More info" and "Run anyway". The version is shown at the foot of the Settings window.

To build it yourself:

```
dotnet build
dotnet test
dotnet publish src/Logbook.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false
```

## Where things are

- [docs/](docs/) is the spec: [grammar](docs/grammar.md), [record schema and file format](docs/file-format.md), [locking](docs/locking.md), [main window](docs/main-window.md), [export](docs/export.md), [settings](docs/settings.md) and [development notes](docs/development.md) on layout, decisions, build and release.
- [TODO.md](TODO.md) is the roadmap: what is left before 1.0, the backlog, and what is out of scope.
- [CLAUDE.md](CLAUDE.md) holds the conventions for working in this repo.
- `src/Logbook.Core` is the UI-free library (model, parser, crypto, file store, query, export); `src/Logbook.App` is the WPF tray app; `tests/Logbook.Tests` holds the Core tests.

## License

MIT, see [LICENSE](LICENSE).
