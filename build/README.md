# build

> [!NOTE]
> The packaging files under the `build` folder are used by GitHub Actions. For a local release build, use `build-release.ps1` from the repository root.

## Local release build

Run the script with PowerShell 7 from the repository root. It defaults to the current platform and architecture and writes the published app to `artifacts/release/<runtime>`:

```powershell
./build-release.ps1
```

You can specify one of `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`, and optionally choose another output directory:

```powershell
./build-release.ps1 -RuntimeIdentifier linux-x64 -OutputDirectory out/linux-x64
```

The publish runs in Release configuration with up to 16 MSBuild worker processes. It produces a self-contained single executable, so the target machine does not need .NET installed. This script uses the .NET single-file bundler instead of NativeAOT to embed the runtime and native graphics libraries; native libraries extract automatically at startup. Debug symbols are embedded too.

## How to build this project manually

1. Make sure [.NET SDK 10](https://dotnet.microsoft.com/en-us/download) is installed on your machine.
2. Clone this project
3. Run the follow command under the project root dir
```sh
dotnet publish -c Release -r $RUNTIME_IDENTIFIER -o $DESTINATION_FOLDER src/YAGT.csproj
```
> [!NOTE]
> Please replace the `$RUNTIME_IDENTIFIER` with one of `win-x64`,`win-arm64`,`linux-x64`,`linux-arm64`,`osx-x64`,`osx-arm64`, and replace the `$DESTINATION_FOLDER` with the real path that will store the output executable files.
