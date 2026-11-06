# AGENTS.md

YAGT ("Yet Another Git Client") is a public fork of [SourceGit](https://github.com/sourcegit-scm/sourcegit), a cross-platform git GUI built with C# / .NET 10 / Avalonia 11. The fork adds features for large, busy repositories (hundreds of branches, heavy history) and AI-assisted daily work. Everything here stays generic. Nothing in it is specific to any organization.

## Core principles (keep these when changing anything)

1. **The git CLI is the engine.** Every repository operation shells out to the user's `git` through `src/Commands/`. Output is parsed from explicit `--format` strings. Don't add libgit2 or a reimplementation of git logic. Git config, hooks, credential helpers and LFS must keep working as they do in the terminal.
2. **Stay responsive at scale.** Never block the UI thread. Queries run in the background and their results are marshalled back to the UI thread. Stale refreshes are cancelled. The file watcher is debounced and locked during operations. History is capped by `MaxHistoryCommits`. Hot paths use virtualized lists and hand-drawn controls (`CommitGraph`, `CommitRefsPresenter`…).
3. **Behave the same on all three OSes.** One codebase serves Windows, macOS and Linux, on x64 and arm64. OS-specific code lives in `src/Native/` or behind `OperatingSystem.IsXxx()`. A feature that only works on one OS needs a very good reason.
4. **Stay lean and self-contained.** Releases are NativeAOT and trimmed. Keep dependencies minimal. Avoid reflection. There is no DI container, plugin system or ORM. Plain MVVM with a few static singletons (`Preferences.Instance`, `App.GetLauncher()`) is the norm.
5. **Write simple, explicit code.** Each git operation gets one small class, and each user action gets one popup. View-only logic (context menus, key handling) stays in code-behind. Add an abstraction only after three real call sites need it.
6. **Keep the UI consistent, themeable and localized.** Colors come from theme brushes. User-facing text comes from locale keys. Icons are **vector-only** geometries kept in one file. The existing PNGs are legacy and should be replaced, never added to. Every visual must work in both the Light and Dark themes.

## Build, run, check

```sh
git submodule update --init                              # depends/AvaloniaEdit (a fork) is required
dotnet build                                             # whole solution
dotnet run --project src/SourceGit.csproj
dotnet format --verify-no-changes src/SourceGit.csproj   # CI gate (.editorconfig); run `dotnet format` to fix
python skills/ui-theme-and-styles/scripts/check_resources.py   # catches resource-key typos the compiler can't see
```

- There is no unit-test project. Verify a change by building and running the app.
- Treat new trim/AOT warnings from `dotnet publish src/SourceGit.csproj -c Release -r <rid>` as errors.

## Architecture (namespace = folder, `SourceGit.<Folder>`)

| Layer | Path | Role | May reference |
|---|---|---|---|
| Commands | `src/Commands/` | One class per git invocation, derived from `Command` | Models |
| Models | `src/Models/` | POCOs, parsers, persistence, `Watcher`, helpers | (minimal Avalonia) |
| ViewModels | `src/ViewModels/` | `ObservableObject` state and actions; `Repository` is the per-repo hub | Commands, Models |
| Views | `src/Views/` | `.axaml` plus code-behind, custom controls, context menus | everything |
| Native | `src/Native/` | `OS` facade over Windows/MacOS/Linux backends | Models |
| Resources | `src/Resources/` | `Icons`, `Themes`, `Styles`, `Fonts`, `Locales/*.axaml` | — |
| App | `src/App.*.cs` | Entry point, launch modes, `App.Text()`, `JsonCodeGen`, extension helpers | — |

- A View is located by **naming convention**: `ViewModels.Foo` renders with `Views.Foo` (see `ControlExtensions.CreateFromViewModels`).
- Inside `SourceGit`, refer to sibling namespaces by qualified prefix (`Models.Branch`, `Commands.Tag`, `ViewModels.Repository`). Don't add `using SourceGit.X;`.
- For "where is X?" questions, see [skills/codebase-map](skills/codebase-map/AGENTS.md).

## Hard rules

- **AOT and trimming.** Serialize JSON only through the source-generated `JsonCodeGen` (`App.JsonCodeGen.cs`), and add a `[JsonSerializable]` entry for every new persisted type. Write regexes as `[GeneratedRegex]` members of a `partial` class, named like `REG_FORMAT()`. Don't add reflection (`Activator`, `Type.GetType`, `GetMethod`…). The only sanctioned use is the ViewModel→View lookup in `ControlExtensions.CreateFromViewModels`, which works because `SourceGit` is a `TrimmerRootAssembly`.
- **Compiled bindings** are on by default. Every `.axaml` root and every `DataTemplate` needs an `x:DataType`.
- **Threading.** Async git calls end with `.ConfigureAwait(false)`. Return to the UI with `Dispatcher.UIThread.Invoke/Post`. The synchronous `ReadToEnd()`/`GetResult()` are only for trivial, instant queries such as reading one config value.
- **Mutating the repo.** Do it from a Popup's `Sure()` or a `Repository` method that holds `using var lockWatcher = _repo.LockWatcher();` and logs through `_repo.CreateLog("...")` with `.Use(log)`. Pass a `CancellationToken` only to read-only commands.
- **Security.** Quote user-supplied values with `.Quoted()`. Validate ref names with `Models.RefName`. Pass messages through a temp file (`-F <file>`), never inline on the command line. Open URLs only through `Native.OS.OpenBrowser`, which enforces a scheme allow-list.
- **Strings.** User-visible text uses `{DynamicResource Text.X}` in axaml and `App.Text("X", args...)` in code. Add new keys to `src/Resources/Locales/en_US.axaml` only. Following upstream practice, validation errors, notifications and `ProgressDescription` are plain English literals.
- **Visuals.** Colors use `{DynamicResource Brush.X}`. Icons use `{StaticResource Icons.X}` and are vector `StreamGeometry` only; never add raster images. Never hardcode hex colors in views. See [skills/ui-theme-and-styles](skills/ui-theme-and-styles/AGENTS.md) and [skills/icons-and-graphics](skills/icons-and-graphics/AGENTS.md).
- **Comparisons.** Always pass an explicit `StringComparison.Ordinal` or `StringComparison.OrdinalIgnoreCase`.

## Code conventions

- `.editorconfig` is authoritative and `dotnet format` enforces it:
  - Allman braces; 4 spaces in C#, 2 spaces in axaml/xml/json/yaml; `var` everywhere.
  - Block-scoped namespaces; `System.*` usings first; unused usings are errors.
  - No `this.` qualification.
  - Field naming:

    | Kind | Style |
    |---|---|
    | Private/internal fields | `_camelCase` |
    | Private static fields | `s_camelCase` |
    | Constants (in practice) | `UPPER_SNAKE` |

- **Class layout:** public properties, then constructors, then public methods, then private methods, then **all fields at the bottom**. Observable properties are written explicitly: `get => _x; set => SetProperty(ref _x, value);`.
- **Acronyms are all-caps** in identifiers: `SHA`, `URL`, `LFS`, `SSH`, `GPG`, `AI`, `UI` (`SetURLAsync`, `LFSLocks`, `QueryRefsContainsCommit`). Async methods end in `Async`.
- **Naming by layer:**

  | Kind | Naming pattern | Examples |
  |---|---|---|
  | Git commands | Named for the git verb, with action methods | `Tag.AddAsync`, `Remote.SetURLAsync` |
  | Read-only queries | `QueryXxx` with `GetResult[Async]()` | `QueryRemotes` |
  | Popups | Named for the user action | `CreateTag`, `DeleteMultipleBranches`, `PushRevision` |
  | Command palettes | `XxxCommandPalette` | — |
  | Locale keys | `Text.<Area>.<Item>` | — |
  | Context menu keys | `Text.<Thing>CM.<Action>` | — |

- Match the style of the file you're editing. Upstream code is the reference.

## Commits

Use upstream's format: `<prefix>: <short lowercase imperative summary>`, optionally followed by `(#issue)`. The prefixes are:

| Prefix | Use for |
|---|---|
| `feature:` | New features |
| `enhance:` | Improvements to existing features |
| `fix:` | Bug fixes |
| `ux:` | Visual or UX tweaks |
| `refactor:` | Restructuring without behavior change |
| `code_style:` | Code cleanup, renames, `dotnet format` fixes |
| `localization:` | Translations |
| `doc:` | Documentation |
| `project:` | Build, dependencies and repo setup |
| `ci:` | GitHub workflows |
| `version:` | Version bumps |

Keep one logical change per commit, so fixes can be cherry-picked across forks.

## Fork policy (upstream compatibility comes first)

- **Keep upstream identity in code.** Leave the `SourceGit` namespace, assembly and product names, data directories, and the `sourcegit.*` per-repo files alone. Renaming them causes endless merge conflicts and breaks users' existing settings. Fork branding belongs in docs only.
- **Keep the footprint small:**
  - Prefer new files (a new Command/ViewModel/View) over editing large upstream files.
  - When you do edit upstream code, make small, localized insertions.
  - Never reformat, reorder or rename upstream code you didn't otherwise need to touch.
  - Add new locale keys rather than changing existing English values.
- **Upstream intake priority:** security and bug fixes first, then compatibility and maintenance, then features (only if needed). Use [skills/upstream-sync](skills/upstream-sync/AGENTS.md).
- If a change would benefit upstream, write it so it could be sent as an upstream PR (upstream PRs target `develop`).

## Skills index

Read only the skill that matches your task. Each one is `skills/<name>/AGENTS.md`, with an optional `scripts/` folder.

| Skill | Use when |
|---|---|
| [codebase-map](skills/codebase-map/AGENTS.md) | Finding where a feature, panel, or behavior lives. Covers refresh/watcher flow, persistence files, launch modes. |
| [add-feature](skills/add-feature/AGENTS.md) | Adding a git operation or UI action end-to-end: Command → Popup ViewModel → View → menu/palette/hotkey → strings → settings. Includes a popup scaffolder. |
| [ui-theme-and-styles](skills/ui-theme-and-styles/AGENTS.md) | Any axaml/visual work: brushes and their meaning, semantic colors, style classes, layout metrics, fonts. Includes the resource-key checker. |
| [icons-and-graphics](skills/icons-and-graphics/AGENTS.md) | Adding or changing icons (SVG to `StreamGeometry`, with a converter script), icon naming and sources, replacing legacy PNGs, app icons. |
| [upstream-sync](skills/upstream-sync/AGENTS.md) | Pulling fixes or releases from upstream SourceGit, resolving merge conflicts, auditing fork divergence. Includes a missing-commits report. |

**Adding a skill:** create one only for non-obvious, repo-specific knowledge. Keep it short, link to real files instead of copying code, and add a row to the index above.
