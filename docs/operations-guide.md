# ORG AI Governance Program: Operations Guide

A technical operations manual for running the program end to end. It covers the three
agents, the operator workflow that ties them together, both engine surfaces (the operator
window and the CLI), the ID Registry, the artifact types, configuration, the offline
travel pack, and troubleshooting. It is a how-to reference. Every command, flag, check ID,
and field is drawn from the live repo.

Terminology used throughout: an **AGR** is an Artifact Generation Request (one JSON
object). The **engine** is the Registrar, reached either through the operator window
(`Registrar.App`) or the CLI (`registrar`); both drive the same validation and rendering
and neither makes a governance decision the other does not. A **deliverable** is a
generated DOCX plus its machine-readable output package.

If you have never used the Registrar, `docs/first-artifact-walkthrough.md` takes you from
an empty folder to a promotion build in the window. This guide is the wider reference
behind it.

---

## 1. Overview and architecture

### The four roles

The program separates collection, judgment, transport, and enforcement into four roles
that never merge:

- **The Steward** (a Copilot Studio declarative agent) gathers and structures engagement
  facts into an Engagement Brief. It reads knowledge and talks; it makes no governance
  decisions and calls no systems.
- **The Governor** (a Copilot Studio declarative agent) makes the governance decisions:
  findings, risk classification, clause selection, narrative content, approval-status
  requests, GO/NO-GO. It emits an AGR. It reads knowledge and talks; it never renders a
  document and never calls the engine.
- **The operator** (a human) is the transport and the control point. The operator carries
  the brief from the Steward to the Governor, saves the AGR, runs the engine, reviews the
  validation report, and pastes failures back to the Governor. This human checkpoint is a
  deliberate control, not a step to automate away.
- **The Registrar** (the engine) validates every field and renders the DOCX. It makes no
  governance decision and trusts no input; it re-validates everything and fails closed on
  any violation.

### The pipeline

    Steward            Operator         Governor          Operator        Registrar
    gather facts  -->  carry brief -->  decide, emit -->  save AGR,  -->  validate,
    -> brief                            the AGR           run engine      render, enforce

The Steward and the Governor cannot talk to each other or to the engine. The operator is
the only connection between stages. There are no API or flow connections anywhere.

### Non-negotiable engine properties

These are operating facts, not preferences. Do not design around them:

1. **Fail closed.** Any controlled-document violation blocks the deliverable. There is no
   partial output.
2. **Zero LLM calls at runtime.** The engine is deterministic. The same AGR plus the same
   config always produces the same bytes. No model runs inside the generation path.
3. **Configuration over code.** Artifact structure, vocabularies, clauses, placeholders,
   style, and QA checks are versioned data in `/config` and `/schemas`. Changing a
   template is a config release, not a code change.
4. **Clauses by reference only.** The AGR names a clause by ID; the engine inserts the
   canonical text from `clause-library.json` and proves it by SHA-256. Governor-supplied
   clause prose is ignored by design.
5. **The ID Registry is authoritative.** The engine verifies the Document ID and its state
   against the registry before promoting, and refuses on any disagreement. The engine
   never mints IDs.
6. **No invented structure.** An artifact type with no defined section schema refuses
   generation. The engine does not improvise sections.

---

## 2. The end-to-end workflow

This is the happy path, start to finish: what happens at each step and why. It does not
name a surface, because the same seven steps run whether you work in the operator window
or on the command line. Section 3 is the reference for the window, section 4 for the CLI,
and sections 5 onward are the reference for everything both surfaces share.

Steps 1 and 2 are the Governor path, in which an AI agent supplies the content and you
carry it across. Steps 3 through 7 are the same on either surface.

### Step 1: Discovery (Steward -> brief)

Open the Steward. Describe the engagement in plain terms. The Steward asks for whatever
is missing and produces an Engagement Brief: a structured, labeled block naming the
client, tenant, tool, security owner, support contact, the artifacts requested and their
per-type facts, and metadata (registered Document ID, version, mode, effective date).
Any fact it could not obtain is marked `MISSING`. Copy the finished brief.

### Step 2: Generation (Governor -> AGR)

Open the Governor. Paste the brief and say "generate the [type] AGR from this brief." The
Governor makes the governance calls the brief does not contain (risk, clauses, approval
status) and replies with the AGR as one JSON object inside a fenced `json` block.

**Copy it with the code block's copy button.** That gives you the exact bytes. A mouse
selection gives you rendered markdown instead and rewrites the payload: markdown escaping
turns `[TOKEN]` into `\[TOKEN\]`, which is the entire placeholder vocabulary. Take the
reply exactly as it is and do not clean it up.

Paste it into a text editor and save it as a `.json` file in your working folder, for
example `ds-northwind.json`. In the window you then choose that file; on the command line
you pass its path.

### Step 3: Build a draft (Registrar)

Build in draft mode. Draft is the forgiving mode: the registry is optional (without one,
the registry check is skipped), and unresolved placeholder tokens and similar issues appear
as warnings rather than blocking failures, so the document still renders and you can review
it. If you do supply a registry, draft checks against it too, and a Document ID that is not
registered fails the build.

On a pass the engine writes the DOCX and the rest of the output package. On a failure it
writes no DOCX at all and returns a validation report listing every problem. There is
never a partial document: that is rule 1, fail closed.

### Step 4: Read the validation report

