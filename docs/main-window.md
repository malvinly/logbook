# Main window

Opened from the tray icon, from the toast, or by a second hotkey (default `Ctrl+Alt+Shift+L`).

## Filter bar

One text box using the grammar in [grammar.md](grammar.md). Results update live as you type (debounced ~150 ms). A date-range picker beside it rewrites the `since:` / `until:` tokens in the box rather than being a separate hidden filter.

The header above the results always shows the count and range: **12 entries · Jul 14 – Oct 28, 2026**. "How many times" is a driving question; the number must never be hidden in the export.

## Saved queries

A row of buttons under the filter bar, one per saved query (a name and a filter string), in the order they were saved. Clicking one puts its filter into the box exactly as if typed, so the results, the count header and the export follow it. "Save filter as button" saves the current filter text under a name you type (the filter itself is offered as the name); saving under a name already in use replaces that button's filter in place. Right-clicking a button offers Remove. Nothing is seeded: tag names are your own, so the typical questions become buttons when you save them (for example `#asked @j-doe`, `#reported #printer`, `last:7d`).

## Results list

Newest first, grouped under day headers. Each row: time, summary, tag chips, person chips, and a small marker if a reason or refs exist. Selecting a row opens the detail pane.

## Detail pane

All fields editable: occurredAt (date and time pickers), summary, reason, tags, people, refs. Save appends a new revision. Delete appends a tombstone after a confirm. The revision caption and the "Follows up on" line stay at the top of the pane and the Save, Follow up and Delete buttons stay at its foot; only the fields and the revisions between them scroll, so the actions are in reach at any window height.

A **Follow up** button beside Save opens the capture box pre-filled with the selected entry's tags and people as `#tag @person` text (caret at the end, so the chip strip shows them) and a caption naming the original; the new entry's `relatedTo` holds the original's id. It sits in the detail pane rather than on each row because the pane already holds the per-entry actions and a row button would need a hover template the Fluent list does not have. The pre-fill comes from an explicit button, so it does not break the rule that the capture box remembers nothing between invocations: it lives only in that capture box, and Escape drops it along with the link. If a capture box is already open, it is brought forward unchanged rather than overwritten. No tag is added for the thread. A stored name that would not parse back as a token goes into the "more" section's additional tags or people instead. An entry with `relatedTo` shows one line per original under the revision caption, **Follows up on: Tue Oct 6, 2026 10:14 AM — summary**, which selects the original when clicked (clearing the filter if it hides it), or says the original has since been deleted. Nothing is shown on the row. The original lists its follow-ups the same way: one line per entry whose `relatedTo` names it, **Followed up by: Thu Oct 15, 2026 2:30 PM — summary**, oldest first, which selects that entry when clicked.

A **Reminder** row sets `remindAt`: a date picker and a time box in the style of occurredAt, and a Clear button that empties them. Like every other field it is saved by Save as a new revision. A new reminder takes the local offset at the date and time typed; an edited one keeps its stored offset, as occurredAt does. While the app is unlocked, the reminders are checked at unlock and every 30 seconds after, and the check stops on lock, since the entries cannot be read then. When one has passed, a tray balloon titled "Reminder" says that a reminder is due and when it was due, never the entry's summary (Windows keeps balloon text in its notification history long after the app has locked); clicking it opens the entry in the main window, also after Windows has taken the balloon off the screen, until another balloon replaces it. One balloon per check, earliest first. Each reminder shows once per unlock: the record of what was shown is dropped with the log on lock and exit, so a passed reminder that was never cleared shows once more after the next unlock or restart, reminders that passed while the app was closed or locked included, and the pane says so under the field. Clearing the reminder (a new revision) is what ends it; moving it to a new time makes it a new reminder. A reminder balloon still waiting its turn in the tray queue ([settings.md](settings.md)) is dropped on lock, since it would otherwise appear once after the lock and again after the next unlock; queued balloons of other kinds still show. No snooze, no repeat, no sound.

A **Revisions** section lists every prior revision of this entry, read-only, with its `recordedAt`. The current fields are shown against the previous revision with changed fields highlighted. The list row carries an "edited" marker only because this view exists; a marker without the history would be meaningless for the purpose this app serves.

## Autocomplete

Typing `#` or `@` in the filter bar, the capture box, or the detail pane shows suggestions. Ranked by most recently used, capped at 8, prefix-filtered as you type over the full set. Cap and ranking (recent vs. frequent) are settings.
