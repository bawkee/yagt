# Skill: codebase-map

Where things live and how data flows at runtime. Paths are relative to `src/`. Pairs are `ViewModels/X.cs` ↔ `Views/X.axaml(.cs)` unless noted.

## Feature → files

| Area | Start here |
|---|---|
| Main window, tabs, global hotkeys | `ViewModels/Launcher.cs`, `Views/Launcher.axaml.cs` (`OnKeyDown`), `Views/LauncherTabBar` |
| Tab page = popup host + notifications | `ViewModels/LauncherPage.cs` (`ProcessPopupAsync`), `Views/LauncherPage.axaml`, `Views/PopupDataTemplates.cs` |
| Welcome page (repo tree, groups, bookmarks) | `Welcome`, `RepositoryNode`, `Views/WelcomeToolbar`, `Models/Bookmarks.cs` |
| Repository page hub (sidebar, toolbar, refreshes, all actions) | `ViewModels/Repository.cs`, `Views/Repository.axaml`, `Views/RepositoryToolbar.axaml` |
| Repository sub-pages (`SelectedViewIndex` 0/1/2) | `Histories`, `WorkingCopy`, `StashesPage` |
| History list + graph | `Histories`, `Models/CommitGraph.cs` (layout), `Views/CommitGraph.cs` (drawing), `Commands/QueryCommits.cs` |
| Commit details / files at revision | `CommitDetail`, `Views/CommitBaseInfo`, `Views/CommitChanges`, `Views/RevisionFiles`, `RevisionFileTreeNode` |
| Graph filters / search | `FilterModeInGraph`, `Models/HistoryFilter.cs`, `SearchCommitContext`, `Views/CommitSearchView` |
| Diff (text/image/binary/LFS/submodule) | `DiffContext`, `TextDiffContext`, `Views/DiffView`, `Views/TextDiffView.axaml.cs` (AvaloniaEdit), `Views/ImageDiffView`, `Models/DiffResult.cs`, `Commands/Diff.cs` |
| Branch / remote tree + context menus | `BranchTreeNode` (builder), `Views/BranchTree.axaml.cs` |
| Tags / submodules / worktrees | `TagCollection` + `Views/TagsView`, `SubmoduleCollection` + `Views/SubmodulesView`, `Worktree` |
| Commit composer | `WorkingCopy`, `Views/CommitMessageToolBox`, `ConventionalCommitMessageBuilder`, `Models/CommitTemplate.cs`, `AIAssistant` + `src/AI/` |
| Conflicts / ongoing merge, rebase, cherry-pick, revert | `Conflict`, `MergeConflictEditor`, `InProgressContexts`, `InteractiveRebase` |
| Blame / file & dir history / compare | `Blame`, `FileHistories`, `DirHistories`, `Compare`, `RevisionCompare` |
| Command palettes (Ctrl/⌘+Shift+P) | `ICommandPalette`, `RepositoryCommandPalette` (entry list), `*CommandPalette`, `Views/CommandPaletteDataTemplates.cs` |
| Global preferences | `ViewModels/Preferences.cs`, `Views/Preferences.axaml` |
| Per-repo configuration dialog | `RepositoryConfigure` (remotes defaults, templates, issue trackers, custom actions) |
| Custom actions (user scripts) | `Models/CustomAction.cs`, `ExecuteCustomAction`, `ConfigureCustomActionControls` |
| Issue-tracker links in subjects | `Models/IssueTracker.cs`, `Commands/IssueTracker.cs` (shared `.issuetracker` file in the repo root, or local git config) |
| External editors / merge tools / terminals | `Models/ExternalTool.cs`, `Models/ExternalMerger.cs`, `Models/ShellOrTerminal.cs`, discovery in `Native/*.cs` |
| Workspaces (tab sets) | `Workspace`, `ConfigureWorkspace` |
| Command log viewer | `CommandLog`, `ViewLogs` |
| Notifications | `Models/Notification.Send(repoPath, msg, isError)` → shown on that repo's `LauncherPage` |
| Statistics, LFS, git-flow, bisect, SSH keys | `Statistics`, `LFS*`, `GitFlow*`/`InitGitFlow`, `Models/Bisect.cs`, `SSHKeyHelper`/`SSHKeyGenerator` |
| Custom drawn controls | `Views/*.cs` without `.axaml` (Avatar, CommitGraph, Chart, NameHighlightedTextBlock, ...) |
| Value converters | `Converters/*` (static `FuncValueConverter` fields, used as `{x:Static c:BoolConverters.X}`) |
| OS integration | `Native/OS.cs` (facade), `Native/Windows.cs`, `Native/MacOS.cs`, `Native/Linux.cs` |

