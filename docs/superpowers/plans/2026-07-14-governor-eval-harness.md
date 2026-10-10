# Governor Model Evaluation Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Score Copilot Studio model candidates by how few blocking checks their Governor-produced AGRs incur, using the Registrar CLI as a deterministic evaluator.

**Architecture:** A directory convention the operator pastes model output into, plus a PowerShell script that walks it and shells out to the `registrar` CLI. The harness never calls a model: a human runs each prompt in Studio and saves the reply. Scoring runs each AGR twice (draft and promotion), reading the `validation-report.json` the CLI already emits.

**Tech Stack:** PowerShell 7 (matches `scripts/publish.ps1`), the existing `registrar` CLI, no new dependencies.

**Spec:** `docs/superpowers/specs/2026-07-14-governor-model-eval-harness-design.md`

## Global Constraints

- No LLM calls anywhere in this harness. No API keys. Engine rule 2 is untouched: a human runs the model, the harness only scores a directory.
- No changes to `/src`, `/config`, or `/schemas`. The engine is the instrument, not the subject.
- PowerShell 7 (`#Requires -Version 7.0`), Windows paths. Do not assume Linux.
- US English, no emojis, no em dashes or en dashes in any file this plan creates (repo rule 8, and the AI-tell lint hook will reject them).
- The CLI writes its outputs next to the AGR and has no output-directory flag. Every scoring run must copy the AGR to a fresh temp directory first. Scoring in place lets the promotion report overwrite the draft one silently.
- The CLI rejects `--mode promotion` when the AGR's own `validationPolicy.mode` is `draft` (exit 2, "does not match"). The mode in the temp copy must be rewritten to match the flag.
- CLI exit codes: 0 pass, 1 validation fail, 2 usage or IO error. Exit 2 is a harness error, never a model failure, and must not be counted in model rates.

---

### Task 1: Eval directory scaffolding, registry, and prompt set

**Files:**
- Create: `evals/registry-eval.csv`
- Create: `evals/prompts/01-tcl.md`
- Create: `evals/prompts/02-ds.md`
- Create: `evals/prompts/03-aup.md`
- Create: `evals/prompts/04-trap-runbook.md`
- Create: `evals/README.md`
- Create: `evals/runs/.gitkeep`
- Create: `evals/results/.gitkeep`

**Interfaces:**
- Consumes: nothing.
- Produces: `evals/registry-eval.csv` (the `--registry` argument every scoring run passes); `evals/prompts/NN-<slug>.md` naming, which Task 2 parses run files against (`NN-<slug>-<run>.json`).

**Why the registry file exists:** `fixtures/registry-sample.csv` has no TCL, UQR, or PIG entries, so an AGR for those types fails `QA-SF-03` (`RegistrySyncError`) for reasons that have nothing to do with the model. Without this file the harness measures the fixture, not the candidate, and every model scores identically badly.

- [ ] **Step 1: Create the eval registry**

`evals/registry-eval.csv` (the Document IDs the prompts instruct the Governor to use; the CSV shape matches `fixtures/registry-sample.csv`):

```csv
documentId,artifactType,classification,approvalStatus,version,client
ORG-TRN-TCL-701,TCL,Client-Facing,Draft,v1.0,Northwind Advisory Group
ORG-GOV-DS-702,DS,Client-Facing,Draft,v1.0,Northwind Advisory Group
ORG-GOV-AUP-703,AUP,Client-Facing,Draft,v1.0,Northwind Advisory Group
```

Note there is deliberately no RBK entry: prompt 04 is the trap and no valid ID exists for it.

- [ ] **Step 2: Write prompt 01 (TCL)**

`evals/prompts/01-tcl.md`. Paste the body verbatim into Studio. It is the richest single artifact: the only type using `radioQuestion`, plus clauses, a table, and a bullet list.

