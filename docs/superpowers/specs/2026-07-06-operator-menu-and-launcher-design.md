# Operator Menu, QoL Shell, and Self-Contained Launcher - Design

**Status:** Approved (design)
**Date:** 2026-07-06
**Context:** Post-Slice-7. First operator-accessibility work; not a numbered engine
slice (it adds no governance logic), but follows the same spec -> plan -> execute flow.

## Goal

Make `registrar` easy to run for manual testing and MSP go-live without turning it
into anything other than the deterministic, fail-closed engine it already is. Add
an interactive `menu` mode inside the CLI, quality-of-life features around the
existing `validate`/`build` verbs, a self-contained single-file `registrar.exe`, and
a double-click launcher - so an operator can unzip, double-click, pick an AGR, and
read a legible result on a clean machine with no .NET SDK installed.

## Governance guardrails (binding)

1. **Zero LLM calls, unchanged.** The menu is deterministic console UI; rule 2
   holds.
2. **The menu is a thin shell over the existing verbs.** It adds no validation or
   rendering logic and cannot bypass fail-closed. A menu action calls the same
   `ValidateCommand.Run` / `BuildCommand.Run` the verbs call, which write the same
   `validation-report.json` / `.docx` / `promotion-payload.json`; the menu then
   deserializes the written report and displays it. All governance stays in the
   engine, where it is already tested.
3. **The operator checkpoint stays a human decision.** The menu never auto-fixes,
   never auto-submits, never contacts the Governor. "Copy failures for the
   Governor" places text on the operator's clipboard for the operator to paste; it
   assists the human transport, it does not automate the governance judgment.
4. **Settings store paths only** (last folder, recent AGR paths) in
   `%APPDATA%\registrar\settings.json` - never AGR content, client data, or secrets.
5. **Config remains a versioned data release.** The packaged `config/`, `schemas/`,
   and `assets/` ship as editable files next to the exe, never embedded (rule 3).

## Non-goals

- Not a GUI; console menu only.
- Not a Governor client - no chat integration, no auto-paste.
- Does not replace the `validate`/`build` verbs; those remain for scripting/CI. The
  menu is additive.
- No in-app AGR editing (the operator edits AGRs via the Governor or their editor).
- No console UI dependency (Spectre.Console et al. rejected): the governed tool
  stays dependency-light (its only third-party dep remains OpenXml), fully under our
  control, and maximally portable over RDP/varied terminals.
- Wiring the packaging into a GitHub Actions release workflow is a follow-up, not
  this slice.

## Interaction model

A numbered-menu loop, deliberately not arrow-key/full-screen navigation, for
portability across RDP sessions and varied MSP terminals; it degrades gracefully and
bad input re-prompts rather than crashing. Color is `Console.ForegroundColor` (green
pass / yellow warn / red fail), ASCII only, no emoji.

Main loop:

    registrar  |  working folder: C:\ORG\AGRs

      1) Validate an AGR
      2) Build an AGR  (validate, then render on pass)
      3) Change working folder
      0) Quit
    >

Choosing 1 or 2: list `*.json` AGRs in the working folder plus the recent list
(numbered) -> select -> mode (draft/promotion, defaulted from the AGR's own
`validationPolicy.mode`, usually just Enter) -> optional registry CSV -> run the
existing pipeline -> print the human-readable result -> post-run action menu (open
DOCX / open report / copy failures / back). `0`/Enter conventions are consistent
throughout.

## Architecture

Pure, testable logic is separated from a thin I/O shell. All new units live in
`Registrar.Cli`.

- **`MenuCommand`** - the `menu` verb's action: load settings, run the main loop,
  dispatch actions. The only unit that touches the live console, through an
  `IConsole` seam (ReadLine / WriteLine / color) so tests drive it with scripted
  input.
- **`IConsole`** - console I/O abstraction (real impl over `System.Console`; a
  scripted fake for tests).
- **`ReportPresenter`** (pure) - `ValidationReport -> formatted lines`: overall
  banner (document id + mode), a tally (N checks, X failed, Y warnings), then each
  failing/warning check's `checkId`, severity, layer, message, `jsonPath`,
  expected/actual, remediation, and `ruleSource`, failures first. On a passing build
  it reports the DOCX path (and payload path in promotion). No console dependency.
- **`GovernorClipboardText`** (pure) - `ValidationReport -> string`: a header naming
  the document, then one entry per failing check (`checkId`, `jsonPath`,
  expected/actual, remediation, `ruleSource`). Invoked only when failures exist.
- **`SettingsStore` + `MenuSettings`** - load/save `%APPDATA%\registrar\settings.json`
  (`lastFolder`, capped `recentAgrs`). Injected path for tests; a missing/corrupt
  file loads defaults without throwing.
- **`AgrPicker`** - list `*.json` in the working folder, merge the recent list,
  return the choice; the listing/merge/numbering is pure and tested, the "read a
  number" part is the shell.
