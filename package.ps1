# Builds the DLL and zips TWO packages into dist\:
#   SuperBattleBros-<ver>.zip        the public one, for Thunderstore: exactly what players install
#   SuperBattleBros-<ver>-priv.zip   the same plus the working documents (CLAUDE.md, the handoffs),
#                                    for the developer's own records; never uploaded
#
#   .\package.ps1                     # DevDebug: the TEST build, what ships while testing
#   .\package.ps1 -Configuration Release
#
# While the mod is in testing the published build IS the dev build, so everyone in
# the lobby runs the same thing the developer runs. Switch the default to Release
# when TEST comes off the title.
#
# The version comes from Plugin.cs (the one place it lives); manifest.json in
# thunderstore\ is stamped with it on the way into the zip, so the two cannot
# drift. Thunderstore refuses a version number it has already seen, so bump
# Plugin.Version before packaging an update.
param([string]$Configuration = 'DevDebug')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$m = Select-String -Path Plugin.cs -Pattern 'public const string Version\s*=\s*"([^"]+)"'
if (-not $m) { throw "Could not read Version from Plugin.cs" }
$ver = $m.Matches[0].Groups[1].Value

dotnet build -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) { throw "$Configuration build failed" }
$dll = "bin\$Configuration\netstandard2.1\SbgShields.dll"
if (-not (Test-Path $dll)) { throw "DLL not found at $dll" }

$stage = "obj\thunderstore"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

$manifest = Get-Content thunderstore\manifest.json -Raw | ConvertFrom-Json
$manifest.version_number = $ver
$manifest | ConvertTo-Json -Depth 5 | Set-Content "$stage\manifest.json" -Encoding UTF8
Copy-Item README.md, CHANGELOG.md, thunderstore\icon.png, $dll $stage
# Sound files the mod plays itself (parry). r2modman keeps the folder next to the DLL.
if (Test-Path sounds) { Copy-Item sounds "$stage\sounds" -Recurse }

New-Item -ItemType Directory -Force dist | Out-Null
$zip = "dist\SuperBattleBros-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip

Write-Host ""
Write-Host "Packaged $zip (version $ver, $Configuration build):"
Get-ChildItem $stage | Select-Object Name, Length | Out-Host

# The private zip: the public package plus the working documents.
$priv = "dist\SuperBattleBros-$ver-priv.zip"
if (Test-Path $priv) { Remove-Item $priv }
$docs = @(Get-ChildItem -Path . -Filter 'SBG-Shields-Handoff-*.md' | ForEach-Object { $_.FullName })
if (Test-Path CLAUDE.md) { $docs += (Resolve-Path CLAUDE.md).Path }
$docsDir = "$stage-priv"
if (Test-Path $docsDir) { Remove-Item $docsDir -Recurse -Force }
New-Item -ItemType Directory -Force "$docsDir\docs" | Out-Null
Copy-Item "$stage\*" $docsDir -Recurse
foreach ($d in $docs) { Copy-Item $d "$docsDir\docs" }
Compress-Archive -Path "$docsDir\*" -DestinationPath $priv
Write-Host "Packaged $priv (private: + docs\ $(($docs | ForEach-Object { Split-Path $_ -Leaf }) -join ', '))"
