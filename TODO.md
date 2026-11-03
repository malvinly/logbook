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

Main window:

- The detail pane footer (Save, Follow up, Delete) needs about 400 px, but the pane gets two fifths of the width with a 340 px minimum, so below roughly 1050 px of window width the Delete button is clipped unless the splitter is dragged. The window allows 860 px.

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
