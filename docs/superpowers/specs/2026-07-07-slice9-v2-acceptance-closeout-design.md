# Slice 9: v2 Acceptance Closeout - Design

**Status:** Approved (design)
**Date:** 2026-07-07
**Predecessor:** Slice 8 (content lint); the adopted v2 contract in
`docs/v2-acceptance-roadmap.md` and `docs/v2-coding-agent-directive.md`.

## Goal

Declare Generation Engine v2 accepted against its adopted Definition of Done with
no new engine behavior. This slice closes the honest gaps between the contract's
finish line and the current test/documentation coverage. Slices 1 through 8 already
deliver the substance (DS and AUP render, validation and promotion outputs, fail-closed
enforcement); most of the contract's Phases 2, 3, 4, and 6 are already satisfied by
existing tests. This slice adds only what is genuinely missing: promotion-mode render
proof for DS and AUP, a published failure-path coverage matrix, a lifecycle failing
fixture, and honest documentation of the placeholder release gate.

## Scope decisions (locked)

1. **No production engine code changes.** Every change is a fixture, a test, one config
   `$comment`, or documentation. This is a coverage and documentation closeout, not a
   behavior change. If the new unknown-token test surfaces a behavior gap, that is a
   finding to raise, not planned work.
2. **Dead error categories are documented, not wired.** `RenderingError`,
   `PromotionPolicyError`, `DependencyResolutionError`, and `SharePointIntegrationError`
   have no emitter in the engine (confirmed by an exhaustive `ErrorCategory.<X>` scan of
   `src/**`). They are recorded as reserved-but-unreachable in v2 with the reason each is
   not emitted. The `ErrorCategory` constants stay in `Vocabulary.cs` as reserved
   vocabulary; they are not pruned.
3. **The `reconciledAgainstPdxXlsx` flag is documented as provenance-only, not wired.**
   No code reads it; the real release gate is `PlaceholderLayer`'s mode-gating (unknown or
   unresolved tokens warn in draft and fail on promotion), which is the directive's
   Option B and already works. Wiring the flag is post-v2 (the PDX-reconciliation work).
4. **Both DS and AUP get a promotion build test.** Finish-line criterion 3.1 requires
   both types to render end-to-end in promotion; today neither has a promotion-mode build
   (render) test, only a DS promotion validate test.

## Architecture (four gaps, all fixture/test/doc)

### 1. Promotion render coverage (finish-line 3.1)

- **New fixture** `fixtures/agr-sample-aup-promotion.json`: a copy of
  `fixtures/agr-sample-aup-draft.json` with exactly the three-field delta the DS
  promotion fixture uses - `artifactIdentity.approvalStatus` `Draft` -> `In Review`,
  `context.reviewDate` `null` -> `"2026-12-01"` (non-stale against the fixed
  2026-07-03 clock; AUP review interval is 6 months), `validationPolicy.mode` `draft`
  -> `promotion`. `documentId` (`ORG-GOV-AUP-004`), sections, and placeholderMap are
  unchanged, so all content, clause, and placeholder validation carries over.
- **New registry row** in `fixtures/registry-sample.csv`:
  `ORG-GOV-AUP-004,AUP,Client-Facing,Draft,v1.0,"Talewind CPA, LLC"`. This ID is absent
  from every registry fixture today, so an AUP promotion currently fails QA-SF-03. The
  `Draft` registry status makes the `Draft` -> `In Review` transition legal (QA-SF-04),
  and type/classification match (QA-SF-03).
- **Two build tests** in `tests/Registrar.Tests/BuildCommandTests.cs`, one for DS (using
  the existing `agr-sample-ds-promotion.json`) and one for AUP (the new fixture), each
  invoking `BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
  "promotion", registryPath)` with `registryPath` = repo `fixtures/registry-sample.csv`.
  Each asserts: exit code 0; a `.docx` is produced in the output directory (AUP name
  `ORG - Acceptable Use Policy - Talewind CPA - v1.0.docx`, DS name
  `ORG - Deployment Summary - Talewind CPA - v1.0.docx`); the DOCX passes
  `OpenXmlValidator` (mirroring the existing AUP-draft build test); and
  `promotion-payload.json` exists with `registryUpdate.toStatus == "In Review"` and
  `attestation.checkId == "QA-SPEC-18"` (mirroring the DS promotion validate test).

### 2. Failure-path coverage matrix (Phase 4 acceptance)

New reference document `docs/failure-path-coverage.md`: a table with one row per
`ErrorCategory`, each row recording reachable (yes/no), the emitter(s) (checkId and
source file), the covering fixture or test, and notes. Reachable categories and their
emitters (from the reachability scan):

- `SchemaValidationError` - QA-SCHEMA-01 (`SchemaValidationLayer`) - fixture
  `agr-sample-ds-invalid-schema.json`.
- `ClauseResolutionError` - QA-ENG-04 (`ClauseReferenceLayer`, `ClauseResolver`) -
  fixture `agr-sample-ds-missing-clause.json`.
- `RegistrySyncError` - QA-SF-03 (`RegistryLayer`, `ValidationService` malformed-registry),
  QA-SPEC-18 (`PromotionLayer`) - fixture `agr-sample-ds-unregistered.json`.