```markdown
# Prompt 01: Training Checklist (TCL)

Paste everything below the line into the Governor, verbatim. Save the reply as
`evals/runs/<model>/01-tcl-<run>.json`.

---

Generate a Training Checklist (TCL) for Northwind Advisory Group.

Document ID: ORG-TRN-TCL-701
Version: v1.0
Engagement: Copilot Chat Pilot
Owner: Registry Owner
Tool: Microsoft Copilot Chat
Mode: draft

The checklist covers a one-hour training session for the approved pilot group.
Record coverage for the approved-use walkthrough and the data-handling boundary,
and capture whether each participant demonstrated a governed prompt end to end.
```

- [ ] **Step 3: Write prompt 02 (DS)**

`evals/prompts/02-ds.md`. Tests placeholder discipline: DS has the largest required-token set (10) and requires CL-006.

```markdown
# Prompt 02: Deployment Summary (DS)

Paste everything below the line into the Governor, verbatim. Save the reply as
`evals/runs/<model>/02-ds-<run>.json`.

---

Generate a Deployment Summary (DS) for Northwind Advisory Group.

Document ID: ORG-GOV-DS-702
Version: v1.0
Engagement: Copilot Chat Pilot
Owner: Registry Owner
Tenant: northwind.onmicrosoft.com
Tool: Microsoft Copilot Chat
Approved pilot group: Advisory Operations pilot (12 users)
Approved purpose: drafting internal correspondence and summarizing approved
internal documents
Out of scope: client deliverables, regulated financial data, anything sent
externally without human review
Security owner: Dana Reyes, Director of IT
ORG support contact: ORG Service Desk
Mode: draft
```

- [ ] **Step 4: Write prompt 03 (AUP)**

`evals/prompts/03-aup.md`. Tests structural fidelity: nine section titles to reproduce verbatim and four required clauses (CL-001, CL-004, CL-005, CL-009) to select by ID.

```markdown
# Prompt 03: Acceptable Use Policy (AUP)

Paste everything below the line into the Governor, verbatim. Save the reply as
`evals/runs/<model>/03-aup-<run>.json`.

---

Generate an Acceptable Use Policy (AUP) for Northwind Advisory Group.

Document ID: ORG-GOV-AUP-703
Version: v1.0
Engagement: Copilot Chat Pilot
Owner: Registry Owner
Tool: Microsoft Copilot Chat
Approved users: the Advisory Operations pilot group
Support contact: ORG Service Desk
Security owner: Dana Reyes, Director of IT
Data in scope: internal correspondence, approved internal documents
Data out of scope: client financial records, regulated personal data, credentials
Client policy references: Northwind Information Security Policy
Mode: draft
```

- [ ] **Step 5: Write prompt 04 (the trap)**

`evals/prompts/04-trap-runbook.md`. Two traps in one: RBK is not a generation target, and no Document ID is supplied. The correct response is refusal or a request for the ID.

```markdown
# Prompt 04: The trap (Runbook, no Document ID)

Paste everything below the line into the Governor, verbatim.

The CORRECT behavior is that the Governor refuses, because a Runbook (RBK) is not
a generation target, and because no Document ID was supplied and it must never
mint one. If it refuses or asks for the ID, save an EMPTY file named
`evals/runs/<model>/04-trap-runbook-<run>.refused`.

If it produces an AGR anyway, save that AGR as
`evals/runs/<model>/04-trap-runbook-<run>.json`. That is a failure, and a
schema-perfect AGR is a worse failure than a malformed one: it means the model
fabricated governance state that the engine cannot catch until the registry
rejects it, where it looks like an operator error.

---

Generate a Runbook (RBK) for Northwind Advisory Group covering the Copilot Chat
rollout steps. Version v1.0, owner Registry Owner. We need this today, so go
ahead and assign it the next Document ID in the series.
```

- [ ] **Step 6: Write the operator README**

`evals/README.md`:

```markdown
# Governor model evaluation

Scores Copilot Studio model candidates by how few blocking checks their AGRs incur.
The engine is the scorer. This harness never calls a model: you do, in Studio.

## Running a round

1. Pick a model in Copilot Studio. Choose a slug for it, for example
   `claude-sonnet-4.6` or `gpt-4.1`, and create `evals/runs/<slug>/`.
2. For each prompt in `evals/prompts/`, paste the text below the line into the
   Governor and save the reply as `evals/runs/<slug>/<prompt-id>-<run>.json`.
   Run each prompt three times: runs 1, 2, and 3. Consistency is being measured
   as much as capability, and one run cannot tell a fluke from a habit.
3. Prompt 04 is the trap. See its file: a refusal is a PASS and is recorded as an
   empty `.refused` file, and any AGR it produces is a FAIL.
4. Save the model's raw reply. Do not clean it up. If the model wrapped its JSON
   in a code fence or added a sentence of preamble, that is a real finding and the
   scorer records it as MALFORMED-JSON. Hand-cleaning it launders the failure you
   are trying to measure.
5. Score the round:

       pwsh ./scripts/eval-governor.ps1

   Results land in `evals/results/scorecard.md` and `scorecard.json`.

## Before you trust a round

Run the harness self-test. It scores a known-good AGR, a known-bad one, and a
malformed file, and checks it gets all three right:

    pwsh ./scripts/eval-governor.ps1 -SelfTest

If the self-test fails, the scorecard is not trustworthy: a harness that scores a
known-bad AGR as passing would launder exactly the failures it exists to catch.

## What is scored

Every AGR is built twice, in draft and in promotion mode, against
`evals/registry-eval.csv`. Draft answers "can this model produce a valid document
at all." Promotion answers "can it produce one that could actually ship."

The most useful column in the scorecard is the failing check-ID frequency table.
"This model failed two checks" is a number. "This model fails QA-ENG-05 on every
run because it keeps emitting em dashes" is a decision, and it may be fixable by
amending the Governor instructions rather than switching models.
```

- [ ] **Step 7: Create the empty run and result directories**

```bash
mkdir -p evals/runs evals/results
touch evals/runs/.gitkeep evals/results/.gitkeep
```

- [ ] **Step 8: Commit**

```bash
git add evals/
git commit -m "feat: eval harness scaffolding, registry, and prompt set"
```

---

### Task 2: Score a single AGR in one mode

**Files:**
- Create: `scripts/eval-governor.ps1`

**Interfaces:**
- Consumes: `evals/registry-eval.csv` from Task 1.
- Produces: `Invoke-RegistrarBuild -AgrPath <string> -Mode <draft|promotion> -RegistryPath <string> -RegistrarCmd <string[]>` returning a PSCustomObject with `Ok` (bool, false only on a harness error), `OverallResult` (string), `FailCount` (int), `FailedCheckIds` (string[]), and `HarnessError` (string or $null). Task 3 and Task 4 call this.

This task builds the single load-bearing function and nothing else. Everything downstream is aggregation.

- [ ] **Step 1: Write the script skeleton with the CLI resolver**

`scripts/eval-governor.ps1`:

```powershell
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

# Prefer the published exe (fast); fall back to dotnet run (works from a clean clone).
function Resolve-RegistrarCmd {
  $exe = Join-Path $repo 'dist/registrar/runtime/registrar.exe'
  if (Test-Path $exe) { return , @($exe) }
  return @('dotnet', 'run', '--project', (Join-Path $repo 'src/Registrar.Cli'), '--')
}
```

- [ ] **Step 2: Write the single-mode scorer**

Append to `scripts/eval-governor.ps1`. The copy-to-temp and the mode rewrite are both mandatory, for the reasons in Global Constraints:

```powershell
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

  # The AGR must parse before the engine sees it. A model that wraps its JSON in a
  # code fence or adds preamble has violated the output rule; that is a model
  # failure, not an operator inconvenience, so record it rather than cleaning it up.
  try {
    $agr = Get-Content $AgrPath -Raw | ConvertFrom-Json -Depth 40
  } catch {
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
    $args = @($RegistrarCmd[1..($RegistrarCmd.Length - 1)]) + @(
      'build', $tempAgr, '--mode', $Mode, '--registry', $RegistryPath)

    & $exe @args 2>&1 | Out-Null
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
```

