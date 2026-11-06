# Skill: icons-and-graphics

Where graphics come from and how to add new ones.

**Use vectors only.** Every new icon, glyph or logo is a `StreamGeometry` in `src/Resources/Icons.axaml`. Don't add PNG, JPG or ICO files for UI elements. The PNGs already in the repo are legacy and should be replaced (see the section below). Two kinds of raster file are exempt:

- Content, such as the diff image viewer and fetched avatars.
- App icon containers that the OS requires.

## How icons work

- **One file.** `src/Resources/Icons.axaml` holds about 170 one-line entries, `<StreamGeometry x:Key="Icons.X">…</StreamGeometry>`, sorted case-insensitively by key.
- **Using an icon.**
  - In axaml: `<Path Width="14" Height="14" Data="{StaticResource Icons.X}"/>`.
  - For context menus in code: `item.Icon = this.CreateMenuIcon("Icons.X")` (`Views/ControlExtensions.cs:34`).
  - `Views/RepositoryCommandPaletteIcon.cs` builds `Icons.{cmd.Icon}` from a string. Before you rename or delete an icon, grep for its bare name as well as the full key.
  - Sizes are listed in [ui-theme-and-styles](../ui-theme-and-styles/AGENTS.md#sizes-and-metrics).
- **Rendering.**
  - The global `Path` style is `Stretch=Uniform` with `Fill=Brush.FG1`. The geometry's bounds stretch to the Path's `Width`/`Height`, so coordinate scale and padding are irrelevant. Only the aspect ratio matters.
  - Color comes only from semantic `Fill` overrides on the `Path`: `Brush.FG2`, `Brush.Link`, `DarkOrange`, or bindings. Never put color in the geometry.
- **Geometry rules.**
  - Fill-only (no strokes), one color, integer coordinates on roughly a 1024 grid.
  - With no prefix, the fill rule is **EvenOdd** (the StreamGeometry default), so inner subpaths become holes. An `F1 ` prefix selects **NonZero**.
  - Use solid, bold shapes. Thin line art disappears at 12–14 px.
- **Naming.**
  - Name by meaning, not shape, in PascalCase: `Icons.Fetch`, `Icons.Worktree`, `Icons.Search`.
  - Variants use a dotted group: `Icons.Branch.Add`, `Icons.File.Checkout`, `Icons.Window.Close`.
  - Shape names (`Icons.Check`, `Icons.Plus`, `Icons.Down`) are reserved for generic glyphs.
  - Acronyms are all caps (`Icons.LFS`).
  - Brand logos go under `Icons.Logo.<Brand>`.

## Adding an icon

1. **Reuse first.** Search `Icons.axaml` for the concept. `python skills/ui-theme-and-styles/scripts/check_resources.py --unused` lists icons nothing references yet.
2. **Pick a source.**
   - Take a *filled* icon from a permissively licensed set (MIT, Apache-2.0, ISC or CC0), for example Bootstrap Icons `*-fill`, Phosphor Fill or Material Symbols (filled). Check the license of the exact version you take, and avoid CC-BY, GPL, custom and proprietary licenses.
   - Stroke-based sets (Lucide, Feather, Tabler outline) must be outlined first. In Inkscape, use Path → Stroke to Path.
   - Hand-written path data on a 1024 grid is also fine.
3. **Convert and insert.**

   ```sh
   python skills/icons-and-graphics/scripts/svg_to_icon.py icon.svg --name Icons.Worktree.Prune --insert
   ```

   - `--insert` adds the entry in sorted position. Use `--replace` to update an existing key, or pass `-` to read the SVG from stdin.
   - The script flattens shapes and transforms, fits the result to 1024, and picks the fill rule. It rejects strokes, `<use>` and `<text>`.
   - **Don't ignore its warnings.** A "fills merged" warning means a light "cutout" shape will render solid. Delete that shape and make it a hole instead (an inner subpath under EvenOdd).
4. **Verify.** Run `check_resources.py` and `dotnet build`. Then look at the icon at 12 and 14 px in both Light and Dark, at 100% and 150% zoom.
5. **Commit.** Use `ux: add <concept> icon`, and name the source set and its license in the commit body.

## Legacy raster images (replace them with vectors)

| Files (64×64 PNG) | Loaded by | Shown in |
|---|---|---|
| `Resources/Images/ExternalToolIcons/*.png`, `…/JetBrains/*.png` | `Models/ExternalTool.cs:42` (`Bitmap IconImage`), `Models/ExternalMerger.cs:22` | "Open in" menus (code-behind in `CommitDetail`, `RepositoryToolbar`, `RevisionFileTreeView`, `WorkingCopy`), merger list in `Preferences.axaml` |
| `Resources/Images/ShellIcons/*.png` | `Models/ShellOrTerminal.cs:20` (`Bitmap Icon`) | Shell/terminal list in `Preferences.axaml` |
| `Resources/Images/github.png`, `unreal.png` | `Models/AvatarManager.cs:219` | Default avatars for bot e-mails (a `Bitmap` pipeline) |

**How to replace them** (one model per isolated `ux:` commit, to keep upstream merges clean):

1. Convert monochrome brand marks to `Icons.Logo.<Brand>`. [Simple Icons](https://simpleicons.org) (CC0) is a good source. Logos are still their owners' trademarks, so use them only to identify the tool.
2. Add a geometry-key property next to the existing `Bitmap` property.
3. Switch the views to `<Path Width="16" Height="16">`. Logos render in `FG1` like every other icon.
4. Delete the PNGs, the loader, and the matching `AvaloniaResource` glob in `src/SourceGit.csproj`. The globs are per folder and non-recursive.

Upstream adds new tool and terminal PNGs regularly. When merging upstream, convert any new ones rather than keeping them. Avatars come last: they need a geometry rendered into a `Bitmap`.

## App icons (raster formats required by the OS)

| File | Used by |
|---|---|
| `src/App.ico` | Windows executable (`ApplicationIcon` in the csproj), and window or `About` icons via `/App.ico` |
| `build/resources/app/App.icns` | macOS app bundle |
| `build/resources/_common/icons/sourcegit.png` | Linux `.desktop` file and AppImage |

These files belong to upstream's identity (see the root fork policy), so leave them alone. If they ever have to change, add an SVG master next to them and regenerate all three from it.
