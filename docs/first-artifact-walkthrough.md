# Your first artifact

This walks you through producing one governed deliverable from start to finish.
It assumes you have never opened the Registrar before and that nobody is
available to ask. Follow it in order.

The example is a Training Checklist (TCL). Every other artifact type works the
same way; only the sections differ.

Budget about twenty minutes the first time.

## How the pieces fit

Three parties, three jobs.

- **The Governor** (the Copilot agent) decides what the document says. It writes
  an Artifact Generation Request, called an AGR, as a block of JSON.
- **The Registrar** checks that request and builds the Word document. It never
  makes a governance decision and it never trusts the Governor, so it re-checks
  every field.
- **You** carry the JSON from the Governor to the Registrar, read the result, and
  carry any failures back to the Governor. That checkpoint is a governance
  control. It is your job, not friction.

The Registrar window does not edit anything. The AGR file is the document's
content, and you change it only by asking the Governor for a new one.

## What you need before you start

1. **A working folder.** Usually a synced SharePoint library folder. The
   Governor's files and the Registrar's output both live here.
2. **The Governor's reply saved as a `.json` file in that folder.** In the
   Governor chat, copy the JSON from the fenced code block with the block's copy
   button, paste it into a text editor, and save it with a `.json` name.
3. **For a promotion build only, an ID registry export.** This is a `.csv` of the
   PMC Registry sheet. The Document ID in your file must already be registered
   by Registry Owner, in a status from which your file's own status is the
   same or a legal next step. A first promotion normally starts from a registry
   row in Draft. The Registrar never issues IDs. A draft build does not need the
   registry.

The sample files used below live in different places depending on how you got the
Registrar:

- **In a repository clone:** `fixtures\agr-sample-tcl-draft.json`,
  `fixtures\agr-sample-tcl-promotion.json` and `fixtures\registry-sample.csv`.
- **In the travel pack:** `sample-agrs\agr-sample-tcl-draft.json` and
  `sample-agrs\registry-sample.csv`. The promotion sample is
  `eval-harness\fixtures\agr-sample-tcl-promotion.json`.
- **In the release zip alone:** none of these ship. Use a clone or the travel
  pack.

The example below uses the sample file `agr-sample-tcl-draft.json`
(Document ID `ORG-TRN-TCL-001`, client Talewind CPA). Copy it into an empty
folder to follow along.

## Step 1: open the window

Double-click `runtime\RegistrarApp.cmd`. A single page opens. There are no tabs
and no menus. You work down it from top to bottom.

## Step 2: choose the working folder

Under **1. Your working folder**, press **Choose folder** and pick your folder.
Until you do, the page says "No folder chosen yet."

## Step 3: choose the registry (promotion only)

Under **2. ID registry (optional)**, press **Choose registry file** and pick the
`.csv`. **Clear** removes it. For this first draft run, skip it. The page says
"Without one, the registry check is skipped in a draft and fails a promotion
build." Once chosen, the registry is remembered by the window and is checked in
draft runs too. Press **Clear** to draft without it. A draft check against the
registry fails if the Document ID is not registered.

## Step 4: pick the file

Under **3. The file to check**, you see the `.json` files in your folder, most
recently used first. Select `agr-sample-tcl-draft.json`.

The Registrar's own output files are hidden from this list on purpose.

If your file is not listed, press **Add a file...** and pick it. Drag and drop
does not work. If the folder holds no `.json` files, the page says "No .json
files in this folder yet. Choose another folder, or add a file."

## Step 5: read the mode line

Under the list, one line shows the mode the file declares. You cannot change it
here.

- "Draft: a working copy. Warnings are allowed."
- "Promotion: the controlled copy. A clean pass is required."
- "Mode unknown. Press Check and the report will say what is wrong with this
  file."

The third line means the file's mode is missing or unreadable. Press **Check**
and the report names the problem.

To use the other mode, ask the Governor for an AGR with that mode. The sample
file says Draft.

## Step 6: press Check

**Check** validates only. It writes `validation-report.json` and builds nothing.
Both **Check** and **Build** stay off until a file is selected, and while a run
is going ("Working...") the folder, registry and file controls lock.

Read the banner:

