# Governance Library Normalization Tool - Design

Date: 2026-08-01
Status: Approved for planning
Owner: Adam Powell

## 1. Purpose

The ORG AI Governance Program SharePoint library has accumulated structural
drift. This document designs a tool that normalizes the library root back to a
defined baseline and reports on drift across the rest of the library, without
ever making a governance decision or moving a file a human has not approved.

The tool is not part of the Registrar engine. The engine renders and enforces;
it never touches the document library. This tool operates on a locally synced
copy of the library and is run by the operator.

## 2. Scope

In scope:

- Read-only inventory of a locally synced governance library.
- A normalization workbook proposing dispositions for root-level files.
- Execution of moves and renames that a human has approved in that workbook.
- Verification and undo of an executed run.
- A read-only drift report over the entire library.

Out of scope:

- Reorganizing files already placed inside approved folders. The Governance
  Root Folder Normalization SOP scopes normalization to root-level files and
  states it "does not apply to files already placed in approved folders unless
  they are intentionally included in a later cleanup pass." The full-library
  report produces the evidence such a pass would need, but proposes no moves.
- Any write to the ID Registry. The registry is a read-only input.
- Any modification to the Registrar engine.
- Deletion of any file or folder, under any mode.

## 3. Governing inputs

The baseline was obtained from the Governor agent on 2026-08-01, with a
follow-up round the same day resolving six internal contradictions. Both
responses are the source of the rules encoded here. Key determinations:

**ADR-001 Repository Architecture** is Approved (2026-07-03, approved by Adam
Powell). It establishes Master Development Workspace as the development branch,
Controlled Documents as the release repository, and Architecture Decision
Records as repository history. It does not define sub-folder names within
Controlled Documents, a target folder for Retired files, a quarantine folder,
or a rule for engine output files.

**Placement is status-first.** Deciding fields in priority order are
approvalStatus, then idCategory. artifactType was removed from the deciding
fields in the follow-up round: it validates category-code pairing legality and
AGR-to-registry agreement, and does not route folders.

**Identification is blocked.** Placement depends on approvalStatus and
idCategory, which live in the ID Registry keyed by Document ID. No mandatory
filename convention is published, and no controlled document authorizes an
automated process to open and parse DOCX content to read the Document ID from
the Document Control Header. The Governor named two possible resolutions, both
owner decisions: publish a filename convention embedding the Document ID, or
formally authorize a DOCX content parsing method.

This blocker is the central constraint on the design. It is why the tool
proposes rather than decides, and why the approval workbook is the architecture
rather than a convenience.

**The SOP defines the sanctioned workflow.** The Governance Root Folder
Normalization SOP (How to apply bulk changes to directory.docx) prescribes:
extract root inventory, prepare a normalization workbook, classify with
Copilot, validate the classification, then move files in controlled batches.
Classify first, move second, validate third. Its stated escalation conditions
require stopping when file purpose, ownership, sensitivity, or destination
cannot be determined with reasonable confidence.

**Four dispositions are undefined and resolve to inaction:** the target folder
for Retired files, whether non-controlled files belong in the library at all,
the disposition of unrecognized files, and the physical filename convention.
The Governor confirmed the required interim behavior for each is to leave the
file in place, log it, and escalate.

## 4. Architecture

### 4.1 Zones

The library divides into two zones with different permissions.

- **Normalization zone**: files at the library root. Movable, but only through
  the approval gate. This is the scope the SOP authorizes.
- **Observation zone**: everything already inside folders. Read-only in every
  mode. Reported on, never touched.

### 4.2 Pipeline

    Inventory  ->  inventory-<stamp>.csv                 read-only sweep
                        |
    Plan       ->  GOV-ROOT-NORMALIZATION-RUN-v1.xlsx    proposes only
                        |
                   [ human reviews and approves rows ]   the gate
                        |
    Apply      ->  executes approved rows only           writes undo journal
                        |
    Verify     ->  post-move export diffed against plan

Report runs independently of this pipeline as a read-only pass over the whole
library.

### 4.3 Invariants

These are structural properties of the tool, not configurable settings.

