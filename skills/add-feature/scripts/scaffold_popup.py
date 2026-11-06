#!/usr/bin/env python3
"""Scaffold a repository popup (ViewModel + View + strings, optionally a git Command).

Example:
  python skills/add-feature/scripts/scaffold_popup.py PruneWorktrees \
      --title "Prune Worktrees" --tip "Remove stale worktree metadata" \
      --icon Icons.Worktree --command WorktreePrune --git-args "worktree prune -v"

Creates (refuses to overwrite):
  src/ViewModels/<Name>.cs          Popup subclass with the standard Sure() body
  src/Views/<Name>.axaml(.cs)       header + tip layout, x:DataType bound
  src/Commands/<Command>.cs         only with --command
and inserts Text.<Name>.Title / Text.<Name>.Tip into Locales/en_US.axaml in sorted
position. Then prints the entry-point snippet for ViewModels/Repository.cs.
"""

import argparse
import re
import sys
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
SRC = ROOT / "src"
EN_US = SRC / "Resources" / "Locales" / "en_US.axaml"
ICONS = SRC / "Resources" / "Icons.axaml"
RE_IDENT = re.compile(r"^[A-Z][A-Za-z0-9]*$")
RE_TEXT_LINE = re.compile(r'^\s*<x:String x:Key="([^"]+)"')

VM_TEMPLATE = """using System.Threading.Tasks;

namespace SourceGit.ViewModels
{{
    public class {name} : Popup
    {{
        public {name}(Repository repo)
        {{
            _repo = repo;
        }}

        public override async Task<bool> Sure()
        {{
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = "{title} ...";

            var log = _repo.CreateLog("{title}");
            Use(log);

{body}
            log.Complete();
            return succ;
        }}

        private readonly Repository _repo = null;
    }}
}}
"""

VM_BODY_WITH_COMMAND = """            var succ = await new Commands.{command}(_repo.FullPath)
                .Use(log)
                .ExecAsync();
"""

VM_BODY_WITHOUT_COMMAND = """            // TODO: run Commands.* here with .Use(log)
            var succ = await Task.FromResult(true);
"""

VIEW_TEMPLATE = """<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:SourceGit.ViewModels"
             mc:Ignorable="d" d:DesignWidth="500" d:DesignHeight="450"
             x:Class="SourceGit.Views.{name}"
             x:DataType="vm:{name}">
  <StackPanel Orientation="Vertical" Margin="8,0">
    <StackPanel Orientation="Horizontal">
      <Path Width="16" Height="16"
            Data="{{StaticResource {icon}}}"/>

      <TextBlock FontSize="18"
                 Margin="8,0,0,0"
                 Classes="bold"
                 Text="{{DynamicResource Text.{name}.Title}}"/>
    </StackPanel>

    <TextBlock Text="{{DynamicResource Text.{name}.Tip}}"
               Margin="0,16,0,0"
               HorizontalAlignment="Center"/>
  </StackPanel>
</UserControl>
"""

VIEW_CS_TEMPLATE = """using Avalonia.Controls;

namespace SourceGit.Views
{{
    public partial class {name} : UserControl
    {{
        public {name}()
        {{
            InitializeComponent();
        }}
    }}
}}
"""

COMMAND_TEMPLATE = """namespace SourceGit.Commands
{{
    public class {command} : Command
    {{
        public {command}(string repo)
        {{
            WorkingDirectory = repo;
            Context = repo;
            Args = "{git_args}";
        }}
    }}
}}
"""

ENTRY_SNIPPET = """
Next: add an entry point to src/ViewModels/Repository.cs (near similar actions):

        public void {verb}()
        {{
            if (CanCreatePopup())
                ShowPopup(new {name}(this));
        }}

Then wire a surface (context menu, command palette, toolbar or hotkey) as described in
skills/add-feature/AGENTS.md, build, and run skills/ui-theme-and-styles/scripts/check_resources.py.
"""


def newline_of(path):
    return "\r\n" if b"\r\n" in path.read_bytes() else "\n"


