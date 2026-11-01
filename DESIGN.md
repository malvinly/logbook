# Logbook — Design

A personal, single-user Windows tray app for recording decisions and events several times a day, so you can later answer "when did X happen", "how many times has X happened since Y", and "did I tell anyone about Z", and hand someone a dated list that proves it.

This document is the source of truth for the app. Anything not in Phase 1 is listed under Later or Not Building so it isn't forgotten and isn't accidentally built early.

## 1. Purpose and driving scenarios

Logbook exists because a text file in Notepad gets lost, and anything that takes more than a hotkey and one line to record stops being used within a week. A log with gaps is worse than no log when someone asks for proof.

Three scenarios shaped every decision below:

- **Unanswered requests.** You ask for something, you're told it'll be looked into, nothing happens. You ask again. Later you need to show each ask and its date, with the gaps between them.
- **Recurring problems you can't see the history of.** You report the same issue to another group repeatedly but have no access to their ticket history. You need your own count and dates, with reference numbers.
- **Things you'll be asked about months later.** Something happens that you'll need an accurate, dated account of well after the fact, along with the reason you thought it mattered at the time, rather than a vague memory.

Decisions and events are the same record. There is no distinction in the schema; tags carry whatever distinction you want.

## 2. Non-goals

- Not shared. One user, one machine. No sync conflict handling, no multi-user, no permissions.
- Not tamper-evident. You are the only person who could forge it; nobody will audit it.
- Not automated. Capture is human and manual. No CLI, no script input, no integrations writing entries.
- Not a database. The entire log fits in memory; there is no index on disk.

## 3. Constraints

- .NET 10, WPF, Windows only. No third-party installs on the target machine, so the app is a self-contained build you drop in a folder; NuGet packages are fine.
- The data file lives in a folder managed by a file sync service, because that is the only backed-up location. Sync services sync files; they do not understand databases. The file format is designed to be sync-safe: append-only, one record per line, never rewritten in place (the single exception is line 1 on a passphrase change, written over itself at the same length; section 8).
- The Windows user account may change. Nothing may depend on the user profile surviving: no DPAPI, no per-user registry secrets, no absolute paths into `C:\Users\<login>`.
- The author must be able to understand every line. Prefer the boring implementation. Keep the core free of UI dependencies so it can be ported (likely to TypeScript) later.

## 4. Solution structure

- `Logbook.Core` — class library, `net10.0`, no UI framework reference. Record model, grammar parser, crypto, file store, query, export rendering. This is the part worth porting.
- `Logbook.App` — WPF tray app. Hotkey, capture popup, main window, settings. Thin; calls Core.
- `Logbook.Tests` — unit tests for Core. The parser, the file round-trip, the truncated-line loader, and the export renderer are the four things that must have tests before the UI exists.

## 5. Capture

A global hotkey (default `Ctrl+Alt+L`, configurable) pops a single-line text box centered on the active monitor, launcher-style. Type, Enter, gone. Escape cancels.

The line is parsed with the grammar in section 6. Whatever isn't a token becomes the summary. `occurredAt` defaults to now unless an `at:` token is present. `recordedAt` is always now.

A small "more" expander (`Ctrl+Enter` or a chevron) reveals optional fields: reason (multi-line), additional tags, additional people, refs. Nothing here is required.

After Enter, a toast appears for ~5 seconds: **Logged — click to edit.** Clicking opens that entry in the main window's detail pane. This is how typos and second thoughts get fixed; there is no separate undo.

If the app is locked (section 9) when the hotkey fires, the passphrase prompt appears first, then the capture box. Nothing is ever held in memory unencrypted and unsaved.

## 6. Grammar

One grammar serves both capture and query. Learn it once.

### Tokens

- `#tag` — a tag. Lowercase-kebab; the parser lowercases and replaces whitespace/underscores with hyphens. In capture it attaches the tag; in query it filters to entries having that tag (exact match; multiple `#` tokens are ANDed).
- `@person` — a person. Same normalisation as tags. In capture it attaches the person; in query it filters.
- `at:<when>` — capture only. Sets `occurredAt`. Forms: `at:2026-10-06`, `at:2026-10-06,15:00`, `at:yesterday`, `at:yesterday,3pm`, `at:15:00` (today at that time), `at:mon` (the most recent Monday before today; on a Monday that is last week). Date without time stores 12:00 local; use the compact export when the time isn't meaningful.
- `since:<date>` / `until:<date>` — query only. Inclusive bounds on `occurredAt`. Date forms: ISO `2026-10-06`, `today`, `yesterday`.
- `last:<n><unit>` — query only. `last:30d`, `last:12w`, `last:6m`. Shorthand for `since:`. Inclusive of today and of the day N days ago, so `last:7d` spans eight calendar days.
- `"quoted phrase"` — query only. Substring match on the exact phrase over summary and reason. Quoted phrases, like bare words, match case-insensitively.
- Anything else — capture: summary text. Query: case-insensitive substring match over summary and reason; multiple bare words are ANDed.

### Collision rules

