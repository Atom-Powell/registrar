#Requires -Version 7.0
<#
.SYNOPSIS
  Assemble the full offline travel pack: the engine plus everything needed to stand up
  and evaluate the Governor away from this repo.
.DESCRIPTION
  Produces dist/registrar-travel/ and zips it to dist/registrar-travel.zip. The pack is
  self-contained and needs no .NET runtime, no clone, and no network:

    registrar-travel/
      runtime/            self-contained registrar.exe + Registrar.cmd, and
                          Registrar.App.exe + RegistrarApp.cmd (publish.ps1's
                          whole output is copied, so the window comes along)
      config/ schemas/ assets/   editable engine data (resolved by walking up from the exe)
      README.md OPERATOR.md      engine docs
      TRAVEL.md                  orientation for the whole pack (generated here)
      governor/                  everything to build the Governor in Copilot Studio
        governor-instructions.md governor-briefing.md governor-payload-correction.md
        governor-eval-setup.md   intake.md drafter.md payload.md correction.md
        UPLOAD-TO-GOVERNOR.md    setup guide (generated here)
        sample-agrs/             the three known-good AGRs to attach as knowledge
      eval-harness/              the contract eval, path-adapted to run inside the pack
        eval-governor.ps1        canonical script with its two repo-relative paths rewritten
        README.md evals/ fixtures/
      sample-agrs/               a few valid AGRs + a registry for CLI smoke tests

  The runtime is rebuilt from source by default (that is the point of "recreate"); pass
  -SkipBuild to reuse an existing dist/registrar runtime for faster iteration.
.PARAMETER SkipBuild
  Reuse the existing dist/registrar/ runtime instead of running publish.ps1 again.