- **`IOperatorShell`** - `OpenFile(path)` (shell-open in the default app,
  `UseShellExecute=true`) and `CopyToClipboard(text)` (pipe to `clip.exe`), behind an
  interface with a real impl and a test fake, so tests never launch Word or touch the
  clipboard.

**Reuse, not duplication:** a menu action invokes the same `ValidateCommand.Run` /
`BuildCommand.Run` (capturing their exit code and stdout), then reads the written
`validation-report.json`, deserializes it into `ValidationReport` (the record already
carries `JsonPropertyName` attributes), and hands it to `ReportPresenter`. The report
file is the source of truth; the menu displays what the engine wrote.

## QoL feature details

- **Human-readable report** (`ReportPresenter`): as above. The single biggest
  day-to-day win - the operator stops opening raw JSON.
- **Copy failures for the Governor** (`GovernorClipboardText` + `IOperatorShell`):
  offered only on failures; builds the paste block and pipes it to `clip.exe`;
  confirms "copied N failures." Pasted straight into the Governor chat.
- **Auto-open outputs** (`IOperatorShell.OpenFile`): the post-run menu offers
  open-DOCX and open-report; each shell-opens the file (so `.docx` opens in Word).
  Always an explicit choice, never automatic.
- **Remember folder + recent AGRs** (`SettingsStore`): working folder defaults to
  last used (falls back to the exe folder first run); each run pushes the AGR onto a
  deduped, capped (8) recent list; "change working folder" persists the new path;
  paths only.

## Packaging and launcher

- **Self-contained single-file publish:**
  `dotnet publish src/Registrar.Cli -c Release -r win-x64 --self-contained
  -p:PublishSingleFile=true` -> `registrar.exe` with the runtime bundled. No trimming
  (protects `System.Text.Json`/OpenXml reflection).
- **Packaged layout (one zip):** `registrar.exe` + `config/` + `schemas/` + `assets/`
  + `Registrar.cmd` + a run guide. Config/schemas/assets are editable files next to
  the exe.
- **`ConfigLocator` adjustment:** it currently resolves config/schemas/assets by
  walking up from the dev exe location to the repo root; in the packaged layout they
  sit next to the exe, so verify (and adjust if needed) that it searches the base
  directory itself, covered by a test for the packaged layout.
- **Packaging script (PowerShell 7):** `scripts/publish.ps1` runs the publish and
  assembles the release folder into a zip. One documented, repeatable command.
- **Launcher (`Registrar.cmd`):** double-click opens a console, `cd /d "%~dp0"`, runs
  `registrar.exe menu`. `.cmd` chosen over `.ps1` for frictionless double-click with
  no execution-policy issues; the PS7 preference governs the packaging script (real
  automation), not this shortcut.
- **Run guide** in the README: unzip, double-click `Registrar.cmd`, done; plus a note
  that `validate`/`build` verbs remain for scripting/CI.

## Testing

- **Pure units:** `ReportPresenter` (fail -> banner + checkId + message +
  remediation; clean pass -> `PASS`, no failure blocks); `GovernorClipboardText`
  (paste block contains each failing checkId/jsonPath/remediation + header);
  `SettingsStore` (save/load round-trip; dedup + cap; missing/corrupt -> defaults, no
  throw); `AgrPicker` (folder listing merged with recent, numbered, deduped).
- **Menu loop (scripted-input integration):** drive `MenuCommand` through `IConsole`
  with canned input and a fake `IOperatorShell`, against a temp working folder holding
  the real DS/AUP fixtures so the engine runs for real. Cases: validate clean ->
  `PASS`; validate a failing fixture -> `FAIL` and "copy failures" hands real failure
  text to the fake clipboard; build DS -> a `.docx` appears and open calls the fake
  shell; bad input re-prompts; quit exits cleanly.
- **Packaging:** a `ConfigLocator` test proving resolution works when the search base
  is the directory containing config/schemas/assets (packaged layout), not only an
  ancestor.
- The real `IOperatorShell` (clip.exe pipe, shell-open Word) is faked in tests and
  verified once by hand against the packaged build.

## Component boundaries summary

- `ReportPresenter` / `GovernorClipboardText` - pure report-to-text; depend only on
  `ValidationReport`.
- `SettingsStore` / `AgrPicker` - pure file/list logic; depend on an injected path /
  folder.
- `IConsole` / `IOperatorShell` - the only OS/console seams; real impls thin, fakes
  for tests.
- `MenuCommand` - orchestration; the only unit that knows the menu flow, wiring the
  above plus the existing `ValidateCommand`/`BuildCommand`.
- `ConfigLocator` - extended to resolve config from the exe's own directory.
- `scripts/publish.ps1` + `Registrar.cmd` - packaging and launch; no engine logic.