- A `#` or `@` starts a token only at the start of the line, after whitespace, or right after opening punctuation (`(` or `"`), and only when followed by a letter. So `(#asked)` and `"@j-doe"` are tokens, while `ticket #12345`, `C#`, `someone@example.com`, and `@ 3pm` are all plain text. In a query a `"` at a word start opens a quoted phrase first, so `"@j-doe"` there is the phrase, not a person.
- A tag or person token ends at whitespace, at end of line, or at a `,` or `;` (Phase 7), so `@j-doe,@a-smith` is two people, not one named `j-doe,@a-smith`. Trailing punctuation (`,` `.` `;` `:` `)` `"`, and from Phase 7 also `?`, `!` and a possessive `'s`) is stripped from the token and left in the text. In a capture the space before it goes as well, so `asked @j-doe, then` stores the summary `asked, then`, not `asked , then`; a space typed before the punctuation (`asked @j-doe , then`) is kept. Punctuation that a token leaves behind with nothing but whitespace around it is dropped too (Phase 7): `done (#asked)` stores `done`, not `done ()`, and in a filter `(#asked)` or `@j-doe,` requires no literal `(` or `,` in the text.
- Keyword tokens (`at:`, `since:`, `until:`, `last:`) are recognised only at a word boundary followed immediately by a non-space value. Trailing punctuation is stripped from the value the same way as from a tag (Phase 7), so `at:yesterday.` and `last:7d,` parse.
- To put a literal `#word` or `@word` in a summary where it would otherwise be a token, prefix with a backslash: `\#notatag`. The backslash is dropped only there (Phase 7); where the `#` or `@` could not start a token anyway, as in `\\server\#share`, it is kept as typed.

### Conventions (not enforced, write them down)

- `@person` means **anyone involved**, in any role: the person you told, the person you asked, the person who did the thing. The grammar does not distinguish roles.
- The **verb goes in a tag**: `#asked`, `#told`, `#decided`, `#escalated`, `#reported`. "Did I tell anyone about Z" becomes `#told Z`; "everything involving this person" becomes `@person`.
- Keep tags coarse. A tag you will use once is a word in the summary, not a tag.

## 7. Record schema

Plaintext JSON, one object per record, before encryption. Property names are camelCase. Unknown properties are ignored on load so fields can be added later without a format bump.

```json
{
  "id": "01JBK3Q9W6X2Y8Z4V5T7R1N0M3",
  "rev": 1,
  "occurredAt": "2026-10-06T10:14:03-05:00",
  "recordedAt": "2026-10-06T10:14:09-05:00",
  "summary": "Asked for access to the staging environment; told it would be looked into",
  "reason": null,
  "tags": ["asked", "staging-access"],
  "people": ["j-doe"],
  "refs": [],
  "relatedTo": [],
  "remindAt": null,
  "deleted": false
}
```

- `id` — ULID. Sortable, no coordination needed. Never reused.
- `rev` — starts at 1. Every edit appends a new line with the same `id` and `rev + 1`. The loader keeps the highest `rev` per `id`.
- `occurredAt`, `recordedAt` — ISO-8601 with UTC offset (`DateTimeOffset`). `recordedAt` of `rev 1` is the immutable "when I wrote it down". Later revisions carry their own `recordedAt`, which is the edit time. Rendering uses the stored offset, not the current machine's zone.
- `summary` — required, single line.
- `reason` — optional, may be multi-line.
- `tags`, `people` — lists of normalised strings. May be empty.
- `refs` — list of strings: ticket numbers, URLs, file paths, message links. Free-form; rendered verbatim.
- `relatedTo` — list of entry ids (ULIDs) this entry follows up on, set by the Follow up button (section 10). It records the thread, which tags cannot: a tag says what an entry is about, not which earlier entry it continues. May be empty; a line written before this field existed has no such property and loads with an empty list. The timeline export does not show it; the decrypted export writes it.
- `remindAt` — optional ISO-8601 time with UTC offset (`DateTimeOffset`), null when no reminder is set. Stored and rendered with its own offset like `occurredAt`; only the "has it passed" check compares instants. Set, changed and cleared by new revisions like any other field, so the latest revision decides; a line written before this field existed loads as null. The timeline export does not show it; the decrypted export writes it.
- `deleted` — a tombstone is a record with `deleted: true` and `rev + 1`; other fields may be omitted. Deleted entries are hidden everywhere except the revision view and the "include history" plaintext export.

## 8. File format

One file, default name `logbook.jsonl`, in the configured synced folder.

### Line 1 — header (plaintext JSON)

```json
{
  "format": "logbook/1",
  "fileId": "01JBK3Q9W6X2Y8Z4V5T7R1N0M3",
  "createdAt": "2026-10-06T09:00:00-05:00",
  "kdf": { "alg": "pbkdf2-hmac-sha256", "salt": "<base64 16 bytes>", "iterations": 600000 },
  "wrappedKey": "<base64 nonce‖ciphertext‖tag>"
}
```

### Lines 2..n — records

Each line is `base64(nonce ‖ ciphertext ‖ tag)` of the UTF-8 JSON record, encrypted with AES-256-GCM under the data key, with a fresh random 96-bit nonce per line and a 128-bit tag. No additional authenticated data. Lines are separated by `\n`.

### Key hierarchy

