[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $architecture = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        'X64' { 'x64' }
        'Arm64' { 'arm64' }
        default { throw "Unsupported architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)" }
    }

    $operatingSystem = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        'win'
    }
    elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        'linux'
    }
    elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        'osx'
    }
    else {
        throw 'Unsupported operating system.'
    }

    $RuntimeIdentifier = "$operatingSystem-$architecture"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "artifacts/release/$RuntimeIdentifier"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot $OutputDirectory
}

$projectPath = Join-Path $PSScriptRoot 'src/YAGT.csproj'
$arguments = @(
    'publish'
    $projectPath
    '--configuration'
    'Release'
    '--runtime'
    $RuntimeIdentifier
    '--output'
    $OutputDirectory
    '--self-contained'
    'true'
    # Use the single-file bundler to include the runtime and native graphics libraries.
    '-p:DisableAOT=true'
    '-p:PublishSingleFile=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-p:DebugType=embedded'
    '-maxcpucount:16'
)

Write-Host "Publishing $RuntimeIdentifier to $OutputDirectory"
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($RuntimeIdentifier.StartsWith('linux-', [System.StringComparison]::Ordinal)) {
    Move-Item -LiteralPath (Join-Path $OutputDirectory 'YAGT') -Destination (Join-Path $OutputDirectory 'yagt') -Force
}
