# Export

## Copy as timeline

Takes the **current filtered set**, flips it to oldest-first, and puts a plain-text block on the clipboard. Plain text, Unicode bullets, no markdown; mail and chat clients mangle markdown on paste.

```
#staging-access — 3 entries, Oct 6 – Oct 20, 2026

• Tue Oct 6, 2026 10:14 AM — Asked for access to the staging environment; told it would be looked into.
• Thu Oct 15, 2026 2:30 PM (9 days later) — Followed up; no response to the first ask.
• Tue Oct 20, 2026 9:02 AM (5 days later) — Asked again in standup; told it would be this week.
      Reason: wanted it on record before the release freeze.
```

Rules:

- Header line is the filter text as typed (or "All entries"), then the count and the occurredAt range of the set. When the filter was put there by a saved-query button and has not been edited since, the button's name stands in for the filter text, so a weekly digest reads `This week — 2 entries` rather than `last:7d — 2 entries`.
- Each line: weekday, date, time in the entry's stored offset, em dash, summary. Refs are appended as `[INC0041233]` when present.
- Reason, when present, is indented beneath its line.
- Tags and people are **not** included by default. They are indexing for you, not information for the reader; the **Tags and people** toggle below adds them when the filter is broader than one person.

Toggles in the export panel, remembered between uses:

- **Show gaps** — appends `(N days later)` after the timestamp, computed from the previous entry in the set. Off for the first entry. A gap of 0 reads `(same day)` and a gap of 1 reads `(1 day later)`. Gaps are calendar days between the entries' stored dates in export order and are never negative: when entries in different offsets put a later instant on an earlier stored date, the gap is `(same day)`.
- **Group by month** — inserts a second header line with per-month counts: `Jul (1) · Aug (3) · Sep (1) · Oct (2)`. When the set spans more than one year, each month carries its year: `Jul 2025 (1) · Jan 2026 (2)`. The list below is unchanged.
- **Compact** — drops time of day and the weekday: `• Oct 6, 2026 — …`.
- **Tags and people** — appends the entry's tags and then its people to its line, after the refs, each with its `#` or `@` and separated by single spaces: `• Tue Jul 14, 2026 9:00 AM — Reported the printer outage to facilities [INC0040001] #reported #printer @a-smith`. Order is as stored. An entry with no tags and no people is unchanged, and the header line and the reason are unchanged.

"Copy to clipboard" puts two formats on the clipboard in one go: the plain text above, unchanged, and the same timeline as HTML, so mail clients paste bold dates and real bullets while plain-text targets keep taking the text. The HTML follows the same toggles and says the same words: the header line (and the month counts after a `<br>` when Group by month is on) as one paragraph, then a `<ul>` with one `<li>` per entry, its date part in `<b>`, then the gap, the em dash, the summary, refs, tags and people exactly as in the text; the reason is a `<div>` inside its item, `Reason: ` first and a `<br>` per line break. All text is HTML-escaped; there are no classes, no styles and no external resources. An empty set is the header paragraph alone. The HTML goes on the clipboard as UTF-8 in the CF_HTML envelope Windows requires (a `Version:0.9` header with byte offsets and `<!--StartFragment-->` / `<!--EndFragment-->` markers). The preview in the panel stays plain text.

## CSV export

"Save as CSV…" in the export panel writes the same current filtered set as a CSV file, one row per entry, oldest first, for a spreadsheet.

- Columns, in this order, under a header row with these names: `occurredAt`, `recordedAt`, `summary`, `reason`, `tags`, `people`, `refs`, `relatedTo`, `remindAt`, `id`.
- Timestamps are the ISO-8601 round-trip form (`"O"`) with the stored offset, never converted: `2026-10-06T10:14:00.0000000-05:00`. A null `reason` or `remindAt` is an empty field.
- Lists are joined by a single space (`tags`, `people`, `relatedTo`, none of which can hold a space), except `refs`, which may contain spaces and are joined by `; `.
- RFC 4180: comma separated, CRLF after every row including the last, a field enclosed in double quotes when it holds a comma, a double quote, CR or LF, with its double quotes doubled; line breaks inside a reason are kept as stored. An empty set is the header row alone.
- A text cell (`summary`, `reason`, `refs`, `tags` and `people`; a tag or a name may start with `-` or `+`) that starts with `=`, `+`, `-` or `@` is written with a leading `'`, because spreadsheet apps read such a cell as a formula; those cells are therefore not verbatim, and the panel's one sentence says so. The remaining columns (timestamps and ids) cannot start with those characters.
- Encoding is UTF-8 with a byte order mark, so spreadsheet apps detect it.
- The file is plaintext, so it follows the Export decrypted rules: the save dialog defaults to the last export folder, else `%LOCALAPPDATA%\Logbook\exports`, whichever is outside the log's folder, the panel says in one sentence that the app cannot tell which folders are synced and that another folder should be one that is not, choosing the log's folder asks first, the log itself and `logbook*.jsonl` names are refused, and the folder is remembered in the same `%LOCALAPPDATA%` marker as the decrypted export. The file name is `logbook-export-YYYY-MM-DD.csv`.

## Export decrypted

Tools → Export decrypted. Writes a plaintext JSONL file (one record per line, latest revision per id, tombstoned ids excluded) to a location you choose. A checkbox includes the full revision history, in which case every line in the file, tombstones included, is written in original order.

The save dialog defaults to the last export folder, else a private folder under the Windows profile, `%LOCALAPPDATA%\Logbook\exports` (created on demand; Documents if it cannot be), the first that is **outside** the log's folder, and explains why in one sentence: the export is unencrypted and contains notes about people and situations, and the app cannot tell which folders are synced, so it defaults to a folder sync services do not back up and tells the user that any other folder should be one that is not synced. The default is deliberately not detection: the profile folder is simply never a synced location by default. The file name is `logbook-export-YYYY-MM-DD.jsonl`.
