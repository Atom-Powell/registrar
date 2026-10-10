#Requires -Version 7.0
<#
.SYNOPSIS
  Publish a self-contained, single-file registrar.exe and assemble a release zip.
.DESCRIPTION
  Produces dist/registrar/ with the binaries and launchers in a runtime/ subdirectory
  (runtime/registrar.exe, runtime/Registrar.cmd, runtime/Registrar.App.exe,
  runtime/RegistrarApp.cmd) alongside editable config/, schemas/,
  assets/, README.md, and OPERATOR.md, then zips it to dist/registrar.zip. The engine resolves
  config/schemas/assets by walking up from the exe location, so runtime/ sitting one
  level below the data folders resolves correctly (verified against the single-file
  self-contained build). No .NET runtime is required on the target machine. No trimming
  (protects System.Text.Json / OpenXml reflection).
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$stage = Join-Path $dist 'registrar'
$runtime = Join-Path $stage 'runtime'

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $runtime -Force | Out-Null

dotnet publish (Join-Path $repo 'src/Registrar.Cli/Registrar.Cli.csproj') `
  -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishTrimmed=false `
  -o $runtime

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# The app resolves config/schemas/assets by walking up from its own location, exactly
# as the CLI does, so it ships into the same runtime/ folder and shares one payload.
# Not self-contained single-file: WPF plus WebView2 does not support single-file
# packaging, and the CLI's self-contained runtime is already present in this folder.
dotnet publish (Join-Path $repo 'src/Registrar.App/Registrar.App.csproj') `
  -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=false -p:PublishTrimmed=false `
  -o $runtime

if ($LASTEXITCODE -ne 0) { throw "dotnet publish (app) failed with exit code $LASTEXITCODE" }

# Ship config as editable data one level above the runtime folder (rule 3). The engine
# walks up from runtime/ to find these siblings.
foreach ($dir in 'config','schemas','assets') {
  Copy-Item (Join-Path $repo $dir) (Join-Path $stage $dir) -Recurse -Force
}
# Both launchers self-locate via %~dp0 and run their executable from their own folder.
# RegistrarApp.cmd is the only thing in runtime/ that tells a technician which of the
# several hundred published files is the window this unit built.
Copy-Item (Join-Path $repo 'Registrar.cmd') $runtime -Force
Copy-Item (Join-Path $repo 'RegistrarApp.cmd') $runtime -Force
Copy-Item (Join-Path $repo 'README.md') $stage -Force
Copy-Item (Join-Path $repo 'OPERATOR.md') $stage -Force

# Keep only the launcher-relevant published files tidy: remove the .pdb if present.
Get-ChildItem $runtime -Filter '*.pdb' | Remove-Item -Force -ErrorAction SilentlyContinue

$zip = Join-Path $dist 'registrar.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Published to $stage and zipped to $zip"
