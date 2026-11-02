# Capture and grammar

## Capture

A global hotkey (default `Ctrl+Alt+L`, configurable) pops a single-line text box centered on the active monitor, launcher-style. Type, Enter, gone. Escape cancels.

The line is parsed with the grammar below. Whatever isn't a token becomes the summary. `occurredAt` defaults to now unless an `at:` token is present. `recordedAt` is always now.

A small "more" expander (`Ctrl+Enter` or a chevron) reveals optional fields: reason (multi-line), additional tags, additional people, refs. Nothing here is required.

After Enter, a toast appears for ~5 seconds: **Logged — click to edit.** Clicking opens that entry in the main window's detail pane. This is how typos and second thoughts get fixed; there is no separate undo.

If the app is locked ([locking.md](locking.md)) when the hotkey fires, the passphrase prompt appears first, then the capture box. Nothing is ever held in memory unencrypted and unsaved.

As you type, the parser runs on every keystroke and renders recognised `#tags`, `@people`, and `at:` as chips in a strip under the text box, so you see what will be stored before pressing Enter. The box remembers nothing between invocations. Escape clears and hides.

## Grammar

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

- A `#` or `@` starts a token only at the start of the line, after whitespace, right after opening punctuation (`(` or `"`), or right after a `,` or `;` that ended a token, and only when followed by a letter. So `(#asked)` and `"@j-doe"` are tokens, while `ticket #12345`, `C#`, `someone@example.com`, and `@ 3pm` are all plain text. In a query a `"` at a word start opens a quoted phrase first, so `"@j-doe"` there is the phrase, not a person.
- A tag or person token ends at whitespace, at end of line, or at a `,` or `;`, so `@j-doe,@a-smith` is two people, not one named `j-doe,@a-smith`. Trailing punctuation (`,` `.` `;` `:` `)` `"` `?` `!` and a possessive `'s`) is stripped from the token and left in the text. In a capture the space before it goes as well, so `asked @j-doe, then` stores the summary `asked, then`, not `asked , then`; a space typed before the punctuation (`asked @j-doe , then`) is kept. Punctuation that a token leaves behind with nothing but whitespace around it is dropped too: `done (#asked)` stores `done`, not `done ()`, and in a filter `(#asked)` or `@j-doe,` requires no literal `(` or `,` in the text. Three further rules: a `,` or `;` that ended one token and is followed directly by another is dropped, so `asked @j-doe,@a-smith about #printer? at:yesterday.` stores `asked about?.` (the `?` and `.` stripped from the tag and the keyword stay attached to the word before); a possessive `'s` stripped from a token goes with the token, so `told @j-doe's manager` stores `told manager`, not `told's manager`; and an opening `(` or `"` is dropped only when its closing partner is left alone too, so `done (#asked)` still stores `done`, but `done (#asked and more)` stores `done (and more)` and `said "@j-doe agreed"` stores `said "agreed"`.
- Keyword tokens (`at:`, `since:`, `until:`, `last:`) are recognised only at a word boundary followed immediately by a non-space value. Trailing punctuation is stripped from the value the same way as from a tag, so `at:yesterday.` and `last:7d,` parse. A value also ends at a `,` or `;` when a `#` or `@` and a letter follow, so `last:7d,#printer` is a keyword and a tag, not one unparseable word.
- To put a literal `#word` or `@word` in a summary where it would otherwise be a token, prefix with a backslash: `\#notatag`. The backslash is dropped only there; where the `#` or `@` could not start a token anyway, as in `\\server\#share`, it is kept as typed.

### Conventions (not enforced, write them down)

- `@person` means **anyone involved**, in any role: the person you told, the person you asked, the person who did the thing. The grammar does not distinguish roles.
- The **verb goes in a tag**: `#asked`, `#told`, `#decided`, `#escalated`, `#reported`. "Did I tell anyone about Z" becomes `#told Z`; "everything involving this person" becomes `@person`.
- Keep tags coarse. A tag you will use once is a word in the summary, not a tag.
- Date-only `at:` stores noon. Use compact export when the time isn't real.
- The decrypted export is sensitive. Keep it out of synced storage and delete it when done.
- Your own notes are discoverable in a dispute, and that cuts both ways. Write entries you would be comfortable reading aloud.
