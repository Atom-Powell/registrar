# SW-4: Operator docs for the simple window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every orientation and operator document describe the window that actually shipped (SW-1 through SW-3), and stop describing the authoring GUI that was removed.

**Architecture:** Docs only. One shared "Facts of record" section below is the single source for what the window does; every task uses it, and reviewers check claims against the code (`src/Registrar.App/Components/Home.razor`, `ResultPanel.razor`, `HealthPanel.razor`, `src/Registrar.Pipeline/*`), not against the old docs. Delivered history is appended to `docs/DELIVERED.md`; shipped work is NOT repeated in `CLAUDE.md`.

**Tech Stack:** Markdown. Spec: `docs/superpowers/specs/2026-10-08-simple-operator-window-design.md` (see "SW-4 doc work").

## Global Constraints

- Rule 8 applies to every file written here: US English, no emojis, no em or en dashes (use a colon, comma or a plain hyphen inside a word), no decorative unicode. The repo has an AI-tell lint hook; if an edit to `README.md` is blocked by pre-existing em dashes, replace those dashes in the lines you touch, and report any you could not.
- Write for a technician who avoids the command line: short sentences, plain words, exact button names as they appear on screen.
- Do not touch source code, config, schemas or tests. Docs only. Never change a doc's claims about the CLI; the CLI is unchanged.
- Do not describe anything that is not in the Facts below. If a doc currently describes a feature that is not in the Facts (editor, rail, Import screen, drafts, autosave, New artifact, engagement list, `.agrdraft`, live validation while typing, copy-the-brief button), remove or replace it.
- Do not edit historical records: `docs/DELIVERED.md` entries already written, `docs/superpowers/specs/*` and `plans/*` other than the SW-4 plan, ADRs in `docs/reference/`, and `docs/tech-debt.md` items (only append).
- Untracked `fixtures/ORG-GOV-AUP-004.agrdraft` must never be staged. Stage by explicit path.
- Ship as a PR to `main`; do not push inside a task.

## Facts of record (the window as shipped)

