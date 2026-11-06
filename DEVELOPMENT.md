# Local development

Run these commands in PowerShell 7 from the repository root. Read [AGENTS.md](AGENTS.md) before changing code.

## Prerequisites

- Git for Windows 2.25.1 or later.
- .NET SDK 10.0.403 or a later stable .NET 10 SDK, as selected by `global.json`.
- PowerShell 7 for `build-release.ps1`.
- Python 3 for resource checks, translation helpers, and repository scripts. No Python packages are required for the resource check.
- Node.js 24 and npm for the optional translation report generator.
- For Windows NativeAOT releases: Visual Studio 2022 or later with **Desktop development with C++**, including its default MSVC tools and Windows SDK. See [Microsoft's prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#prerequisites). The portable release script does not require this workload.

The project uses NuGet.org. Check `dotnet nuget list source` if restore fails; add `https://api.nuget.org/v3/index.json` only if it is missing.

For a project-local SDK, use Microsoft's installer from the repository root:

```powershell
New-Item -ItemType Directory -Path artifacts/setup -Force | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile artifacts/setup/dotnet-install.ps1
./artifacts/setup/dotnet-install.ps1 -Version 10.0.403 -InstallDir .dotnet -NoPath
dotnet --version
```

`global.json` searches `.dotnet` before the host SDKs. The local SDK and generated artifacts are ignored by Git. SDK 10.0.403 includes runtime 10.0.14, which fixes the [Brotli vulnerability reported against 10.0.12](https://github.com/advisories/GHSA-p3jp-9v83-76w9).

## Build and run

```powershell
git submodule update --init --recursive
dotnet restore
dotnet build
dotnet run --project src/YAGT.csproj -- C:\Source\yagt
```

Replace the final path with the repository you want to open, or omit `--` and the path to open the launcher. Close the development instance before rebuilding on Windows, because it locks the build output.

Application settings normally live in `%APPDATA%\YAGT`. To isolate development settings, create `src/bin/Debug/net10.0/data` before launching the built executable; the adjacent `data` directory enables portable mode.

## Checks before submitting changes

```powershell
dotnet build
dotnet format --verify-no-changes src/YAGT.csproj
python skills/ui-theme-and-styles/scripts/check_resources.py
dotnet list src/YAGT.csproj package --vulnerable --include-transitive
```

There is no application unit-test project. Launch the app and exercise the changed behavior. CI also builds and publishes for Windows, macOS, and Linux; a local Windows build does not verify other platforms.

For translation maintenance, install the same dependencies used by CI:

```powershell
npm install --no-save --package-lock=false fs-extra@11.2.0 path@0.12.7 xml2js@0.6.2
node build/scripts/localization-check.js
```

The translation command rewrites locale files and `TRANSLATION.md`. Run it when updating translations and review the diff. `python translate_helper.py <locale> --check` checks missing translations without prompting.

## Release builds

Portable, self-contained Windows release (includes .NET):

```powershell
./build-release.ps1
./artifacts/release/win-x64/YAGT.exe
```

NativeAOT release, matching the Windows x64 CI publish mode:

```powershell
dotnet publish src/YAGT.csproj -c Release -r win-x64 -o artifacts/native/win-x64
```

If publishing reports `Platform linker not found`, install the C++ workload and Windows SDK through Visual Studio Installer with administrator approval. Do not disable AOT or suppress warnings to validate the native release. See [build/README.md](build/README.md) for other runtime identifiers and packaging details.

The repository currently has no tags, so its version metadata command may print `fatal: No names found, cannot describe anything.` during a successful build. This message comes from the existing best-effort `git describe` target; check the final build result.

## Updating the checkout

Review or commit local changes first, then:

```powershell
git pull --ff-only
git submodule update --init --recursive
dotnet restore
```

Rerun the checks after updating. Keep the pinned AvaloniaEdit submodule revision unless deliberately updating that dependency. For SourceGit upstream intake, follow [skills/upstream-sync/AGENTS.md](skills/upstream-sync/AGENTS.md).