- **PASS**: nothing wrong.
- **PASS WITH WARNINGS**: allowed in a draft, not in a promotion.
- **FAIL**: nothing will be built. Go to "When a check fails" below.

## Step 7: press Build and open the document

**Build** validates, then renders the Word document if the file passes. Press it
now. For the sample, the result is PASS and a new button appears.

Press **Open document**. It opens
`ORG - Training Checklist - Talewind CPA - v1.0.docx`. Read it. The structure,
the section titles and any clause text come from the Registrar's approved
configuration, not from you.

After a run, **Open report** opens `validation-report.json` and **Open folder**
opens the folder the files were written to. **Open document** shows only if a
document was made in this run. **Copy failures for the Governor** shows only if a
check failed.

## When a check fails

Each failing or warning check has its own block: its check ID, what is wrong,
where (`at:`), `expected:` and `actual:`, a `fix:` line, and a `rule:` line naming
the governing document.

1. Read the `fix:` lines. They are written as instructions the Governor can
   carry out.
2. Press **Copy failures for the Governor**.
3. Paste into the Governor chat and ask for a corrected AGR.
4. Copy the new JSON, save it over the old `.json` file (or as a new one), and
   select it.
5. Press **Check** again. Repeat until you get PASS.

If the window shows a red error message instead of a banner, there is no report:
the file was not found, the registry could not be read, or a configuration
problem exists. Fix what the message says and try again.

## Step 8: promote

A promotion build produces the controlled copy. It is stricter: warnings fail it,
and it needs the registry.

1. Under **2. ID registry (optional)**, press **Choose registry file** and pick
   the `.csv` export. For the sample, that is `registry-sample.csv` (repository
   clone: `fixtures\`; travel pack: `sample-agrs\`). Confirm the Document ID is
   registered, and that its registry status allows your file's status as the same
   or a legal next step. A first promotion normally starts from a registry row in
   Draft.
2. Ask the Governor for the same AGR with the mode set to promotion. The sample
   for this is `agr-sample-tcl-promotion.json` (repository clone: `fixtures\`;
   travel pack: `eval-harness\fixtures\`).
3. Select that file. The mode line now reads "Promotion: the controlled copy. A
   clean pass is required."
4. Press **Build**.

On a pass, the Registrar writes `promotion-payload.json`. It never changes the
registry itself. Applying that update to the PMC Registry is your signoff. Do it
by hand.

One wording trap: if a promotion fails for a missing registry, the failure text
says "--registry <path>". In this window that means press **Choose registry
file**.

## Where the files landed

Next to the AGR, in the same folder:

| File | When it appears |
|---|---|
| The `.docx` | Only on a pass (or pass with warnings, in draft mode) |
| `validation-report.json` | Every run |
| `generation-log.json` | Whenever rendering was attempted |
| `artifact-manifest.json` | Only when a document was produced |
| `promotion-payload.json` | Only on a passing promotion run |

Keep them together. The `.json` files are the evidence that the document was
produced under control.

## The Health check

At the bottom of the page, **Health check** runs the same self-check as
`registrar doctor`. It prints one line per check (OK, INFO, WARN or FAIL) and a
verdict: "Healthy: everything the Registrar needs is in place." or "Not healthy:
one or more checks failed. Send this list to whoever supports you." The verdict is
fixed text. The lines above it name the failing check. The check uses the registry
chosen in step 2, if there is one. Run it first if anything behaves oddly.

## If something goes wrong

- **A step fails.** Its message shows beside the controls and the window stays
  open.
- **A dialog titled "Registrar - unexpected failure" appears.** It names the
  error. The window stays open. Note the message and report it.
- **The window will not open, or opens blank.** Open PowerShell in the
  `runtime` folder, run `registrar.exe doctor` and read the `webview user data`
  line.

## If the window is unavailable

The command line does everything the window does and is fully supported:

    registrar validate <agr.json>
    registrar build <agr.json> --mode draft
    registrar build <agr.json> --mode promotion --registry <registry.csv>

It reads the same files, enforces the same rules and writes the same package.

> **One honest caveat.** The hand-off between the Governor and this
> walkthrough, producing the JSON in Copilot Studio, is the one step this
> walkthrough has not observed end to end. Everything from the saved `.json` file
> onward is exercised by the sample files above.