- A random 256-bit **data key** is generated once when the file is created. All records are encrypted with it.
- The **passphrase** is run through PBKDF2-HMAC-SHA256 (salt and iteration count from the header) to produce a 256-bit **key-encryption key**. The data key is wrapped (AES-256-GCM) with that and stored in the header as `wrappedKey`.
- Unlock = derive KEK from passphrase, unwrap data key. If the GCM tag on `wrappedKey` fails to verify, the passphrase is wrong. There is no separate verifier field; the wrap is the verifier.
- Changing the passphrase rewrites only line 1. Records are untouched. (Phase 1 ships the hierarchy; the change-passphrase UI is Phase 5, under Tools → Change passphrase in the main window.) The data key does not change: a new salt is drawn, the key-encryption key is derived from the new passphrase with the iteration count the header already has, and the same data key is wrapped again; `format`, `fileId` and `createdAt` are kept. Because the same data key is re-wrapped, a copy of the file made before the change (sync version history, a conflict copy, a backup) still opens with the old passphrase, and its data key decrypts every record written since; rotating the data key would need a whole-file rewrite and is not built (section 14, Not building).
- How line 1 is rewritten, the one deliberate exception to the append-only rule: **in place, at the same byte length**, so not one byte of lines 2..n moves or changes and the file is never truncated or rewritten whole. The header JSON escapes `+` in base64 as `\u002B`, so its length depends on its random values; the new salt is drawn until it needs no escaping, and the key is wrapped again with fresh nonces until the line comes out exactly as long as the old one, in the same JSON form `Create` writes. Before writing: the current passphrase is verified against the header on disk (a wrong one is reported, nothing written), that header must unwrap to the open log's data key, and the new header is verified in memory to unwrap it with the new passphrase. The write opens the file with `FileMode.Open`, `FileAccess.ReadWrite`, `FileShare.Read`, `FileOptions.WriteThrough`, checks that line 1 is still the bytes it read, writes the new bytes at offset 0, flushes and closes, with the same retry schedule as appends; the file is not held open between operations. Line 1 is then read back and must match and unwrap; if not, the previous bytes (kept in memory until then) are written back and the failure reported. A retry that finds line 1 already holding the new bytes (an earlier attempt wrote them and then failed to flush or close) takes the write as done and goes on to the read-back; the write-back goes only over the new bytes, so a line 1 that holds neither header is left alone; and when the header is left in an unknown state the message says to try the new passphrase first. Refused, with nothing written: an empty new passphrase, a header that is not byte for byte what this app writes for its own values (an unknown property, other spacing or order) or whose length a rewritten one cannot match (one this app did not write; there is no fallback to a whole-file rewrite), a file on disk with another data key, and any time a capture is pending or a write is in flight (the same checks as Lock now), since a header rewrite racing an append is not worth reasoning about. After the change the app stays unlocked with the same data key.
- There is no recovery. If the passphrase is lost, the log is lost. The first-run screen says this in plain words and tells you to write the passphrase down somewhere physical, since the machine and its password manager may not be yours forever.

### Write rules

- Open with `FileMode.Open`, `FileAccess.Write`, `FileShare.Read`, `FileOptions.WriteThrough` and seek to the end. Write the line and `\n` in one call, flush, close. The file is never held open between writes. A data file that has gone missing (deleted or moved by the user or a sync client) is never recreated by an append; the write fails, is retried and then queued like any other failure, so the user hears about it.
- Sync clients briefly lock files during upload, and a scanner replacing the file can report access denied for a moment. On `IOException` or `UnauthorizedAccessException`, retry with backoff: 100 ms, 200 ms, 400 ms, 800 ms, then 1 s, up to 10 attempts (about 6.5 s in all); longer outages are the 30-second timer's job, because the user must hear about an unsaved capture sooner than that. On final failure, keep the record in an in-memory pending queue, show a visible error toast, and retry the queue on the next capture and on a 30-second timer. The user must always know if something is unsaved. Losing a capture silently is the one unforgivable bug in this app.
- Writes never sleep while holding the lock that reads take, so the main window stays responsive while a write is retrying.
- If the file did not end with `\n` when it was opened (a crash cut a write short), the first append writes `\n` first, so the new line never fuses with the partial one. A complete line that only lost its newline is recovered by this; a partial one becomes a counted unreadable line.
- Exit with unsaved captures asks first. A Windows sign-out gets one more write attempt and is refused while anything is still unsaved, so the loss is never silent.
- The file is never truncated, rewritten, or compacted by the app; the in-place header rewrite of a passphrase change (key hierarchy, above) touches line 1 only. Revisions accumulate. At three entries a day with occasional edits this is a few hundred KB per year.

### Load rules

- Read all lines. Parse the header; refuse to open on a bad or unknown `format`.
- Decrypt each record line. A line that fails base64 or GCM verification in the **middle** of the file is counted and skipped; the main window shows "N unreadable lines" so corruption is visible, not hidden. A **trailing** line that is truncated is skipped silently with a log entry; it is the expected result of a crash mid-write. Truncated means exactly this: the last line has no terminating `\n`, or it is shorter than any valid record line could be (the 40 base64 characters of a nonce and a tag). A full-length last line that ends in `\n` was written whole, since a line and its newline go in one write, so if it fails it is counted as unreadable like a middle line.
- Group by `id`, keep the highest `rev`. Drop tombstoned ids from the live list but keep them for the revision view. The edge cases, in file order: on equal revs the later line wins; a tombstone followed by a higher non-tombstone rev brings the entry back; a lower rev after a higher one is ignored for the live list (it still appears in the revision view).
- Everything lives in `List<Entry>` for the life of the process. Queries are LINQ. No persisted index.

### Sync-service specifics

