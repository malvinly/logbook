# Roadmap

Logbook is feature complete for its purpose: capture with a hotkey, an encrypted append-only log in a synced folder, filtered timelines on demand. What is listed here is what is still planned, what is parked, and what is deliberately out of scope. The spec lives in [docs/](docs/).

## Before 1.0

- Let the user choose the data file location freely. Today the first-run picker offers only the folders named by `OneDriveCommercial`, `OneDrive` and `USERPROFILE`, and the settings name the file relative to that root. Instead:
  - First run shows a plain file picker: any folder, a new or an existing `logbook*.jsonl`. No sync-service detection and no warning about unsynced folders; where the file lives is the user's responsibility. The app keeps assuming the folder might be synced, so everything that makes the file sync-safe stays as it is: append-only writes, write-through, the file never held open, placeholder hydration, the conflict-copy sibling picker and the "still downloading" dialog.
  - The absolute path of the chosen file is kept per Windows profile in `%LOCALAPPDATA%\Logbook\data-file`, through the existing `LocalMarker` helper. It is the only thing that finds the file at launch. When it is missing, the first-run picker runs; when it points at a file that is gone, the "Log file not found" dialog runs it. Browsing to an existing file picks up the `logbook.settings.json` beside it, so losing the profile costs one dialog, never data or settings.
  - The settings file stays beside the data file and stops naming it: `syncRootEnvVar` and `dataFileRelativePath` are removed, with the "data file outside the synced folder" check and dialog. Choosing a different file in the sibling picker updates the pointer, not the settings.
  - Plaintext exports (CSV and decrypted JSONL) default to a folder outside the log's own folder and ask before writing inside it, in place of "outside every synced root"; `KnownSyncEnvVars` and the root list go away. The wording keeps saying why: the log's folder may be synced, and synced storage is searchable by people who are not you.
  - Settings gets a read-only "Data file" row with a "Choose another…" button that reruns the picker and rewrites the pointer. There is no built-in move: moving the file is done in Explorer with the app quit, then "Choose another…" or the not-found dialog points the app at it. Copying a file a sync client may be mid-upload on is not the app's job.
  - No migration code. The settings files that exist today belong to dummy logs.
  - Docs in the same commits: [docs/settings.md](docs/settings.md) (contents, first run, how the file is found), [docs/development.md](docs/development.md) (the synced-folder constraint becomes a recommendation; the Files section drops the variables), [docs/file-format.md](docs/file-format.md), [docs/export.md](docs/export.md) and the README.
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

Tests:

- Two leftover-punctuation cases now take the separator path and their names are stale.
- Missing cases: a capture of a line that is only a possessive token; `at:yesterday's,#a`; capture-side `said #a,"x"`; the 60 s cap test does not assert that the fallback held.

Build:

- `PublishTrimmed` once the source-generated JSON context is proven to cover every serialised type.
- Windows 10 is untested; the Mica and rounded-corner fallback has only been reasoned about.

## Out of scope

- AI auto-tagging. Sending names and incidents to a third-party API is a policy problem, and recency-ranked autocomplete over a small tag set solves the real need.
- Data-key rotation on passphrase change. A change re-wraps the same data key, so a copy of the file made before it still opens with the old passphrase and decrypts every later record. Rotating would mean re-encrypting every record into a new file under a new key, a whole-file rewrite that contradicts the append-only rule and would have to be reconciled with the sync folder. [docs/file-format.md](docs/file-format.md) tells the user about old copies instead.
- Hash chains or any tamper evidence. You are the only person who could forge the log.
- SQLite, a persisted index, or full-text ranking. A few thousand records in a list is the design.
- Prefix tag matching. Exact plus autocomplete is strictly better.
- Sharing, multi-user, sync conflict merging, permissions. One user, one machine.
- CLI or script input, integrations writing entries. Capture is human and manual.
- Rich text, attachments, duration or numeric value fields, file compaction, passphrase recovery.