Every run writes `validation-report.json`, whether it passed or failed. `overallResult` is
`pass`, `pass-with-warnings`, or `fail`. Each entry in `checks[]` has a `checkId`, `layer`,
`severity`, `message`, `jsonPath`, `expected`, `actual`, `remediation`, and `ruleSource`.
Read the whole check, not just the message. Severity tells a blocking failure from a
warning.

In the window you rarely open this file: the result shows each failing or warning check
with its fix line, and **Open report** opens the file if you want it. The report is always
written, because it is the machine record and the thing you paste back to the Governor
(**Copy failures for the Governor** formats it for you).

### Step 5: Remediate (round-trip to the Governor)

For each blocking failure, paste the check back into the Governor. The `remediation` line
is written as an instruction the Governor can execute. The Governor corrects the named
field and re-emits the complete AGR. Rebuild. Repeat until the report passes. Do not
hand-edit the AGR to force a pass; that defeats the control.

### Step 6: Promote (the governed deliverable)

Once the draft passes and the artifact is ready to become official, build in promotion
mode against the registry. Promotion is the strict mode. It requires the ID Registry
export, the Document ID must already be registered there in a status from which the AGR's
own status is the same or a legal next step (a first promotion normally starts from a
registry row in Draft), and things that were warnings in draft become blocking: unresolved
placeholder tokens, internal-only notes, and warning callouts all fail here, because this
is the copy that reaches the client.

A promotion pass emits the full five-file package, including `promotion-payload.json`,
which records the intended registry update and the repository target. The engine does not
change the registry itself. You apply that update to the PMC Registry sheet and confirm
the new state, and that manual step is your signoff: a governance control, not a missing
feature.

### Step 7: Deliver

The output package is the deliverable plus its provenance. The DOCX goes to the client (or
into the controlled-documents repository at the path named in the promotion payload); the
JSON files are the machine-readable record of what was produced and validated.

---

## 3. The operator window (Registrar.App)

The window is a thin front end over the same engine as the CLI. It is a peer of the CLI,
not a wrapper around it: both drive the same validation and rendering, and neither can do
a governance thing the other cannot. The window does one job. It points the engine at an
AGR file you already have, runs it, and shows you the result.

Launch it with `runtime\RegistrarApp.cmd`, which starts `Registrar.App.exe`.

The window is one page with no navigation. You work down it from top to bottom.

### The page, top to bottom

**1. Your working folder.** Shows the folder you chose, or "No folder chosen yet." Press
**Choose folder** to pick one with the standard folder picker. This is the folder where you
save the Governor's AGR files.

**2. ID registry (optional).** Shows the registry file you chose, or "No registry chosen."
The hint under it reads: "Without one, the registry check is skipped in a draft and fails a
promotion build." Press **Choose registry file** to pick the registry export (a `.csv`).
Once one is chosen, **Clear** removes it. A promotion build needs a registry (see section 5).
The window remembers the registry you chose, and checks it in draft runs too. Press
**Clear** to draft without it. A draft check against the registry fails if the Document ID
is not registered.

**3. The file to check.** A list of the `.json` files in the working folder, with the files
you used most recently first. Select one with its radio button. The tool's own output files
(`validation-report.json`, `generation-log.json`, `artifact-manifest.json` and
`promotion-payload.json`) are hidden from this list so that you do not pick one by mistake.
If no folder is chosen, the list says "Choose a working folder to see its files, or add a
file." If the folder has no `.json` files, it says "No .json files in this folder yet.
Choose another folder, or add a file." **Add a file...** opens a file picker and selects the
file you pick, wherever it lives.

**The mode line.** Under the list, one line shows the mode the AGR declares. You cannot
change it in the window. It is one of:

- "Draft: a working copy. Warnings are allowed."
- "Promotion: the controlled copy. A clean pass is required."
- "Mode unknown. Press Check and the report will say what is wrong with this file."

To run the other mode, ask the Governor for an AGR that declares it.

**Check and Build.** **Check** validates only and writes the report. **Build** validates,
then renders the DOCX if the AGR passes. Both stay disabled until a file is selected. While
a run is in progress the window shows "Working..." and also locks the folder, registry, file
list and **Add a file...** controls.

### The result

After a run the page shows a banner reading PASS, PASS WITH WARNINGS or FAIL, with the
Document ID and the mode. Under it is a count of failing and warning checks, then one block
per failing or warning check:

- the check ID and what is wrong;
- `at:` where in the AGR it is;
- `expected:` and `actual:`;
- `fix:` an instruction you can paste to the Governor;
- `rule:` the document section that governs the check.

A one-line summary can follow the blocks, for example "no document (promotion requires a
clean pass)". A usage or IO problem that produces no report (a file not found, an
unreadable registry, a configuration problem) shows as a red error message instead of a
banner. Read it. It is usually a missing file or registry.

After a run, only the buttons that apply appear:

- **Open document**: only if a DOCX was produced in this run.
- **Open report**: opens `validation-report.json`.
- **Open folder**: opens the folder the files were written to.
- **Copy failures for the Governor**: only if a check failed. It puts formatted text on the
  clipboard. You paste it into the Governor chat, which is step 5 of the workflow.

### Where the files land

The window writes next to the AGR, in the AGR's own folder, exactly as the CLI does. There
is no per-artifact subfolder. The package is flat:

