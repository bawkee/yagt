# Skill: add-feature

The end-to-end recipe for a new git operation or user action. Follow the existing patterns exactly; upstream code is the reference.

**Quick start.** The scaffolder writes steps 2–3 and 6, plus the step 1 command if you want one:

```sh
python skills/add-feature/scripts/scaffold_popup.py PruneWorktrees --title "Prune Worktrees" \
    --tip "Remove stale worktree metadata" --icon Icons.Worktree \
    --command WorktreePrune --git-args "worktree prune -v"     # --dry-run to preview
```

It never overwrites files. Afterwards, rename the printed `Repository` entry method to a verb phrase and do the remaining steps. Before you add a command, check that an existing one doesn't already cover it: `src/Commands/` often has the verb already (`Branch`, `Remote`, `Tag`, `Worktree`, `Stash`, ...).

## 1. Command (`src/Commands/`)

One class per git verb. It derives from `Command` and its constructor sets `WorkingDirectory`, `Context` (the repo path) and `Args`.

- **Mutation.** Add action methods that set `Args` and `return await ExecAsync().ConfigureAwait(false);`. Example: `Commands/Tag.cs`. `ExecAsync` returns success as a `bool`, and stderr is already shown to the user as a notification. Set `RaiseError = false` for probes that are allowed to fail.
- **Query.** Name it `QueryXxx` and give it a `GetResultAsync()` that awaits `ReadToEndAsync().ConfigureAwait(false)` and parses `rs.StdOut` when `rs.IsSuccess`. Example: `Commands/QueryCommitFullMessage.cs`.
  - Ask git for a machine-readable `--format` and avoid porcelain text.
  - For big outputs, parse line by line with a span or a `[GeneratedRegex]` instead of using LINQ.
- **Arguments.**
  - Quote user values and paths with `.Quoted()`.
  - Put multi-line text in a temp file and pass it as `-F <tmp>`. Delete the file afterwards.
  - Build long argument strings with a `StringBuilder`.
  - Leave `--no-pager`, the editor and askpass setup to `Command.CreateGitStartInfo`.

## 2. Popup ViewModel (`src/ViewModels/<Action>.cs`)

Derive from `Popup` and name the class after the user action (`CreateTag`, `DeleteMultipleBranches`). Store `private readonly Repository _repo`.

- **Validation.**
  - Put `[Required(ErrorMessage = "...")]` and `[CustomValidation(typeof(X), nameof(ValidateY))]` on the property.
  - The validator is `public static ValidationResult ValidateY(string v, ValidationContext ctx)`.
  - The setter is `set => SetProperty(ref _y, value, true);`, where the third argument triggers validation.
  - Use `Models.RefName` for ref names.
  - Messages are plain English.
- **The `Sure()` body**, which is the canonical sequence (see `ViewModels/CreateTag.cs`):

  ```csharp
  using var lockWatcher = _repo.LockWatcher();
  ProgressDescription = "Doing X ...";
  var log = _repo.CreateLog("Do X");
  Use(log);
  var succ = await new Commands.X(_repo.FullPath, ...).Use(log).ExecAsync();
  log.Complete();
  return succ;          // true closes the popup
  ```

  - If the watcher can't see what changed (for example, config only), call `_repo.MarkBranchesDirtyManually()` or a sibling method after the operation.
  - For long, cancellable operations, set `CanTerminate = true` and override `Terminate()`.
- **Remembered options.**
  - Per worktree: a property on `Models/RepositoryUIStates.cs`, exposed through a pass-through property (see `CreateTag.Annotated`).
  - Shared repo settings: `Models/RepositorySettings.cs`.
  - Global: `Preferences.Instance`.
- **Auto-start.** `repo.ShowAndStartPopupAsync(vm)` runs `Sure()` immediately when `CanStartDirectly()` returns true, which is the default. Toolbar buttons use it for Ctrl+click. Override `CanStartDirectly()` to refuse when the user must choose something first.

## 3. View (`src/Views/<Action>.axaml` + `.axaml.cs`)

The view **must** have the same class name as the ViewModel. It is found by name at runtime. Put `x:DataType="vm:<Action>"` on the root. The code-behind contains only `InitializeComponent()`. Copy the metrics from `Views/CreateTag.axaml`:

- **Root:** `StackPanel Margin="8,0,0,0"`.
- **Header:** a 16×16 `Path` icon, then a `TextBlock FontSize="18" Classes="bold" Margin="8,0,0,0"` bound to `Text.<Action>.Title`.
- **Form:** `Grid Margin="0,16,8,0" ColumnDefinitions="120,*"` with 32 px rows (`Auto` for multi-line rows).
  - Labels: `HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,8,0"`.
  - Inputs: `Height="26" CornerRadius="3"` with a `Watermark`.
  - Multi-line TextBox: add `Tag="{Binding Source={x:Static v:StealHotKey.Enter}}"` so Enter inserts a newline and doesn't submit.
