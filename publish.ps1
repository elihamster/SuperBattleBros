# Publishes the current version to Thunderstore.
#
#   .\publish.ps1
#
# 1. Reads the version from Plugin.cs and refuses if Thunderstore already has it
#    (Thunderstore never accepts the same version twice).
# 2. Runs package.ps1, which builds the DLL and writes the public and -priv zips.
# 3. Uploads ONLY the public zip with the Thunderstore CLI (tcli), using the token
#    in the SBB_THUNDERSTORE_TOKEN user environment variable.
#
# The token is never written to this repo, the config, or the console.
param([string]$Configuration = 'DevDebug')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$token = [Environment]::GetEnvironmentVariable('SBB_THUNDERSTORE_TOKEN', 'Process')
if ([string]::IsNullOrWhiteSpace($token)) { $token = [Environment]::GetEnvironmentVariable('SBB_THUNDERSTORE_TOKEN', 'User') }
if ([string]::IsNullOrWhiteSpace($token)) { throw "No token: set the SBB_THUNDERSTORE_TOKEN user environment variable first." }
if (-not (Get-Command tcli -ErrorAction SilentlyContinue)) { throw "tcli not found: dotnet tool install -g tcli" }

$m = Select-String -Path Plugin.cs -Pattern 'public const string Version\s*=\s*"([^"]+)"'
if (-not $m) { throw "Could not read Version from Plugin.cs" }
$ver = $m.Matches[0].Groups[1].Value

# Already published? Thunderstore would refuse it anyway; say so before building.
try {
    $pkg = Invoke-RestMethod "https://thunderstore.io/api/experimental/package/hamsterbby/SuperBattleBros/"
    $known = @($pkg.versions | ForEach-Object { $_.version_number })
    if ($known -contains $ver) { throw "Version $ver is already on Thunderstore. Bump Plugin.Version first." }
    Write-Host "Thunderstore has $($pkg.latest.version_number); publishing $ver."
} catch [System.Net.WebException] { Write-Host "Could not read the package from Thunderstore; trying the upload anyway." }

& .\package.ps1 -Configuration $Configuration
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) { throw "package.ps1 failed" }

$zip = "dist\SuperBattleBros-$ver.zip"
if (-not (Test-Path $zip)) { throw "Public zip not found: $zip" }

tcli publish --config-path thunderstore.toml --file $zip --token $token   # the version comes from the manifest inside the zip
if ($LASTEXITCODE -ne 0) { throw "tcli publish failed (exit $LASTEXITCODE)" }
Write-Host "Published SuperBattleBros $ver. r2modman may take a few minutes to see it."
