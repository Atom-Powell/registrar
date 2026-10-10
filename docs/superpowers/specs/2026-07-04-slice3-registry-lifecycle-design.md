# Slice 3: ID Registry and Lifecycle Enforcement - Design

**Status:** Approved (design) - governance rulings ratified via the Governor against Spec v1.2 / ADR-001 / ADR-002
**Date:** 2026-07-04
**Predecessor:** Slice 2 (AGR semantic validation layers)

## Goal

Make the ID Registry authoritative in the validate pipeline. After schema
validation passes, verify that the AGR's Document ID exists in the registry and
that the AGR agrees with the registry on artifact type, classification, and
lifecycle state, enforcing that any requested approval-status transition is
legal. This realizes CLAUDE.md rule 5 ("The ID Registry is authoritative state")
and closes the two release-tier registry checks the catalog has been waiting on.

## Scope decisions (locked)

1. **Registry adapter + two enforcement layers.** Slice 3 implements a real
   `IIdRegistry` with a CSV-backed adapter and the two release-tier layers
   QA-SF-03 (existence + registry drift) and QA-SF-04 (lifecycle state and
   transition legality). The `promotion-payload.json` output (QA-SF-18 /
   QA-SPEC-18) is deferred to a later output-package slice; it is the only
   "assisted" operator-attestation item and belongs with the manifest /
   generation-log / promotion-mode machinery, none of which exists yet.