- `validation-report.json`: always.
- `generation-log.json`: when rendering was attempted.
- The DOCX: only on a pass, or on a pass-with-warnings in draft mode.
- `artifact-manifest.json`: only when a DOCX was produced.
- `promotion-payload.json`: only on a passing promotion run.

### Health check

The **Health check** button at the bottom of the page runs the same self-check as
`registrar doctor`. It prints one line per check, marked OK, INFO, WARN or FAIL, and ends
with a verdict: "Healthy: everything the Registrar needs is in place." or "Not healthy: one
or more checks failed. Send this list to whoever supports you." The lines above the verdict
name the failing check. The check uses the registry chosen in step 2, if there is one.

### Settings

The window remembers the working folder, the recently used files and the registry path in
`%APPDATA%\registrar\settings.json`. The console menu (`registrar menu`) shares that file
for the folder and the recent files. It asks for the registry on every run and ignores the
one saved there.

### When something goes wrong

A step that fails shows its message beside the controls, and the window stays open. If
something unexpected escapes, a dialog titled "Registrar - unexpected failure" names the
error, and the window still stays open. If the window will not open, or opens blank, run
`registrar.exe doctor` and read the `webview user data` line. That check warns rather than
fails, because a machine that cannot open the window can still validate, build and promote
through the CLI.

One wording quirk: when a promotion fails for a missing registry, the failure text says
"--registry <path>". In the window that means "choose a registry file with **Choose
registry file**".

### What the window does not do

- It never edits an AGR. There is no editor, no draft file and no import step. The AGR JSON
  you saved is the file, and it is the source of truth.
- It cannot change the mode. The AGR declares it.
- It does not support drag and drop. Use **Add a file...**.
- It never changes the registry. On a passing promotion it writes
  `promotion-payload.json`, and applying that update to the PMC Registry sheet is your
  signoff.

---

## 4. The Registrar (CLI reference)

The CLI is not being retired and is not reduced as the window grows. It is the fallback
when the window will not open, the scripting surface, and a complete alternative for
anyone who prefers it. Like the window, it writes the output package next to the AGR.

### Running the engine

Three ways, all equivalent:

- **Self-contained executable** (no .NET install): `runtime/registrar.exe <verb> ...` from
  a published or travel-pack directory.
- **Travel pack**: the same exe, packaged with config, schemas, docs, and sample AGRs. See
  section 11.
- **From source**: `dotnet run --project src/Registrar.Cli -- <verb> ...`.

The engine resolves `config/`, `schemas/`, and `assets/` by walking up from the executable,
so keep those folders alongside the runtime.

### Verbs

**`doctor`** - runtime self-diagnostics. Checks config load, schema validity, placeholder
alignment, and clause hashes. Optionally checks a registry export.

    registrar doctor
    registrar doctor --registry registry.csv

**`validate`** - validate an AGR against `agr.schema.json` and the semantic layers, without
rendering. Writes a validation report.

    registrar validate <agr.json>
    registrar validate <agr.json> --registry registry.csv

**`build`** - validate and, on a pass, render the DOCX and the output package.

    registrar build <agr.json> --mode draft
    registrar build <agr.json> --mode promotion --registry registry.csv

  - `--mode draft|promotion`. Draft produces working output and does not require a registry.
    Promotion requires the registry, enforces the lifecycle transition, and emits the
    promotion payload. The `--mode` value must match the AGR's own `validationPolicy.mode`;
    a mismatch is a usage error.
  - `--registry <csv>`. The ID Registry export. Required for promotion; optional for draft.
    If supplied in draft, the registry checks (QA-SF-03, QA-SF-04) run there too.

**`test`** - run committed fixtures through the engine and compare to stored goldens.
Primarily for engine development, but useful to confirm a build is behaving.

    registrar test --all
    registrar test --fixture <name>
    registrar test --all --update      # regenerate the goldens (engine dev only)

**`menu`** - an interactive operator menu over validate and build with quality-of-life
features (an AGR picker, output-folder handling). Launch it with `registrar menu` or via
`Registrar.cmd` in a packaged runtime.

### Exit codes

- `0` - pass. The requested operation succeeded (validation passed, or the DOCX was
  produced).
- `1` - fail closed. A controlled-document violation blocked the deliverable. A validation
  report was written; no DOCX.
- `2` - usage or IO error. A bad flag, a missing file, an unreadable AGR, or a mode
  mismatch. Not a governance failure; fix the invocation.

### The output package

Up to five files, written next to the AGR. Which ones appear depends on the run:

- **Generated DOCX** - only on a pass (or pass-with-warnings in draft).
- **`validation-report.json`** - every run. Conforms to `validation-report.schema.json`.
- **`generation-log.json`** - whenever rendering was attempted. Records config versions
  loaded, rules applied, clause insertions, timing.
- **`artifact-manifest.json`** - only when a DOCX is produced. Resolved metadata, section
  inventory, dependency map, registry references, and the artifact SHA-256.
- **`promotion-payload.json`** - only on a promotion pass. The registry update payload and
  the repository target.

### Reading a validation report

Fields on every check:

- `checkId` - the QA check (see section 13 for the catalog).
- `layer` - the phase: `input`, `content`, `structural`, or `release`. Layers run in that
  order.