- `LifecycleTransitionError` - QA-SF-04 (`LifecycleLayer`) - unit test
  `LifecycleLayerTests.QA_SF_04_fails_on_illegal_transition` plus the new fixture (gap 3).
- `ConfigurationError` - QA-ENG-06 (`BlockTypeSupportLayer`), QA-ENG-02
  (`SectionSchemaLayer`) - fixtures `agr-sample-ds-unsupported-block.json`,
  `agr-sample-ds-section-mismatch.json`.
- `MetadataViolation` - QA-ENG-05, QA-ENG-03, QA-SPEC-07, QA-SF-05, QA-SF-07, QA-SF-08,
  QA-SF-09, QA-ENG-07 - fixtures `agr-sample-ds-prohibited-char.json`,
  `agr-sample-ds-id-code-mismatch.json`, `agr-sample-ds-placeholder-drift.json`,
  `agr-sample-ds-review-stale.json`, `agr-sample-ds-token-residue.json`, and the Slice 8
  heading fixtures.

Reserved-but-unreachable in v2 (documented, no fixture, no code):

- `RenderingError` - no emitter. Unsupported block types are rejected pre-render as
  `ConfigurationError` (QA-ENG-06), so the renderer never reaches a failure state that
  would emit a `RenderingError`.
- `PromotionPolicyError` - no emitter. Promotion blocking is a `BuildCommand`
  output-eligibility decision comparing `OverallResult` and `RequestedMode`, not a
  `Check`. Promotion registry failures use `RegistrySyncError` (`PromotionLayer`).
- `DependencyResolutionError` - no emitter in v2 (AGR `dependencies` are not
  cross-resolved at runtime in the file-system-only v2).
- `SharePointIntegrationError` - no emitter; SharePoint/Graph integration is post-v2.

### 3. LifecycleTransitionError fixture (fixtures-directory parity)

- **New fixture** `fixtures/agr-sample-ds-illegal-transition.json`: a copy of
  `agr-sample-ds-draft.json` with `artifactIdentity.approvalStatus` `Draft` ->
  `Approved`. Against the `registry-sample.csv` record for `ORG-GOV-DS-016`
  (status `Draft`, whose only legal next states are `In Review` and `Retired`), the
  target `Approved` is an illegal transition, so QA-SF-04 fails with
  `LifecycleTransitionError`. Kept in `draft` mode so the lifecycle defect is the point of
  interest; `LifecycleLayer` is not mode-gated and fires whenever a registry is supplied.
- **New end-to-end test** in `tests/Registrar.Tests/ValidateCommandTests.cs`: run
  `ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
  registryPath)` with `registryPath` = `fixtures/registry-sample.csv`, read the emitted
  `validation-report.json`, and assert it contains a check with `checkId == "QA-SF-04"`,
  `result == fail`, `severity == blocking`, and `errorCategory == "LifecycleTransitionError"`.
  The plan confirms the fixture isolates the lifecycle defect (the test asserts the QA-SF-04
  check is present; it does not require it to be the only failing check).

### 4. Release-gate documentation and one test (Phase 6)

- **Config `$comment`**: in `config/placeholder-dictionary.json`, clarify the
  `reconciledAgainstPdxXlsx` provenance note to state that the flag is not read by the
  engine today (provenance/status metadata only) and that the runtime gate is
  `PlaceholderLayer`'s draft-warn / promotion-fail behavior; wiring the flag is post-v2.
- **Docs reconciliation**: adjust the known-gaps wording in `README.md` and `CLAUDE.md`
  that currently reads "until `reconciledAgainstPdxXlsx` is set to true," which implies
  flipping the flag changes behavior. Note that the flag is not yet wired and that the
  promotion gate is already active regardless of the flag.
- **New test** in `tests/Registrar.Tests/PlaceholderLayerTests.cs`:
  `QA_SPEC_07_warns_on_unknown_token_in_draft_and_fails_in_promotion`, pinning the
  unknown-canonical-token branch of `ModeGated` (a key that is neither a canonical token
  nor a known drift alias) - warn/Warning in draft, fail/Blocking in promotion. The
  existing tests cover the null-value and drift-alias branches but not this one.

## Error handling

No new failure paths are introduced. All new fixtures exercise existing checks through
their existing layers; all new tests assert on the existing `Check`/report shape. The
full suite must remain green.

## Testing

Per repo convention, coverage is expressed through the existing harnesses:

- `BuildCommandTests` - DS and AUP promotion build tests (gap 1).
- `ValidateCommandTests` - the lifecycle end-to-end test (gap 3).
- `PlaceholderLayerTests` - the unknown-token mode-gating test (gap 4).
- No new checkId is introduced, so no new catalog entry or catalog test is required.
- **Acceptance milestone:** with promotion render proven for DS and AUP, the failure-path
  matrix published, the lifecycle fixture in place, and the release gate pinned and
  documented, the engine meets the adopted v2 Definition of Done.

## Non-goals (recorded, not silently dropped)

- Wiring `reconciledAgainstPdxXlsx` into runtime behavior (post-v2 PDX reconciliation).
- Emitting `RenderingError` / `PromotionPolicyError` / `DependencyResolutionError` /
  `SharePointIntegrationError`, or pruning their reserved constants.
- `artifact-manifest.json` and `generation-log.json` emission (deferred to post-v2 by
  the adopted contract).
- Any rendering, block type, or artifact type beyond DS and AUP.
