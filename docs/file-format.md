# Record schema and file format

## Record schema

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
- `relatedTo` — list of entry ids (ULIDs) this entry follows up on, set by the Follow up button ([main-window.md](main-window.md)). It records the thread, which tags cannot: a tag says what an entry is about, not which earlier entry it continues. May be empty; a line written before this field existed has no such property and loads with an empty list. The timeline export does not show it; the decrypted export writes it.
- `remindAt` — optional ISO-8601 time with UTC offset (`DateTimeOffset`), null when no reminder is set. Stored and rendered with its own offset like `occurredAt`; only the "has it passed" check compares instants. Set, changed and cleared by new revisions like any other field, so the latest revision decides; a line written before this field existed loads as null. The timeline export does not show it; the decrypted export writes it.
- `deleted` — a tombstone is a record with `deleted: true` and `rev + 1`; other fields may be omitted. Deleted entries are hidden everywhere except the revision view and the "include history" plaintext export.

## File format

One file, default name `logbook.jsonl`, in the folder the user chose (which may be synced).

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
- Changing the passphrase (Tools → Change passphrase in the main window) rewrites only line 1. Records are untouched. The data key does not change: a new salt is drawn, the key-encryption key is derived from the new passphrase with the iteration count the header already has, and the same data key is wrapped again; `format`, `fileId` and `createdAt` are kept. Because the same data key is re-wrapped, a copy of the file made before the change (sync version history, a conflict copy, a backup) still opens with the old passphrase, and its data key decrypts every record written since; rotating the data key would need a whole-file rewrite and is not built ([TODO.md](../TODO.md), Out of scope).
- How line 1 is rewritten, the one deliberate exception to the append-only rule: **in place, at the same byte length**, so not one byte of lines 2..n moves or changes and the file is never truncated or rewritten whole. The header JSON escapes `+` in base64 as `+`, so its length depends on its random values; the new salt is drawn until it needs no escaping, and the key is wrapped again with fresh nonces until the line comes out exactly as long as the old one, in the same JSON form `Create` writes. Before writing: the current passphrase is verified against the header on disk (a wrong one is reported, nothing written), that header must unwrap to the open log's data key, and the new header is verified in memory to unwrap it with the new passphrase. The write opens the file with `FileMode.Open`, `FileAccess.ReadWrite`, `FileShare.Read`, `FileOptions.WriteThrough`, checks that line 1 is still the bytes it read, writes the new bytes at offset 0, flushes and closes, with the same retry schedule as appends; the file is not held open between operations. Line 1 is then read back and must match and unwrap; if not, the previous bytes (kept in memory until then) are written back and the failure reported. A retry that finds line 1 already holding the new bytes (an earlier attempt wrote them and then failed to flush or close) takes the write as done and goes on to the read-back; the write-back goes only over the new bytes, so a line 1 that holds neither header is left alone; and when the header is left in an unknown state the message says to try the new passphrase first. Refused, with nothing written: an empty new passphrase, a header that is not byte for byte what this app writes for its own values (an unknown property, other spacing or order) or whose length a rewritten one cannot match (one this app did not write; there is no fallback to a whole-file rewrite), a file on disk with another data key, and any time a capture is pending or a write is in flight (the same checks as Lock now), since a header rewrite racing an append is not worth reasoning about. After the change the app stays unlocked with the same data key.
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

- Some sync clients keep only a placeholder locally and download the file on first access. On launch, check the file's attributes for a placeholder (`Offline` or recall-on-data-access; the reparse-point flag alone proves nothing, since sync clients leave it on fully downloaded files too). The file is not read before unlock ([locking.md](locking.md)), so the first read at unlock is what hydrates it; when the launch check saw a placeholder, a balloon after that unlock says the log was downloaded and recommends marking the folder as always available offline.
- On launch, and when the first-run picker returns an existing file, glob the data folder for `logbook*.jsonl`, leaving out `logbook-export-*` names (the decrypted export's own default name, [export.md](export.md)), which are never logs. If more than one file matches (sync conflict copies, old-profile re-syncs), do not guess: show each with its header `createdAt` and line count and ask which is the real one.