- `severity` - `blocking`, `warning`, or `info`. Only blocking stops a deliverable.
- `result` - `pass`, `fail`, or `skipped`.
- `message`, `jsonPath`, `expected`, `actual` - what failed and where in the AGR.
- `remediation` - an instruction the Governor can execute to fix it.
- `ruleSource` - the governing document and section.

The report also carries `overallResult` and `documentId`.

---

## 5. The ID Registry

### What it is

A CSV export of the ID Registry (the PMC Registry sheet). Registry Owner owns it.
Mechanically it is a plain file you point `--registry` at. The engine never writes to it
and never mints an ID; it only reads it to verify a Document ID exists in the expected
state.

### When it matters

- **Draft mode**: the registry is optional. With no registry, the registry check
  (QA-SF-03) is skipped and you can build drafts with no registry at all. With a registry
  supplied, the registry and lifecycle checks run in draft too: a Document ID that is not
  registered fails the build and produces no document.
- **Promotion mode**: the registry is required. Without it, promotion is blocked. With it,
  the Document ID must exist in a status from which the AGR's own status is the same or a
  legal next step (a first promotion normally starts from a registry row in Draft), and
  the AGR's artifact type and classification must match the registry row, or the build
  fails closed.

The registry is the boundary between a draft (working output, registry optional) and a
promotion (the controlled, registry-verified deliverable).

### CSV format

The parser requires exactly these five columns (a header row plus one row per registered
ID). A `client` column is allowed as an extra and is ignored.

    documentId,artifactType,classification,approvalStatus,version
    ORG-GOV-DS-016,DS,Client-Facing,Draft,v1.0
    ORG-GOV-AUP-004,AUP,Client-Facing,In Review,v1.0

- `documentId` - the full ID, matching the type's pattern (see section 6).
- `artifactType` - the code (DS, AUP, and so on). Must match the AGR.
- `classification` - Client-Facing, Controlled Signoff, or Internal Working Document. Must
  match the AGR.
- `approvalStatus` - the ID's current state: Draft, In Review, Approved, Superseded, or
  Retired. Drives the lifecycle transition at promotion.
- `version` - for example v1.0.

Duplicate Document IDs, a missing required column, or a malformed row cause a registry
format error (reported, not silently ignored).

### Lifecycle transitions

Promotion advances the ID one legal step from its registry state (QA-SF-04). From Draft
the only forward move is In Review; Draft to Approved is illegal. The Governor requests
the next legal state in the AGR; the engine checks it against the registry's current value.
These checks also run in draft mode when a registry is supplied, but a draft never emits
a promotion payload, so the registry status stays whatever it holds.

### Managing it (forward adoption)

You do not need to back-fill existing hand-drafted deliverables. Stand up one registry CSV,
add a row for each ID you issue going forward, and let the engine verify against it at
promotion. The registry becomes the single source of truth for what has been officially
issued and at what status.

---

## 6. The artifact types

Eight generation targets. Classification and review interval are fixed per type. The
required clauses and section schema are enforced by the engine.

| Code | Name | Classification | Review | ID pattern |
|---|---|---|---|---|
| DS | Deployment Summary | Client-Facing | 6 mo | `ORG-GOV-DS-NNN` |
| AUP | Acceptable Use Policy | Client-Facing | 6 mo | `ORG-GOV-AUP-NNN` |
| UQR | User Quick Reference | Client-Facing | 12 mo | `ORG-OPS-UQR-NNN` |
| TCL | Training Checklist | Client-Facing | 12 mo | `ORG-TRN-TCL-NNN` |
| TAK | Training Acknowledgment | Client-Facing | 12 mo | `ORG-TRN-TAK-NNN` |
| RAF | Risk Acceptance and Sign-off Form | Controlled Signoff | 6 mo | `ORG-RSK-RAF-NNN` |
| DCN | Delivery Cover Note | Client-Facing | 12 mo | `ORG-OPS-DCN-NNN` |
| PIG | Package Inventory and Executive Guide | Internal Working Document | 12 mo | `ORG-INT-PIG-NNN` |

(SCL, the Shared Clause Library reference, is a ninth generable type but is internal and
config-projected; it needs only metadata, no client discovery.)

### Per-type required facts, clauses, and sections

**DS - Deployment Summary.** Summarizes approved deployment posture, access, controls, and
validation.
- Facts: client org, tenant, approved user/pilot group, tool, approved purpose,
  out-of-scope uses, security owner, ORG support contact, date, version.
- Clauses: CL-006.
- Sections: Purpose > Deployment Scope > Access Model > Control Framework > Audit, Logging,
  and Discoverability Validation > Roles and Responsibilities > Client Acknowledgment >
  Version and Change Notes.

**AUP - Acceptable Use Policy.** Defines approved use, prohibited use, data handling, and
user obligations.
- Facts: client org, tool, approved users, support contact, security owner, data types in
  scope, data types out of scope, client policy references, date, version.
- Clauses: CL-001, CL-004, CL-005, CL-009.
- Sections: Purpose > Approved Use > Prohibited Use > Data Handling Requirements > Output
  Responsibility > Audit and Sensitive-Content Validation > User Acknowledgment >
  Escalation Requirements > Policy Review and Ownership.

**UQR - User Quick Reference.** Plain-language daily-use guide. Single page preferred.
- Facts: client and tool (Tier 1); no unique required placeholders. Optional daily-use
  specifics.
