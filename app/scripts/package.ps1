# Builds the release artifacts into <repo>\dist (git-ignored):
#   BDOTempo-Setup-<version>.exe      installer (Inno Setup 6): app + the 17 asset parts; assembles assets.bdoa on install
#   BDOTempo-Portable-<version>.zip   portable: app + assets.bdoa (assembled here) + portable.flag, no installer
#   SHA256SUMS.txt                         sha256sum format, for the artifacts of this version (the updater verifies the installer)
# The version is the single source of truth: <Version> in app/Directory.Build.props.
# Run through mise: `mise run package`, `mise run package-installer`, `mise run package-portable` (app/mise.toml).
[CmdletBinding()]
param(
    [ValidateSet('installer', 'portable', 'all')]
    [string]$Target = 'all',
    [switch]$KeepWork
)

$ErrorActionPreference = 'Stop'

$appRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $appRoot
$dist = Join-Path $repoRoot 'dist'
$work = Join-Path $dist 'work'
$publish = Join-Path $work 'publish'
$partsDir = Join-Path $appRoot 'assets\parts'
$project = Join-Path $appRoot 'src\BdoComboSetup\BdoComboSetup.csproj'
$issScript = Join-Path $appRoot 'installer\BDOTempo.iss'

$props = Get-Content -Raw -LiteralPath (Join-Path $appRoot 'Directory.Build.props')
$match = [regex]::Match($props, '<Version>\s*([^<\s]+)\s*</Version>')
if (-not $match.Success) {
    throw 'The <Version> of app/Directory.Build.props was not found.'
}
$version = $match.Groups[1].Value
if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$') {
    throw "The version '$version' is not SemVer 2.0 (app/Directory.Build.props)."
}

$installerName = "BDOTempo-Setup-$version.exe"
$portableName = "BDOTempo-Portable-$version.zip"
$partFiles = @(Get-ChildItem -LiteralPath $partsDir -Filter 'assets.part*.bdoa' -File -ErrorAction SilentlyContinue)
if ($partFiles.Count -eq 0) {
    throw "No asset parts in $partsDir. Check out app/assets/parts (they are committed)."
}

function Invoke-Publish {
    if (Test-Path -LiteralPath $publish) {
        Remove-Item -LiteralPath $publish -Recurse -Force
    }
    Write-Host "Publishing the framework-dependent win-x64 app $version ..."
    & dotnet publish $project -c Release -r win-x64 -p:Platform=x64 -p:SelfContained=true "-p:PublishDir=$publish\"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'BDOTempo.exe'))) {
        throw "The publish output has no BDOTempo.exe."
    }
    if (Test-Path -LiteralPath (Join-Path $publish 'assets.bdoa')) {
        throw 'The publish output must not contain assets.bdoa: it is assembled by the packaging or the installer.'
    }
}

# The app's own assembler (the same code the installer runs). Its exit code is the contract (app/docs/asset-pack-format.md).
function Invoke-Assemble([string]$outFile) {
    $arguments = '--assemble-assets "' + $partsDir + '" "' + $outFile + '"'
    $process = Start-Process -FilePath (Join-Path $publish 'BDOTempo.exe') -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $outFile)) {
        throw "assets.bdoa could not be assembled (exit code $($process.ExitCode))."
    }
}

function New-Portable {
    $stage = Join-Path $work "portable\BDOTempo-Portable-$version"
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item -Path (Join-Path $publish '*') -Destination $stage -Recurse -Force
    Invoke-Assemble (Join-Path $stage 'assets.bdoa')
    # The marker that makes the app portable: the updater opens the release page instead of running an installer.
    Set-Content -LiteralPath (Join-Path $stage 'portable.flag') -Value "portable build ${version}: updates open the release page" -Encoding ascii

    $zip = Join-Path $dist $portableName
    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    Write-Host "Written $zip"
}

# Inno Setup records where it is installed in the Uninstall keys (per machine, 32-bit view, per user). winget installs
# that way too, into a folder of its own choice, so the standard folders alone do not find it.
function Find-IsccInRegistry {
    $roots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
    )
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }
        foreach ($key in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $entry = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if (-not $entry -or $entry.DisplayName -notlike 'Inno Setup*') {
                continue
            }
            if ($entry.InstallLocation) {
                Join-Path $entry.InstallLocation.TrimEnd('\') 'ISCC.exe'
            }
            if ($entry.UninstallString) {
                # The uninstaller sits next to the compiler: "C:\...\Inno Setup 6\unins000.exe" (quoted, maybe with arguments).
                $uninstaller = [regex]::Match([string]$entry.UninstallString, '^\s*"?([^"]+?\.exe)"?').Groups[1].Value
                if ($uninstaller) {
                    Join-Path (Split-Path -Parent $uninstaller) 'ISCC.exe'
                }
            }
        }
    }
}

function Find-Iscc {
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($env:INNO_SETUP_COMPILER) {
        $candidates.Add($env:INNO_SETUP_COMPILER)
    }
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) {
        $candidates.Add($onPath.Source)
    }
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        if ($root) {
            $candidates.Add((Join-Path $root 'Inno Setup 6\ISCC.exe'))
        }
    }
    foreach ($registered in @(Find-IsccInRegistry)) {
        $candidates.Add($registered)
    }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }
    return $null
}

function New-Installer {
    $iscc = Find-Iscc
    if (-not $iscc) {
        throw 'Inno Setup 6 (ISCC.exe) was not found. Install it once: winget install --id JRSoftware.InnoSetup -e (or https://jrsoftware.org/isdl.php), or set INNO_SETUP_COMPILER to the full path of ISCC.exe. Then run the task again.'
    }
    Write-Host "Compiling the installer with $iscc ..."
    & $iscc "/DAppVersion=$version" "/DPublishDir=$publish" "/DPartsDir=$partsDir" "/DOutputDir=$dist" $issScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed with exit code $LASTEXITCODE."
    }
    $installer = Join-Path $dist $installerName
    if (-not (Test-Path -LiteralPath $installer)) {
        throw "Inno Setup did not write $installerName."
    }
    Write-Host "Written $installer"
}

function Write-Checksums {
    $artifacts = @($installerName, $portableName) |
        ForEach-Object { Join-Path $dist $_ } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $lines = foreach ($artifact in $artifacts) {
            $stream = [System.IO.File]::OpenRead($artifact)
            try {
                $hash = ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
            }
            finally {
                $stream.Dispose()
            }
            "$hash  $(Split-Path -Leaf $artifact)"
        }
    }
    finally {
        $sha256.Dispose()
    }
    Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Value $lines -Encoding ascii
    Write-Host 'Written SHA256SUMS.txt'
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
try {
    Invoke-Publish
    if ($Target -eq 'portable' -or $Target -eq 'all') {
        New-Portable
    }
    if ($Target -eq 'installer' -or $Target -eq 'all') {
        New-Installer
    }
    Write-Checksums
}
finally {
    if (-not $KeepWork -and (Test-Path -LiteralPath $work)) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}

Get-ChildItem -LiteralPath $dist -File | ForEach-Object {
    Write-Host ("{0,-40} {1,12:N0} bytes" -f $_.Name, $_.Length)
}