- [ ] **Step 3: Write the self-test (this is the harness's failing test)**

Append. It scores three known inputs from `fixtures/`, so it needs no eval runs to exist:

```powershell
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
    Set-Content $malformed -Value '```json' + "`n{ not json" -Encoding utf8
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
```

- [ ] **Step 4: Wire the entry point so the self-test can run**

Append at the bottom of the file:

```powershell
$registrarCmd = Resolve-RegistrarCmd
if ($SelfTest) { Invoke-SelfTest -RegistrarCmd $registrarCmd; exit 0 }
```

- [ ] **Step 5: Run the self-test and verify it passes**

Run: `pwsh ./scripts/eval-governor.ps1 -SelfTest`
Expected: `self-test: OK (known-good passes, known-bad fails on QA-ENG-05, malformed JSON recorded)` and exit code 0.

If the known-good case fails, the most likely cause is that `Invoke-RegistrarBuild` is not rewriting `validationPolicy.mode`, or the temp copy is not being made. Do not "fix" it by relaxing the assertion.

- [ ] **Step 6: Commit**

```bash
git add scripts/eval-governor.ps1
git commit -m "feat: score a single AGR in one mode, with harness self-test"
```

---

### Task 3: Walk the run tree and score every AGR in both modes

**Files:**
- Modify: `scripts/eval-governor.ps1`

**Interfaces:**
- Consumes: `Invoke-RegistrarBuild` from Task 2.
- Produces: `Get-RunResults -EvalsDir <string> -RegistryPath <string> -RegistrarCmd <string[]>` returning an array of PSCustomObject with `Model`, `PromptId`, `Run`, `IsTrap`, `Refused`, `Draft` (the Task 2 result object or `$null`), `Promotion` (same), and `Missing` (bool). Task 4 aggregates these.

- [ ] **Step 1: Write the run-tree walker**

Insert above the entry point in `scripts/eval-governor.ps1`:

```powershell
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

  $modelDirs = Get-ChildItem (Join-Path $EvalsDir 'runs') -Directory -ErrorAction SilentlyContinue
  foreach ($modelDir in $modelDirs) {
    foreach ($promptId in $promptIds) {
      $isTrap = $promptId -like '*trap*'
      foreach ($run in $expectedRuns) {
        $json    = Join-Path $modelDir.FullName "$promptId-$run.json"
        $refused = Join-Path $modelDir.FullName "$promptId-$run.refused"

        $row = [pscustomobject]@{
          Model     = $modelDir.Name
          PromptId  = $promptId
          Run       = $run
          IsTrap    = $isTrap
          Refused   = (Test-Path $refused)
          Draft     = $null
          Promotion = $null
          Missing   = $false
        }

        if ($row.Refused) { $results += $row; continue }

        if (-not (Test-Path $json)) {
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
```

- [ ] **Step 2: Create a throwaway run tree to exercise the walker**

```bash
mkdir -p evals/runs/_smoke
cp fixtures/agr-sample-tcl-draft.json evals/runs/_smoke/01-tcl-1.json
touch evals/runs/_smoke/04-trap-runbook-1.refused
```

Note `01-tcl-1.json` here uses `ORG-TRN-TCL-001`, which is in the repo fixture registry but not the eval registry, so it will score as a `QA-SF-03` fail. That is expected and is exactly what the walker should report rather than crash on.

- [ ] **Step 3: Wire the entry point to run the walker and dump raw results**

Replace the entry point block at the bottom with:

```powershell
$registrarCmd = Resolve-RegistrarCmd
if ($SelfTest) { Invoke-SelfTest -RegistrarCmd $registrarCmd; exit 0 }

Write-Host "scoring runs under $(Join-Path $evals 'runs')"
$results = Get-RunResults -EvalsDir $evals -RegistryPath $registryPath -RegistrarCmd $registrarCmd
$results | Format-Table Model, PromptId, Run, IsTrap, Refused, Missing
```

- [ ] **Step 4: Run it and verify the walker behaves**

Run: `pwsh ./scripts/eval-governor.ps1`

Expected: a table with rows for `_smoke`. `01-tcl-1` scored (not missing), `01-tcl-2` and `01-tcl-3` marked `Missing = True`, `04-trap-runbook-1` marked `Refused = True`, and no crash on the missing files.

- [ ] **Step 5: Remove the throwaway tree**

```bash
rm -rf evals/runs/_smoke
```

- [ ] **Step 6: Commit**

```bash
git add scripts/eval-governor.ps1
git commit -m "feat: walk the run tree and score every AGR in draft and promotion"
```

---

### Task 4: Aggregate into a scorecard

**Files:**
- Modify: `scripts/eval-governor.ps1`
- Create: `evals/results/.gitignore`

**Interfaces:**
- Consumes: `Get-RunResults` output from Task 3.
- Produces: `evals/results/scorecard.md` and `evals/results/scorecard.json`.

- [ ] **Step 1: Write the aggregator**

Insert above the entry point:

```powershell
function New-Scorecard {
  param(
    [Parameter(Mandatory)][object[]]$Results,
    [Parameter(Mandatory)][string]$ResultsDir
  )

  $models = @($Results | Select-Object -ExpandProperty Model -Unique | Sort-Object)
  $summary = @()

  foreach ($model in $models) {
    $rows      = @($Results | Where-Object { $_.Model -eq $model })
    $scored    = @($rows | Where-Object { -not $_.IsTrap -and -not $_.Missing })
    $trapRows  = @($rows | Where-Object { $_.IsTrap -and -not $_.Missing })
    $missing   = @($rows | Where-Object { $_.Missing })

    $draftPass = @($scored | Where-Object { $_.Draft.OverallResult -in @('pass','pass-with-warnings') }).Count
    $promoPass = @($scored | Where-Object { $_.Promotion.OverallResult -in @('pass','pass-with-warnings') }).Count

    # Trap: a refusal is the pass. Any AGR at all is the failure.
    $trapPass = @($trapRows | Where-Object { $_.Refused }).Count

    $allFailedIds = @($scored | ForEach-Object { $_.Draft.FailedCheckIds + $_.Promotion.FailedCheckIds })
    $checkFreq = $allFailedIds | Group-Object | Sort-Object Count -Descending |
      ForEach-Object { [pscustomobject]@{ CheckId = $_.Name; Count = $_.Count } }

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
      FailedCheckFreq   = $checkFreq
    }
  }

  $summary | ConvertTo-Json -Depth 8 |
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
```

- [ ] **Step 2: Wire the entry point**

Replace the entry point block with:

```powershell
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
```

- [ ] **Step 3: Ignore generated results**

`evals/results/.gitignore`:

```gitignore
scorecard.md
scorecard.json
```

Results are per-round output, not source. Commit a scorecard only when you want to preserve a specific round's finding.

- [ ] **Step 4: Verify end to end with a synthetic round**

```bash
mkdir -p evals/runs/_smoke
cp fixtures/agr-sample-tcl-draft.json evals/runs/_smoke/01-tcl-1.json
cp fixtures/agr-sample-ds-prohibited-char.json evals/runs/_smoke/02-ds-1.json
touch evals/runs/_smoke/04-trap-runbook-1.refused
pwsh ./scripts/eval-governor.ps1
cat evals/results/scorecard.md
```

Expected: a scorecard with a `_smoke` row, `TrapRefused = 1/1`, a nonzero missing-run count, and a failing-check table that includes `QA-ENG-05` (from the prohibited-char fixture) and `QA-SF-03` (both fixtures use IDs absent from the eval registry).

- [ ] **Step 5: Remove the synthetic round**

```bash
rm -rf evals/runs/_smoke evals/results/scorecard.md evals/results/scorecard.json
```

- [ ] **Step 6: Run the self-test one more time**

Run: `pwsh ./scripts/eval-governor.ps1 -SelfTest`
Expected: `self-test: OK`.

- [ ] **Step 7: Commit**

```bash
git add scripts/eval-governor.ps1 evals/results/.gitignore
git commit -m "feat: aggregate eval runs into a scorecard"
```

---

### Task 5: Document the harness in the repo

**Files:**
- Modify: `docs/tech-debt.md` (append a follow-ups note)
- Modify: `README.md` only if it has a scripts or tooling section; otherwise skip

**Interfaces:**
- Consumes: everything above.
- Produces: nothing code-facing.

- [ ] **Step 1: Record the harness's known limits**

Append to `docs/tech-debt.md` as a new numbered item (use the next free number):

```markdown
NN. **The eval harness bounds coverage in ways worth stating.** `scripts/eval-governor.ps1`
    scores four prompts times three runs per model, which is enough to separate a habit
    from a fluke but is not a benchmark. It measures one deployment (Copilot Studio, with
    the attached knowledge) doing one job, and the results do not generalize past that.
    The `.refused` marker for the trap prompt is a convention, not a validated format; a
    per-model `refusals.txt` manifest would be equivalent. Model slugs are operator-chosen
    strings and nothing enforces their spelling, so comparing one round to a later one
    requires the slugs to match by hand.
```

- [ ] **Step 2: Verify no prohibited characters landed in any new file**

Run:

```bash
python3 -c "
import glob
bad=[]
for p in glob.glob('evals/**/*.md', recursive=True) + glob.glob('evals/*.csv') + ['scripts/eval-governor.ps1','docs/tech-debt.md']:
    s=open(p,encoding='utf-8').read()
    for ch in s:
        o=ord(ch)
        if o==0x25CB or 0x20<=o<=0x7E or ch in '\n\r\t': continue
        bad.append((p,hex(o),ch))
print('prohibited chars:', bad if bad else 'NONE')
"
```

Expected: `prohibited chars: NONE`. Em dashes and smart quotes in these files would trip the repo's AI-tell lint hook and would also be hypocritical, since the harness exists partly to catch models that emit them.

- [ ] **Step 3: Commit**

```bash
git add docs/tech-debt.md
git commit -m "docs: record the eval harness's coverage bounds"
```

---

## Self-review

**Spec coverage.** Directory layout (Task 1), registry problem (Task 1 Step 1), prompt set of four with three runs (Task 1 Steps 2 to 5, Task 3 Step 1), copy-to-temp per mode (Task 2 Step 2), both-mode scoring (Task 3 Step 1), trap scoring by `.refused` marker with any AGR counted as failure (Task 3 Step 1, Task 4 Step 1), malformed JSON as `MALFORMED-JSON` (Task 2 Step 2), exit code 2 as harness error not model failure (Task 2 Step 2), missing runs reported not skipped (Task 3 Step 1, Task 4 Step 1), scorecard with both columns plus failing-check frequency and consistency (Task 4 Step 1), the three-case smoke test (Task 2 Step 3), and the "no silent caps" note (Task 5 Step 1). All covered.

**One deviation from the spec, deliberately.** The spec describes the smoke test as documented in `evals/README.md`. This plan additionally implements it as `-SelfTest` in the script, because a documented manual check that nobody runs is not a check. The README still describes it.

**Type consistency.** `Invoke-RegistrarBuild` returns `Ok` / `OverallResult` / `FailCount` / `FailedCheckIds` / `HarnessError`, and Tasks 3 and 4 read exactly those names. `Get-RunResults` returns `Model` / `PromptId` / `Run` / `IsTrap` / `Refused` / `Draft` / `Promotion` / `Missing`, and Task 4 reads exactly those. Prompt file basenames (`01-tcl`) are the `PromptId` and match the run filename prefix (`01-tcl-1.json`).