- Some sync clients keep only a placeholder locally and download the file on first access. On launch, check the file's attributes for a placeholder (`Offline` or recall-on-data-access; the reparse-point flag alone proves nothing, since sync clients leave it on fully downloaded files too). The file is not read before unlock (section 9), so the first read at unlock is what hydrates it; when the launch check saw a placeholder, a balloon after that unlock says the log was downloaded and recommends marking the folder as always available offline.
- On launch, glob the data folder for `logbook*.jsonl`, leaving out `logbook-export-*` names (the decrypted export's own default name, section 11), which are never logs. If more than one file matches (sync conflict copies, old-profile re-syncs), do not guess: show each with its header `createdAt` and line count and ask which is the real one.

## 9. Passphrase and locked state

- The app starts locked. The tray icon shows a lock glyph. No file is read until unlock.
- Unlock is prompted at launch. If dismissed, the app stays in the tray locked, and the next hotkey press prompts for the passphrase before showing the capture box.
- After unlock, the data key stays in process memory until the app exits or is locked, by Lock now or by idle auto-lock.
- Idle auto-lock (Phase 5) takes the Lock now path below after `idleLockMinutes` (section 12) without input, and the next hotkey takes the same "prompt first, then capture" path. "Without input" means no keyboard or mouse input to the Windows session at all (`GetLastInputInfo`), not just to Logbook: the point is that you walked away from the machine, and locking while you work in other apps would make every capture ask for the passphrase and kill the habit the app exists for. It is checked once a minute while unlocked and the check stops on lock; due means at least N minutes since the last input, and a last input that appears to lie in the future (a clock change) is not idle. A check never shows a dialog: while anything is pending or being written it skips and tries again next minute, since the tray icon already shows the pending count, and while one of the app's modal dialogs is open it waits too, so a half-made decision there is not closed under you, though not forever: once twice the idle limit has passed without input it locks anyway and the dialog closes with it (Phase 7). Otherwise it closes what Lock now closes, so a capture box with typed text and an unsaved edit in the detail pane are lost, which is "clear the data key after N minutes" taken literally.
- "Lock now" in the tray menu returns to the startup state: the open log (data key and decrypted entries) is dropped and the data key zeroed in memory, every window that shows entries closes (the toasts too, since the "Logged" toast shows a summary), the lock glyph shows, and the next hotkey prompts first. Unsaved captures live only in the open log, so locking is refused with an explanation while anything is pending or still being written, or while a passphrase change is rewriting the header.
- Workstation lock (Phase 7): when Windows locks the session or signs the user out (`WM_WTSSESSION_CHANGE`), the log is locked the same way as by Lock now, since walking away is usually what locking the workstation means. The same refusals apply: if a capture is pending, a write is in flight or a passphrase change is under way at that moment, the lock is postponed and taken as soon as that work is done, as the idle check does.
- The passphrase is never written anywhere. The derived key is never written anywhere.

## 10. Main window

Opened from the tray icon, from the toast, or by a second hotkey (default `Ctrl+Alt+Shift+L`).

### Filter bar

One text box using the section 6 grammar. Results update live as you type (debounced ~150 ms). A date-range picker beside it rewrites the `since:` / `until:` tokens in the box rather than being a separate hidden filter.

The header above the results always shows the count and range: **12 entries · Jul 14 – Oct 28, 2026**. "How many times" is a driving question; the number must never be hidden in the export.

### Saved queries

A row of buttons under the filter bar, one per saved query (a name and a filter string), in the order they were saved. Clicking one puts its filter into the box exactly as if typed, so the results, the count header and the export follow it. "Save filter as button" saves the current filter text under a name you type (the filter itself is offered as the name); saving under a name already in use replaces that button's filter in place. Right-clicking a button offers Remove. Nothing is seeded: tag names are your own, so the three driving scenarios become buttons when you save them (for example `#asked @j-doe`, `#reported #printer`, `last:7d`).

### Results list

Newest first, grouped under day headers. Each row: time, summary, tag chips, person chips, and a small marker if a reason or refs exist. Selecting a row opens the detail pane.

### Detail pane

All fields editable: occurredAt (date and time pickers), summary, reason, tags, people, refs. Save appends a new revision. Delete appends a tombstone after a confirm. The revision caption and the "Follows up on" line stay at the top of the pane and the Save, Follow up and Delete buttons stay at its foot; only the fields and the revisions between them scroll, so the actions are in reach at any window height.

A **Follow up** button beside Save opens the capture box pre-filled with the selected entry's tags and people as `#tag @person` text (caret at the end, so the chip strip shows them) and a caption naming the original; the new entry's `relatedTo` holds the original's id. It sits in the detail pane rather than on each row because the pane already holds the per-entry actions and a row button would need a hover template the Fluent list does not have. The pre-fill comes from an explicit button, so it is not memory in the sense of section 16: it lives only in that capture box, and Escape drops it along with the link. If a capture box is already open, it is brought forward unchanged rather than overwritten. No tag is added for the thread. A stored name that would not parse back as a token goes into the "more" section's additional tags or people instead. An entry with `relatedTo` shows one line per original under the revision caption, **Follows up on: Tue Oct 6, 2026 10:14 AM — summary**, which selects the original when clicked (clearing the filter if it hides it), or says the original has since been deleted. Nothing is shown on the row. The original lists its follow-ups the same way (Phase 7): one line per entry whose `relatedTo` names it, **Followed up by: Thu Oct 15, 2026 2:30 PM — summary**, oldest first, which selects that entry when clicked.

A **Reminder** row sets `remindAt`: a date picker and a time box in the style of occurredAt, and a Clear button that empties them. Like every other field it is saved by Save as a new revision. A new reminder takes the local offset at the date and time typed; an edited one keeps its stored offset, as occurredAt does. While the app is unlocked, the reminders are checked at unlock and every 30 seconds after, and the check stops on lock, since the entries cannot be read then. When one has passed, a tray balloon titled "Reminder" says that a reminder is due and when it was due, never the entry's summary (Phase 7; Windows keeps balloon text in its notification history long after the app has locked); clicking it opens the entry in the main window, also after Windows has taken the balloon off the screen, until another balloon replaces it. One balloon per check, earliest first. Each reminder shows once per unlock: the record of what was shown is dropped with the log on lock and exit, so a passed reminder that was never cleared shows once more after the next unlock or restart, reminders that passed while the app was closed or locked included, and the pane says so under the field. Clearing the reminder (a new revision) is what ends it; moving it to a new time makes it a new reminder. No snooze, no repeat, no sound.

A **Revisions** section lists every prior revision of this entry, read-only, with its `recordedAt`. The current fields are shown against the previous revision with changed fields highlighted. The list row carries an "edited" marker only because this view exists; a marker without the history would be meaningless for the purpose this app serves.

### Autocomplete

Typing `#` or `@` in the filter bar, the capture box, or the detail pane shows suggestions. Ranked by most recently used, capped at 8, prefix-filtered as you type over the full set. Cap and ranking (recent vs. frequent) are settings.

## 11. Export

### Copy as timeline

Takes the **current filtered set**, flips it to oldest-first, and puts a plain-text block on the clipboard. Plain text, Unicode bullets, no markdown; mail and chat clients mangle markdown on paste.

```
#staging-access — 3 entries, Oct 6 – Oct 20, 2026

• Tue Oct 6, 2026 10:14 AM — Asked for access to the staging environment; told it would be looked into.
• Thu Oct 15, 2026 2:30 PM (9 days later) — Followed up; no response to the first ask.
• Tue Oct 20, 2026 9:02 AM (5 days later) — Asked again in standup; told it would be this week.
      Reason: wanted it on record before the release freeze.
```

Rules:

- Header line is the filter text as typed (or "All entries"), then the count and the occurredAt range of the set. When the filter was put there by a saved-query button and has not been edited since, the button's name stands in for the filter text (Phase 7), so a weekly digest reads `This week — 2 entries` rather than `last:7d — 2 entries`.
- Each line: weekday, date, time in the entry's stored offset, em dash, summary. Refs are appended as `[INC0041233]` when present.
- Reason, when present, is indented beneath its line.
- Tags and people are **not** included by default. They are indexing for you, not information for the reader; the **Tags and people** toggle below adds them when the filter is broader than one person.

Toggles in the export panel, remembered between uses:

- **Show gaps** — appends `(N days later)` after the timestamp, computed from the previous entry in the set. Off for the first entry. A gap of 0 reads `(same day)` and a gap of 1 reads `(1 day later)`. Gaps are calendar days between the entries' stored dates in export order and are never negative: when entries in different offsets put a later instant on an earlier stored date, the gap is `(same day)`.
- **Group by month** — inserts a second header line with per-month counts: `Jul (1) · Aug (3) · Sep (1) · Oct (2)`. When the set spans more than one year, each month carries its year: `Jul 2025 (1) · Jan 2026 (2)`. The list below is unchanged.
- **Compact** — drops time of day and the weekday: `• Oct 6, 2026 — …`.
- **Tags and people** — appends the entry's tags and then its people to its line, after the refs, each with its `#` or `@` and separated by single spaces: `• Tue Jul 14, 2026 9:00 AM — Reported the printer outage to facilities [INC0040001] #reported #printer @a-smith`. Order is as stored. An entry with no tags and no people is unchanged, and the header line and the reason are unchanged.

"Copy to clipboard" puts two formats on the clipboard in one go (Phase 6): the plain text above, unchanged, and the same timeline as HTML, so mail clients paste bold dates and real bullets while plain-text targets keep taking the text. The HTML follows the same toggles and says the same words: the header line (and the month counts after a `<br>` when Group by month is on) as one paragraph, then a `<ul>` with one `<li>` per entry, its date part in `<b>`, then the gap, the em dash, the summary, refs, tags and people exactly as in the text; the reason is a `<div>` inside its item, `Reason: ` first and a `<br>` per line break. All text is HTML-escaped; there are no classes, no styles and no external resources. An empty set is the header paragraph alone. The HTML goes on the clipboard as UTF-8 in the CF_HTML envelope Windows requires (a `Version:0.9` header with byte offsets and `<!--StartFragment-->` / `<!--EndFragment-->` markers). The preview in the panel stays plain text.

### CSV export

"Save as CSV…" in the export panel (Phase 6) writes the same current filtered set as a CSV file, one row per entry, oldest first, for a spreadsheet.

- Columns, in this order, under a header row with these names: `occurredAt`, `recordedAt`, `summary`, `reason`, `tags`, `people`, `refs`, `relatedTo`, `remindAt`, `id`.
- Timestamps are the ISO-8601 round-trip form (`"O"`) with the stored offset, never converted: `2026-10-06T10:14:00.0000000-05:00`. A null `reason` or `remindAt` is an empty field.
- Lists are joined by a single space (`tags`, `people`, `relatedTo`, none of which can hold a space), except `refs`, which may contain spaces and are joined by `; `.
- RFC 4180: comma separated, CRLF after every row including the last, a field enclosed in double quotes when it holds a comma, a double quote, CR or LF, with its double quotes doubled; line breaks inside a reason are kept as stored. An empty set is the header row alone.
- A text cell (`summary`, `reason`, `refs`) that starts with `=`, `+`, `-` or `@` is written with a leading `'` (Phase 7), because spreadsheet apps read such a cell as a formula; those cells are therefore not verbatim, and the panel's one sentence says so. The other columns cannot start with those characters.
- Encoding is UTF-8 with a byte order mark, so spreadsheet apps detect it.
- The file is plaintext, so it follows the Export decrypted rules: the save dialog defaults to a folder outside every synced root, the panel says why in one sentence, choosing a synced folder asks first, the log itself and `logbook*.jsonl` names are refused, and the folder is remembered in the same `%LOCALAPPDATA%` marker as the decrypted export. The file name is `logbook-export-YYYY-MM-DD.csv`.

### Export decrypted

Tools → Export decrypted. Writes a plaintext JSONL file (one record per line, latest revision per id, tombstoned ids excluded) to a location you choose. A checkbox includes the full revision history, in which case every line in the file, tombstones included, is written in original order.

The save dialog defaults to a folder **outside** the synced root and explains why in one sentence: the export is unencrypted and contains notes about people and situations; synced storage is searchable by people who are not you. The file name is `logbook-export-YYYY-MM-DD.jsonl`.

## 12. Settings, startup, and the moving user profile

- Settings are a plain JSON file **beside the data file** in the synced folder, not in `%APPDATA%`. They contain no secrets. Contents: data file path (relative to the sync root), the environment variable to resolve the sync root from (configurable, since each sync client exposes its own), hotkeys, autocomplete cap and ranking, `idleLockMinutes` (lock after that many minutes without input, section 9; 0 never locks; default 60 from Phase 7, 30 before; edited in the Settings window as a whole number from 0 to 1440), `theme` (`system`, the default and what a file without the property gets, follows the Windows light or dark setting; `light` or `dark` forces one; edited in the Settings window as "Theme: Follow Windows / Light / Dark"; applied at startup and when Settings is saved, and open windows repaint at once, as does the tray icon from Phase 7, which until then kept its startup colours whether the theme changed from Settings or from Windows; a forced theme ignores Windows changing mode), export toggles, saved queries (section 10). The last export folder is remembered per Windows profile in `%LOCALAPPDATA%` instead, because it is an absolute local path that would point a reader of the synced file at the plaintext copy.
- On launch, if the app is not registered to start with Windows for the current user, it offers once to register (a shortcut in the Startup folder). A login change wipes that shortcut; the offer is how it comes back.
- Single instance: a named mutex. A second launch activates the first instance's main window and exits.
- `RegisterHotKey` failure is surfaced immediately with a balloon naming the hotkey and offering Settings. A silently dead hotkey means a week of nothing logged.
- Tray balloons queue (Phase 7): one shows at a time, the next waits until the current one has closed, and each keeps its own click action, so a hotkey failure at startup is not written over by the sync or reminder balloon that follows it.

### First run

1. Choose or create the data file (file picker defaulting to the detected sync root).
2. If creating: set a passphrase twice, read the no-recovery warning, confirm.
3. If opening an existing file: enter the passphrase.
4. Offer startup registration.
5. Show the hotkey and the grammar cheat sheet once.

## 13. Build order

1. `Logbook.Core`: record model, ULID, grammar parser with tests covering every collision rule in section 6.
2. Crypto: KDF, data-key wrap/unwrap, per-line encrypt/decrypt. Round-trip tests. Wrong-passphrase test.
3. File store: append with retry, loader with truncated-trailing-line and corrupt-middle-line tests, revision folding.
4. Query and export renderer, with tests for each toggle against a fixed fixture set.
5. `Logbook.App`: tray icon, lock/unlock prompt, single-instance mutex, hotkey registration with failure surfacing.
6. Capture popup, inline parsing preview, "more" expander, toast.
7. Main window: filter bar, list, detail pane, revisions, autocomplete.
8. Export panel and decrypted export.
9. Settings, first run, startup registration, sync hydration and multi-file checks.

Steps 1–4 are usable from a test harness before any UI exists and are the part most worth getting right.

## 14. Phases

Each phase is approved as a whole. Once a phase is started, every step in it is built, tested and committed without stopping for approval between steps; the summary comes at the end of the phase. Later phases are not started unasked.

### Phase 1 (done)

Everything above.

### Phase 2 (done) — make it something you will keep using

The app has to look good before any further feature matters, and the small grammar items ride along. No change to the file format.

1. Design pass over the whole app. Phase 1 restyled only the basics (text boxes, buttons, chips, list rows) and left the rest on WPF's default look, which reads as dated: the menu bar, date pickers, combo boxes, scroll bars, checkboxes, message boxes, and the window chrome of the main window and dialogs. The brief:
   - Direction: Windows 11 Fluent, as in PowerToys Command Palette (the successor of PowerToys Run): a translucent Mica or acrylic surface, rounded corners, one large input with results beneath it, generous padding, Segoe UI Variable, light or dark following the OS with the system accent colour.
   - Base: WPF's built-in Fluent theme (first-party, so section 16's no-third-party-suite rule holds). It restyles every stock control, handles light and dark and the accent colour. The hand-rolled control templates from Phase 1 that would fight it are removed; Logbook's own tokens, chips and markers stay in Logbook's own dictionaries (section 16), so the dark theme remains a second dictionary of colour tokens. Verify the theme's status on .NET 10 before building on it.
   - Capture box: a launcher. Mica or acrylic, rounded, centred high on the active monitor, one large input, with the chip strip and the "more" section as rows beneath it.
   - Toast: Windows 11 notification style.
   - Main window: Fluent list and detail pane in the manner of PowerToys Settings, with the count header kept prominent.
   - Message boxes come from Windows, so they become a small dialog window of the app's own in the same style.
   - Mica and rounded corners on borderless windows need two DWM calls; they work on Windows 11 and must degrade to a plain surface on Windows 10 rather than fail.
   - Done means: no control falls through to the stock look, both themes work, and the app is recognisable as a sibling of the launcher it borrows from.