1. It never deletes a file or folder in any mode. The SOP requires governance
   repository owner approval for deletion during rollback, so no delete code
   path exists to be misused.
2. It never moves or renames a row not explicitly marked approved in the
   workbook.
3. doNotTouch in config overrides the workbook. A protected path is refused
   even when an approved row orders the change.
4. Renames pass through the same approval gate as moves.
5. It never writes to the ID Registry.
6. Every Apply run writes an undo journal of source and destination pairs.
7. It refuses to act on any rule whose status is notDefined. Affected rows
   become Escalate and are never auto-planned.
8. Read-only is the default. Apply requires an explicit flag and a path to an
   approved workbook.

### 4.4 Cloud placeholder handling

Files in a synced library are frequently cloud-only placeholders. Reading one
triggers a download, so a naive sweep can hydrate an entire library onto local
disk. Inventory therefore enumerates file metadata and attributes only and
never opens file content.

The identification constraint in section 3 enforces the same discipline from
the governance side: since parsing DOCX content is not authorized, the tool has
no reason to open a file at all.

## 5. Baseline configuration

The baseline lives at `config/library-baseline.json` as versioned data, not in
script code. It contains folder structure and rules only. It carries no client
data and no machine-specific paths.

Every rule carries a `status` that drives tool behavior:

| status      | Behavior                                                        |
|-------------|-----------------------------------------------------------------|
| governed    | May propose a disposition for matching rows                     |
| unconfirmed | Must verify the path against the live library first; a mismatch produces an Escalate row |
| notDefined  | Never proposes. Row becomes Escalate, carrying the reason and the named decider |
| blocked     | Capability disabled entirely                                    |

Shape:

    {
      "baselineVersion": "1.0",
      "sourceOfTruth": "Governor baseline 2026-08-01 plus follow-up 2026-08-01",
      "nestingOrder": "category-first",

      "folderTree": [
        { "path": "Controlled Documents/Governance", "createIfEmpty": true,
          "status": "governed", "ruleSource": "ADR-002 Governance Layer" },
        { "path": "Archive", "createIfEmpty": true, "status": "unconfirmed",
          "note": "Spec Section 15 says _Archive; deployed library says Archive.",
          "ruleSource": "ORG-MST-SPEC-001 Section 15 vs observed" }
      ],

      "placementRules": {
        "decidingFieldsInPriorityOrder": ["approvalStatus", "idCategory"],
        "mappings": [
          { "match": { "approvalStatus": "Approved", "idCategory": "GOV" },
            "targetPath": "Controlled Documents/Governance",
            "status": "governed", "ruleSource": "ADR-002" }
        ]
      },

      "dispositions": {
        "retired":       { "action": "leaveInPlace", "status": "notDefined",
                           "decider": "ADR-001 successor" },
        "nonControlled": { "action": "leaveInPlace", "status": "notDefined",
                           "decider": "ADR or operational runbook" },
        "unrecognized":  { "action": "escalate",     "status": "notDefined",
                           "decider": "Registry Owner" }
      },

      "fileNaming": { "status": "notDefined", "template": null },

      "identification": {
        "method": "none", "status": "blocked",
        "note": "No governed automated method. Identity is resolved by a human in the workbook.",
        "ruleSource": "Governor follow-up answer 7"
      },

      "doNotTouch": [
        { "pattern": "Architecture Decision Records/*",
          "reason": "Repository history; reorganization destroys design rationale traceability.",
          "ruleSource": "ADR-003 Governor Agent Design" },
        { "pattern": "Document Registry/ORG-ID-Registry.xlsx",
          "reason": "Authoritative state. No automated process may modify it.",
          "ruleSource": "ORG-ID-Registry.xlsx Instructions Sections 1 and 2" },
        { "pattern": "Templates/Approved/*",
          "reason": "Controlled artifacts; version changes require a governed registry transition.",
          "ruleSource": "ORG-MST-SPEC-001 Section 12" },
        { "pattern": "Document Registry/Blockers/*",
          "reason": "Active governance blocker records.",
          "ruleSource": "Observed; referenced in PMC Registry Sheet Status Inquiry.docx" }
      ]
    }

