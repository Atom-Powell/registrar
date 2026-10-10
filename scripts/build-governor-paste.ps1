#Requires -Version 7.0
<#
.SYNOPSIS
  Generate docs/governor-instructions-paste.txt from docs/governor-instructions.md.
.DESCRIPTION
  Copilot Studio's instructions editor drops intra-paragraph newlines without
  substituting a space. The canonical instruction text is hard-wrapped, so pasting it in
  directly glues words together ("clauseselection", "TheRegistrar") and collapses the
  code-to-name table onto one line. That table is the control added after the 2026-07-15
  eval caught the agent confabulating full type names, so its collapse hits the one place
  drift is most expensive.

  This script emits the same text reflowed to one physical line per paragraph, which
  leaves no intra-paragraph newline to swallow, and converts the markdown table into an
  inline sentence list that survives collapse.

  It is generated rather than hand-maintained on purpose. The first attempt at this file
  was a hand-written second copy of the instruction text; it went stale within days when
  the knowledge sources changed under it, which is how it ended up stranded on an
  unmerged branch. A generated file cannot drift from its source.

  Fails closed: a paste block over the Studio cap, or carrying a character the engine's
  own hygiene rules prohibit, is not written.
.PARAMETER Check
  Verify the committed file matches what would be generated, and change nothing. Exits
  non-zero on any difference.
#>
[CmdletBinding()]
param(
  [switch]$Check
)
$ErrorActionPreference = 'Stop'
$repo   = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'docs/governor-instructions.md'
$target = Join-Path $repo 'docs/governor-instructions-paste.txt'

# Studio's instructions field caps at 8000 characters. Measure against CRLF rather than
# LF: the file may reach a browser textarea with either, and clearing the cap on the
# pessimistic count beats relying on the textarea to normalize.
$cap = 8000

$lines = Get-Content $source

# The operative block is what sits between the first two horizontal rules. Everything
# above the first rule is guidance for the operator and must not reach the agent.
$rules = @(0..($lines.Count - 1) | Where-Object { $lines[$_].Trim() -eq '---' })
if ($rules.Count -lt 2) {
  throw "expected at least two '---' rules in $source, found $($rules.Count). The paste block is delimited by them."
}
$block = $lines[($rules[0] + 1)..($rules[1] - 1)]

# Group into paragraphs on blank lines, preserving order.
$paragraphs = @()
$current = @()
foreach ($line in $block) {
  if ($line.Trim() -eq '') {
    if ($current.Count) { $paragraphs += ,$current; $current = @() }
  } else {
    $current += $line
  }
}
if ($current.Count) { $paragraphs += ,$current }

# Reflow. A markdown table becomes "CODE = Name." sentences appended to the paragraph
# above it, because a table is precisely what the newline collapse destroys.
$out = @()
foreach ($p in $paragraphs) {
  if ($p[0].TrimStart().StartsWith('|')) {
    $cells = foreach ($row in $p) {
      $parts = $row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
      # Skip the header row and the |---|---| separator.
      if ($parts.Count -eq 2 -and $parts[0] -ne 'Code' -and $parts[0] -notmatch '^-+$') {
        "$($parts[0]) = $($parts[1])."
      }
    }
    if (-not $cells) { throw "table paragraph produced no rows; the table shape in $source changed." }
    if (-not $out) { throw "a table appeared before any paragraph to attach it to." }
    $out[-1] = ($out[-1] + ' ' + ($cells -join ' '))
  } else {
    $out += (($p | ForEach-Object { $_.Trim() }) -join ' ')
  }
}

# The prose points at "this table", which no longer exists once it is inlined. Rewrite
# exactly that phrase and assert the rewrite took, so a reword upstream fails this build
# instead of shipping a paste block that refers to something the agent cannot see.
$joined = $out -join "`n`n"
$before = $joined
$joined = $joined.Replace('copy it from this table.', 'copy it from this list.')
if ($joined -eq $before) {
  throw "expected the phrase 'copy it from this table.' in the block and found none. If it was reworded, update this script."
}
if ($joined -match '(?m)^\|') { throw "a markdown table survived reflow." }

# Fail closed on the two properties that make this file safe to paste.
$nonAscii = [regex]::Matches($joined, '[^\x20-\x7E\n]')
if ($nonAscii.Count) {
  $shown = ($nonAscii | ForEach-Object { "U+{0:X4}" -f [int][char]$_.Value } | Sort-Object -Unique) -join ', '
  throw "paste block carries $($nonAscii.Count) non-printable-ASCII character(s): $shown. Studio and the engine's character policy both want plain ASCII here."
}
$lf   = $joined.Length
$crlf = $joined.Replace("`n", "`r`n").Length
if ($crlf -gt $cap) {
  throw "paste block is $crlf characters counting CRLF, over Studio's $cap cap. Trim the instructions; do not raise the cap."
}

$existing = if (Test-Path $target) { (Get-Content $target -Raw) -replace "`r`n", "`n" } else { $null }
$expected = $joined + "`n"

if ($Check) {
  if ($existing -ne $expected) {
    throw "$target is out of date with $source. Run scripts/build-governor-paste.ps1 to regenerate."
  }
  Write-Host "paste block up to date: $lf characters (LF), $crlf (CRLF), cap $cap"
  return
}

Set-Content $target $expected -Encoding utf8NoBOM -NoNewline
Write-Host "Wrote $target"
Write-Host "$lf characters (LF), $crlf (CRLF), against Studio's $cap cap. $($out.Count) paragraphs, one physical line each."
