# Logbook — working notes for coding sessions

Logbook is a personal Windows tray app for logging decisions and events with a hotkey, storing them in one encrypted append-only JSONL file, and producing dated timelines on demand. The spec is in `docs/`: read `docs/development.md` first, then the file that covers the area you are touching. `TODO.md` is the roadmap; nothing is built that is not on it.

## How to work in this repo

- Work is approved per TODO.md item. Build it with its tests green and committed, then stop and summarize what was built, the commit list, and any judgment calls made. Do not start the next item unasked.
- Core before UI. A rule with no UI in it lives in `Logbook.Core` with a test before the App calls it.
- Tests first for Core. Every parser rule in `docs/grammar.md`, every file-format rule in `docs/file-format.md`, and every export toggle in `docs/export.md` has a test. A behavior without a test is not done.
- Build only what the spec and TODO.md say. If something seems missing or a better idea comes up, add it to TODO.md and say so; do not implement it. If the spec is silent on a detail that matters, ask rather than guess.
- When the code and the spec disagree, say so and propose which one changes; do not quietly work around it. A behaviour change lands in `docs/` in the same commit as the code.
- The author intends to understand every line. Prefer the boring, explicit implementation over the clever one. No framework-style indirection, no DI container, no reflection tricks. A non-obvious block gets a short comment explaining why.
- No new NuGet packages in `Logbook.Core` or `Logbook.App`. The test project has only xUnit and the test SDK.
- WPF is plain code-behind with a hand-written `INotifyPropertyChanged` base. No MVVM framework, no behaviors package, no third-party controls. Theme values live in the `Theme` folder's dictionaries only: `Theme.xaml` for fonts, sizes, radii and styles, `Theme.Light.xaml` and `Theme.Dark.xaml` for colours; never hardcode a color or font size in a view.

## Conventions

- C# 14, nullable enabled, warnings as errors, file-scoped namespaces, `sealed` by default, immutable `record` types for the model. See `docs/development.md`.
- Project layout: `src/Logbook.Core`, `src/Logbook.App`, `tests/Logbook.Tests`, `docs/`, `Logbook.sln` at the root.
- This folder is the git repo root. Commit small and often: one commit per coherent unit (a project scaffold, a type with its tests, a parser rule group with its tests), never one commit for a whole TODO item. Every commit builds and passes tests. Subject line is a short imperative sentence, e.g. `Add ULID generator and tests`.
- Markdown files in this repo are not hard-wrapped: one line per paragraph or bullet.
- No real names, employer names, or vendor names. This repo is public. Sample data in tests and docs uses placeholders like `j-doe`.

## Commands

- `dotnet build` — build everything.
- `dotnet test` — run the Core tests.
- `dotnet publish src/Logbook.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=false` — produce the single-file release exe.
- A release is a pushed tag `vX.Y.Z`; `.github/workflows/release.yml` builds it and creates the GitHub Release. Bump `<Version>` in `src/Logbook.App/Logbook.App.csproj` in the same commit.

## Things that are easy to get wrong

- Timestamps are `DateTimeOffset` with the captured offset preserved end to end. Never convert to UTC or to the current machine zone for storage or rendering.
- The data file is append-only. Nothing in the app may truncate, rewrite, or reorder it. Edits and deletes are new lines. The one exception is line 1 on a passphrase change, written over itself at the same byte length (`docs/file-format.md`); nothing else may write anywhere but the end.
- A capture that fails to persist must be visible to the user and retried. Silent loss is the one unforgivable bug.
- `#` and `@` start a token only at the start of the line, after whitespace, after an opening `(` or `"`, or right after a `,` or `;` that ended a token, and only before a letter. `ticket #12345`, `C#`, and `name@example.com` are plain text; `@j-doe,@a-smith` is two people.
- Export text is plain text with Unicode bullets and fixed US-style dates. No markdown.
- Settings → Save re-registers the Startup shortcut (`Startup\Logbook.lnk`) at the running exe when "Start with Windows" is on. A smoke test that saves Settings from a test build must back the shortcut up first and restore it byte for byte afterwards, and remove any `%LOCALAPPDATA%\Logbook\export-folder` marker a smoke export created, or the real installation ends up pointing at the test exe. A smoke test that runs the first-run picker or "Choose another…" from a test build rewrites the real pointer `%LOCALAPPDATA%\Logbook\data-file`, so it must back that file up first (or note its absence) and restore or delete it afterwards.