- Clauses: none.
- Sections: What This Tool Does > Good Uses > Do Not Use It For > Strong Prompt Pattern >
  Prompt Examples > When to Escalate > Support Contact.

**TCL - Training Checklist.** Preparation, access validation, coverage, completion
evidence. Uses the radio-option selection pattern.
- Facts: client and tool (Tier 1); optional session scope.
- Clauses: CL-003, CL-004.
- Sections: Pre-Training Preparation > Access Validation > Required Training Coverage >
  Demonstration Requirements > Training Completion Record > Open Follow-Up Items > Record
  Storage Location.

**TAK - Training Acknowledgment.** Per-user training completion and guardrail acceptance.
One per user if required.
- Facts: client org, tool, date, version; confirm one-per-user vs shared.
- Clauses: CL-001, CL-004, CL-005, CL-009.
- Sections: Purpose > Acknowledgment Statements > Policy Documents Reviewed >
  Responsibility Statement > User Acknowledgment and Signature.

**RAF - Risk Acceptance and Sign-off Form.** One approved deviation, its risks,
compensating controls, and signatures. Produced only when a deviation exists.
- Facts: client org, tenant, date, version; plus the deviation, its originating runbook,
  the client's reason, and who signs off.
- Clauses: CL-008.
- Sections: Identification > Description > Risks Acknowledged > Compensating Controls >
  Review and Sunset > Sign-off.

**DCN - Delivery Cover Note.** Introduces a package to the client and carries a suggested
delivery message. Client-facing; its internal counterpart is the PIG.
- Facts: client org, approved user/pilot group, security owner, ORG support contact, date,
  version; plus which package it introduces and any required review items.
- Clauses: CL-006, CL-007.
- Sections: Purpose > Package Contents and Required Actions > Required Client Review Items >
  Suggested Delivery Message > Version and Change Notes.

**PIG - Package Inventory and Executive Guide.** Internal inventory of what the engine can
produce. Metadata only, no client discovery.
- Clauses: none.
- Sections: Purpose > Deliverable Categories > Available Deliverables > Planned
  Deliverables > How a Type Becomes Available > Version and Change Notes.

---

## 7. Building blocks (reference)

### Block types

Ten render. A block whose type is outside this set, or whose schema field is wrong, fails
closed (QA-ENG-06 and QA-SCHEMA-01).

- `paragraph`, `h2`, `h3`, `bulletList`, `table` - ordinary prose blocks.
- `clauseInsertion` - inserts a canonical clause by ID. Carries only `clauseId`.
- `radioQuestion` - a selection item using the U+25CB glyph (TCL coverage items).
- `warnCallout` - a config-styled warn-red callout box. Gated out of client-facing output
  at promotion (QA-SF-09).
- `aggregationTable` - a config-sourced table. Carries only an `aggregationSource`
  (`clauseLibrary` is the only live source; `placeholderDictionary` is reserved). Renders
  the whole library from config. Used by the SCL.
