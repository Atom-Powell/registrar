# Simple operator window (replaces GUI-6 through GUI-8)

Status: proposed, awaiting owner review. SW-1, SW-2 and SW-3 are built; SW-4 (the docs) is not.
Date: 2026-10-08.

## Why this exists

The operator asked for a GUI version of the CLI, friendlier to lower-proficiency
users, with a little quality of life on top. What shipped as GUI-6 through GUI-8 was
an authoring tool: New artifact, an editor with block editors, a live validation
rail, autosave, and an Import screen. The operator's verdict on 2026-10-08 is that it
is more confusing than the CLI. The scope was wrong, not the code quality. This spec
replaces that scope and retires the screens.

The test for every decision below: could a technician who avoids the command line
finish a job without reading a manual, and is the window simpler than `registrar menu`?

## What the window does

One window, one page, no navigation. It does what `registrar menu` does today and
nothing more.

1. **Working folder.** Browse to a folder, remembered between runs.
2. **Registry CSV.** Browse to the ID Registry export, remembered. (The console menu
   asks for this on every run; the window asks once.)
3. **AGR list.** The `.json` AGRs in the folder, most recently used first. An Add a file
   button opens a native file picker and selects the chosen file. Drag and drop is
   deferred: the embedded browser swallows OS file drops and only exposes a path
   through a web-message bridge that cannot be verified without a person at the
   window, so it is its own small unit after the owner has run the window.
4. **Mode.** Shown, read-only, as the AGR's own declared mode with a one-line
   plain-language explanation. It is not a toggle, because a mode that disagrees with
   the AGR is already a usage error (exit 2) in the CLI, and a control that can only
   produce an error is the kind of confusion we are removing.
5. **Check** (validate only) and **Build** (validate, then render on pass).
6. **Result panel.** The same content `ReportPresenter` produces: a PASS, PASS WITH
   WARNINGS or FAIL banner, then each failing and warning check with its location, the
   fix instruction and the rule source. Plain rows, no tabs.
7. **After a run:** Open document, Open report, Open folder, and Copy failures for the
   Governor (the existing `GovernorClipboardText`).
8. **Health check.** One button that runs `doctor` and shows its result.

The human checkpoint stays as the constitution requires. The window shows the report
and hands the operator the copy button. It never sends anything to the Governor and
never edits an AGR.

## What it deliberately does not do

No authoring, no editing, no New artifact, no `.agrdraft`, no registry browsing, no
Governor brief, no engagement scan. All of those were GUI-6 through GUI-8 and are
retired. Validation and rendering remain the engine's job; the window holds none of it.

## Architecture

CLAUDE.md fixes the layering: `Registrar.App` is a peer of `Registrar.Cli`, must not
reference it, and holds no engine logic. Today everything the window needs lives in
`Registrar.Cli`: `BuildCommand`, `ValidateCommand`, `CommandSupport`, `ReportPresenter`,
`GovernorClipboardText`, `AgrPicker`, `SettingsStore`, `MenuSettings`, the report and
DOCX lookup helpers in `MenuCommand`, and `Doctor`. `BuildCommand` also writes to
`TextWriter`s and returns exit codes, so it cannot be called for a structured result.

So the work is an extraction, in the order below.

- **Pipeline gains a file-based run** (working name `AgrFileRun`): given an AGR path,
  mode, registry path and clock, it validates, writes the output package, builds when
  asked, and returns a structured result: exit code, the ordered stdout and stderr
  messages, the report written this run (null when none), the output directory, the
  files written and withheld, and the document file name. It absorbed the former
  `CommandSupport` and composes `OutputPackage` and `BuildPipeline`.
- **`ValidateCommand` and `BuildCommand` become thin wrappers** over it that format
  stdout, stderr and the exit code exactly as today.
