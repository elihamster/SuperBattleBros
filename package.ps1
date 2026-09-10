# Builds the Release DLL and zips a Thunderstore package into dist\.
#
#   .\package.ps1
#
# The version comes from Plugin.cs (the one place it lives); manifest.json in
# thunderstore\ is stamped with it on the way into the zip, so the two cannot
# drift. Thunderstore refuses a version number it has already seen, so bump
# Plugin.Version before packaging an update.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$m = Select-String -Path Plugin.cs -Pattern 'public const string Version\s*=\s*"([^"]+)"'
if (-not $m) { throw "Could not read Version from Plugin.cs" }
$ver = $m.Matches[0].Groups[1].Value

dotnet build -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Release build failed" }
$dll = "bin\Release\netstandard2.1\SbgShields.dll"
if (-not (Test-Path $dll)) { throw "Release DLL not found at $dll" }

$stage = "obj\thunderstore"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

$manifest = Get-Content thunderstore\manifest.json -Raw | ConvertFrom-Json
$manifest.version_number = $ver
$manifest | ConvertTo-Json -Depth 5 | Set-Content "$stage\manifest.json" -Encoding UTF8
Copy-Item thunderstore\README.md, thunderstore\CHANGELOG.md, thunderstore\icon.png, $dll $stage

New-Item -ItemType Directory -Force dist | Out-Null
$zip = "dist\SuperBattleBros-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip

Write-Host ""
Write-Host "Packaged $zip (version $ver):"
Get-ChildItem $stage | Select-Object Name, Length | Out-Host