2. "Lock now" in the tray menu: drop the data key, show the lock glyph, and reuse the "prompt first, then capture" path on the next hotkey.
3. Grammar: let `#` and `@` start a token after opening punctuation (`(`, `"`) as well as after whitespace, so `(#asked)` and `"@j-doe"` are tokens. Today they are plain text under the whitespace-only rule in section 6.
4. Grammar: when trailing punctuation is stripped from a token, drop the space before it in the summary, so `asked @j-doe, then` stores `asked, then` rather than `asked , then`.

### Phase 3 (done) — the three driving scenarios as buttons

Pure UI over the existing query and renderer, plus one settings field. No risk to the data.

1. Saved queries — name → filter string, one click each, stored in the settings file. Nothing is seeded (section 10); the three driving scenarios become buttons when you save them.
2. Weekly digest = saved query `last:7d` + export; falls out of saved queries for free.
3. Export toggle to include people and tags per line (useful when the filter is broader than one person).

### Phase 4 (done) — the first format additions

Both items add a field to the record: `relatedTo` a list that may be empty, `remindAt` a nullable time. The schema's unknown-property tolerance means no format bump, but it is the first time the model changes, so they land together with their own Core tests.

1. `relatedTo` — list of ids linking follow-ups to the original entry, plus a "Follow up" button that opens capture pre-filled with the entry's tags and people. Built in the detail pane rather than on each row; section 10 says why. Tags conflate topic with thread; this separates them.
2. Follow-up reminder — a single nullable `remindAt` on an entry and a tray balloon when it passes. Nothing more elaborate.