2. **CSV is transport, not the system of record.** The authoritative registry is
   the Registry sheet inside the Package Manifest and ID Registry (ORG-INT-PMC),
   per Spec v1.2 ("Registry sheet is the interim source of truth for all ORG
   Document IDs until the SharePoint list is stood up"). The engine ingests a CSV
   export of that sheet through the `IIdRegistry` interface. The future SharePoint
   List is the eventual system of record; an xlsx or SharePoint adapter drops in
   behind the same interface without touching the layers.
3. **Extended registry record.** The record carries
   `documentId, artifactType, classification, approvalStatus, version`. The real
   registry is a governance surface (~12 columns including Client, Engagement,
   owner); the adapter reads the subset it enforces and ignores the rest.

## Authority model

- **System of record (current):** the PMC (ORG-INT-PMC) Registry sheet. The
  Registrar (Registry Owner) owns it; the engine never mints IDs.
- **System of record (future):** the SharePoint List, behind the same interface.
- **Transport:** a CSV export of the PMC Registry sheet, supplied to the engine
  per run.
- **Not authoritative for Document IDs:** the AI Asset Inventory. It is an
  Operational-layer asset-tracking artifact, structurally separate from the
  Document Registry per ADR-002's layered model. Slice 3 does not read it.

## Non-goals (deferred, not silently dropped)

- **QA-SF-18 / QA-SPEC-18 promotion-payload output** -> later output-package
  slice: emitting `promotion-payload.json` (registry-update payload + repository
  target) on a passing promotion run, plus the assisted post-update verification
  and operator attestation line.
- **xlsx and SharePoint adapters** -> future: only the CSV adapter is built now.
  The interface is defined so they slot in later.
- **The tracked Slice 2 Minor findings** (see `.superpowers/sdd/progress.md`).

## Architecture

The Slice 2 seam already anticipates this: `SemanticContext` carries an
`IIdRegistry?` (null until now) and the empty `IIdRegistry` marker exists. Slice 3
gives the interface real shape and adds two release-tier layers that consume it.

### New and changed components

- **`IIdRegistry`** (replace the empty marker, `src/Registrar.Validation/`):
  `RegistryRecord? Lookup(string documentId)` - returns the authoritative record
  for a Document ID, or null if absent.
- **`RegistryRecord`** (`src/Registrar.Core/Model/`): a record with
  `DocumentId, ArtifactType, Classification, ApprovalStatus, Version`.
- **`CsvIdRegistry : IIdRegistry`** (`src/Registrar.Validation/`): parses a CSV
  export and indexes records by `documentId`. Column mapping is by header name,
  case-insensitive, tolerant of the registry's wider column set: it requires
  `documentId, artifactType, classification, approvalStatus, version` and ignores
  any others. A duplicate `documentId` or a missing required column is a load
  error surfaced as a blocking RegistrySyncError (fail closed - the engine never
  proceeds on an ambiguous or malformed authoritative state).
- **`RegistryLoader`** (`src/Registrar.Validation/`): reads the CSV from the path
  the CLI passes and constructs a `CsvIdRegistry`. Absence of a path yields no
  registry (null), handled per the mode rules below.
- **`ConfigBundle` extension** (`src/Registrar.Core/Config/`): additionally load
  `vocabularies.json`'s `approvalStatus.values` (the controlled status set) and
  `approvalStatus.transitions` (the transition map). Slice 2 only loaded the
  review intervals. The lifecycle model stays fully config-driven; the engine
  holds no embedded transition logic.
- **`RegistryLayer`** (QA-SF-03, RegistrySyncError, release tier): the Document ID
  must exist in the registry, and the registry's recorded `artifactType` and
  `classification` must agree with the AGR. Drift on either is a blocking
  RegistrySyncError. `version` is carried for context but is not a drift check -
  it legitimately changes across versions of the same persistent Document ID.
- **`LifecycleLayer`** (QA-SF-04, LifecycleTransitionError, release tier):
  validates the AGR's `approvalStatus` against the registry's current status and
  the transition map (see Lifecycle semantics below).
- **`ValidationService`**: when a registry is supplied, build the
  `SemanticContext` with it and run the two release-tier layers after the schema
  gate, alongside the Slice 2 layers. When it is absent, apply the mode rules.

### CLI

`registrar validate <agr.json> [--registry <path>]`. The `--registry` option
supplies the CSV export path. Exit codes are unchanged (0 pass /
pass-with-warnings, 1 fail, 2 usage/IO error). A `--registry` path that does not
exist or cannot be read is a usage/IO error (exit 2), symmetric with the AGR read
path.

### Data flow

```
registrar validate <agr.json> [--registry <path>]
  1. schema gate (unchanged): blocking schema fail -> schema checks only, STOP
  2. parse AgrDocument; load ConfigBundle (now incl. status set + transitions)
  3. if --registry supplied: RegistryLoader -> CsvIdRegistry (else registry=null)
  4. SemanticContext(agr, config, mode, registry)
  5. run Slice 2 layers + RegistryLayer + LifecycleLayer, aggregate checks[]
  6. OverallResultPolicy -> overall result
  7. write validation-report.json
```

## Registry presence and mode behavior

The two registry layers are release-tier promotion preconditions. Their behavior
depends on whether a registry was supplied and the AGR's `validationPolicy.mode`:

- **Registry supplied:** both layers run in either mode. Existence miss, drift,
  status disagreement, illegal transition, and terminal-state regeneration are
  all blocking fails regardless of mode - the registry is authoritative and a
  disagreement is never a mere warning.
- **Registry absent + promotion:** QA-SF-03 emits a blocking RegistrySyncError
  ("the ID Registry export is required to promote"), which fails the run.
  QA-SF-04 emits `skipped` - it cannot evaluate a transition without the
  registry, and emitting a registry-absence failure under LifecycleTransitionError
  would smear its error category. The single blocking fail is enough to refuse
  promotion. Fail closed: rule 5 refuses promotion on any registry disagreement,
  and "no registry" means the engine cannot verify authoritative state.
- **Registry absent + draft:** both layers emit a `skipped` result (a Check with
  `result: "skipped"`), so the report shows the checks did not run rather than
  silently passing. Draft is for iteration before the operator attaches the
  registry export. (`skipped` results are neither fail nor warn, so
  `OverallResultPolicy` ignores them.)

## Lifecycle semantics (QA-SF-04)

The AGR's `approvalStatus` is the status this generation targets; the registry
holds the current status. `LifecycleLayer` applies, in order:

1. **Registry status sanity.** The registry's `approvalStatus` must be one of the
   controlled `approvalStatus.values`. An unrecognized registry status is a
   blocking LifecycleTransitionError (the authoritative state is corrupt).
2. **Terminal-state block.** If the registry status is `Superseded` or `Retired`,
   regeneration is blocked with a blocking LifecycleTransitionError, no
   exceptions. A terminal artifact is a historical record; a refresh must issue a
   new Document ID or version (Registrar action), not regenerate the terminal one.
3. **Same-state regeneration (non-transition).** If `agr.approvalStatus` equals
   the registry status, this is a regeneration in place - allowed, and NOT
   validated against the transition map. Same-state pairs are deliberately absent
   from `vocabularies.json` transitions (no self-loops); the engine encodes the
   allowance directly rather than adding self-loops to the governed vocabulary.
4. **Transition legality.** Otherwise the transition `registryStatus ->
   agr.approvalStatus` must appear in the `transitions` map. If it does not, a
   blocking LifecycleTransitionError. The map is the sole authority; the engine
   adds no transitions of its own.

## Encoding notes (interpretations made explicit)

- **Drift maps to QA-SF-03, not QA-SF-04.** The governance review grouped
  artifactType/classification consistency under "QA-SF-04 enhanced." Because drift
  is a registry-sync concern, the engine emits it under QA-SF-03 with
  `RegistrySyncError`, preserving the catalog's checkId-to-errorCategory mapping;
  QA-SF-04 stays scoped to status alignment, transition legality, and the terminal
  block with `LifecycleTransitionError`. Same checks the review mandated, correct
  error categories.
- **`version` is not enforced for agreement.** Document IDs persist across
  versions (vocabularies.json), so the registry's `version` and the AGR's
  `version` legitimately differ during a version bump. Version is carried in the
  record for future checks, not compared here.
- **Registry integrity is fail-closed.** A malformed CSV, a missing required
  column, or a duplicate Document ID row is a blocking RegistrySyncError, never a
  best-effort parse.

## Testing

Per CLAUDE.md, every QA check ID gets a passing and a failing test named after
it. Coverage:

- **QA-SF-03:** pass (ID present, type + classification agree); fail on absence;
  fail on artifactType drift; fail on classification drift.
- **QA-SF-04:** pass on a legal transition (e.g. Draft -> In Review); pass on
  same-state regeneration; fail on an illegal transition; fail on a terminal
  (Superseded/Retired) registry status; fail on an unrecognized registry status.
- **Mode/presence:** registry absent + promotion -> QA-SF-03 blocking
  RegistrySyncError, QA-SF-04 `skipped`, run fails; registry absent + draft ->
  both layers `skipped`, run not failed by them.
- **Adapter:** `CsvIdRegistry` parses a fixture export and looks up by Document
  ID; a duplicate-ID or missing-column CSV surfaces a RegistrySyncError.
- **ConfigBundle:** the status set and transition map load from vocabularies.json.
- **CLI:** `--registry` wires through; a missing registry path exits 2.

### Fixtures

- A synthetic registry CSV export (a handful of rows spanning Draft / In Review /
  Approved / Superseded, covering the DS golden fixture's Document ID).
- Malformed-registry fixtures for the duplicate-ID and missing-column cases.
- AGR fixtures (or in-test mutations of the Slice 2 golden AGR) for drift,
  illegal transition, and terminal-state cases.

## Component boundaries summary

- `RegistryRecord` - the authoritative registry row the engine cares about;
  depends on nothing.
- `IIdRegistry` - the swappable registry seam; one method, `Lookup`.
- `CsvIdRegistry` / `RegistryLoader` - CSV ingestion; depends on the CSV export
  and `RegistryRecord`.
- `RegistryLayer` - existence + drift (QA-SF-03); depends on `SemanticContext`.
- `LifecycleLayer` - status + transition + terminal (QA-SF-04); depends on
  `SemanticContext` and the ConfigBundle transition map.
- `ValidationService` - orchestration; the only component that knows registry
  presence and the mode rules.
