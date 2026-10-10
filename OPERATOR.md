# The Registrar - Operator Quickstart

This is the go-live copy of the engine. It turns an Artifact Generation Request
(AGR) - the JSON the Governor produces in chat - into a controlled DOCX plus a
validation report. You are the transport and the trigger: you save the AGR, run
the tool, read the report, and paste any failures back to the Governor. The
engine makes no governance decisions and never contacts the Governor.

## Keep the folder shape (do not flatten)

The tool finds its `config/`, `schemas/`, and `assets/` by walking up from the
executable. Move the whole folder as one unit and keep this shape:

    registrar/
      runtime/        <- registrar.exe, Registrar.cmd, Registrar.App.exe and
                         RegistrarApp.cmd live here
      config/         <- the versioned template pack (editable, no rebuild)
      schemas/
      assets/
      README.md
      OPERATOR.md     <- this file

You can unzip it anywhere (a OneDrive-synced folder is fine). No .NET install is
needed - the runtime is bundled.

## Run it

Start with the window. Double-click `runtime\RegistrarApp.cmd`.

The window is a single page. You work down it from top to bottom:

1. Under **1. Your working folder**, press **Choose folder** and pick the folder
   where you save the Governor's AGR files.
2. Under **2. ID registry (optional)**, press **Choose registry file** and pick
   the registry export (a .csv). Without one, the registry check is skipped in a
   draft and fails a promotion build. Once chosen, the registry is remembered by
   the window and is checked in draft runs too: a draft check against it fails if
   the Document ID is not registered. **Clear** removes your choice, so press it
   to draft without a registry.
3. Under **3. The file to check**, pick the AGR from the list of `.json` files in
   your folder. Files you used recently come first. If yours is not there, press
   **Add a file...** and pick it. Drag and drop does not work.
4. Read the line under the list. It shows the mode the file declares: draft or
   promotion, or "Mode unknown. Press Check and the report will say what is wrong
   with this file." You cannot change the mode here. To change it, ask the
   Governor for an AGR with the other mode.
5. Press **Check** to validate only, or **Build** to validate and then render the
   DOCX if it passes. Both stay off until you pick a file.
6. Read the result. The banner says PASS, PASS WITH WARNINGS or FAIL. Each failing
   or warning check has its own block with what is wrong, where, what was
   expected, what was found, a `fix:` line you can paste to the Governor, and the
   rule it comes from.
7. After a run, these buttons appear when they apply: **Open document** (only if
   a DOCX was made this run), **Open report**, **Open folder**, and **Copy
   failures for the Governor** (only if a check failed).

The **Health check** button at the bottom runs the same self-check as
`registrar doctor` and ends with a verdict: healthy or not healthy.

The window never edits your AGR. The window remembers your folder, recent files
and registry choice between runs.

A step that fails shows its message beside the controls and the window stays
open. If an unexpected error escapes, a dialog titled "Registrar - unexpected
failure" names it, and the window still stays open.

If you have never used it, `docs/first-artifact-walkthrough.md` walks you from an
empty folder to a promotion build. That file is in the travel pack and in the
repository. The release zip alone does not include `docs/`, so if you only have
the release zip, get the walkthrough from the repository or the travel pack.

If the window opens blank, run `registrar.exe doctor` and read the `webview user
data` line. That check warns rather than fails, because a machine that cannot open
the window can still validate, build and promote normally through the CLI.

## The CLI, still here

The command line does everything the window does and is not being retired. Use it
when the window will not open, when you are scripting, or when you prefer it.

- Double-click `runtime\Registrar.cmd` for the interactive menu.
- Or from a terminal in the `runtime` folder:

      registrar.exe build <path-to-agr.json> --mode draft

## The everyday loop (draft)

In the window: save the Governor's AGR into your folder, choose it, press
**Build**, and read the result. If it failed, press **Copy failures for the
Governor**, paste them into the Governor chat, save the regenerated AGR, and run
again.

Through the CLI it is the same loop, done by hand:

1. Save the AGR JSON from the Governor chat into your working folder.
2. Build it in draft mode (menu: choose build; or the command above).
3. Open `validation-report.json` next to the AGR and read `overallResult`.
   - Pass: open the DOCX and review it.
   - Fail: copy the failing checks (the menu's copy option formats them for
     chat), paste them back into the Governor, regenerate the AGR, and run again.

Draft mode is forgiving: unknown placeholder tokens and similar issues appear as
warnings, and the document still renders so you can review it.

## Promotion (final controlled release)

Promotion is stricter and enforces registry state:

1. You must supply the ID Registry export (a CSV of the PMC Registry sheet).
   Point the menu at it, or pass `--registry <path-to-registry.csv>`. In the
   window, choose the export in step 2 with **Choose registry file**, and make
   sure the AGR file's mode is promotion.
2. The Document ID in the AGR must already be registered in that registry, in a
   status from which the AGR's own status is the same or a legal next step (a
   first promotion normally starts from a registry row in `Draft`). If it is not,
   the run fails closed - no document is produced.
3. On a pass, the engine writes `promotion-payload.json`. It does NOT change the
   registry. That file tells you the intended update (for example Draft -> In
   Review) and the repository target.
4. You apply that update to the PMC Registry sheet and confirm the new state.
   That manual step is your signoff - a governance control, not a missing feature.

## What each run writes (next to the AGR)

- `validation-report.json` - always, on every run.
- `generation-log.json` - whenever rendering was attempted. What config was
  loaded, which rules ran, which clauses were inserted.
- The DOCX - only on a pass (or pass-with-warnings in draft mode).
- `artifact-manifest.json` - only when a DOCX was produced. Its metadata,
  section inventory, and the SHA-256 of the document itself.
- `promotion-payload.json` - only on a passing promotion run.

The two you read are the report and the DOCX. The other three are the machine
record of what happened, kept so a controlled document can be traced back to the
inputs and config that produced it.

## Fail-closed is normal

A controlled-document violation produces NO document, by design. The report gives
you the `checkId`, the `expected` and `actual` values, and a `remediation` phrased
so the Governor can act on it. There is never a partial document.

## Commands

    registrar validate <agr.json> [--registry <registry.csv>]
    registrar build    <agr.json> --mode draft|promotion [--registry <registry.csv>]

The tool never edits your AGR and never talks to the Governor. Copying failures
just puts text on your clipboard for you to paste into the Governor chat - the
human checkpoint stays with you.