- **Presenter and helpers move to Pipeline or Core:** `ReportPresenter`,
  `GovernorClipboardText`, `AgrPicker`, `SettingsStore` and `MenuSettings`. The report
  and DOCX lookup helpers stayed in `MenuCommand`, because `AgrFileRunResult` carries
  the report and the document file name directly, so the window never needs to look
  them up. `Doctor` moved to Pipeline unchanged; it had no console dependency, so no
  split was needed. `MenuCommand.TryReadMode` (reads the AGR's declared mode) stayed in
  Cli, and the window needs the same one-liner for the read-only mode display, so SW-3
  moves it to Pipeline.
- **App becomes one Blazor page** over those types, in the existing WPF host.

### The CLI must stay provably intact

The operator ruled during GUI-8 that the CLI is the fallback and must not be reduced.
Reuse the same two guards, in the same order:

1. `BuildOutputFileSetTests` already asserts the complete file set per outcome against
   the unmodified CLI. Run it before and after.
2. `BuildCommandTests`, `ValidateCommandTests` and the menu tests take zero edits across
   the extraction. If one needs editing, the extraction changed behavior and is wrong.

## Retirement

Removed from `Registrar.App` in this work: `Editor`, `EditorHeader`, `NewArtifact`,
`Engagement`, `Import`, `IdentityPane`, `SectionPanel`, `ProseBlock`, `RadioBlock`,
`ClauseBlock`, `ReadOnlyBlock`, `ValidationRail`, `Home` (replaced), `DraftSettler`,
`BuildRunner`, `AppState` (reduced to settings), `wwwroot/editor.js`, and their tests in
`Registrar.App.Tests`.

Not removed here: the Core authoring code from GUI-3 and GUI-4 (`AgrAuthoring`,
`DraftEdits`, `EditorForm`, `EditorRail`, `DraftCreation`, `FieldPath`, `EngagementScan`,
the `.agrdraft` format, `GovernorBrief`). It becomes dead code with tests. It is a few
thousand lines in Core, so deleting it gets its own PR and its own review rather than
riding along with the window. Track it in `docs/tech-debt.md`.

Kept: the GUI-5 design tokens (`emit-tokens`, generated from `style-rules.json`) that
style the page, and the `doctor` WebView2 user-data-folder check.

## Decisions for the owner

Each has a default. Say "defaults" and I proceed on all of them.

1. **Keep WPF hosting Blazor, or go plain WPF.** Default: keep. The shell, tokens and
   bUnit harness exist and work. Plain WPF would drop WebView2 (the blank-window trap)
   and the Blazor handler-crash hazard found in GUI-8, at the cost of rebuilding the
   styling and test setup. Worth choosing plain WPF only if WebView2 has bitten you on the
   MSP machines.
2. **Paste-from-Governor.** A button that takes the clipboard, recovers the fenced `json`
   block with the existing `ImportParser`, and saves it into the working folder, removing
   the hand-save step of the transport. Default: not in v1. It is the one QoL item that
   changes the operator's checkpoint (the operator would no longer see the raw file), so
   it deserves its own decision after the basic window is in use.
3. **Core authoring code.** Default: leave in place now, delete in a follow-up PR.
4. **`.agrdraft` files.** `fixtures/ORG-GOV-AUP-004.agrdraft` is untracked and of unknown
   origin. Default: leave it alone until the follow-up PR, then delete with the format.

## Units

IDs are new and follow the roadmap's rules (no renumbering; append to the Amendments log).

| Unit | Content | Depends on |
|---|---|---|
| SW-1 | Extract `AgrFileRun` and the presenter, settings and helper types into Pipeline or Core. CLI delegates. CLI test guards hold. | none |
| SW-2 | Remove the retired screens, `DraftSettler`, `BuildRunner` and their tests from App. | SW-1 |
| SW-3 | The one-page window: folder, registry, AGR list, drag and drop, Check, Build, result panel, post-run actions, health check. | SW-1, SW-2 |
| SW-4 | Update every orientation doc the old GUI made false: `README.md`, `OPERATOR.md`, `docs/first-artifact-walkthrough.md`, `docs/operations-guide.md` currency note, ROADMAP, DELIVERED, CLAUDE.md layout note. | SW-3 |

SW-3 shipped without drag and drop (see item 3).

SW-4 is in the definition of done on purpose. The vault's lesson from the last refresh
was that a doc with no owner in the definition of done goes stale first.

## Acceptance

- A technician who has never seen it validates and builds a fixture AGR using only the
  window, with no instructions beyond "pick a folder".
- Every outcome the CLI reports, the window reports with the same words: pass, warnings,
  fail, an unreadable AGR, a missing registry. (The mode-disagreement usage error cannot
  occur in the window, because mode is read-only there.)
- **The operator runs it by hand before merge.** GUI-7's 723 green tests missed a
  shipping bug that a person found in minutes. A green suite does not close this.
- Suite is green in Release (CI builds Release), zero warnings.
- Each unit ships as its own PR to `main`, not a direct push.

## Notes for SW-3

- `AgrFileRun` is not total. `EngineContext.Load` and `ValidationService` exceptions on
  bad config propagate exactly as they do in the CLI, so the window must wrap the call,
  because an exception escaping a Blazor handler crashes the process.
- Doctor's registry check text says "pass --registry <csv>", which a window operator
  cannot act on. SW-3 needs window-appropriate wording without changing the CLI's output.
- (c) `_Imports.razor` now has only `Microsoft.AspNetCore.Components.Web` and
  `Registrar.App`, so SW-3 pages need `@using Registrar.Pipeline`, `Registrar.Validation`
  and `Registrar.Core` as they use them.
- (d) ScreenError's `.what`, `.advice` and `button.recover` have no styling, and the removed
  `.toolbar button:focus-visible` rule was the only visible button focus style, so SW-3
  controls need their own focus style. `app.css` keeps `.error` and `.notice` for SW-3 to
  use or remove. Tech-debt item 71 wants `:root { color-scheme: light; }` once inputs exist.
- (e) Do not cut a release between SW-2 and SW-3/SW-4: the packaged `RegistrarApp.cmd` would
  open a placeholder while OPERATOR.md says to start there, and `dist/` holds packages built
  before SW-2 that still contain the editor, so republish before any handoff.

## SW-4 doc work

Files that still describe the retired authoring window: `README.md` (line 27 and the GUI-8
paragraph around lines 122-132), `OPERATOR.md` (lines 28 and 64), `CLAUDE.md` (the
Division-of-labor paragraph about GUI-authored `.agrdraft` files), and the comment in
`RegistrarApp.cmd`.

## Out of scope

Authoring or editing of any kind. Any new engine rule. Any change to validation,
rendering, the output package or the five-file contract. SharePoint integration. A
model call anywhere (rule 2).
