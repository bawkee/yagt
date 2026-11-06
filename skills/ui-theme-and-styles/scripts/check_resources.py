#!/usr/bin/env python3
"""Static checks for resource keys that the C#/XAML compiler cannot see.

Checks:
  * {DynamicResource|StaticResource X} in .axaml and literal keys in .cs
    ("Icons.X", "Brush.X", App.Text("X"), command palette entries) resolve
    to a defined key. Text.* must exist in Locales/en_US.axaml.
  * Light and Dark theme dictionaries define the same Color.* keys, and every
    Color.X has a matching Brush.X.
  * Every ViewModels class deriving from Popup or ICommandPalette has a
    Views/<Name>.axaml twin (views are resolved by name at runtime).

Usage:  python skills/ui-theme-and-styles/scripts/check_resources.py [--unused]
Exit code is 1 when problems are found. --unused only lists candidates
(keys can also be built dynamically) and never fails.
"""

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SRC = ROOT / "src"
RES = SRC / "Resources"
EN_US = RES / "Locales" / "en_US.axaml"
CHECKED_PREFIXES = ("Text.", "Icons.", "Brush.", "Color.", "Fonts.")

RE_DEF = re.compile(r'x:Key="([^"]+)"')
RE_AXAML_REF = re.compile(r"\{(?:DynamicResource|StaticResource)\s+(?:ResourceKey=)?([A-Za-z_][\w.]*)\s*\}")
RE_CS_TEXT = re.compile(r'App\.Text\(\s*"([^"]+)"')
RE_CS_LITERAL = re.compile(r'"((?:Text|Icons|Brush|Color|Fonts)\.[A-Za-z0-9_.]+)"')
RE_PALETTE_CMD = re.compile(r'new(?:\s+\w+)?\(\s*"([^"]+)",\s*"[^"]*",\s*"([^"]+)"')
RE_VM_CLASS = re.compile(r"\bclass\s+(\w+)\s*:\s*(Popup|ICommandPalette)\b")


def read(path):
    return path.read_text(encoding="utf-8-sig")


def rel(path):
    return path.relative_to(ROOT).as_posix()


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def collect_definitions():
    defined = set()
    for path in list(SRC.rglob("*.axaml")):
        if path.parent.name == "Locales":
            continue
        defined.update(RE_DEF.findall(read(path)))
    text_keys = set(k for k in RE_DEF.findall(read(EN_US)) if k.startswith("Text."))
    return defined, text_keys


def collect_references():
    refs = []  # (key, file, line)
    for path in SRC.rglob("*.axaml"):
        if path.parent.name == "Locales":
            continue
        text = read(path)
        for m in RE_AXAML_REF.finditer(text):
            refs.append((m.group(1), path, line_of(text, m.start())))

    for path in SRC.rglob("*.cs"):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        text = read(path)
        for m in RE_CS_TEXT.finditer(text):
            refs.append(("Text." + m.group(1), path, line_of(text, m.start())))
        for m in RE_CS_LITERAL.finditer(text):
            refs.append((m.group(1), path, line_of(text, m.start())))
        if path.name.endswith("CommandPalette.cs"):
            for m in RE_PALETTE_CMD.finditer(text):
                line = line_of(text, m.start())
                refs.append(("Text." + m.group(1), path, line))
                refs.append(("Icons." + m.group(2), path, line))
    return refs


def check_theme_parity(problems):
    path = RES / "Themes.axaml"
    text = read(path)
    dicts = {}
    for m in re.finditer(r'<ResourceDictionary x:Key="(\w+)">(.*?)</ResourceDictionary>', text, re.S):
        dicts[m.group(1)] = set(re.findall(r'<Color x:Key="([^"]+)"', m.group(2)))

    light, dark = dicts.get("Light", set()), dicts.get("Dark", set())
    for key in sorted(light - dark):
        problems.append(f"{rel(path)}: {key} is defined in Light but not in Dark")
    for key in sorted(dark - light):
        problems.append(f"{rel(path)}: {key} is defined in Dark but not in Light")

    brushes = set(re.findall(r'<SolidColorBrush x:Key="(Brush\.[^"]+)"', text))
    for key in sorted(light | dark):
        brush = "Brush." + key[len("Color."):]
        if brush not in brushes:
            problems.append(f"{rel(path)}: {key} has no matching {brush}")


def check_view_twins(problems):
    for path in sorted((SRC / "ViewModels").glob("*.cs")):
        text = read(path)
        for m in RE_VM_CLASS.finditer(text):
            name = m.group(1)
            if not (SRC / "Views" / f"{name}.axaml").exists():
                problems.append(f"{rel(path)}:{line_of(text, m.start())}: {m.group(2)} '{name}' has no src/Views/{name}.axaml")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--unused", action="store_true", help="also list Icons.* and Text.* keys with no literal reference")
    args = parser.parse_args()

    defined, text_keys = collect_definitions()
    refs = collect_references()
    problems = []

    for key, path, line in refs:
        if not key.startswith(CHECKED_PREFIXES):
            continue
        known = text_keys if key.startswith("Text.") else defined
        if key not in known:
            where = "Locales/en_US.axaml" if key.startswith("Text.") else "any resource dictionary"
            problems.append(f"{rel(path)}:{line}: '{key}' is not defined in {where}")

    check_theme_parity(problems)
    check_view_twins(problems)

    for p in problems:
        print(p)

    if args.unused:
        used = set(k for k, _, _ in refs)
        icons = sorted(k for k in defined if k.startswith("Icons.") and k not in used)
        texts = sorted(k for k in text_keys if k not in used)
        print(f"\nUnreferenced candidates (may be used via computed keys): {len(icons)} icons, {len(texts)} texts")
        for k in icons + texts:
            print(f"  {k}")

    if problems:
        print(f"\n{len(problems)} problem(s) found.")
        return 1
    print("Resource check passed.")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
