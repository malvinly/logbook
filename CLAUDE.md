# Logbook — working notes for coding sessions

Logbook is a personal Windows tray app for logging decisions and events with a hotkey, storing them in one encrypted append-only JSONL file, and producing dated timelines on demand. `DESIGN.md` is the source of truth. Read it fully before doing anything; section 16 holds the implementation decisions and section 13 the build order.

## How to work in this repo

- Build one step of DESIGN.md section 13 at a time. When the step is done and its tests pass, stop and summarize what was built and any judgment calls made. Do not start the next step unasked.
- Core before UI. Steps 1–4 (`Logbook.Core` and its tests) must be complete and green before any WPF code exists.
- Tests first for Core. Every parser rule in section 6, every file-format rule in section 8, and every export toggle in section 11 has a test. A behavior without a test is not done.
- Build only what DESIGN.md specifies. If something seems missing or a better idea comes up, add a line under Phase 2 in DESIGN.md and say so; do not implement it. If the doc is silent on a detail that matters, ask rather than guess.
- If a decision in section 16 turns out to be wrong in practice, say so and propose the change; do not quietly work around it.
- The author intends to understand every line. Prefer the boring, explicit implementation over the clever one. No framework-style indirection, no DI container, no reflection tricks. A non-obvious block gets a short comment explaining why.
- No new NuGet packages in `Logbook.Core` or `Logbook.App`. The test project has only xUnit and the test SDK.
- WPF is plain code-behind with a hand-written `INotifyPropertyChanged` base. No MVVM framework, no behaviors package, no third-party controls. Theme values live in one `ResourceDictionary`; never hardcode a color or font size in a view.

## Conventions

- C# 14, nullable enabled, warnings as errors, file-scoped namespaces, `sealed` by default, immutable `record` types for the model. See DESIGN.md §16.
- Project layout: `src/Logbook.Core`, `src/Logbook.App`, `tests/Logbook.Tests`, `Logbook.sln` at the root.
- This folder is the git repo root. Source lives under `src/`, tests under `tests/`, the solution file and docs at the root. Commit small and often: one commit per coherent unit (a project scaffold, a type with its tests, a parser rule group with its tests), never one commit for a whole build step. Every commit builds and passes tests. Subject line is a short imperative sentence, e.g. `Add ULID generator and tests`.
- Markdown files in this repo are not hard-wrapped: one line per paragraph or bullet.
- No real names, employer names, or vendor names beyond what DESIGN.md already uses. This repo is public. Sample data in tests and docs uses placeholders like `j-doe`.

## Commands

- `dotnet build` — build everything.
- `dotnet test` — run the Core tests.
- `dotnet publish src/Logbook.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false` — produce the single-file release exe.

## Things that are easy to get wrong

- Timestamps are `DateTimeOffset` with the captured offset preserved end to end. Never convert to UTC or to the current machine zone for storage or rendering.
- The data file is append-only. Nothing in the app may truncate, rewrite, or reorder it. Edits and deletes are new lines.
- A capture that fails to persist must be visible to the user and retried. Silent loss is the one unforgivable bug.
- `#` and `@` start a token only after whitespace and only before a letter. `ticket #12345`, `C#`, and `name@example.com` are plain text.
- Export text is plain text with Unicode bullets and fixed US-style dates. No markdown.