1. **Launch.** `RegistrarApp.cmd` in `runtime\` starts `Registrar.App.exe`. The CLI (`Registrar.cmd`, `registrar.exe`) is unchanged and remains the fallback and the scripting surface.
2. **One page, no navigation.** Top to bottom:
   - **"1. Your working folder"**: the chosen path (or "No folder chosen yet."), button **Choose folder** (native folder picker).
   - **"2. ID registry (optional)"**: the chosen path (or "No registry chosen."), a hint "Without one, the registry check is skipped in a draft and fails a promotion build.", buttons **Choose registry file** (native picker, .csv) and, once chosen, **Clear**.
   - **"3. The file to check"**: a list of the `.json` files in the working folder, files used recently first, the tool's own output files (`validation-report.json`, `generation-log.json`, `artifact-manifest.json`, `promotion-payload.json`) hidden. One is selected (a radio button). If the folder has none: "No .json files in this folder yet. Choose another folder, or add a file." If no folder is chosen: "Choose a working folder to see its files, or add a file." Button **Add a file...** opens a native file picker and selects the file.
   - A **mode line** under the list, read from the AGR and not changeable: "Draft: a working copy. Warnings are allowed.", "Promotion: the controlled copy. A clean pass is required.", or "Mode unknown. Press Check and the report will say what is wrong with this file."
   - Buttons **Check** (validate only: writes the report) and **Build** (validate, then render the DOCX if it passes). Both are disabled until a file is selected, and while a run is in progress ("Working...") the folder, registry, file list and Add a file controls are locked too.
   - **Result**: a banner reading PASS, PASS WITH WARNINGS or FAIL with the document ID and mode, a count of failing and warning checks, then one block per failing or warning check with its check ID, what is wrong, where (`at:`), `expected:` and `actual:`, a `fix:` line the Governor can act on, and a `rule:` line citing the governing document. A one-line summary can follow (for example "no document (promotion requires a clean pass)"). A usage or IO error with no report (file not found, unreadable registry, a config problem) shows as a red error message instead of a banner.
   - After a run, only the buttons that apply: **Open document** (only if the DOCX was produced this run), **Open report** (opens `validation-report.json`), **Open folder** (the folder the files were written to), **Copy failures for the Governor** (only if a check failed; puts formatted text on the clipboard; the operator pastes it into the Governor chat).
   - **Health check** button at the bottom: runs the same self-check as `registrar doctor`, one line per check (OK, INFO, WARN, FAIL) and a verdict, "Healthy: everything the Registrar needs is in place." or "Not healthy: ..."
3. **Where files land.** Next to the AGR, in the AGR's own folder, exactly as the CLI does: `validation-report.json` always; `generation-log.json` when rendering was attempted; the DOCX only on a pass (or pass-with-warnings in draft mode); `artifact-manifest.json` only when a DOCX was produced; `promotion-payload.json` only on a passing promotion run.
4. **The window never edits an AGR.** There is no editor, no draft file, no Import screen, no live validation while typing. The AGR JSON is the file. The mode is whatever the AGR declares; to change the mode the operator asks the Governor for an AGR with the other mode.
5. **The operator transport is unchanged.** The operator saves the Governor's reply (the JSON in the fenced code block, copied with the block's copy button) to a `.json` file in the working folder, uses the window (or the CLI), reviews the report, and pastes failures back into the Governor chat for regeneration. That human checkpoint is a governance control.
6. **Settings.** The window remembers the working folder, recently used files and the registry path in `%APPDATA%\registrar\settings.json`. The console menu (`registrar menu`) shares that file for the folder and recent files, but it asks for the registry every run and ignores the saved one.
7. **Failures stay on screen.** A step that fails shows its message beside the controls and the window stays open. If something unexpected escapes, a dialog titled "Registrar - unexpected failure" names the error and the window stays open. If the window will not open or opens blank: run `registrar.exe doctor` and read the `webview user data` line; a machine that cannot open the window can still validate, build and promote through the CLI.
8. **Drag and drop is not supported.** Use **Add a file...**. It is a candidate follow-up unit.
9. **Known wording.** Failure text from the validation layer for a missing registry in promotion says "--registry <path>"; in the window that means "choose a registry file with Choose registry file".
10. **Registry and promotion.** Promotion needs a registry export (CSV of the PMC Registry sheet) chosen in step 2 (CLI: `--registry`). The Document ID must exist in the registry in the expected status (a first promotion expects `Draft`). On a pass the engine writes `promotion-payload.json`; it never changes the registry. Applying that update to the PMC Registry is the operator's signoff.

## File Structure

| Path | Action | Task |
|---|---|---|
| `CLAUDE.md` | edit one bullet (Division of labor) | 1 |
| `README.md` | edit two passages | 1 |
| `OPERATOR.md` | rewrite "Run it" and the draft loop; keep the CLI, promotion, output and commands sections accurate | 1 |
| `RegistrarApp.cmd` | leave unchanged | n/a |
| `docs/first-artifact-walkthrough.md` | rewrite | 2 |
| `docs/operations-guide.md` | rewrite the window sections, fix cross references, fix troubleshooting | 3 |
| `docs/ROADMAP.md` | units table, status line, amendments log | 4 |
| `docs/DELIVERED.md` | append | 4 |

---

### Task 1: CLAUDE.md, README.md, OPERATOR.md

**Files:** Modify `CLAUDE.md`, `README.md`, `OPERATOR.md`.

**Interfaces:** Consumes the Facts section. Produces nothing later tasks depend on, but Task 2 and Task 3 link to `OPERATOR.md` headings, so keep the heading `## Promotion (final controlled release)` and `## What each run writes (next to the AGR)` and `## Fail-closed is normal` and `## Commands` as they are.

- [ ] **Step 1: CLAUDE.md.** In "Division of labor", replace the bullet that begins "For a GUI-authored artifact the direction reverses..." (about lines 21 to 25) with a bullet saying: the operator window (`Registrar.App`) is a front end over the same engine as the CLI, it never edits an AGR, and the operator's transport role above applies unchanged to both surfaces. Change nothing else in the file. This edit is the owner's explicit amendment (the approved spec lists it); the rest of the constitution is untouched.
- [ ] **Step 2: README.md.** Replace the paragraph near line 27 ("For an artifact authored in the operator window instead, the direction reverses...") with two sentences saying the same thing as the CLAUDE.md bullet. Replace the "Running The Registrar" opening (about lines 122 to 130, from "For everyday use, run the window" through "...and press Build. Validation runs as they type.") with a short description from the Facts: the one-page window, its three steps, Check and Build, the result and the after-run buttons, and the Health check; keep the sentence pointing to `docs/first-artifact-walkthrough.md`. Keep the rest of that section (the CLI fallback paragraph, the packaged release paragraph, the direct verbs). In the packaged-release paragraph change "the draft loop" to wording that matches the new OPERATOR.md. Check every other mention of "window" in README against the Facts and fix any that still describe authoring.
- [ ] **Step 3: OPERATOR.md.** Rewrite "Run it" (about lines 26 to 51): the window is a single page; list what you do (steps 1 to 3, Check, Build, read the result, the four after-run buttons, Health check); keep the paragraph about the blank window and `registrar.exe doctor`. Remove the bullets about creating artifacts, the Import screen and copying the brief. Rewrite "The everyday loop (draft)" so the window path reads: save the Governor's AGR into your folder, choose it, press Build, read the result, fix and repeat via the Governor; keep the "Through the CLI" numbered steps as they are. In "Promotion", add one sentence that in the window you choose the registry export in step 2 and the file's mode must be promotion. Keep "What each run writes", "Fail-closed is normal" and "Commands" unchanged unless a sentence contradicts the Facts.
- [ ] **Step 4: Verify.** Run `grep -n -i "editor\|rail\|Import screen\|New artifact\|agrdraft\|engagement folder\|autosave\|as they type\|as you type" README.md OPERATOR.md CLAUDE.md`. Expected: no hit that describes the window. A hit that is genuinely about something else (for example `.agrdraft` in a historical sentence you were told not to touch) is reported with its line number. Run a non-ASCII and dash check on every line you changed: `git diff -U0 | grep -n -P '^\+.*[^\x00-\x7F]'` must print nothing.
- [ ] **Step 5: Commit.**