def write_new(path, content, nl, dry_run):
    print(f"create {path.relative_to(ROOT).as_posix()}")
    if not dry_run:
        path.write_bytes(content.replace("\n", nl).encode("utf-8"))


def insert_locale_keys(entries, nl, dry_run):
    content = EN_US.read_text(encoding="utf-8")
    lines = content.splitlines()
    existing = {m.group(1) for m in map(RE_TEXT_LINE.match, lines) if m}
    for key, value in entries:
        if key in existing:
            sys.exit(f"error: {key} already exists in en_US.axaml")

    for key, value in entries:
        new_line = f'  <x:String x:Key="{key}" xml:space="preserve">{escape(value)}</x:String>'
        index = len(lines) - 1  # before </ResourceDictionary>
        for i, line in enumerate(lines):
            m = RE_TEXT_LINE.match(line)
            if m and m.group(1).lower() > key.lower():
                index = i
                break
        lines.insert(index, new_line)
        print(f"insert {key} into {EN_US.relative_to(ROOT).as_posix()}:{index + 1}")

    if not dry_run:
        tail = nl if content.endswith("\n") else ""
        EN_US.write_bytes((nl.join(lines) + tail).encode("utf-8"))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("name", help="PascalCase popup name named after the user action, e.g. PruneWorktrees")
    parser.add_argument("--title", required=True, help="English title shown in the popup header")
    parser.add_argument("--tip", default="", help="English one-line description shown under the header")
    parser.add_argument("--icon", default="Icons.Action", help="existing icon key from Resources/Icons.axaml")
    parser.add_argument("--command", help="also create src/Commands/<COMMAND>.cs (a git mutation)")
    parser.add_argument("--git-args", help="git arguments for --command, e.g. \"worktree prune -v\"")
    parser.add_argument("--dry-run", action="store_true", help="print what would change without writing")
    args = parser.parse_args()

    if not RE_IDENT.match(args.name):
        sys.exit("error: name must be PascalCase, e.g. PruneWorktrees")
    if args.command and not RE_IDENT.match(args.command):
        sys.exit("error: --command must be PascalCase, e.g. WorktreePrune")
    if args.command and not args.git_args:
        sys.exit("error: --command requires --git-args")
    if f'x:Key="{args.icon}"' not in ICONS.read_text(encoding="utf-8"):
        sys.exit(f"error: {args.icon} is not defined in Resources/Icons.axaml")

    nl = newline_of(EN_US)
    name = args.name
    vm_path = SRC / "ViewModels" / f"{name}.cs"
    view_path = SRC / "Views" / f"{name}.axaml"
    view_cs_path = SRC / "Views" / f"{name}.axaml.cs"
    cmd_path = SRC / "Commands" / f"{args.command}.cs" if args.command else None
    for path in (vm_path, view_path, view_cs_path, cmd_path):
        if path and path.exists():
            sys.exit(f"error: {path.relative_to(ROOT).as_posix()} already exists")

    if args.command:
        git_args = args.git_args.replace("\\", "\\\\").replace('"', '\\"')
        write_new(cmd_path, COMMAND_TEMPLATE.format(command=args.command, git_args=git_args), nl, args.dry_run)
        body = VM_BODY_WITH_COMMAND.format(command=args.command)
    else:
        body = VM_BODY_WITHOUT_COMMAND

    title = args.title.replace("\\", "\\\\").replace('"', '\\"')
    write_new(vm_path, VM_TEMPLATE.format(name=name, title=title, body=body), nl, args.dry_run)
    write_new(view_path, VIEW_TEMPLATE.format(name=name, icon=args.icon), nl, args.dry_run)
    write_new(view_cs_path, VIEW_CS_TEMPLATE.format(name=name), nl, args.dry_run)
    insert_locale_keys([(f"Text.{name}.Title", args.title), (f"Text.{name}.Tip", args.tip or args.title)], nl, args.dry_run)

    print(ENTRY_SNIPPET.format(name=name, verb=name))
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