- `copyReadyMessage` - a config-styled box for an operator-facing message copied verbatim
  to the client (the DCN's suggested delivery message). Scanned for token residue, unlike
  aggregationTable.

Table shape: a `table` block uses `columnHeaders` (array of strings) and `rows` (array of
rows, each row an array of strings), even for one row: `"rows": [["a","b","c"]]`.

### Clauses

Ten clauses in `clause-library.json`. The AGR references a clause by ID; the engine inserts
the canonical text and verifies it by SHA-256 (QA-SF-11). Clauses that contain a placeholder
token require that token in the placeholderMap.

| ID | Name | Contains token |
|---|---|---|
| CL-001 | First-draft rule | yes |
| CL-002 | Approved access boundary | yes |
| CL-003 | Least-content rule | no |
| CL-004 | Prohibited credential handling | no |
| CL-005 | Sensitive data boundary | no |
| CL-006 | Audit validation limitation | no |
| CL-007 | Restricted Content Discovery caution | no |
| CL-008 | Risk acceptance trigger | no |
| CL-009 | Escalation trigger | no |
| CL-010 | System of record limitation | yes |

### Placeholders

Nineteen canonical tokens in `placeholder-dictionary.json`. Use the exact spelling. Known
drift aliases are rejected with the canonical replacement named. Placeholders are resolved
only in the Document Control Header and the Configuration callout, from validated metadata;
body prose is not substitution-scanned, so write resolved values directly into section
text.

Canonical tokens: `[CLIENT ORGANIZATION NAME]`, `[CLIENT TENANT NAME / TENANT ID]`,
`[APPROVED USER GROUP OR PILOT GROUP]`, `[TOOL / CONNECTOR NAME]`, `[APPROVED PURPOSE]`,
`[OUT OF SCOPE USES]`, `[DESIGNATED SECURITY OWNER]`, `[ORG SUPPORT CONTACT]`,
`[SUPPORT CONTACT]`, `[APPROVED USERS]`, `[DATA TYPES IN SCOPE]`, `[DATA TYPES OUT OF SCOPE]`,
`[CLIENT POLICY REFERENCES]`, `[DATE]`, `[VERSION]`, `[PACKAGE]`, `[ARTIFACT]`,
`[DOCUMENT TITLE]`, `[CLIENT OR ENGAGEMENT NAME]`.

Common drift aliases (rejected): `[CLIENT]`, `[CUSTOMER]`, `[ORG NAME]`, `[CLIENT NAME]` ->
`[CLIENT ORGANIZATION NAME]`; `[TOOL]`, `[PRODUCT]` -> `[TOOL / CONNECTOR NAME]`; `[OWNER]`
-> `[DESIGNATED SECURITY OWNER]`.

### Character hygiene

Generated output must be US English with no em dashes, en dashes, curly or smart quotes,
ellipsis characters, emojis, non-breaking spaces, or decorative unicode. This is enforced
on input (QA-ENG-05) and after render (QA-SF-08). Use straight quotes, standard hyphens,
commas, periods, and colons everywhere, including placeholder values and the owner and
audience fields.

---

## 8. The Governor

### What it does and does not do

The Governor owns judgment: findings, risk classification, clause selection, narrative
content, approval-status requests, and GO/NO-GO. It emits an AGR and nothing else. It does
not render documents, mint Document IDs, approve anything itself, or call any system.

### Copilot Studio setup

- **Instructions**: paste the block between the two rules in `docs/governor-instructions.md`
  into the agent's instructions field. It fits the 8000-character cap.
- **Knowledge**: attach the four files generated into `docs/governor/` -
  `intake.md`, `drafter.md`, `payload.md`, `correction.md` (the artifact catalogue,
  section titles and clause selection, AGR shape and identity rules, and the check
  catalogue, in that order) - and the sample AGRs (`fixtures/agr-sample-*-draft.json`)
  so the agent grounds on known-good shapes.
- **Connections**: none. The operator is the transport.

### Using it

Discuss and ask questions freely, then ask for the artifact. The Governor replies with one
JSON object inside a fenced `json` block and nothing else: no preamble, nothing after the
closing fence. Save the reply exactly. When the engine returns failures, paste them back;
the Governor corrects the named field and re-emits the complete AGR.

### The eval harness

`scripts/eval-governor.ps1` scores model candidates by how few blocking checks their AGRs
incur. The harness never calls a model: a human runs each prompt in Copilot Studio and
saves the reply into `evals/runs/<model-slug>/`, then the script builds every reply through
the engine and writes a scorecard. Run `scripts/eval-governor.ps1 -SelfTest` before trusting
a round. Prompts are in `evals/prompts/`; prompt 04 is a refusal trap where any produced AGR
is a failure.

---

## 9. The Steward

### What it does and does not do

The Steward gathers and structures engagement facts into an Engagement Brief. It collects
what it is told; it never rules on whether a use is approved, what risk applies, which
clauses fit, which artifacts to include, the approval status, a Document ID, or GO/NO-GO.
Those are the Governor's (or, for IDs, Registry Owner's). It calls no system.

### Copilot Studio setup

- **Instructions**: paste the block from `docs/steward/steward-instructions.md`. It fits the
  8000-character cap with room to spare.
- **Knowledge**: attach the three files: `steward-discovery-contract.md` (what facts each
  type needs), `steward-engagement-brief-template.md` (the output shape), and
  `steward-boundary-card.md` (the facts-versus-decisions line).
- **Connections**: none.

### Using it

Describe the engagement. The Steward collects the engagement-level facts, then the
per-artifact facts for each deliverable, then the metadata, asking for exactly what is
missing. When discovery is complete it emits the brief in the template shape, marking any
gap as `MISSING`. Carry the brief to the Governor.

---

## 10. Configuration

The system's behavior lives in versioned data, not code. Changing a template, a clause, or
a check is a config edit and a config release.

### What lives where

`/config`:
- `artifact-types.json` - the types, their classification, review interval, ID pattern,
  required placeholders, required clauses, and section schemas.
- `clause-library.json` - the canonical clause text and its SHA-256.
- `placeholder-dictionary.json` - the canonical tokens and their drift aliases.
- `qa-checklist.json` - the QA check catalog (the check IDs and their rules).
- `style-rules.json` - visual identity, callout styles, the radio-option pattern, character
  lint, file naming.
- `repository-targets.json` - the controlled-documents path per category (used in the
  promotion payload).
- `vocabularies.json` - controlled values (classifications, approval statuses, review
  intervals).

`/schemas`:
- `agr.schema.json` - the AGR contract.
- `validation-report.schema.json`, `promotion-payload.schema.json`,
  `generation-log.schema.json`, `artifact-manifest.schema.json` - the output package
  schemas.

### Making a change

To add or change a clause, edit `clause-library.json` and recompute its SHA-256 (the engine
proves clause text against the recorded hash). To change a type's structure, edit its
section schema in `artifact-types.json`. To adjust wording rules or checks, edit
`qa-checklist.json` and the relevant validation layer. Authoring a section schema for a
type that has none is a documentation-owner decision, not an engine default.

### Operational note: the config hash coupling

