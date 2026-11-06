# Skill: ui-theme-and-styles

How visuals are themed and styled. Read this before you touch any `.axaml` file or draw anything in code.

## Where things are

| File | Contains |
|---|---|
| `src/Resources/Themes.axaml` | `Color.*` defined twice, in the `Light` and `Dark` ThemeDictionaries, plus one `Brush.*` per color |
| `src/Resources/Styles.axaml` | Global control styles and the style classes below. Default `Path` style: `Fill=Brush.FG1`, `Stretch=Uniform` |
| `src/Resources/Fonts.axaml` | `Fonts.Default` (Inter) and `Fonts.Monospace` (JetBrains Mono NL), both overridable in Preferences |
| `src/Resources/Icons.axaml` | Vector icons. See [icons-and-graphics](../icons-and-graphics/AGENTS.md) |
| `src/App.axaml` | Merges the above with the Fluent theme and AvaloniaEdit styles |

The theme is chosen by `Preferences.Theme` (Default/Light/Dark) through `App.SetTheme`. A user **theme overrides** JSON file (`Models/ThemeOverrides.cs`) can replace any `Color.<Key>` as well as `SystemAccentColor`, the graph pen colors and the graph pen thickness. This only works because views consume `{DynamicResource Brush.X}`, so keep it that way.

## Brush semantics (use the meaning, not the look)

| Brush | Use for |
|---|---|
| `FG1` | Primary text and icons (the default `Path` fill) |
| `FG2` | Secondary text: labels, hints, timestamps, `group_header_label` |
| `Window` / `Contents` | App background / list, editor and content backgrounds |
| `TitleBar` / `ToolBar` / `Popup` / `SubPanel` | Those surfaces. `Popup` is also used for floating panels |
| `Border0` | Hairline separators between panes and the title bar's bottom border |
| `Border1` | Stronger borders (selectors, focused areas) |
| `Border2` | Control borders: flat buttons, inputs, cards (the most common) |
| `FlatButton.Background` / `.BackgroundHovered` | `Button.flat` states |
| `Badge` / `BadgeFG` | Neutral count and ref badges |
| `Accent` / `AccentHovered` | Selection and focus (follows the OS accent color) |
| `Link` | Clickable URLs and issue links |
| `InlineCode` / `InlineCodeFG` | Inline code spans in commit messages |
| `Conflict*` | Merge-conflict editor |
| `Diff.*` | Diff viewer: added/deleted backgrounds and highlights, empty lines, block border |

**Adding a color:**

1. Add `Color.X` to **both** the Light and Dark dictionaries.
2. Add `<SolidColorBrush x:Key="Brush.X" Color="{DynamicResource Color.X}"/>` next to the other brushes.
3. Reference it as `{DynamicResource Brush.X}`.

Never write hex colors in views. Reuse an existing brush whenever its meaning fits; most "new color" needs are really `FG2`, `Border2` or `Badge`.

**Named colors that are allowed (an established upstream convention).** They read well on both themes:

| Color | Meaning |
|---|---|
| `DarkOrange` | SHAs, rendered in `Fonts.Monospace` |
| `Green` + `White` text | "new", local, remote or global badges |
| `Red` / `OrangeRed` | Deleted, conflict, invalid |
| `Goldenrod` / `DarkGoldenrod` | Warnings (`BoolConverters.IsWarningToBrush`) |
| `Transparent` | Hit-test backgrounds |

Badge pattern: `Border Height="16" CornerRadius="8" Background="Green"` containing `TextBlock FontSize="10" Margin="8,0" Foreground="White"`. Commit graph lanes use `Models/CommitGraph.cs` pens and can be overridden by the theme.

## Style classes (`Classes="..."`)

| Control | Classes |
|---|---|
| `Button` | `flat` (bordered text button), `flat primary` (accent default action), `icon_button` (borderless icon), `caption_button` |
| `TextBlock` | `bold`, `italic`, `small` (font size − 1), `group_header_label` (FG2 bold), `table_header`, `info_label` (FG2 bold, right-aligned), `tab_header`, `dropped` (strikethrough) |
| `Run` | `issue_link`, `commit_link`, `inline_code` |
| `TextBox` | `no_background`, `no_border` |
| `ToggleButton` | `tree_expander`, `group_expander`, `sidebar_expander`, `filter_expander`, `folder`, `line_path`, `toggle_untracked` |
| `RadioButton` | `switch_button` |
| Others | `ScrollViewer.static_scrollbar`, `ListBox.repo_left_content_list`, `MenuItem.filter_mode_switcher`, `Window.custom_window_frame`, `Path.rotating` / `Path.waiting` (via `v:LoadingIcon`, `PopupRunningStatus`) |

Prefer an existing class over local setters. Add a new global class to `Styles.axaml` only when three or more views need it.

## Sizes and metrics

- **Fonts.** Font sizes bind to `Preferences.DefaultFontSize` and `Preferences.EditorFontSize`. Use relative classes (`small`, `tab_header`) instead of fixed `FontSize` values, except for popup titles (18) and badges (10). Code and SHAs use `{DynamicResource Fonts.Monospace}`.
- **Icon sizes.**

  | Context | Size |
  |---|---|
  | Context menu | 12 (`CreateMenuIcon`) |
  | Inline in lists and buttons, toolbar, title bar | 14 |
  | Popup header | 16 |
  | Empty state or loading | 48 |

- **Corner radius.** Inputs and buttons use 3–4, badges use 8.
- **Spacing.** Common steps are 4/8/16 px. Popup forms use a 120 px label column and 32 px rows (see [add-feature](../add-feature/AGENTS.md) §3).
- **Windows.**
  - New top-level windows derive from `v:ChromelessWindow`. Copy the title-bar block from `Views/Hotkeys.axaml`: a 28 px `Brush.TitleBar` row, `CaptionButtons`, and `IsVisible="{Binding !#ThisControl.UseSystemWindowFrame}"`.
  - Use `{OnPlatform ..., macOS=...}` where macOS draws its own traffic lights.
  - Open the window with `this.ShowWindow(vm)` or `ShowDialog`.

## Verify

- `python skills/ui-theme-and-styles/scripts/check_resources.py`:
  - Fails on undefined `Text.`/`Icons.`/`Brush.`/`Color.`/`Fonts.` keys in axaml or C#.
  - Fails on Light/Dark color mismatches and on a `Color.X` without a `Brush.X`.
  - Fails on Popup or command-palette ViewModels without a `Views/` twin.
  - `--unused` lists candidate dead icons and strings.
- Run the app and look at the change in **both** Light and Dark (Preferences → Appearance) at 100% and 150% zoom (Ctrl/⌘ + `=`/`-`).