- **Values:** SHAs use `Fonts.Monospace` and `Foreground="DarkOrange"`. Branches and commits use the `ContentControl` + `DataTemplate DataType=m:Branch|m:Commit` block from `CreateTag.axaml`.
- **No buttons.** The OK/Cancel buttons, Enter/Esc handling and the progress overlay come from `LauncherPage`.

## 4. Entry point (`src/ViewModels/Repository.cs`)

```csharp
public void PruneWorktrees()
{
    if (CanCreatePopup())
        ShowPopup(new PruneWorktrees(this));
}
```

Context menus usually call `repo.ShowPopup(new ViewModels.X(repo, target))` directly, guarded by `repo.CanCreatePopup()`. Only add a `Repository` method when two or more surfaces share the action. For a quick action that needs no input, run the command inside a `Repository` method that holds `LockWatcher()` and `CreateLog()`; you don't need a popup.

## 5. Surfaces

- **Context menu.** Menus are built in Views code-behind on `ContextRequested` (see `Views/TagsView.axaml.cs` and `Views/BranchTree.axaml.cs`).
  - Create items as `new MenuItem { Header = App.Text("TagCM.Push", tag.Name), Icon = this.CreateMenuIcon("Icons.Push") }`.
  - In `Click`, set `ev.Handled = true`. Add separators with `new MenuItem { Header = "-" }`. Open the menu with `menu.Open(control)`, then set `e.Handled = true`.
  - Write `${0}$` around a name in the menu text to render it highlighted (`Delete ${0}$...`).
  - A trailing `...` means the item opens a popup.
  - To show a hotkey hint, set `item.Tag = OperatingSystem.IsMacOS() ? "⌘+X" : "Ctrl+X"`.
- **Command palette.** Add an entry in the `ViewModels/RepositoryCommandPalette.cs` constructor: `_cmds.Add(new("<Action>.Title", "keyword", "IconWithoutPrefix", () => repo.X()));`. The label key omits `Text.`, the icon key omits `Icons.`, and "..." is appended automatically.
- **Toolbar.** Add to `Views/RepositoryToolbar.axaml`: `Button Classes="icon_button" Width="32"` with a `ToolTip.Tip` and a 14×14 `Path`. Keep the bar short; it's prime real estate.
- **Hotkey.** Handle it in `Views/Launcher.axaml.cs` `OnKeyDown`, which handles keys globally and per repository.
  - Use `cmdKey`: ⌘ on macOS, Ctrl elsewhere. Avoid AltGr combinations and keys taken by text boxes.
  - Document the hotkey in `Views/Hotkeys.axaml` with `{OnPlatform Ctrl+Shift+X, macOS=⌘+⇧+X}` and a `Text.Hotkeys.Repo.*` key.

## 6. Strings (`src/Resources/Locales/en_US.axaml` only)

- **Key naming.** Use `Text.<Action>.Title`, `Text.<Action>.<Field>` and `Text.<Action>.<Field>.Placeholder`. Context-menu items use `Text.<Thing>CM.<Verb>`.
- **Placement.** Insert keys in alphabetical position. Each entry is written as `<x:String x:Key="..." xml:space="preserve">`.
- **Content.** Use `{0}` placeholders with `App.Text("Key", arg)`. XML-escape `&amp;`, `&lt;` and `&gt;`.
- **Don't** edit other locales. Translators and CI (`build/scripts/localization-check.js`) handle them, and missing keys fall back to English.

## 7. New settings

- **Global.** Add a property to `ViewModels/Preferences.cs`:
  - Use `SetProperty`.
  - If the setter has side effects, guard them with `!_isLoading`.
  - Initialize the field at the bottom of the class.
  - Add a row to the matching tab in `Views/Preferences.axaml` and update its `RowDefinitions`.
  - The property persists automatically.
- **Per repository.**
  - `Models/RepositorySettings.cs` is shared by all worktrees and edited in `RepositoryConfigure`.
  - `Models/RepositoryUIStates.cs` is per worktree, saved on close, and holds remembered UI choices.
- **New persisted types** need a `[JsonSerializable(typeof(T))]` entry in `App.JsonCodeGen.cs`. Without it, AOT builds break or fail silently.

## 8. Checklist

- [ ] `dotnet build`, then `dotnet format --verify-no-changes src/SourceGit.csproj`
- [ ] `python skills/ui-theme-and-styles/scripts/check_resources.py`
- [ ] Test it in the running app with both the Light and Dark themes. Check that the popup keeps focus on the first input, and that Enter and Esc work.
- [ ] Check that the UI doesn't freeze on a large repo, and that the watcher refreshes the affected panels afterwards.
- [ ] Check that it works on Windows, macOS and Linux: no hardcoded `\`, use `Path.Combine`, and use `Native.OS` for shell and file-manager calls.
- [ ] Commit it as `feature: ...` or `enhance: ...` with one logical change.
