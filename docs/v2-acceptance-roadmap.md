# Generation Engine v2 - Roadmap (v2 Acceptance)

> **Status:** Adopted 2026-07-07 as an authoritative v2 execution contract, alongside
> `docs/v2-coding-agent-directive.md`. This roadmap governs what "v2 done" means.
> The living, slice-by-slice status board is `docs/ROADMAP.md`; deferred work is in
> `docs/tech-debt.md`.

## 1. Objective

Deliver a deterministic, governed document generation engine aligned to the ORG AI
Governance Program.

The goal is not feature expansion. The goal is controlled completion of the existing
contract:

- Governor produces judgment (AGR)
- Engine enforces validation and renders output
- All violations fail closed
- No runtime AI usage

Reference: README.md, ADR-003.

---

## 2. Scope (Locked)

### Included Artifact Types

- DS - Deployment Summary
- AUP - Acceptable Use Policy

These are the only generation targets for v2 acceptance.

Reference: config/artifact-types.json.

### Supported Modes

- draft
- promotion

Reference: schemas/validation-report.schema.json.

---

## 3. Finish Line (Definition of Done)

v2 is complete only when ALL conditions are met.

### 3.1 Rendering

- DS renders end-to-end in draft mode
- DS renders end-to-end in promotion mode
- AUP renders end-to-end in draft mode
- AUP renders end-to-end in promotion mode

Reference: README.md.

### 3.2 Validation Output

Every execution MUST produce a valid `validation-report.json`.

Each failed check MUST include:

- checkId
- jsonPath
- expected
- actual
- remediation
- ruleSource

Reference: schemas/validation-report.schema.json.

### 3.3 Promotion Output

Every successful promotion run MUST produce `promotion-payload.json`.

Payload MUST include:

- registryUpdate object
- repositoryTarget
- attestation (QA-SPEC-18)

Reference: schemas/promotion-payload.schema.json.

### 3.4 Fail-Closed Enforcement

The engine MUST block output for any of the following:

- Invalid or missing Document Control Header
- Clause mismatch or paraphrase
- Unregistered Document ID
- Illegal lifecycle transition
- Unknown placeholder in promotion mode
- Unsupported artifact type
- Unsupported content block

Reference: README.md, config/placeholder-dictionary.json, config/artifact-types.json.

### 3.5 Unsupported Cases

The engine MUST refuse generation when:

- `sectionSchema.status = not-defined-in-spec`
- Unsupported block types are present

Failure types: ConfigurationError (both cases in the current engine; the engine rejects
unsupported blocks pre-render via QA-ENG-06 rather than reaching a RenderingError path).

Reference: config/artifact-types.json.

---

## 4. Out of Scope (Explicit)

The following are NOT part of v2 and MUST NOT be implemented:

- UQR rendering
- TCL rendering
- TAK, RAF, DCN generation
- SharePoint / Graph API integration
- Placeholder auto-correction
- Partial document output
- Runtime LLM calls
- artifact-manifest.json and generation-log.json emission (deferred to post-v2; the v2
  output package is DOCX + validation-report.json + promotion-payload.json)
  (generation-log.json shipped 2026-07-15, post-v2; see docs/tech-debt.md item 11)
  (artifact-manifest.json shipped 2026-07-15, post-v2)

Reference: README.md, config/artifact-types.json.

---

## 5. Execution Plan

### Phase 1 - Contract Alignment

Tasks:

- Reconcile the output contract (README.md and CLAUDE.md vs current engine behavior).
- Confirm the presence or deferral of `artifact-manifest.json` and `generation-log.json`.
  (generation-log.json shipped 2026-07-15, post-v2; see docs/tech-debt.md item 11)
  (artifact-manifest.json shipped 2026-07-15, post-v2)

Acceptance Criteria:

- A single authoritative output definition exists.
- No conflicting documentation.

### Phase 2 - Validation Engine

Tasks:

- Implement schema validation (AGR strict contract).
- Implement semantic validation: clause resolution via hash, placeholder validation,
  lifecycle validation, registry validation.

Requirements:

- All rules map to qa-checklist checkIds.
- All failures produce structured remediation.

Acceptance Criteria:

- Deterministic `validation-report.json` generation.
- No silent acceptance of invalid inputs.

Reference: config/qa-checklist.json, schemas/validation-report.schema.json.

### Phase 3 - Rendering Engine

Tasks:

- Implement universal structure: header table, Document Control Header, Configuration
  callout, ordered sections, footer.
- Enforce section ordering, clause insertion by reference only, no paraphrasing, style
  rules.

Acceptance Criteria:

- DS and AUP render correctly.
- No structural violations pass validation.

Reference: config/artifact-types.json, Master Template System Specification.

### Phase 4 - Failure Path Coverage

Tasks:

- Create negative test cases for: SchemaValidationError, ClauseResolutionError,
  RegistrySyncError, LifecycleTransitionError, RenderingError, PromotionPolicyError.

Acceptance Criteria:

- Every reachable failure produces actionable remediation.
- Validation output is Governor-consumable without human interpretation.
- Any error category with no reachable path is documented as such.

Reference: schemas/validation-report.schema.json.

### Phase 5 - Promotion Path

Tasks:

- Generate `promotion-payload.json`.
- Validate payload structure and fields.

Acceptance Criteria:

- Payload conforms to schema.
- Emitted only on successful promotion runs.
- Registry update intent is correctly represented.

Reference: schemas/promotion-payload.schema.json.

### Phase 6 - Release Gate

Tasks:

- Resolve placeholder dictionary state:
  - Option A: reconcile against the PDX workbook.
  - Option B: enforce promotion failure for unresolved tokens.

Acceptance Criteria:

- Behavior matches the `reconciledAgainstPdxXlsx` configuration flag.
- No ambiguity in placeholder handling.

Reference: config/placeholder-dictionary.json.

---

## 6. Test Requirements

### Passing Fixtures

- DS (draft)
- DS (promotion)
- AUP (draft)
- AUP (promotion)

### Failing Fixtures

At least one per reachable error category:

- SchemaValidationError
- ClauseResolutionError
- RegistrySyncError
- LifecycleTransitionError
- RenderingError
- PromotionPolicyError

Each test MUST produce deterministic validation output.

---

## 7. Definition of Done

The system is complete when:

- All finish-line criteria are met.
- All test fixtures pass.
- All reachable failure modes are enforced.
- No unsupported artifact type can be generated.
- Output is deterministic and reproducible.
- Documentation reflects actual system behavior.

There is no partial completion state.

---

## 8. Post-v2 Backlog (Deferred)

The following items are explicitly deferred:

- artifact-manifest.json emission (shipped 2026-07-15, post-v2)
- Placeholder Dictionary reconciliation (if not completed under Phase 6)
- UQR rendering
- TCL rendering
- Section schema authoring for TAK, RAF, DCN
- Support for advanced block types: radioQuestion (shipped 2026-07-14, post-v2),
  warnCallout (shipped 2026-07-15, post-v2), aggregationTable, copyReadyMessage
- SharePoint / Graph API registry integration

---

## 9. Architectural Rationale

This roadmap enforces:

- Separation of concerns (Governor vs engine)
- Deterministic output (no runtime AI)
- Fail-closed governance model
- Spec-driven template rendering
- Controlled artifact lifecycle and promotion

Reference: ADR-003 Governor Agent Design, ADR-001 Repository Architecture
(`docs/reference/`).
