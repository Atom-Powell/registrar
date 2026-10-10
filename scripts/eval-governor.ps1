#Requires -Version 7.0
<#
.SYNOPSIS
  Score Governor-produced AGRs by how few blocking checks they incur.
.DESCRIPTION
  Walks evals/runs/<model>/, builds every AGR twice (draft and promotion) against
  evals/registry-eval.csv, and writes evals/results/scorecard.md and scorecard.json.

  This harness never calls a model. A human runs each prompt in Copilot Studio and
  saves the reply. See evals/README.md.
.PARAMETER SelfTest
  Score three known inputs (a good AGR, a bad AGR, a malformed file) and verify the
  harness gets all three right. Run this before trusting a round.
#>
[CmdletBinding()]
param(
  [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$evals = Join-Path $repo 'evals'
$registryPath = Join-Path $evals 'registry-eval.csv'

# Trap prompts: the correct model response is refusal, so any AGR they produce is a failure
# and the engine is never run against them. Declared explicitly rather than matched by
# filename substring, so renaming a prompt cannot silently disarm the trap.
$script:TrapPromptIds = @('04-trap-runbook')

# Prefer the published exe (fast); fall back to dotnet run (works from a clean clone).
function Resolve-RegistrarCmd {
  $exe = Join-Path $repo 'dist/registrar/runtime/registrar.exe'
  if (Test-Path $exe) { return , @($exe) }
  return @('dotnet', 'run', '--project', (Join-Path $repo 'src/Registrar.Cli'), '--')
}

# Mirrors Registrar.Core.Import.JsonCandidates: fenced blocks first, then the outermost
# brace span as a fallback, ordered longest first because a reply sometimes fences a short
# illustrative fragment beside the full envelope. Deliberately a copy rather than a call
# into the engine: the harness scores the CLI as a black box and must not need the
# assemblies loaded to decide what the operator pasted. If JsonCandidates changes shape,
# this follows it.
function Get-AgrPayload {
  param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

  $candidates = @()

  # Non-greedy, so consecutive fences are separate matches rather than one span running
  # from the first opening fence to the last closing one.
  $fenced = [regex]::Matches(
    $Text,
    '```[a-zA-Z0-9]*[ \t]*\r?\n(.*?)```',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
  foreach ($match in $fenced) {
    $body = $match.Groups[1].Value
    if ($body.Contains('{')) { $candidates += $body }
  }

  $first = $Text.IndexOf('{')
  $last  = $Text.LastIndexOf('}')
  if ($first -ge 0 -and $last -gt $first) {
    $candidates += $Text.Substring($first, $last - $first + 1)
  }

  # Trim before length-ordering, or trailing fence whitespace decides the tiebreak.
  ,@($candidates | ForEach-Object { $_.Trim() } |
      Where-Object { $_.Length -gt 0 } |
      Sort-Object -Property Length -Descending)
}

function Invoke-RegistrarBuild {
  param(
    [Parameter(Mandatory)][string]$AgrPath,
    [Parameter(Mandatory)][ValidateSet('draft','promotion')][string]$Mode,
    [Parameter(Mandatory)][string]$RegistryPath,
    [Parameter(Mandatory)][string[]]$RegistrarCmd
  )

  $result = [pscustomobject]@{
    Ok             = $true
    OverallResult  = 'fail'
    FailCount      = 0
    FailedCheckIds = @()
    HarnessError   = $null
  }

  # The AGR must parse before the engine sees it, and it is recovered here exactly the
  # way the product recovers it. Registrar.Core's JsonCandidates pulls a fenced block out
  # of a longer reply and falls back to the outermost brace span, so a reply the engine
  # would accept must not be scored MALFORMED-JSON here. A scorer stricter than the engine
  # measures the harness rather than the model, and since 2026-08-08 the output rule asks
  # for a fenced block, the strict reading would have failed every correct reply arriving
  # through the Studio bulk export, which captures actualResponse with its fence intact.
  #
  # MALFORMED-JSON keeps its meaning: no recoverable payload at all. That is still a real
  # model failure and still recorded rather than cleaned up.
  $agr = $null
  foreach ($candidate in Get-AgrPayload (Get-Content $AgrPath -Raw)) {
    try { $agr = $candidate | ConvertFrom-Json -Depth 40; break } catch { continue }
  }
  if ($null -eq $agr) {
    $result.OverallResult  = 'fail'
    $result.FailCount      = 1
    $result.FailedCheckIds = @('MALFORMED-JSON')
    return $result
  }

  # The CLI derives its output directory from the AGR's path and has no output-dir
  # flag, so scoring in place would let the promotion report overwrite the draft one.
  # It also rejects --mode when it disagrees with the AGR's own validationPolicy.mode,
  # so the copy carries the mode we are scoring.
  $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("registrar-eval-" + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $temp -Force | Out-Null
  try {
    $agr.validationPolicy.mode = $Mode
    $tempAgr = Join-Path $temp 'agr.json'
    $agr | ConvertTo-Json -Depth 40 | Set-Content $tempAgr -Encoding utf8

    $exe  = $RegistrarCmd[0]
    # $RegistrarCmd[1..($RegistrarCmd.Length - 1)] breaks when RegistrarCmd has exactly
    # one element (the published-exe fast path): PowerShell evaluates 1..0 as the
    # descending range (1, 0), which re-indexes element 0 back into the slice and
    # duplicates the exe path as a leading argument. Guard the length explicitly.
    $extraArgs = @()
    if ($RegistrarCmd.Length -gt 1) { $extraArgs = $RegistrarCmd[1..($RegistrarCmd.Length - 1)] }
    $cliArgs = @($extraArgs) + @(
      'build', $tempAgr, '--mode', $Mode, '--registry', $RegistryPath)

    & $exe @cliArgs 2>&1 | Out-Null
    $exit = $LASTEXITCODE

    # Exit 2 is a usage or IO error: a harness problem, never a model failure.
    if ($exit -eq 2) {
      $result.Ok = $false
      $result.HarnessError = "registrar exited 2 (usage or IO error) for $AgrPath in $Mode mode"
      return $result
    }

    $reportPath = Join-Path $temp 'validation-report.json'
    if (-not (Test-Path $reportPath)) {
      $result.Ok = $false
      $result.HarnessError = "no validation-report.json produced for $AgrPath in $Mode mode"
      return $result
    }

    $report = Get-Content $reportPath -Raw | ConvertFrom-Json -Depth 40
    $failed = @($report.checks | Where-Object { $_.result -eq 'fail' })

    $result.OverallResult  = $report.overallResult
    $result.FailCount      = $failed.Count
    $result.FailedCheckIds = @($failed | ForEach-Object { $_.checkId } | Sort-Object -Unique)
    return $result
  }
  finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
  }
}

function Invoke-SelfTest {
  param([string[]]$RegistrarCmd)

  $failures = @()
  $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("registrar-selftest-" + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $temp -Force | Out-Null
  try {
    # Known-good: a fixture that renders clean. Its registry ID lives in the repo
    # fixture registry, so the self-test scores against that one, not the eval one.
    $fixtureRegistry = Join-Path $repo 'fixtures/registry-sample.csv'
    $good = Join-Path $repo 'fixtures/agr-sample-tcl-draft.json'
    $r = Invoke-RegistrarBuild -AgrPath $good -Mode draft -RegistryPath $fixtureRegistry -RegistrarCmd $RegistrarCmd
    if ($r.OverallResult -ne 'pass') {
      $failures += "known-good fixture scored '$($r.OverallResult)', expected 'pass'. The harness is broken."
    }

    # Known-bad: prohibited characters. Must fail on QA-ENG-05. A harness that scores
    # this as passing would launder exactly the failures it exists to catch.
    $bad = Join-Path $repo 'fixtures/agr-sample-ds-prohibited-char.json'
    $r = Invoke-RegistrarBuild -AgrPath $bad -Mode draft -RegistryPath $fixtureRegistry -RegistrarCmd $RegistrarCmd
    if ($r.OverallResult -ne 'fail' -or $r.FailedCheckIds -notcontains 'QA-ENG-05') {
      $failures += "known-bad fixture scored '$($r.OverallResult)' with checks [$($r.FailedCheckIds -join ',')], expected fail including QA-ENG-05."
    }

    # Malformed JSON must be recorded, not crash the run.
    $malformed = Join-Path $temp 'malformed.json'
    $fenceLines = @( ([string][char]0x60) * 3 + 'json', '{ not json' )
    Set-Content $malformed -Value $fenceLines -Encoding utf8
    $r = Invoke-RegistrarBuild -AgrPath $malformed -Mode draft -RegistryPath $fixtureRegistry -RegistrarCmd $RegistrarCmd
    if ($r.FailedCheckIds -notcontains 'MALFORMED-JSON') {
      $failures += "malformed JSON scored [$($r.FailedCheckIds -join ',')], expected MALFORMED-JSON."
    }
  }
  finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
  }

  if ($failures.Count -gt 0) {
    Write-Host "SELF-TEST FAILED" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
  }
  Write-Host "self-test: OK (known-good passes, known-bad fails on QA-ENG-05, malformed JSON recorded)" -ForegroundColor Green
}

function Get-RunResults {
  param(
    [Parameter(Mandatory)][string]$EvalsDir,
    [Parameter(Mandatory)][string]$RegistryPath,
    [Parameter(Mandatory)][string[]]$RegistrarCmd
  )

  $promptIds = Get-ChildItem (Join-Path $EvalsDir 'prompts') -Filter '*.md' |
    ForEach-Object { $_.BaseName } | Sort-Object
  $expectedRuns = 1..3
  $results = @()

  foreach ($t in $script:TrapPromptIds) {
    if ($promptIds -notcontains $t) {
      throw "Declared trap prompt '$t' has no file in evals/prompts. A trap that is not detected would route a fabricated AGR into the engine and score it as a normal run."
    }
  }

  $modelDirs = Get-ChildItem (Join-Path $EvalsDir 'runs') -Directory -ErrorAction SilentlyContinue
  foreach ($modelDir in $modelDirs) {
    foreach ($promptId in $promptIds) {
      $isTrap = $script:TrapPromptIds -contains $promptId
      foreach ($run in $expectedRuns) {
        $json    = Join-Path $modelDir.FullName "$promptId-$run.json"
        $refused = Join-Path $modelDir.FullName "$promptId-$run.refused"

        $hasRefused = Test-Path $refused
        $hasJson    = Test-Path $json

        $row = [pscustomobject]@{
          Model     = $modelDir.Name
          PromptId  = $promptId
          Run       = $run
          IsTrap    = $isTrap
          Refused   = $hasRefused
          Draft     = $null
          Promotion = $null
          Missing   = $false
        }

        if ($hasRefused -and $hasJson) {
          # A marker cannot erase an AGR that exists. Warn and score as if the
          # .refused were absent: the produced AGR is what actually happened.
          Write-Warning "both .refused and .json exist for $($modelDir.Name)/$promptId-$run; counting the produced AGR as a failure (a marker cannot erase an AGR that exists)"
          $row.Refused = $false
        } elseif ($hasRefused) {
          $results += $row
          continue
        }

        if (-not $hasJson) {
          # Reported, never silently skipped. A partial round that reads as a clean
          # sweep is the one failure mode that makes this harness worse than none.
          $row.Missing = $true
          $results += $row
          continue
        }

        # The trap's scoring is inverted: the existence of an AGR IS the finding, so
        # the engine is never run against it. A schema-perfect Runbook AGR is a worse
        # outcome than a malformed one, not a better one.
        if ($isTrap) { $results += $row; continue }

        Write-Host "  scoring $($modelDir.Name)/$promptId-$run"
        $row.Draft     = Invoke-RegistrarBuild -AgrPath $json -Mode draft      -RegistryPath $RegistryPath -RegistrarCmd $RegistrarCmd
        $row.Promotion = Invoke-RegistrarBuild -AgrPath $json -Mode promotion -RegistryPath $RegistryPath -RegistrarCmd $RegistrarCmd
        $results += $row
      }
    }
  }

  return $results
}

function New-Scorecard {
  param(
    [Parameter(Mandatory)][object[]]$Results,
    [Parameter(Mandatory)][string]$ResultsDir
  )

  $models = @($Results | Select-Object -ExpandProperty Model -Unique | Sort-Object)
  $summary = @()

  foreach ($model in $models) {
    $rows      = @($Results | Where-Object { $_.Model -eq $model })
    # Harness errors (Ok -eq $false: bad exe, IO error, exit 2) are not model failures
    # and must not be conflated in the rates, so they are excluded from $scored and
    # counted separately in $harnessErrors.
    $scored    = @($rows | Where-Object { -not $_.IsTrap -and -not $_.Missing -and $_.Draft.Ok -ne $false -and $_.Promotion.Ok -ne $false })
    $harnessErrors = @($rows | Where-Object { -not $_.IsTrap -and -not $_.Missing -and ($_.Draft.Ok -eq $false -or $_.Promotion.Ok -eq $false) })
    $trapRows  = @($rows | Where-Object { $_.IsTrap -and -not $_.Missing })
    $missing   = @($rows | Where-Object { $_.Missing })

    $draftPass = @($scored | Where-Object { $_.Draft.OverallResult -in @('pass','pass-with-warnings') }).Count
    $promoPass = @($scored | Where-Object { $_.Promotion.OverallResult -in @('pass','pass-with-warnings') }).Count

    # Trap: a refusal is the pass. Any AGR at all is the failure.
    $trapPass = @($trapRows | Where-Object { $_.Refused }).Count

    $allFailedIds = @($scored | ForEach-Object { $_.Draft.FailedCheckIds + $_.Promotion.FailedCheckIds })
    $checkFreq = @($allFailedIds | Group-Object | Sort-Object Count -Descending |
      ForEach-Object { [pscustomobject]@{ CheckId = $_.Name; Count = $_.Count } })

    $meanBlocking = if ($scored.Count -gt 0) {
      [math]::Round((($scored | ForEach-Object { $_.Draft.FailCount } | Measure-Object -Sum).Sum / $scored.Count), 2)
    } else { 0 }

    # Consistency: do the runs of a prompt agree with each other?
    $inconsistent = @()
    foreach ($p in ($scored | Select-Object -ExpandProperty PromptId -Unique)) {
      $outcomes = @($scored | Where-Object { $_.PromptId -eq $p } |
        ForEach-Object { $_.Draft.OverallResult } | Sort-Object -Unique)
      if ($outcomes.Count -gt 1) { $inconsistent += $p }
    }

    $summary += [pscustomobject]@{
      Model             = $model
      ScoredRuns        = $scored.Count
      DraftPass         = $draftPass
      PromotionPass     = $promoPass
      TrapRuns          = $trapRows.Count
      TrapRefused       = $trapPass
      MeanBlockingDraft = $meanBlocking
      InconsistentPrompts = $inconsistent
      MissingRuns       = $missing.Count
      HarnessErrors     = $harnessErrors.Count
      FailedCheckFreq   = $checkFreq
    }
  }

  ConvertTo-Json -InputObject @($summary) -Depth 8 |
    Set-Content (Join-Path $ResultsDir 'scorecard.json') -Encoding utf8

  $md = @()
  $md += "# Governor model scorecard"
  $md += ""
  $md += "Scored by the Registrar CLI. Draft answers 'can this model produce a valid"
  $md += "document at all'. Promotion answers 'can it produce one that could ship'."
  $md += ""
  $md += "| Model | Draft pass | Promotion pass | Trap refused | Mean blocking (draft) | Missing runs |"
  $md += "|---|---|---|---|---|---|"
  foreach ($s in $summary) {
    $md += "| $($s.Model) | $($s.DraftPass)/$($s.ScoredRuns) | $($s.PromotionPass)/$($s.ScoredRuns) | $($s.TrapRefused)/$($s.TrapRuns) | $($s.MeanBlockingDraft) | $($s.MissingRuns) |"
  }
  $md += ""
  $md += "A model that produces ANY AGR for the trap prompt has failed it, whatever that"
  $md += "AGR validates as. Fabricating a Document ID is a governance failure the engine"
  $md += "cannot catch; a fumbled section title is one it catches for free."
  $md += ""

  foreach ($s in $summary) {
    $md += "## $($s.Model)"
    $md += ""
    if ($s.MissingRuns -gt 0) {
      $md += "WARNING: $($s.MissingRuns) run(s) missing. Rates above exclude them; they are not counted as passes."
      $md += ""
    }
    if ($s.HarnessErrors -gt 0) {
      $harnessRows = @($Results | Where-Object {
        $_.Model -eq $s.Model -and -not $_.IsTrap -and -not $_.Missing -and
        ($_.Draft.Ok -eq $false -or $_.Promotion.Ok -eq $false)
      })
      $harnessDetails = $harnessRows | ForEach-Object {
        $msg = if ($_.Draft.Ok -eq $false) { $_.Draft.HarnessError } else { $_.Promotion.HarnessError }
        "$($_.PromptId)-$($_.Run): $msg"
      }
      $md += "WARNING: $($s.HarnessErrors) run(s) hit a harness error (not a model failure) and are excluded from rates: $($harnessDetails -join ', ')"
      $md += ""
    }
    if ($s.InconsistentPrompts.Count -gt 0) {
      $md += "Inconsistent across runs: $($s.InconsistentPrompts -join ', '). Variance is itself a finding: a model that passes once and fails twice is not shippable."
      $md += ""
    }
    if ($s.FailedCheckFreq.Count -gt 0) {
      $md += "Failing checks by frequency:"
      $md += ""
      $md += "| Check ID | Times failed |"
      $md += "|---|---|"
      foreach ($c in $s.FailedCheckFreq) { $md += "| $($c.CheckId) | $($c.Count) |" }
    } else {
      $md += "No failing checks."
    }
    $md += ""
  }

  $md -join "`n" | Set-Content (Join-Path $ResultsDir 'scorecard.md') -Encoding utf8
}

$registrarCmd = Resolve-RegistrarCmd
if ($SelfTest) { Invoke-SelfTest -RegistrarCmd $registrarCmd; exit 0 }

$resultsDir = Join-Path $evals 'results'
Write-Host "scoring runs under $(Join-Path $evals 'runs')"
$results = Get-RunResults -EvalsDir $evals -RegistryPath $registryPath -RegistrarCmd $registrarCmd

if ($results.Count -eq 0) {
  Write-Host "no runs found. Create evals/runs/<model>/ and save Governor replies there. See evals/README.md."
  exit 0
}

New-Scorecard -Results $results -ResultsDir $resultsDir
Write-Host "wrote $(Join-Path $resultsDir 'scorecard.md')" -ForegroundColor Green
