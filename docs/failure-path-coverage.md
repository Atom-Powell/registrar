# Failure-Path Coverage Matrix

Maps every `ErrorCategory` (defined in `src/Registrar.Core/Vocabulary.cs`) to whether the
engine emits it, the emitting check(s), and the covering fixture or test. This satisfies
the v2 acceptance requirement that every reachable failure is covered and every error
category with no reachable path is documented as such
(`docs/v2-acceptance-roadmap.md`, Phase 4).

## Reachable categories (emitted by a check)

| ErrorCategory | Emitter (checkId / layer) | Covering fixture or test |
|---|---|---|
| SchemaValidationError | QA-SCHEMA-01 / SchemaValidationLayer | `fixtures/agr-sample-ds-invalid-schema.json` |
| ClauseResolutionError | QA-ENG-04 / ClauseReferenceLayer, ClauseResolver | `fixtures/agr-sample-ds-missing-clause.json` |
| RegistrySyncError | QA-SF-03 / RegistryLayer, ValidationService; QA-SPEC-18 / PromotionLayer | `fixtures/agr-sample-ds-unregistered.json` |
| LifecycleTransitionError | QA-SF-04 / LifecycleLayer | `fixtures/agr-sample-ds-illegal-transition.json`; `LifecycleLayerTests.QA_SF_04_fails_on_illegal_transition` |
| ConfigurationError | QA-ENG-06 / BlockTypeSupportLayer; QA-ENG-02 / SectionSchemaLayer; QA-ENG-07 config-guard / TokenResidueLayer | `fixtures/agr-sample-ds-unsupported-block.json`; `fixtures/agr-sample-ds-section-mismatch.json`; `TokenResidueLayerTests.QA_ENG_07_fails_closed_when_pattern_is_not_configured` |
| MetadataViolation | QA-ENG-05, QA-ENG-03, QA-SPEC-07, QA-SF-05, QA-SF-07, QA-SF-08, QA-SF-09, QA-ENG-07, QA-ENG-08 | `fixtures/agr-sample-ds-prohibited-char.json`, `-id-code-mismatch.json`, `-placeholder-drift.json`, `-review-stale.json`, `-token-residue.json`, `-empty-h1.json`, `-heading-skip.json`, `-layer-mismatch.json` |

Note: SectionSchemaLayer (QA-ENG-02) also fails closed with ConfigurationError for an
unknown artifact type and for a `not-defined-in-spec` section schema. Those refusal
branches are unit-reachable but unfixtured in v2, whose scope locks to DS and AUP (both
`defined`); a post-v2 slice that adds an undefined generation target should add a fixture
for them.

## Reserved but not emitted in v2

These constants exist in `Vocabulary.cs` as a stable error taxonomy but are not emitted by
any code path in v2. They are intentionally left wired-off (not pruned) so the vocabulary
stays aligned with the spec's error model.

| ErrorCategory | Why it is not emitted in v2 |
|---|---|
| RenderingError | Unsupported block types are rejected pre-render as ConfigurationError (QA-ENG-06), so the renderer never reaches a state that would emit a RenderingError. |
| PromotionPolicyError | Promotion blocking is a BuildCommand output-eligibility decision (comparing OverallResult and RequestedMode), not a Check. Promotion registry failures use RegistrySyncError (PromotionLayer). |
| DependencyResolutionError | AGR `dependencies` are not cross-resolved at runtime in the file-system-only v2. |
| SharePointIntegrationError | SharePoint / Graph integration is post-v2. |