### Phase 5 (done) — security housekeeping

Both touch the key hierarchy and the lock path, so they get a review of their own rather than riding beside UI work.

1. Change passphrase — rewrite the header only.
2. Idle auto-lock — clear the data key after N minutes; next hotkey prompts.

### Phase 6 (done) — outputs and themes

Independent items, none urgent. The dark theme is much cheaper after Phase 2 has templated every control.

1. HTML clipboard format alongside plain text so mail clients get bold dates and real bullets.
2. CSV export.
3. Dark theme override in settings. Phase 2 already follows the Windows light or dark setting with a second token dictionary (`Theme.Dark.xaml`); what is left is a setting to force one or the other. Built as the `theme` setting in section 12: forcing merges WPF's `Fluent.Light.xaml` or `Fluent.Dark.xaml` in place of `Fluent.xaml` together with the matching token dictionary, only at startup and on Settings save, never from the Windows theme-change message.
Items 4 to 11, which this list held as notes while the phase was built, were decided on 2026-11-01: all of them are Phase 7 below, except data-key rotation, which is not being built (Not building, below).

### Phase 7 — the decisions from the Phases 2–6 review

Everything the review of Phases 2 to 6 left open, decided together on 2026-11-01. Each item's behaviour is written into its own section, tagged "(Phase 7)"; this list is the build order. No change to the file format. The docs went first: items 1 and 10 were committed before any code, so a reader of the design sees the target, not the drift.

