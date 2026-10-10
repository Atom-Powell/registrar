#Requires -Version 7.0
<#
.SYNOPSIS
  Delete test residue left under the OS temp path by past runs of the suite.
.DESCRIPTION
  Fixing the suite (issue #69) stops new residue. It does not remove what is already
  on disk, and by the time it was measured that was 18,450 directories holding 10,591
  rendered DOCX files. This script removes them.

  It matches two populations:

  1. Directories named registrar-test-* . Everything the suite creates now carries that
     prefix, from TempDir.Prefix in tests/Registrar.Tests/TempDir.cs. This match is by
     name and is certain.

  2. Historical residue, created before the prefix existed. Directory.CreateTempSubdirectory
     with no prefix produces an opaque 8.3 name indistinguishable from any other
     application's scratch space, so these can only be found by their contents. The
     signatures below were derived by sampling the actual residue on a developer machine
     and are deliberately narrow: a directory is left alone unless it holds something only
     this engine or its fixtures produce.

  Anything not matched is left alone. The default is a dry run.
.PARAMETER Delete
  Actually delete. Without it the script only reports what it would remove.
.PARAMETER StaleHours
  Ignore anything modified more recently than this, so a suite running right now in
  another process is never deleted out from under itself. Default 1.
.EXAMPLE
  pwsh -File scripts/clean-test-temp.ps1
  Report what would be deleted, and delete nothing.
.EXAMPLE
  pwsh -File scripts/clean-test-temp.ps1 -Delete
  Delete it.
#>
[CmdletBinding()]
param(
  [switch]$Delete,
  [int]$StaleHours = 1
)

$ErrorActionPreference = 'Stop'

# Keep in step with TempDir.Prefix in tests/Registrar.Tests/TempDir.cs.
$prefix = 'registrar-test-'

# Names the suite used before TempDir existed, and the GovLib workstream's own scratch.
$legacyNamePattern = '^(registrar-|build-[0-9a-f]{32}$|build-test-|draft-settler-test-|editor-shell-test-|govlib-)'

# Files only this engine writes. A directory holding one of these is an output package.
$outputFiles = @(
  'validation-report.json'
  'generation-log.json'
  'artifact-manifest.json'
  'promotion-payload.json'
)

# Fixture scratch files, from tests that write a named input rather than a package.
$fixtureFiles = @('agr.json', 'locked.json', 'denied.json')

# A rendered deliverable with no report beside it. Some tests render a document and write
# nothing else, so the signatures above miss their directories: the first pass over a real
# machine left 206 of these behind. The names come from config-defined title patterns and
# the synthetic fixture client, which is why matching them is safe. Anchored with -like on
# the leading 'ORG ' rather than a bare '*.docx', so a genuine document that happens to be
# sitting in a temp directory is not swept up with them.
$rendered = { param($n) $n -like 'ORG *.docx' -or $n -eq 'a.docx' }

$temp = [System.IO.Path]::GetTempPath().TrimEnd('\')
$cutoff = (Get-Date).AddHours(-$StaleHours)

$matched = [System.Collections.Generic.List[string]]::new()
$skipped = 0
$tooRecent = 0

foreach ($dir in [System.IO.Directory]::EnumerateDirectories($temp)) {
  $name = [System.IO.Path]::GetFileName($dir)

  $files = @()
  $subdirs = @()
  try {
    $files = [System.IO.Directory]::EnumerateFiles($dir) | ForEach-Object { [System.IO.Path]::GetFileName($_) }
    $subdirs = [System.IO.Directory]::EnumerateDirectories($dir) | ForEach-Object { [System.IO.Path]::GetFileName($_) }
  } catch {
    # Unreadable means not ours to delete.
    $skipped++
    continue
  }

  # Guard. Another application keeps localized resource directories directly under temp
  # and its layout is nothing like ours; naming it explicitly is cheaper than trusting
  # the signatures below to never coincide with it.
  if (($subdirs -contains 'Feedback') -or ($files -contains 'feedback.exe')) {
    $skipped++
    continue
  }

  $isOurs = $false

  if ($name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $isOurs = $true }
  elseif ($name -match $legacyNamePattern) { $isOurs = $true }
  elseif ($files | Where-Object { $outputFiles -contains $_ }) { $isOurs = $true }
  elseif ($files | Where-Object { $fixtureFiles -contains $_ }) { $isOurs = $true }
  elseif ($files | Where-Object { & $rendered $_ }) { $isOurs = $true }
  # ConfigLocatorTests copies a config tree, which is the pair it looks for.
  elseif (($subdirs -contains 'config') -and ($subdirs -contains 'schemas')) { $isOurs = $true }
  elseif ($subdirs | Where-Object { $_ -like 'agr-sample-*' }) { $isOurs = $true }
  # SettingsStoreTests and MenuCommandTests, whose whole directory is one settings.json.
  elseif ($files.Count -eq 1 -and $subdirs.Count -eq 0 -and $files[0] -eq 'settings.json') { $isOurs = $true }
  elseif ($subdirs.Count -eq 0 -and $files.Count -eq 2 -and ($files -contains 'a.json') -and ($files -contains 'b.json')) { $isOurs = $true }

  if (-not $isOurs) {
    $skipped++
    continue
  }

  try {
    if ([System.IO.Directory]::GetLastWriteTime($dir) -ge $cutoff) {
      $tooRecent++
      continue
    }
  } catch {
    $skipped++
    continue
  }

  $matched.Add($dir)
}

Write-Host "Matched as test residue : $($matched.Count)"
Write-Host "Left alone              : $skipped"
Write-Host "Skipped, modified within $StaleHours h : $tooRecent"

if (-not $Delete) {
  Write-Host ''
  Write-Host 'Dry run. Nothing was deleted. Re-run with -Delete to remove them.'
  if ($matched.Count -gt 0) {
    Write-Host ''
    Write-Host 'First 10 that would be deleted:'
    $matched | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
  }
  return
}

$deleted = 0
$failed = 0
foreach ($dir in $matched) {
  try {
    [System.IO.Directory]::Delete($dir, $true)
    $deleted++
  } catch {
    # Best effort. A file still held open is a reason to leave that one directory and
    # keep going, not a reason to abandon the other eighteen thousand.
    $failed++
  }
}

Write-Host ''
Write-Host "Deleted : $deleted"
Write-Host "Failed  : $failed"