Several config files (`agr.schema.json`, `artifact-types.json`, `style-rules.json`,
`vocabularies.json`, `clause-library.json`, `placeholder-dictionary.json`,
`repository-targets.json`) are hashed into every validation report's `configVersions`
block. Editing any of them changes its hash, so the committed fixture goldens go stale and
`registrar test` fails until they are regenerated with `registrar test --all --update`. A
golden refresh after a config edit is expected; confirm it is hash-only (only the changed
file's hash line moves, no rendered-document drift) before accepting it.

---

## 11. The travel pack

For running the program away from the repo, with no clone, no .NET install, and no network.

### What is in it

Built by `scripts/build-travel-pack.ps1` to `dist/registrar-travel/` and zipped to
`dist/registrar-travel.zip`:

- `runtime/` - the self-contained `registrar.exe` and `Registrar.cmd` launcher, plus
  `Registrar.App.exe` and `RegistrarApp.cmd`. The pack copies `publish.ps1`'s output
  wholesale, so it carries the operator window too.
- `config/`, `schemas/`, `assets/` - the editable engine data.
- `docs/` - the whole `docs/` tree, mirrored at the pack root. It comes whole rather than
  hand-picked because these files cite each other by repo-root-relative path, so any
  subset ships dead links: `OPERATOR.md` sends a first-time operator to
  `docs/first-artifact-walkthrough.md`, this guide names the Steward knowledge files,
  and `README.md` cites `DELIVERED.md`, `ROADMAP.md` and `tech-debt.md`, which cite the
  specs and plans in turn. It is 2 MB of markdown against a 100 MB runtime.
- `governor/` - the Governor's instructions, briefing, correction protocol, eval setup,
  the four generated per-role knowledge files, and the sample AGRs to attach as knowledge,
  plus `copilot-studio-setup.md` and `UPLOAD-TO-GOVERNOR.md`.
- `eval-harness/` - the eval script, path-adapted to run inside the pack, with prompts and
  fixtures.
- `sample-agrs/` - a few valid AGRs and a sample registry for CLI smoke tests.

The pack is about 105 MB zipped. Most of that is the runtime: `registrar.exe` is
self-contained and single-file at 74 MB, and `Registrar.App.exe` cannot be packed
single-file (WPF plus WebView2 does not support it), so the window ships as its own set
of published assemblies in the same folder.

### Building it

    pwsh ./scripts/build-travel-pack.ps1          # rebuilds the runtime from source
    pwsh ./scripts/build-travel-pack.ps1 -SkipBuild   # reuse an existing runtime

### Running it

From the extracted pack:

    .\runtime\registrar.exe doctor
    .\runtime\registrar.exe build sample-agrs\agr-sample-ds-draft.json --mode draft --registry sample-agrs\registry-sample.csv

`doctor` should report healthy; `build` should produce a DOCX next to the AGR. Rebuild the
pack after any engine or Governor-doc change so its exe and its guidance match the current
engine (a stale exe would validate more leniently than the current engine).

---

## 12. Troubleshooting

Common failures by check family, and what to do.

- **QA-SCHEMA-01 (schema)** - the AGR does not conform to `agr.schema.json`: a missing
  required field, a wrong enum, or an unknown key. The `jsonPath` names the spot. Correct
  the field in the Governor and re-emit. A reply with no recoverable JSON object in it at
  all lands here as malformed JSON; a fenced block or surrounding preamble does not, since
  the engine recovers the payload from inside it.
- **QA-ENG-06 (block type)** - a block type the engine does not render, or an
  `aggregationTable` with a non-live source. Replace it with a supported block.
- **QA-ENG-02 (section schema)** - section titles do not match the type's schema exactly and
  in order. Reproduce the section list from section 6 character for character.
- **QA-ENG-05 / QA-SF-08 (characters)** - a prohibited character in an input field or the
  rendered output. The message and actual name the exact codepoint and position. Remove the
  em dash, smart quote, or other disallowed glyph.
- **QA-ENG-04 / QA-SF-11 (clauses)** - a clause ID not in the library, a required token
  missing from the placeholderMap, or clause text that no longer matches its recorded hash.
  Reference an existing clause and supply its tokens; if the hash mismatches, reconcile
  `clause-library.json`.
- **QA-SPEC-07 / QA-ENG-07 (placeholders)** - an unresolved bracketed token left in body
  prose. Write the resolved value into the prose; body text is not substitution-scanned.
- **QA-SF-03 (registry)** - the Document ID is absent from the registry (in draft too, when
  a registry was supplied), or no registry was supplied for a promotion. Add the ID to the registry CSV, or supply `--registry` (in the
  window, press **Choose registry file**).
- **QA-SF-04 (lifecycle)** - the requested approval status is neither the same as the
  registry's current status nor a legal next step from it. Request a legal state (from a
  registry row in Draft, request In Review) or leave the status unchanged.
- **Registry format error** - a duplicate Document ID, a missing required column, or a
  malformed row in the CSV. Fix the file.
- **Exit code 2** - a usage or IO problem, not a governance failure: a bad flag, a missing
  file, or a `--mode` that disagrees with the AGR's own `validationPolicy.mode`. Fix the
  invocation.

### Window-specific problems

- **The file list is empty.** Either no working folder is chosen, or the folder has no
  `.json` files. The list says which. Press **Choose folder**, or save the Governor's AGR
  into the folder you chose, or press **Add a file...**.
- **The file you expect is missing from the list.** The window hides the tool's own output
  files (`validation-report.json`, `generation-log.json`, `artifact-manifest.json` and
  `promotion-payload.json`). If the file lives in another folder, press **Add a file...**
  and pick it. Drag and drop is not supported.
- **Check and Build are greyed out.** No file is selected yet, or a run is in progress
  ("Working..."). Select a file in step 3 and wait for the run to finish.
- **A red error message shows instead of a banner.** The run could not produce a report: a
  file that was not found, a registry that could not be read, or a configuration problem.
  Read the message. It is usually a missing file or registry, so check the paths shown in
  steps 1 to 3 and choose them again.
