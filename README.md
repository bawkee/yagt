# YAGT — Yet Another Git Tool

[![stars](https://img.shields.io/github/stars/bawkee/yagt.svg)](https://github.com/bawkee/yagt/stargazers)
[![forks](https://img.shields.io/github/forks/bawkee/yagt.svg)](https://github.com/bawkee/yagt/forks)
[![license](https://img.shields.io/github/license/bawkee/yagt.svg)](LICENSE)
[![latest](https://img.shields.io/github/v/release/bawkee/yagt.svg)](https://github.com/bawkee/yagt/releases/latest)
[![downloads](https://img.shields.io/github/downloads/bawkee/yagt/total)](https://github.com/bawkee/yagt/releases)

YAGT is an independent fork of [SourceGit](https://github.com/sourcegit-scm/sourcegit), a cross-platform Git GUI. It builds on upstream’s work with improvements for large, busy repositories and AI-assisted daily work. YAGT releases and support are maintained in this repository.

## Screenshots

These screenshots show the interface inherited from SourceGit.

* Dark Theme

  ![Theme Dark](./screenshots/theme_dark.png)

* Light Theme

  ![Theme Light](./screenshots/theme_light.png)

* Custom

  You can find custom themes from [sourcegit-theme](https://github.com/sourcegit-scm/sourcegit-theme.git). And welcome to share your own themes.

## Highlights

* Supports Windows/macOS/Linux
* Opensource/Free
* Fast
* Deutsch/English/Español/Bahasa Indonesia/Français/Italiano/Português/Русский/Українська/简体中文/繁體中文/日本語/தமிழ் (Tamil)/한국어
* Built-in light/dark themes
* Customize theme
* Visual commit graph
* Supports SSH access with each remote
* GIT commands with GUI
  * Clone/Fetch/Pull/Push...
  * Merge/Rebase/Reset/Revert/Cherry-pick...
  * Amend/Reword/Squash
  * Interactive rebase
  * Branches
  * Remotes
  * Tags
  * Stashes
  * Submodules
  * Worktrees
  * Archive
  * Diff
  * Save as patch/apply
  * File histories
  * Blame
  * Revision Diffs
  * Branch Diff
  * Image Diff - Side-By-Side/Swipe/Blend
* Git command logs
* Search commits
* GitFlow
* Git LFS
* Bisect
* Issue Link
* Workspace
* Custom Action
* Create PR on GitHub/Gitlab/Gitea/Gitee/Bitbucket...
* Using AI to generate commit message
* Built-in conventional commit message helper.

> [!WARNING]
> **Linux** only tested on **Debian 12** on both **X11** & **Wayland**.

## How to Use

**To use this tool, you need to install Git(>=2.25.1) first.**

You can download the latest stable from [Releases](https://github.com/bawkee/yagt/releases/latest) or download workflow artifacts from [GitHub Actions](https://github.com/bawkee/yagt/actions) to try this app based on latest commits.

This software creates a folder, which is platform-dependent, to store user settings, downloaded avatars and crash logs.

| OS      | PATH                                                                                           |
|---------|------------------------------------------------------------------------------------------------|
| Windows | `%APPDATA%\YAGT`                                                                                |
| Linux   | `${XDG_CONFIG_HOME}/YAGT` (preferences), `${XDG_CACHE_HOME}/YAGT` (avatars & logs)                  |
| macOS   | `~/Library/Application Support/YAGT`                                                            |

> [!TIP]
> * You can open this data storage directory from the main menu `Open Data Storage Directory`.
> * You can create a `data` folder next to the `YAGT` executable to force this app to store data (user settings, downloaded avatars and crash logs) into it (Portable-Mode). Only works with Windows packages and Linux AppImages.

YAGT uses its own settings and cache directories, so it can be installed alongside SourceGit. On first launch, it copies existing SourceGit configuration if the YAGT directory does not exist; the original files are left intact. Portable mode continues to use the adjacent `data` folder. Repository metadata (`sourcegit.settings`, `sourcegit.uistates`, `sourcegit.node`, and `sourcegit.interactive_rebase`) keeps its upstream names for compatibility.

For **Windows**, install [Git for Windows](https://git-scm.com/download/win) and run `YAGT.exe` from a YAGT release or local build. MSYS Git is not supported.

For **macOS**, install Git (for example, through Xcode Command Line Tools) and place `YAGT.app` in Applications. Custom PATH settings can be stored in `~/Library/Application Support/YAGT/PATH`.

For **Linux**, use YAGT's AppImage, Debian, or RPM packages when available. Install `xdg-utils` for native file-manager integration. The packaged Linux executable is named `yagt`.

The existing SourceGit Homebrew, Scoop, distribution, and Flatpak listings distribute upstream SourceGit. Use this repository's releases or [build YAGT locally](build/README.md) to get this fork.

## Commandline arguments

Users can also launch `YAGT` from commandline. Usage:

```
<YAGT_EXEC> <DIR>                       // Open repository in existing `YAGT` instance or a new one
<YAGT_EXEC> --history <FILE_OR_DIR>     // Launch `YAGT` to see the history of a file or dir
<YAGT_EXEC> --blame <FILE_PATH>         // Launch `YAGT` to blame a file (HEAD version only)
```

## OpenAI

This software supports using OpenAI or other AI service that has an OpenAI compatible HTTP API to generate commit message. You need configurate the service in `Preference` window.

For `OpenAI`:

* `Server` must be `https://api.openai.com/v1`

For `OrcaRouter`:

* `Server` should be `https://api.orcarouter.ai/v1`
* `API Key` should be an OrcaRouter API key (prefixed with `sk-orca-`)
* A good starting `Model` is `orcarouter/auto`, which routes each request to the best available model

For `Cheaper Inference`:

* `Server` should be `https://api.cheaperinference.com/v1`
* `API Key` should be a Cheaper Inference API key (prefixed with `ci_live_`)
* A good starting `Model` is `gpt-5.4-mini`

For `Opper`:

* `Server` should be `https://api.opper.ai/v3/compat`
* `API Key` should be an Opper API key, created at https://platform.opper.ai
* A good starting `Model` is `gpt-5.4-mini`

For `API Route`:

* `Server` should be `https://global.api-route.com/v1`
* `API Key` should be your API Route API key
* `Model` can be `gpt-6.1-sol` if it is available to your key; otherwise select an available chat model from the fetched catalog
* See the [API documentation](https://github.com/DennyHo0917/api-route/blob/main/API.md) for authentication and model discovery

For other AI service:

* The `Server` should fill in a URL equivalent to OpenAI's `https://api.openai.com/v1`. For example, when using `Ollama`, it should be `http://localhost:11434/v1` instead of `http://localhost:11434/api/generate`
* The `API Key` is optional that depends on the service

## External Tools

This app supports open repository in external tools listed in the table below.

| Tool                          | Windows | macOS | Linux |
|-------------------------------|---------|-------|-------|
| Visual Studio Code            | YES     | YES   | YES   |
| Visual Studio Code - Insiders | YES     | YES   | YES   |
| VSCodium                      | YES     | YES   | YES   |
| Cursor                        | YES     | YES   | YES   |
| Sublime Text                  | YES     | YES   | YES   |
| Zed                           | YES     | YES   | YES   |
| Visual Studio                 | YES     | NO    | NO    |

> [!NOTE]
> This app will try to find those tools based on some pre-defined or expected locations automatically. If you are using one portable version of these tools, it will not be detected by this app.  
> To solve this problem you can add a file named `external_editors.json` in app data storage directory (config dir for Linux) and provide the path directly.  
> User can also exclude some editors by using `external_editors.json`.

The format of `external_editors.json`:
```json
{
    "tools": {
        "Visual Studio Code": "D:\\VSCode\\Code.exe"
    },
    "excludes": [
        "Visual Studio Community 2019"
    ]
}
```

> [!NOTE]
> This app also supports a lot of `JetBrains` IDEs, installing `JetBrains Toolbox` will help this app to find them.

## Conventional Commit Helper

You can define your own conventional commit types (per-repository) by following steps:

1. Create a json file with your own conventional commit type definitions. For example:
```json
[
  {
    "Name": "New Feature",
    "Type": "Feature",
    "Description": "Adding a new feature",
    "PrefillShortDesc": "this is a test"
  },
  {
    "Name": "Bug Fixes",
    "Type": "Fix",
    "Description": "Fixing a bug"
  }
]
```
2. Configure the `Conventional Commit Types` in repository configuration window.  

## Contributing

Everyone is welcome to submit a PR against this fork’s `master` branch. Changes intended for upstream SourceGit should target its `develop` branch.

This project has a submodule in `depends/AvaloniaEdit` which is a custom fork of [Official AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit). 
Please make sure it is initialized - enable `--recurse-submodules` option while cloning or run `git submodule update --init` after cloned.

In short, here are the commands to get started once [.NET tools are installed](https://dotnet.microsoft.com/en-us/download):

```sh
dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
dotnet restore
dotnet build
dotnet run --project src/YAGT.csproj
```

Thanks to the [YAGT contributors](https://github.com/bawkee/yagt/graphs/contributors) and the [SourceGit authors and contributors](https://github.com/sourcegit-scm/sourcegit/graphs/contributors) whose work makes this fork possible. The original MIT copyright notice is retained in [LICENSE](LICENSE).

## Translation Status

You can find the current translation status in [TRANSLATION.md](TRANSLATION.md)

### Translate Utility Script

A script that assists with translations by reading the target language, comparing it with the base language, and going through missing keys one by one, so the translator can provide the translations interactively without needing to check each key manually.

#### Usage

Check for a given language (e.g., `pt_BR`) and optionally check for missing translations:

```bash
python translate_helper.py pt_BR [--check]
```

- `pt_BR` is the target language code (change as needed), it should correspond to a file named `pt_BR.axaml` in the `src/Resources/Locales/` directory, so you can replace it with any other language code you want to translate, e.g., `de_DE`, `es_ES`, etc.
- `--check` is an optional flag used to only check for missing keys without prompting for translations, useful for getting a list of missing translations.

The script will read the base language file (`en_US.axaml`) and the target language file (e.g., `pt_BR.axaml`), identify missing keys, and prompt you to provide translations for those keys. If the `--check` flag is used, it will only list the missing keys without prompting for translations.

## Third-Party Components

For detailed license information, see [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md).