Corrections from the follow-up round that are baked into the baseline:

- artifactType removed from decidingFieldsInPriorityOrder.
- 00_Master_Base and its sibling folders excluded. They belong to a separate
  SharePoint library named Governed AI Rollout, not to this library.
- Archive marked unconfirmed so the tool resolves the name empirically rather
  than trusting either the spec text or the observed deployment.
- Retired, non-controlled, and unrecognized files resolve to leave-in-place or
  escalate.

The library root is a runtime parameter, never config. Repository paths do not
belong in versioned data, and the OneDrive path differs per machine. The
interactive entry point offers a folder picker that sets it.

### 5.1 Cross-check against repository-targets.json

The tool compares baseline paths against `config/repository-targets.json` and
warns on divergence. Two divergences are known and currently unresolved:

| Category | repository-targets.json                    | Governor baseline           |
|----------|--------------------------------------------|-----------------------------|
| OPS      | Controlled Documents/Operational Artifacts | Controlled Documents/Operational |
| MST      | Controlled Documents/Master Templates      | Templates/Approved          |

Neither gates this tool. Templates/Approved is doNotTouch, and the tool never
files promoted documents; that is the operator's action driven by
promotion-payload.json. These divergences are a repository-targets.json
question for the owner to settle on an independent timeline. The warning exists
so the disagreement stays visible rather than silent.

## 6. Modes

### 6.1 Inventory

Read-only sweep of the library root. Emits `inventory-<stamp>.csv` with the
five columns the SOP names (Name, Folder path, Modified, Modified By, File
Type) plus RelativePath, SizeBytes, IsPlaceholder, and Zone.

Modified By does not exist in the local filesystem; NTFS does not track it.
The tool therefore accepts the SharePoint All Documents export as an optional
input and joins it to the local sweep on relative path. This matches SOP Step 1,
whose output is that export.

The join also surfaces sync divergence. Rows present in the export but absent
on disk are cloud-only or unsynced. Rows on disk but absent from the export are
pending upload. Both are reported before any move is planned, because moving a
file mid-sync produces conflict copies.

### 6.2 Plan

Consumes the inventory, the baseline, and optionally an ID Registry export.
Produces `GOV-ROOT-NORMALIZATION-RUN-v1.xlsx` containing a table named
`GovFiles`. Both names are taken from the SOP.

Classification behavior follows directly from the identification constraint.
Answer 7 blocks parsing DOCX content for a Document ID. It does not restrict
reading a filename. Therefore:

- If the filename contains a valid Document ID matching `ORG-[A-Z]{3}-[A-Z]+-\d+`,
  the tool joins to the registry export, reads approvalStatus and category, and
  proposes a target with a cited rule source.
- Otherwise, Suggested Folder is left blank and the row is marked with whatever
  `dispositions.unrecognized.action` specifies in the baseline config. The tool
  proposes nothing. Classification of these rows is SOP Step 3, which is
  Copilot-assisted and human-owned. The shipped config sets that action to
  `escalate`, so unidentified rows currently read as Escalate; changing the one
  config value changes the label without a code change. When no registry export
  is supplied at all, every row is Review Required regardless, because identity
  was never attempted.

Workbook columns:

| Column                                              | Filled by     |
|-----------------------------------------------------|---------------|
| Name, Folder path, Modified, Modified By, File Type | tool          |
| Suggested Folder                                    | tool or blank |
| Folder Exists                                       | tool          |
| Action Required                                     | tool          |
| Rule Source                                         | tool          |
| Notes                                               | tool, then human |
| Approved                                            | human         |
| Prefix, Suffix, New Name                            | human         |

Action Required values: Move to Existing Folder, Create Folder and Move, Review
Required, Escalate, Leave in Place.

The ID Registry export is optional. Without it the tool cannot resolve
approvalStatus for any file, so no row receives a proposed target and every row
is marked Review Required. Plan still runs and still produces a usable workbook
for human classification; it simply proposes nothing.