1. Doc reconciliations where the code was right and the text was stale: CLAUDE.md and section 16 on the theme files, the line-1 exception and the token-start rule; section 16 on the stored timestamp form; section 8 on when hydration happens; Phase 3 on seeding.
2. Grammar (section 6), one rule group with its tests: a tag or person ends at `,` and `;`; trailing `?`, `!` and `'s` are stripped; keyword values are stripped the same way; punctuation-only leftovers are dropped in summary and filter; a backslash is kept where it escapes nothing.
3. `at:` takes the local offset at the date and time it names (section 16), with tests across a clock change.
4. Core moves with tests (section 16): the decrypted export's line selection and the idle tick-wrap check.
5. The reminder balloon shows no summary (section 10), and tray balloons queue (section 12).
6. Idle lock: default 60 minutes (section 12); locks anyway at twice the limit when a dialog is open (section 9); lock on workstation lock and sign-out (section 9).
7. CSV cells that would read as formulas get a leading `'` (section 11); the timeline header takes a saved-query button's name (section 11).
8. "Followed up by" lines in the detail pane of an original entry (section 10).
9. Window and taskbar icon from the tray badge (section 16); the tray icon redrawn on a theme change (section 12).
10. README rewritten to describe the app as it is.

### Not building

- AI auto-tagging. Sending names and incidents to a third-party API from a managed work machine is a policy problem, and recency-ranked autocomplete over a small tag set solves the real need.
- Data-key rotation on passphrase change. A change re-wraps the same data key (section 8), so a copy of the file made before it still opens with the old passphrase and decrypts every later record. Rotating would mean re-encrypting every record into a new file under a new key, a whole-file rewrite that contradicts the append-only rule and would have to be reconciled with the sync folder. Section 8 tells the user about old copies instead.
- Hash chains or any tamper evidence.
- SQLite, a persisted index, or full-text ranking. A few thousand records in a list is the design.
- Prefix tag matching. Exact plus autocomplete is strictly better.
- CLI or script input, multi-user, sync conflict merging, rich text, attachments, duration or numeric value fields, file compaction, passphrase recovery.

## 15. Things to remember

- The `@` convention: anyone involved, any role. The verb lives in a tag (`#asked`, `#told`, `#decided`, `#reported`).
- Date-only `at:` stores noon. Use compact export when the time isn't real.
- The decrypted export is sensitive. Keep it out of synced storage and delete it when done.
- Your own notes are discoverable in a dispute, and that cuts both ways. Write entries you would be comfortable reading aloud.

## 16. Implementation decisions

Settled up front so no coding session has to guess. Change them here first, then in code.

### UI framework

WPF. The app must look polished, and the main window (grouped results, tag chips, a detail pane with changed-field highlighting, a fading toast) is where WinForms would turn into weeks of owner-drawn GDI+ work that still looks dated. WPF gives vector rendering, native DPI scaling, styles, templates, `ItemsControl` grouping and storyboard animations for free. The hard parts (global hotkey, tray, single instance) are Win32 P/Invoke in either framework. WinUI 3 was rejected because it needs the Windows App SDK runtime or MSIX, which fights single-exe deployment; Avalonia because it is a third-party dependency for a Windows-only app.