- **A promotion fails and the text says "--registry <path>".** That is the CLI wording. In
  the window, press **Choose registry file** in step 2 and pick the registry export.
- **The window opens blank.** Run `registrar.exe doctor` and read the `webview user data`
  line. That check warns rather than fails on purpose: a machine that cannot open the
  window can still validate, build and promote through the CLI, so this is a degraded
  surface rather than a broken install.
- **A dialog titled "Registrar - unexpected failure" appears.** Something unexpected
  escaped. The dialog names the error and the window stays open. Note the error text, press
  **Health check**, and run the same step through the CLI if you need to carry on.
- **Copy failures for the Governor shows an error.** A copy that worked shows "Copied the
  failures. Paste them into the Governor chat." beside the buttons. A copy that failed
  shows "The failures could not be copied: ..." followed by the reason. Press the button
  again. The same findings are in `validation-report.json`, which **Open report** opens.
- **The mode line says "Mode unknown."** The window could not read the mode from the AGR.
  Press **Check**; the report says what is wrong with the file.

General rule: do not work around a failing check or hand-edit the AGR to force a pass.
Correct the named field through the Governor and re-run. The check is the control.

---

## 13. Appendices

### A. QA check catalog

| Check | Layer | Enforces |
|---|---|---|
| QA-SCHEMA-01 | input | AGR conforms to agr.schema.json (required fields, enums, patterns). |
| QA-ENG-02 | input | Section titles match the type's section schema exactly and in order. |
| QA-ENG-03 | input | Document ID category and code match the artifact type. |
| QA-ENG-05 | input | Governor free-text and placeholder values are character-clean. |
| QA-ENG-06 | input | Every block type is one the engine renders; aggregationTable source is live. |
| QA-SF-05 | input | Review date is ISO 8601 and within the category default (or a logged override). |
| QA-ENG-01 | content | Outline-document anti-pattern scan (reject purely descriptive sections). |
| QA-ENG-04 | content | Every clauseInsertion references a defined clause with its required tokens. |
| QA-ENG-07 | content | No unresolved bracketed tokens in body prose. |
| QA-SF-07 | content | Every H1 has body content and required subsections. |
| QA-SF-08 | content | No em/en dashes, emojis, or decorative unicode in any rendered run. |
| QA-SF-09 | content | Zero warn callouts and zero internal-only notes in client-facing output at promotion. |
| QA-SF-10 | content | Selection items use the U+25CB glyph, consistent indentation, a descriptor line. |
| QA-SF-11 | content | clauseInsertion rendered text matches the canonical clause by SHA-256. |
| QA-SPEC-07 | content | All bracketed placeholders resolved or intentionally retained. |
| QA-SPEC-14 | content | Audit and evidence claims phrased as tenant-dependent, not guaranteed. |
| QA-SPEC-15 | content | Restricted Content Discovery language does not overclaim. |
| QA-SPEC-17 | content | Support, escalation, security owner, and record-storage fields present. |
| QA-SF-01 | structural | Header, engagement, footer, artifact, and version consistent across the doc. |
| QA-SF-02 | structural | Document Control Header banner present and both rows populated. |
| QA-SF-06 | structural | Configuration callout present with Artifact, Description, Audience, Settings, ID Pattern. |
| QA-SF-12 | structural | Output filename matches the naming convention and its version. |
| QA-SPEC-19 | structural | Artifact uses the v1.2 DCH, header table, callout, and footer standard. |
| QA-SF-03 | release | Document ID follows the pattern and exists in the registry (promotion). |
| QA-SF-04 | release | Approval status is a controlled value and a legal transition from registry state. |
| QA-SPEC-12 | release | RAF included only when a deviation exists. |
| QA-SPEC-16 | release | Approved users or pilot group identified before rollout materials are final. |
| QA-SPEC-18 | release | Package manifest and registry updated before final delivery (promotion payload). |

### B. Glossary

- **AGR** - Artifact Generation Request. The single JSON object the Governor emits and the
  engine consumes.
- **Brief** - the Engagement Brief the Steward produces; structured facts the Governor turns
  into an AGR.
- **DCH** - Document Control Header. The controlled metadata banner on every artifact.
- **Draft mode** - a build that produces working output. The registry is optional; if one
  is supplied it is checked, and without one the registry check is skipped.
- **Promotion mode** - a build that verifies the registry, advances the ID one lifecycle
  step, and emits the promotion payload.
- **Output package** - the DOCX plus the machine-readable JSON files produced by a run.
- **Clause** - canonical governed text inserted by reference and proven by SHA-256.
- **Placeholder** - a canonical bracketed token resolved from validated metadata.

### C. Quick command reference

    # the operator window
    runtime\RegistrarApp.cmd

    # the interactive CLI menu
    runtime\Registrar.cmd

    # health check
    registrar doctor
    registrar doctor --registry registry.csv

    # validate only (no render)
    registrar validate agr.json
    registrar validate agr.json --registry registry.csv

    # build a draft (registry optional)
    registrar build agr.json --mode draft

    # promote (registry required)
    registrar build agr.json --mode promotion --registry registry.csv

    # interactive operator menu
    registrar menu

    # run fixtures against goldens
    registrar test --all
    registrar test --fixture agr-sample-ds-draft

    exit 0 = pass    exit 1 = fail closed    exit 2 = usage or IO error