A file whose Document ID resolves to a status with a notDefined disposition,
such as Retired, is marked Leave in Place with its rule source, even though the
tool identified it successfully. Identification does not imply a move.

### 6.3 Apply

Reads the approved workbook and executes only what it states, in the order the
SOP prescribes: existing-folder moves as the first batch, then create-folder-
and-move rows.

Per-row pre-flight before any move:

- Source still exists at the recorded path with the timestamp recorded at Plan
  time. A changed timestamp means the plan's assumption is stale; the row is
  skipped and flagged.
- Destination does not already contain a file of that name. The tool never
  overwrites.
- Path is not covered by doNotTouch, regardless of workbook content.
- Computed destination path length is within limits.

Moves execute in batches with a settle pause between them so the sync client is
not asked to reconcile a large number of moves at once.

### 6.4 Verify

Re-inventories the root, diffs against the plan, and confirms every approved
row landed and nothing unapproved changed. Produces the follow-up export the
SOP validation section requires.

### 6.5 Report

Read-only pass over the entire library. Flags:

- Baseline folders that are missing and marked createIfEmpty.
- Folders present that are absent from the baseline.
- Files whose filename-embedded Document ID disagrees with the folder they
  occupy, per the placement rules.
- Registry rows with no corresponding file, and files whose ID is absent from
  the registry, when a registry export is supplied.
- Divergence between the baseline and repository-targets.json.
- The empirical resolution of Archive versus _Archive.

Output is markdown plus CSV. The report proposes no moves.

### 6.6 Run artifacts

All outputs for a run land in `<OutputRoot>/runs/<stamp>/`: the inventory, any
SharePoint export consumed, the workbook, the undo journal, the run log, and
the verification export. This is the evidence package the SOP requires be
retained.

OutputRoot is a runtime parameter and defaults to a location outside the
library. The tool refuses to write run artifacts anywhere inside the library
root, since doing so would sync the evidence package into SharePoint and add to
the drift the tool exists to reduce.

## 7. Tagging

Two ways to populate the Prefix and Suffix columns.

**Derived tag.** For rows where identity is known, the tool fills Prefix with
the file's Document ID. This is the mechanism that resolves the identification
blocker. The Governor named a published filename convention embedding the
Document ID as one of the two possible resolutions. A human resolves identity
once in the workbook; from that point the file is machine-identifiable in every
future run.

**Custom tag.** The operator supplies a literal prefix or suffix and a folder
scope, and the tool populates the column for every row in scope. Review and
approval in the workbook still gate execution.

Prefix sorts the library by Document ID. Suffix preserves sorting by title.

### 7.1 Costs of renaming

- Renaming changes the SharePoint URL. Existing links break. Version history
  survives.
- Modified and Modified By update on rename, which erases the record of who
  last substantively worked on each file. This argues for a single deliberate
  pass rather than repeated ones.
- Windows caps paths at 260 characters and SharePoint caps the full URL at 400.
  A Document ID prefix adds roughly seventeen characters. Plan pre-flights every
  computed path and refuses to propose a rename that would exceed the limit,
  flagging the row instead.

### 7.2 Gate

fileNaming ships with status notDefined, so per invariant 7 the rename path is
inert on delivery. Setting the convention in config with status governed
enables it, with no code change. A supporting ADR or spec revision is
recommended so the convention is documented rather than only configured. The
sequencing is the owner's decision.

## 8. Error handling

Exit 0 healthy, exit 1 failed, matching `registrar doctor`.

**Hard stop, nothing runs:** invalid or missing baseline config; library root
missing; malformed registry export (empty, missing a required column, blank
Document ID, duplicate Document ID, deliberately mirroring the failure set
CsvIdRegistry already enforces); workbook missing required columns; any
approved row with a blank Suggested Folder, which SOP validation requires be
resolved before movement.

**Flag the row, continue the run:** source changed since Plan; destination
occupied; file locked by another process (retry twice with backoff, then skip);
path length exceeded; file matches a sync conflict-copy pattern. Conflict
copies are detected and flagged specifically and never moved.