```bash
git add CLAUDE.md README.md OPERATOR.md
git commit -m "docs: describe the simple window in CLAUDE.md, README and OPERATOR

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The first-artifact walkthrough

**Files:** Modify (rewrite) `docs/first-artifact-walkthrough.md`.

**Interfaces:** Consumes the Facts section. Other docs link to this file by path only.

- [ ] **Step 1: Read the current walkthrough and the code it must now match.** Read `docs/first-artifact-walkthrough.md`, `src/Registrar.App/Components/Home.razor`, `ResultPanel.razor`, `HealthPanel.razor`, and one sample AGR such as `fixtures/agr-sample-tcl-draft.json` (or the TCL fixture in `fixtures/`).
- [ ] **Step 2: Rewrite the walkthrough.** Keep the title, the audience (never opened the Registrar, nobody to ask), the TCL example and the "about twenty minutes" budget. Replace the authoring steps with this flow, in order: what you need (a working folder, the Governor's reply saved as a `.json` file, the registry export for promotion, with the Document ID registered by Registry Owner); open the window and choose the folder; add or select the file; read the mode line; press Check and read the result; press Build and open the document; what to do with a FAIL (read the `fix:` lines, press Copy failures for the Governor, paste into the Governor chat, save the regenerated reply, repeat); promotion (choose the registry in step 2, ask the Governor for a promotion-mode AGR, Build, then apply `promotion-payload.json` to the PMC Registry as your signoff); where the files landed; the Health check; if something goes wrong (the unexpected-failure dialog, a blank window, `registrar.exe doctor`, the CLI fallback). Keep the fixtures-based example concrete: name the real file the reader selects and the real DOCX name that appears. Use exact on-screen names from the Facts. Remove everything about the editor, section panels, the rail, Import, New artifact, the brief, and the engagement list. Keep the honest caveat from the old text, if present, that the Governor hand-off in Copilot Studio is the one step this walkthrough has not observed end to end.
- [ ] **Step 3: Verify.** Every button name and message you quote must appear in the Home, ResultPanel or HealthPanel source (grep each one). Run the character check from Task 1 Step 4 on the diff. Length target: 120 to 200 lines.
- [ ] **Step 4: Commit.**

```bash
git add docs/first-artifact-walkthrough.md
git commit -m "docs: rewrite the first-artifact walkthrough for the simple window

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The operations guide

**Files:** Modify `docs/operations-guide.md`.

**Interfaces:** Consumes the Facts section.

