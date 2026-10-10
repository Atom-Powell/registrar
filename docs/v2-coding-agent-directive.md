# Coding-Agent Directive (Authoritative Work Plan)

> **Status:** Adopted 2026-07-07 as an authoritative v2 execution contract, alongside
> `docs/v2-acceptance-roadmap.md`. Citation artifacts from the source document have been
> removed; content is otherwise preserved. Underlying references are the ORG governance
> documents in `docs/reference/` (Master Template System Specification and Usage Guide
> v1.2, ADR-001 through ADR-003).

## Objective

Drive Generation Engine v2 to a bounded acceptance milestone. Stop scope expansion.
Complete validation and rendering for the defined artifacts under strict fail-closed
governance rules.

## Scope Definition (Non-Negotiable)

The coding agent will implement ONLY:

- **Artifact types:** DS (Deployment Summary), AUP (Acceptable Use Policy)
- **Modes:** draft, promotion

Anything outside this scope is explicitly out of v2.

## Finish Line Criteria (All Must Be True)

### 2.1 Rendering

- DS renders end-to-end in draft mode
- DS renders end-to-end in promotion mode
- AUP renders end-to-end in both modes

### 2.2 Validation

Every run produces a valid `validation-report.json`. Every failed check includes:
checkId, jsonPath, expected, actual, remediation, ruleSource.

### 2.3 Promotion

A passing promotion run emits a valid `promotion-payload.json` that conforms exactly to
schema and includes the `registryUpdate` and `attestation` fields.

### 2.4 Fail-Closed Enforcement

- Missing Document Control Header -> block
- Clause mismatch -> block
- Illegal lifecycle transition -> block
- Unknown placeholders in promotion -> block

### 2.5 Unsupported Conditions

The engine MUST refuse generation when:

- `artifactTypes.sectionSchema.status = not-defined-in-spec`
- Unsupported content blocks appear in the AGR

This is a ConfigurationError path (the engine rejects unsupported blocks pre-render via
QA-ENG-06; it does not reach a separate RenderingError path).

## Explicit Out-of-Scope (Do Not Build)

The following are prohibited for v2:

- UQR rendering
- TCL rendering
- TAK, RAF, DCN generation
- SharePoint or Graph integration
- Auto-correction of placeholders
- Partial output on validation failure
- Runtime LLM usage
- artifact-manifest.json and generation-log.json emission (deferred to post-v2)
  (generation-log.json shipped 2026-07-15, post-v2; artifact-manifest.json shipped
  2026-07-15, post-v2)

These violate either scope, determinism, or spec constraints.

## Execution Phases

### Phase 1 - Contract Alignment

Tasks:

- Reconcile implemented outputs with the README contract; decide whether
  `artifact-manifest.json` and `generation-log.json` exist in v2.
  (generation-log.json shipped 2026-07-15, post-v2; artifact-manifest.json shipped
  2026-07-15, post-v2)
- Confirm validation and promotion outputs match schemas.

Acceptance Criteria:

- A single authoritative output contract exists in the repo.
- No conflicting documentation remains.

### Phase 2 - Core Validation Engine

Tasks:

- Implement schema validation (AGR strict enforcement).
- Implement semantic validation: clause resolution via hash check, placeholder
  validation, lifecycle validation, registry validation.

Acceptance Criteria:

- Every rule maps to a qa-checklist checkId.
- Every failure produces a structured validation report.

### Phase 3 - Rendering Engine

Tasks:

- Implement the universal document structure: header table, Document Control Header,
  Configuration callout, ordered sections, footer.
- Enforce section order, clause insertion via reference, no paraphrasing, style
  compliance.

Acceptance Criteria:

- DS and AUP render with correct structure.
- No placeholder or schema violations pass.

### Phase 4 - Failure Path Completeness

Tasks:

- Build negative test cases for: unknown placeholders, clause mismatch, section-ordering
  violations, unsupported block types, registry mismatch, lifecycle violations.

Acceptance Criteria:

- All failures return actionable remediation text.
- The Governor can regenerate the AGR without human interpretation.

### Phase 5 - Promotion Path

Tasks:

- Generate `promotion-payload.json`.
- Validate: repositoryTarget present, registryUpdate correct, attestation included.

Acceptance Criteria:

- Payload passes schema validation.
- Only emitted on successful promotion runs.

### Phase 6 - Release Gate

Tasks:

- Resolve the placeholder-dictionary state: either reconcile the PDX workbook, or
  explicitly block promotion when unresolved.

Acceptance Criteria:

- No ambiguity in placeholder enforcement.
- Behavior matches the `reconciledAgainstPdxXlsx` config flag.

## Test Requirements

Passing fixtures:

- DS (draft + promotion)
- AUP (draft + promotion)

Failing fixtures - at least one per reachable QA category:

- SchemaValidationError
- ClauseResolutionError
- RegistrySyncError
- LifecycleTransitionError
- RenderingError
- PromotionPolicyError

Each test must produce a deterministic `validation-report.json`.

## Definition of Done

The build is complete when:

- All finish-line criteria (Section 2) are satisfied.
- All tests (Test Requirements) pass.
- No unsupported artifact type can be generated.
- No validation rule is bypassed.
- Output package behavior is consistent and documented.

There is no partial completion state.

## Rationale

- The repository already defines acceptance as DS and AUP rendering end-to-end.
- The artifact-type schema explicitly limits valid generation targets.
- Validation and promotion schemas already define the output and governance loop.
- The placeholder dictionary explicitly introduces a release-gate condition.

Expanding scope before satisfying these constraints introduces drift, breaks
determinism, and violates the governed architecture defined in the repository.