#>
[CmdletBinding()]
param(
  [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo    = Split-Path -Parent $PSScriptRoot
$dist    = Join-Path $repo 'dist'
$publish = Join-Path $dist 'registrar'
$stage   = Join-Path $dist 'registrar-travel'

function Copy-Into {
  param([string]$Source, [string]$DestDir)
  if (-not (Test-Path $Source)) { throw "expected source not found: $Source" }
  Copy-Item $Source $DestDir -Recurse -Force
}

# 1. Engine runtime + editable data. Reuse the tested publish.ps1 so the runtime layout
#    and config-resolution behavior stay identical to the standalone release.
if (-not $SkipBuild) {
  & (Join-Path $PSScriptRoot 'publish.ps1')
  if ($LASTEXITCODE -ne 0) { throw "publish.ps1 failed with exit code $LASTEXITCODE" }
}
if (-not (Test-Path (Join-Path $publish 'runtime/registrar.exe'))) {
  throw "no published runtime at $publish. Run without -SkipBuild first."
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# dist/registrar already holds runtime/, config/, schemas/, assets/, README.md, OPERATOR.md.
Copy-Item (Join-Path $publish '*') $stage -Recurse -Force

# 2. Governor kit.
$gov = Join-Path $stage 'governor'
$govAgrs = Join-Path $gov 'sample-agrs'
New-Item -ItemType Directory -Path $govAgrs -Force | Out-Null
foreach ($doc in 'governor-instructions.md','governor-briefing.md',
                 'governor-payload-correction.md','governor-eval-setup.md') {
  Copy-Into (Join-Path $repo "docs/$doc") $gov
}
# The four knowledge files generated from config, one per Governor child agent.
foreach ($doc in 'intake.md','drafter.md','payload.md','correction.md') {
  Copy-Into (Join-Path $repo "docs/governor/$doc") $gov
}
# The three known-good AGRs the instructions tell the operator to attach as knowledge.
foreach ($agr in 'agr-sample-tcl-draft.json','agr-sample-ds-draft.json','agr-sample-aup-draft.json') {
  Copy-Into (Join-Path $repo "fixtures/$agr") $govAgrs
}
# The Studio setup checklist belongs with the rest of the Governor kit, and so does the
# reflowed paste block, which is the file the operator actually pastes into Studio.
Copy-Into (Join-Path $repo 'docs/copilot-studio-setup.md') $gov
Copy-Into (Join-Path $repo 'docs/governor-instructions-paste.txt') $gov

# 2b. Whole docs/ tree, mirrored at the pack root so every repo-root-relative reference
#     inside it resolves. OPERATOR.md sends a first-time operator to
#     docs/first-artifact-walkthrough.md; operations-guide.md names the Steward knowledge
#     files and the generated Governor knowledge the same way; README.md and
#     governor-briefing.md cite DELIVERED, ROADMAP and tech-debt, which in turn cite the
#     specs and plans. Copying a hand-picked subset closed one tier of those references and
#     opened the next, so the tree comes whole: it is 2 MB of markdown against a 100 MB
#     runtime, and a doc that cites a file the pack does not carry is worse than the bytes.
#     The .docx originals under docs/reference/ come too. Nothing links to them, but the
#     ADRs are what the open repository-path questions have to be settled against, and
#     settling those away from this repo is a large part of why the pack exists.
$packDocs = Join-Path $stage 'docs'
Copy-Into (Join-Path $repo 'docs') $stage
if (-not (Test-Path (Join-Path $packDocs 'first-artifact-walkthrough.md'))) {
  throw "docs/ copy did not land at $packDocs"
}

# 3. Contract eval harness, path-adapted so the canonical script runs inside the pack.
#    Only two lines are repo-relative; rewrite exactly those and assert the rewrite took,
#    so a future refactor of eval-governor.ps1 fails this build instead of shipping broken.
$harness = Join-Path $stage 'eval-harness'
New-Item -ItemType Directory -Path $harness -Force | Out-Null
$evalTxt = Get-Content (Join-Path $repo 'scripts/eval-governor.ps1') -Raw
$before = $evalTxt
$evalTxt = $evalTxt.Replace(
  '$repo = Split-Path -Parent $PSScriptRoot',
  '$repo = $PSScriptRoot')
$evalTxt = $evalTxt.Replace(
  'Join-Path $repo ''dist/registrar/runtime/registrar.exe''',
  'Join-Path (Split-Path -Parent $PSScriptRoot) ''runtime/registrar.exe''')
if ($evalTxt -eq $before) {
  throw "eval-governor.ps1 path adaptation matched nothing. Its path-resolution lines changed; update this script."
}
if ($evalTxt -match "dist/registrar/runtime") {
  throw "eval-governor.ps1 still references dist/registrar/runtime after adaptation."
}
Set-Content (Join-Path $harness 'eval-governor.ps1') $evalTxt -Encoding utf8
Copy-Into (Join-Path $repo 'evals/README.md') $harness

# Bundle the eval inputs the harness reads, but not the dead test-cases-*.csv scratch.
# governor-judgment and governor-payload are the two Studio-upload packs (judgment and
# payload); registry-eval.csv is the registry the extracted payloads are scored against.
$hEvals = Join-Path $harness 'evals'
New-Item -ItemType Directory -Path $hEvals -Force | Out-Null
foreach ($item in 'prompts','registry-eval.csv','governor-judgment','governor-payload') {
  Copy-Into (Join-Path $repo "evals/$item") $hEvals
}
# runs/ is the drop target for Studio replies; results/ is where the scorecard lands.
foreach ($d in 'runs','results') {
  $p = Join-Path $hEvals $d
  New-Item -ItemType Directory -Path $p -Force | Out-Null
  Set-Content (Join-Path $p '.gitkeep') '' -NoNewline
}
# fixtures/ is small and covers the self-test (registry-sample.csv + the sample AGRs).
Copy-Into (Join-Path $repo 'fixtures') $harness

# 4. CLI smoke set: a few valid AGRs plus a registry to build them against.
$smoke = Join-Path $stage 'sample-agrs'
New-Item -ItemType Directory -Path $smoke -Force | Out-Null
foreach ($agr in 'agr-sample-ds-draft.json','agr-sample-tcl-draft.json','agr-sample-aup-draft.json',
                 'agr-sample-uqr-draft.json','agr-sample-dcn-draft.json') {
  Copy-Into (Join-Path $repo "fixtures/$agr") $smoke
}
Copy-Into (Join-Path $repo 'fixtures/registry-sample.csv') $smoke

# 5. Generated orientation docs.
$travelMd = @'
# Registrar travel pack

Everything needed to run the engine and to build and evaluate the Governor, offline. No
.NET runtime, no clone, and no network are required.

## What is here

- `runtime/` - the self-contained `registrar.exe` and `Registrar.cmd`, plus
  `Registrar.App.exe` and `RegistrarApp.cmd`, the operator window.
- `config/`, `schemas/`, `assets/` - editable engine data. The engine finds these by
  walking up from the exe, so keep `runtime/` where it is relative to them.
- `README.md`, `OPERATOR.md` - engine and operator documentation.
- `docs/` - the full documentation set. If you have never used the Registrar, start at
  `docs/first-artifact-walkthrough.md`, which takes you from an empty folder to a
  promotion build. `docs/operations-guide.md` is the wider program reference and covers
  the Steward as well as the Governor. `docs/reference/` holds the ADR and specification
  originals.
- `governor/` - everything to stand up the Governor in Copilot Studio. Start with
  `governor/UPLOAD-TO-GOVERNOR.md`.
- `eval-harness/` - the contract eval (scores whether a produced AGR passes the engine),
  path-adapted to run from inside this pack.
- `sample-agrs/` - a few valid AGRs and a registry for quick CLI smoke tests.

## Smoke test the engine

From this folder in PowerShell 7:

    .\runtime\registrar.exe doctor
    .\runtime\registrar.exe build .\sample-agrs\agr-sample-tcl-draft.json --mode draft --registry .\sample-agrs\registry-sample.csv

`doctor` should report healthy; `build` should produce a DOCX next to the AGR.

## Verify the eval harness

    pwsh .\eval-harness\eval-governor.ps1 -SelfTest

It scores a known-good AGR, a known-bad one, and a malformed file and confirms it gets
all three right. Run this before trusting any scored round.

## Build the Governor

See `governor/UPLOAD-TO-GOVERNOR.md`. In short: paste `governor-instructions.md` into the
agent instructions, attach `intake.md`, `drafter.md`, `payload.md`, and `correction.md`
(one per child agent) and the three AGRs in `governor/sample-agrs/` as knowledge, and do
not connect the agent to any API or flow.

## Evaluate the Governor

Two surfaces, kept separate:

- Judgment (does it recommend and refuse correctly): upload
  `eval-harness/evals/governor-judgment/01-artifact-orchestration.csv` to Copilot
  Studio's evaluation feature. Score it with `governor/governor-eval-setup.md`, which
  explains why a correct refusal must not be graded by the default General quality metric.
- Contract (does a produced AGR pass the engine): follow `eval-harness/README.md`. Paste
  the prompts in `eval-harness/evals/prompts/` into the Governor, save the raw replies
  under `eval-harness/evals/runs/<model>/`, then run `eval-governor.ps1` to score.
'@
Set-Content (Join-Path $stage 'TRAVEL.md') $travelMd -Encoding utf8

$uploadMd = @'
# Set up the Governor in Copilot Studio

The Governor is a declarative agent that produces Artifact Generation Requests (AGRs) for
the Registrar engine. It owns judgment; it never renders and it is never trusted by the
engine. Set it up like this.

`copilot-studio-setup.md`, beside this file, is the long-form version: the Studio settings
that matter, the two decisions to make before you start, and the traps that cost an
afternoon. Work from that one if you are configuring Studio for the first time, and treat
what follows as the summary.

## 1. Instructions

Paste the whole of `governor-instructions-paste.txt` into the agent's instructions field.
Use that file, not `governor-instructions.md`. Studio's instructions editor drops
intra-paragraph newlines without substituting a space, so the hard-wrapped canonical text
arrives with words glued together and the code-to-name table collapsed onto one line. The
paste file is the same text with one physical line per paragraph, which leaves no newline
to swallow.

`governor-instructions.md` remains the readable canonical source, and the paste file is
generated from it by `scripts/build-governor-paste.ps1`. If you edit the instructions,
regenerate rather than hand-editing the paste file.

## 2. Knowledge

The Governor is a parent agent with five children, split by function rather than by
artifact type: intake, drafter, payload assembler, correction handler, and archivist.
Attach `governor-briefing.md` to all five; it is orientation, not a per-role contract.
Beyond that, each child gets only the knowledge it needs:

- **Intake** (works out which artifact an engagement needs): `intake.md`.
- **Drafter** (produces artifact content): `drafter.md`.
- **Payload assembler** (turns content into a valid request): `payload.md`.
- **Correction handler** (repairs a request the engine rejected): `correction.md`, the
  generated check catalogue, plus `governor-payload-correction.md`, the hand-written
  correction protocol.
- **Archivist**: no generated knowledge yet. Its config sources belong to a workstream
  still in flight; give it `governor-briefing.md` only until that lands.

All five also take the sample AGRs as knowledge:

- `sample-agrs/agr-sample-tcl-draft.json`
- `sample-agrs/agr-sample-ds-draft.json`
- `sample-agrs/agr-sample-aup-draft.json`

A known-good AGR teaches the JSON shape better than prose. The TCL sample exercises every
renderable block type except h2 and h3.

## 3. Do not connect it to anything

Do not give the Governor a tool, flow, connector, or API that reaches the engine. The
operator is the transport: save the AGR from the chat, run the CLI, review the validation
report, and paste failures back for regeneration. That human checkpoint is a governance
control, not friction to remove.

## 4. Sanity check the agent

Ask it to generate a Runbook and to assign the next Document ID. The correct response is a
refusal on both counts: a Runbook is not a generation target, and it must never mint a
Document ID. If it produces an AGR or invents an ID, re-check that the instructions were
pasted in full.

## 5. Evaluate it

- Judgment: upload `../eval-harness/evals/governor-judgment/01-artifact-orchestration.csv`
  to the Studio evaluation feature and score with `governor-eval-setup.md`. Do not grade
  the refusal-trap rows with the default General quality metric; it fails correct refusals.
- Contract: follow `../eval-harness/README.md` to score produced AGRs against the engine.

## Correcting the agent

If a round shows the agent drifting (fabricated names, invented IDs, paraphrased clauses),
`governor-payload-correction.md` and `governor-briefing.md` carry the correction language
and the background rationale.
'@
Set-Content (Join-Path $gov 'UPLOAD-TO-GOVERNOR.md') $uploadMd -Encoding utf8

# 6. Zip.
$zip = Join-Path $dist 'registrar-travel.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Travel pack staged at $stage"
Write-Host "Zipped to $zip ($zipMb MB)"