## Runtime flows

**Startup and launch modes.** `App.axaml.cs` `Main` → "Launch Ways" region. The same executable also acts as several helpers:

- `--rebase-todo-editor` and `--rebase-message-editor`: git's interactive rebase editors.
- `--core-editor`: git's `core.editor`.
- `--history <file>` and `--blame <file>`: standalone viewers.
- `SOURCEGIT_LAUNCH_AS_ASKPASS`: SSH/HTTPS password prompt.

Otherwise it starts the normal `Launcher` window. Single-instance hand-off goes through `Models/IpcChannel.cs`. Keep these modes working when you touch `App` or `Commands/Command.cs` (`CreateGitStartInfo`).

**Opening a repo.**

1. `Launcher.OpenRepositoryInTab` creates a `Repository`.
2. `Repository.Open()` loads settings and UI states, starts the `Watcher` and the auto-fetch timer, and creates the sub-pages.
3. It then runs the initial `RefreshXxx()` calls.

**Refresh pipeline (the performance backbone).**

1. `Models/Watcher.cs` receives `FileSystemWatcher` events and sets debounced dirty flags.
2. A 100 ms `Tick` calls `Repository.RefreshBranches/Tags/Commits/WorkingCopyChanges/Stashes/Submodules/Worktrees`.
3. Each refresh does `Task.Run(query)` and then `Dispatcher.UIThread.Invoke(assign)`. A per-area `CancellationTokenSource` drops stale results.
4. During an operation, `using var lockWatcher = _repo.LockWatcher();` suppresses the tick. Changes are picked up after the lock is released.
5. If the watcher can't see a change (for example, config-only changes), call `repo.MarkBranchesDirtyManually()` or a sibling method to force a refresh.

**Popups.**

1. `repo.CanCreatePopup()` then `repo.ShowPopup(vm)` sets `LauncherPage.Popup`.
2. `PopupDataTemplates` resolves the `Views` twin of the ViewModel.
3. Enter or the OK button runs `ProcessPopupAsync`: first `Check()` (DataAnnotations validation), then `await Sure()`. Returning `true` closes the popup.
4. `ShowAndStartPopupAsync` runs `Sure()` immediately when `CanStartDirectly()` returns true.

## Persistence

| Data | Location | Type |
|---|---|---|
| Global preferences, repo tree, workspaces | `<ConfigDir>/preference.json` (or `<exe>/data/` in portable mode on Windows) | `ViewModels.Preferences` |
| Per-repo settings shared by all worktrees | `<git-common-dir>/sourcegit.settings` | `Models.RepositorySettings` |
| Per-worktree UI state (filters, column widths, last options) | `<git-dir>/sourcegit.uistates` | `Models.RepositoryUIStates` |
| Interactive-rebase job | `<git-dir>/sourcegit.interactive_rebase` | `Models.InteractiveRebaseJobCollection` |
| Team-shared issue-tracker rules | `<repo>/.issuetracker` (git-config format, committed by users) | `Models.IssueTracker` |
| Crash logs, avatars cache | `<CacheDir>/crashes`, `<CacheDir>/avatars` | — |

All of these go through `JsonCodeGen` or git config. Never rename these files; existing users depend on them.