**Partial failure.** The undo journal is append-only, written and flushed
before each move rather than assembled in memory and saved at the end. A killed
process, power loss, or thrown batch therefore leaves an on-disk journal that
is accurate for every completed move. There is no window in which a file has
moved but nothing has recorded it.

Undo is a reverse run over that journal, subject to the same invariants. It
never deletes, never overwrites, and flags any file whose original location is
now occupied.

**Sync-state pre-flight** is best-effort. Before Apply the tool looks for
partial and temporary files and recent modification churn in the target area
and warns if the library appears to be mid-sync. It cannot authoritatively know
the sync client's state. Batching and settle pauses are the substantive
mitigation.

## 9. Testing

Pester 6 is available locally and CI already runs on windows-latest, so a
Pester step runs alongside `dotnet test` without new infrastructure.

Every test builds a synthetic tree in a temporary directory and tears it down
afterward. No test targets a real library. This preserves the repository rule
that CI uses synthetic fixtures only.

Following the engine convention of naming tests after the rule they cover, each
invariant from section 4.3 gets a named test:

| Invariant                | Test                                                       |
|--------------------------|------------------------------------------------------------|
| Never deletes            | Apply a full plan; assert no path disappears from the tree |
| doNotTouch beats workbook| Workbook orders a protected file moved; assert refusal      |
| Approval required        | Rows without Approved never move                            |
| notDefined never acts    | Retired and unrecognized rows take their configured disposition, never a move |
| Never overwrites         | Destination collision skips the row; both files survive     |
| Stale row detection      | Source touched after Plan is skipped                        |
| Journal accuracy         | Throw injected mid-batch; journal matches filesystem exactly|
| Undo restores            | Reverse run returns the tree to its original state          |
| Path length              | Over-limit rename is refused at Plan, not Apply             |
| Hard stops               | Blank Suggested Folder and malformed registry each exit 1   |
| Report is read-only      | Tree hash identical before and after                        |

Two additional areas:

**Filename ID extraction** is tested both ways: valid IDs matched and joined to
the registry, and malformed IDs such as an illegal category-code pairing
rejected rather than silently accepted. This is the only point at which the
tool makes an identity claim, so a false positive here would misroute a file.

**No content reads** is tested by proxy. Inventory a fixture containing a file
locked against reading and assert the sweep still succeeds. Any code path that
opened file content would fail this test. This is the guard against hydrating a
cloud-synced library.

**Hygiene.** The script and library-baseline.json are held to the repository
escapes-only rule that SourceHygieneTests enforces on C# source, with
`scripts/normalize-hygiene.ps1` as the existing fix.

## 10. Open decisions for the owner

None of these block delivery. The tool functions with all four open, treating
each as inaction.

1. Target folder for Retired files. Currently leave-in-place and escalate.
2. Whether non-controlled and engine output files belong in the library.
   Currently leave-in-place and escalate.
3. Disposition and quarantine folder for unrecognized files. Currently escalate.
4. Physical filename convention. Gates the rename and tagging feature, which
   ships inert until set.

Two further items are independent of this tool but were surfaced by the
baseline work:

5. The OPS and MST path divergence between repository-targets.json and the
   Governor baseline (section 5.1).
6. The Governor's model of the engine included the engine depositing promoted
   artifacts into Templates/Approved. The engine does not write to the library;
   it emits promotion-payload.json naming the target with
   RequiresOperatorSignoff true, and the operator files the document. Worth
   correcting in the Governor's knowledge set so future answers do not build on
   it.

## 11. References

- Governance Root Folder Normalization SOP (How to apply bulk changes to
  directory.docx), retrieved 2026-08-01 via the Governor.
- ADR-001 Repository Architecture, Approved 2026-07-03.
- ADR-002 AI Governance Program Structure.
- ADR-003 Governor Agent Design.
- ORG-MST-SPEC-001 v1.2, Sections 5, 11, 12, 15.
- ORG-ID-Registry.xlsx, Sections 1, 2, 3, 5, 7, 8.
- config/repository-targets.json, config/vocabularies.json,
  config/artifact-types.json in this repository.