- [ ] **Step 1: Find every window passage.** Run `grep -n -i "window\|editor\|engagement\|Import screen\|\.agrdraft\|draft\b\|New artifact\|rail\|autosave\|brief" docs/operations-guide.md` and read each hit in context. Known areas: the intro paragraphs; the Governor reply hand-off near line 104; the validation report note near line 126; the whole operator-window section from about line 160 to 260 (engagement screen, editor, Import screen, build output folder per artifact and version, the `*.json` in the engagement folder note); the Import-recovery sentence near line 731; the troubleshooting entries from about line 761 (for example "New artifact is disabled"). Also look for any dated "currency note" about the window predating the guide, and remove or replace it.
- [ ] **Step 2: Rewrite.** Replace the operator-window section with a reference section built from the Facts: the one page, each control, the result and the after-run buttons, where files land (next to the AGR, flat, same as the CLI), settings and what is shared with the console menu, the Health check, error behavior, and "what the window does not do" (no editing, no drafts, no drag and drop). Fix the hand-off passage (the operator saves the Governor's JSON to a file and chooses it). Fix the report note. Replace the troubleshooting entries that refer to deleted screens with entries for the real symptoms: the file list is empty, the file you expect is missing from the list (the tool hides its own output files; use Add a file...), Check or Build are greyed out, a red error instead of a banner (read it; usually a missing file or registry), the window is blank (`doctor`, `webview user data`), an unexpected-failure dialog, Copy failures does nothing (clipboard locked by another program). Keep the CLI sections unchanged. Update the intro sentence that sends readers to the walkthrough so it matches the new walkthrough ("takes you from an empty folder to a promotion build in the window" stays true).
- [ ] **Step 3: Verify.** Re-run the Step 1 grep; every remaining hit must be correct under the Facts or about the CLI or the Governor. Run the character check from Task 1 Step 4 on the diff. Report the line range you rewrote and the net line change.
- [ ] **Step 4: Commit.**

```bash
git add docs/operations-guide.md
git commit -m "docs: bring the operations guide in line with the simple window

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Roadmap and delivered record

**Files:** Modify `docs/ROADMAP.md`; append to `docs/DELIVERED.md`.

**Interfaces:** Consumes the Facts section and the PR numbers: SW-1 is #78, SW-2 is #79, SW-3 is #80; SW-4 is this PR (write "this PR" until the number exists).

- [ ] **Step 1: ROADMAP.** In the Units table: change GUI-6, GUI-7 and GUI-8 status to `Superseded (removed in PR #79; replaced by SW-2 and SW-3)` keeping their PR numbers readable in the text; change DOC-1 to `Done (PR #68), rewritten for the simple window in SW-4`; add rows SW-1 (Extract the file-based run into Pipeline, depends on none, Done PR #78), SW-2 (Retire the authoring screens, depends on SW-1, Done PR #79), SW-3 (The one-page operator window, depends on SW-1 and SW-2, Done PR #80), SW-4 (Operator docs for the simple window, depends on SW-3, Done this PR). Replace the sentence "GUI-1 through GUI-8 are delivered. The operator GUI is finished as scoped." with an accurate statement: GUI-1 to GUI-5 stand (layer mapping, generated Governor knowledge, design tokens, and the Core authoring code now unused), GUI-6 to GUI-8 were judged too complex by the owner on 2026-10-08 and replaced by SW-1 to SW-4; the design is in `docs/superpowers/specs/2026-10-08-simple-operator-window-design.md`. Append an entry to the Amendments log dated 2026-10-08 in the log's existing format: what changed (units SW-1 to SW-4 added, GUI-6 to GUI-8 superseded), why (the owner asked for a GUI version of the CLI, not an authoring tool), and that the endpoint is unchanged. Do not renumber any unit. Keep GOV-1 and GOV-2 as they are.
- [ ] **Step 2: DELIVERED.** Append a section `## Delivered 2026-10-08` in the file's existing style (read the last two sections first to match tone and structure) with one entry each for SW-1, SW-2, SW-3 and SW-4. Content, drawn from the merged PRs: SW-1 (#78): `AgrFileRun` in Pipeline, CLI commands now replay its recorded output, helper types moved, `CliOutputStreamTests` pin the CLI text, suite 704 + 87. SW-2 (#79): authoring screens, `DraftSettler`, `BuildRunner` and their tests removed (about 4,000 lines), shell kept, Core authoring code left as dead code (tech-debt 78), suite 710 + 3. SW-3 (#80): the one-page window; `AgrMode`, `GuardedRun`, `AgrRunner`, `DoctorPresenter`, `OutputPackage.PackageFileNames`, `AgrPicker` opt-in `excludeOutputs`, registry path in the shared settings; drag and drop deferred; suite 733 + 25. SW-4: these docs. Mention tech-debt 77 (stale promotion payload after a post-render failure, pre-existing, not fixed).
- [ ] **Step 3: Verify.** Check that no existing DELIVERED entry changed (`git diff docs/DELIVERED.md` shows only additions at the end). Run the character check on the diff.
- [ ] **Step 4: Commit.**

```bash
git add docs/ROADMAP.md docs/DELIVERED.md
git commit -m "docs: record SW-1 to SW-4 in the roadmap and delivered log

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage.** The spec's "SW-4 doc work" list: README line 27 and the GUI-8 paragraph (Task 1), OPERATOR lines 28 and 64 (Task 1), CLAUDE.md Division-of-labor paragraph (Task 1), the `RegistrarApp.cmd` comment (reviewed and left: it describes launching a window, which is still true). Beyond the spec: the walkthrough and the operations guide, which the spec's list did not name but which describe the removed screens at length, and the roadmap and delivered log, which the repo's own convention requires.

**Placeholders.** None: each step names the passages, the replacement content and the verification.

**Consistency.** Every task draws from the one Facts section, so button names, messages and file names agree across documents.