WPF rules for this project: plain code-behind plus a hand-written `INotifyPropertyChanged` base class; no MVVM framework, no behaviors library, no third-party control suite. WPF's built-in Fluent theme is the base (merged as a dictionary; the `Application.ThemeMode` API is still experimental on .NET 10 and is not used). Logbook's own theme values live in the `Theme` folder and nowhere else: `Theme.xaml` holds fonts, type scale, radii and the control styles, and `Theme.Light.xaml` and `Theme.Dark.xaml` hold the colours and brushes, so light and dark differ only in which of those two is merged (section 12 says when). Custom window chrome (`WindowChrome`) for the capture popup and toast; standard chrome for the main window. Every window carries the tray icon's "L" badge as its icon (Phase 7), so title bars and the taskbar do not show WPF's default.

### Projects and layout

```
/DESIGN.md  /CLAUDE.md  /README.md  /LICENSE (MIT)  /Logbook.sln  /.gitignore  /.editorconfig
/src/Logbook.Core/     net10.0 class library, no UI references, no NuGet packages
/src/Logbook.App/      net10.0-windows WPF app (`UseWPF` plus `UseWindowsForms` for `NotifyIcon` only), references Core, no NuGet packages
/tests/Logbook.Tests/  xUnit; packages: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk
```

Namespaces follow folders: `Logbook.Core.Grammar`, `Logbook.Core.Storage`, `Logbook.Core.Crypto`, `Logbook.Core.Export`, `Logbook.App.Capture`, and so on.

A decision rule with no UI in it lives in Core with a test, even when only the App calls it: which lines the decrypted export writes for each of its options, and whether the idle input tick count has wrapped, both of which Phase 7 moves out of the App.

### Language and style

- C# 14, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, file-scoped namespaces.
- Classes are `sealed` unless designed for inheritance. The model is immutable `record` types; a revision is a new record, never a mutated one.
- No reflection-based magic, no DI container, no MediatR-style indirection. Constructors take what they need.
- A non-obvious block (P/Invoke, crypto framing, parser edge cases) gets a short comment saying why, not what.

### Libraries, all from the BCL

- ULID: hand-rolled in Core (~40 lines). 48-bit millisecond timestamp + 80 random bits, Crockford base32, 26 characters. No monotonic counter; two captures in the same millisecond is not a real case.
- JSON: `System.Text.Json` with a source-generated `JsonSerializerContext` for the record and header types, so the app can be published trimmed and single-file without reflection warnings.
- Crypto: `AesGcm`, `Rfc2898DeriveBytes.Pbkdf2`, `RandomNumberGenerator`. Nothing else.
- Hotkey: `RegisterHotKey` / `UnregisterHotKey` via P/Invoke, with `WM_HOTKEY` handled through an `HwndSource` hook on a hidden message window.
- Tray: `System.Windows.Forms.NotifyIcon`, the one reason `UseWindowsForms` is enabled; nothing else from that assembly is used. Single instance: a named `Mutex`; the second instance posts a custom window message to the first and exits.
- Toast: a borderless, topmost, non-activating WPF `Window` (`ShowActivated=false`, `WindowChrome`) at the bottom-right of the active monitor, opacity storyboard fades it out after 5 s, click opens the entry. Not the Windows notification API.

### Time and formatting

- Capture uses `DateTimeOffset.Now`. Storage is ISO-8601 with the offset as `System.Text.Json` writes a `DateTimeOffset`: `2026-10-06T10:14:03-05:00`, with fractional seconds only when they are not zero. Only the CSV export uses the fixed-width round-trip form `"O"` (section 11).
- Export renders with `CultureInfo.InvariantCulture` and fixed patterns: `ddd MMM d, yyyy h:mm tt` for full, `MMM d, yyyy` for compact. Output is intentionally US-style regardless of machine locale, because the format is part of the spec.
- `since:`/`until:` are inclusive calendar dates, compared against the date each entry carries in its own stored offset, never against a converted instant: an entry written at 00:30 in Tokyo is a Tokyo-date entry wherever the log is read. `last:Nd` = N days, `last:Nw` = 7N days, `last:Nm` = `AddMonths(-N)`, all counted back from today's date.
- `at:` stores the local offset in force at the date and time it names, not today's (Phase 7): `at:yesterday,3pm` typed the day after a clock change carries yesterday's offset, as the reminder field already does. A date with no time stores 12:00:00 in that offset.

### Files

- Data file: `logbook.jsonl`. Settings file: `logbook.settings.json`, beside it. Both inside the synced root.
- Settings include `syncRootEnvVar` (default list tried in order: `OneDriveCommercial`, `OneDrive`, `USERPROFILE`) and `dataFileRelativePath`. If the resolved file is missing at launch, the app runs the first-run picker rather than creating a new file silently.
- Diagnostics (not entries, never entry contents) go to a rolling text log in `%LOCALAPPDATA%\Logbook\logs\`. Losing it on a profile change is fine.

### Capture popup details

- As you type, the parser runs on every keystroke and renders recognised `#tags`, `@people`, and `at:` as chips in a strip under the text box, so you see what will be stored before pressing Enter.
- The box remembers nothing between invocations. Escape clears and hides.

### Build and publish

- `dotnet build`, `dotnet test` from the repo root.
- Release: `dotnet publish src/Logbook.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false`. One `Logbook.exe` dropped in a folder is the whole install. Trimming stays off until the source-generated JSON context is proven; revisit later.
