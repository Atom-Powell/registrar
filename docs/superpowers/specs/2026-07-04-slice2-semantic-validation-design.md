# Slice 2: AGR Semantic Validation Layers - Design

**Status:** Approved (design)
**Date:** 2026-07-04
**Predecessor:** Slice 1 walking skeleton (`registrar validate` against `agr.schema.json`)

## Goal

After schema validation passes, run a tier of config-driven semantic checks
against the Artifact Generation Request (AGR) and fold their results into the
same `validation-report.json`. Slice 2 is deterministic, config-driven, and
operates purely on the AGR plus `/config` data. It does not render a DOCX and
does not read an external ID Registry.

The governing distinction (stated in `agr.schema.json` itself): *schema-valid is
not generation-valid*. Slice 1 proved schema conformance. Slice 2 begins proving
generation validity.

## Scope decisions

Locked in during brainstorming:

1. **Config-only semantic tier.** Slice 2 covers the semantic checks that need
   only the AGR and `/config`: section-schema conformance, placeholder
   resolution/drift, cross-field metadata consistency, review-date interval, and
   clause-reference integrity. The ID Registry adapter and lifecycle state
   machine (QA-SF-03, QA-SF-04, QA-SF-18) are deferred to Slice 3.
2. **Schema is a gate.** If schema validation (the input tier) produces any
   blocking failure, the semantic layers are skipped and the report carries only
   the schema checks. Semantic layers never read a field the schema has not
   validated. This also means the typed AGR is only deserialized after the gate
   passes, so deserialization cannot hit surprises.
3. **Upgrade the golden fixture.** `fixtures/agr-sample-ds-draft.json` is filled
   out to be fully generation-valid (all eight ordered DS sections, all ten
   required placeholders resolved, clause CL-006 present) so it remains the
   single golden happy-path fixture and Slice 1's end-to-end tests pass
   unchanged.

## Non-goals (explicitly deferred, not silently dropped)

- **ID Registry adapter and lifecycle state machine** (QA-SF-03, QA-SF-04,
  QA-SF-18) -> Slice 3. The registry introduces a new external file format
  (CSV/xlsx export), a swappable interface, and stateful comparison; it deserves
  its own design.
- **All post-render checks** -> render slice: clause SHA-256 text match
  (QA-SF-11), header/DCH/footer cross-consistency (QA-SF-01/02/06), heading
  hierarchy and body-content presence (QA-SF-07), radio glyph U+25CB (QA-SF-10),
  filename/DCH agreement (QA-SF-12), v1.2 structure (QA-SPEC-19), outline
  anti-pattern (QA-ENG-01), and the client-facing content-hygiene checks that
  scan rendered runs (QA-SF-08/09).
- **Dependency resolution** (`DependencyResolutionError`) -> Slice 3. Blocking
  dependencies of kind `registry-record` or `document` cannot be resolved
  without the registry and document stores.

## Architecture

The schema layer is unchanged: it is the gate, it works on raw AGR text, and it
already re-parses independently so malformed JSON is caught inside it. Everything
new hangs off a typed AGR plus a typed config bundle.

### New components

- **`AgrDocument`** (`src/Registrar.Core/Model/AgrDocument.cs`) - a strongly-typed
  DTO deserialized from the AGR text after the gate passes. Mirrors the schema
  shape closely enough for the semantic layers to need (identity, context,
  `dataInputs.placeholderMap`, `dataInputs.sections[].title` and
  `blocks[].blockType`/`clauseId`, `validationPolicy.mode`). Uses
  `[JsonPropertyName]` mappings like the existing report model. Optional fields
  are nullable; required fields are non-null because the gate guarantees them.

- **`ConfigBundle`** (`src/Registrar.Core/Config/`) - typed load of
  `vocabularies.json`, `artifact-types.json`, `clause-library.json`, and
  `placeholder-dictionary.json`, loaded once per run. Config model records live
  in `Core/Config`; a `ConfigLoader` deserializes and returns the bundle plus
  each file's SHA-256 for `configVersions`.

- **`ConfigLocator.FindConfig(baseDir, fileName)`** - sibling to the existing
  `FindSchema`, same walk-up-to-repo-root logic (find the directory holding both
  `schemas/` and `config/`, then return `config/<fileName>`).

- **`ISemanticLayer` + `SemanticContext`** - the generalized seam.
  `SemanticContext` carries `AgrDocument Agr`, `ConfigBundle Config`,
  `string Mode`, and `IIdRegistry? Registry`. `Registry` is `null` throughout
  Slice 2; the field exists so the Slice 3 registry/lifecycle layers slot in
  without changing the seam. `ISemanticLayer.Validate(SemanticContext)` returns
  `IReadOnlyList<Check>`, matching the existing schema-layer contract shape.

- **`OverallResultPolicy`** (`src/Registrar.Validation/`) - computes
  `pass` / `pass-with-warnings` / `fail` from the aggregated check set, the mode,
  and `validationPolicy.warningThreshold`. Rules: any blocking `fail` -> `fail`;
  otherwise any `warn` -> `pass-with-warnings`; otherwise `pass`. If
  `warningThreshold` is set and the warn count exceeds it, the run is `fail` even
  in draft mode. Slice 1 only ever emitted `pass`/`fail`; this introduces the
  `pass-with-warnings` overall value the report schema already allows.

`IIdRegistry` is introduced in Slice 2 only as an empty marker interface (or a
minimal interface with no Slice 2 callers) so `SemanticContext` can name the
type. Its real shape and CSV/xlsx implementation are a Slice 3 concern; Slice 2
must not build any registry reading.

### The five semantic layers

Each layer is single-purpose, implements `ISemanticLayer`, and is independently
testable. All live in `src/Registrar.Validation/`.

| Layer | Check ID | Error category | Enforces |
|---|---|---|---|
| `SectionSchemaLayer` | QA-ENG-02 (exists) | ConfigurationError | Section titles equal the artifact type's `sectionSchema.orderedSections` exactly and in order; a type whose `sectionSchema.status` is `not-defined-in-spec` fails (refuse to validate structure that is not defined). |
| `PlaceholderLayer` | QA-SPEC-07 (exists) | MetadataViolation | `placeholderMap` keys: canonical tokens pass; known drift aliases fail with the canonical suggestion; unknown bracket tokens warn in draft, fail on promotion. Per-type `requiredPlaceholders` must be present. Unresolved (null) values are permitted in draft for client-facing artifacts, fail on promotion. |
| `ReviewDateLayer` | QA-SF-05 (exists) | MetadataViolation | `reviewDate` is ISO 8601 and no later than `today + categoryIntervalMonths`, unless a `reviewDateOverride` is attached. Null is permitted in draft, required at promotion. |
| `MetadataLayer` | QA-ENG-03 (new) | MetadataViolation | Cross-field consistency the schema pattern cannot check: the artifact code embedded in `documentId` matches the `artifactType` field, and the category segment of `documentId` matches that type's `category` in `artifact-types.json`. |
| `ClauseReferenceLayer` | QA-ENG-04 (new) | ClauseResolutionError | Every `clauseInsertion` block's `clauseId` exists in `clause-library.json`, and all of the artifact type's `requiredClauses` appear as `clauseInsertion` blocks. (The rendered-text SHA-256 match, QA-SF-11, is post-render and deferred.) |

Two new catalog entries, QA-ENG-03 and QA-ENG-04, are added to
`config/qa-checklist.json` (config, not code, per non-negotiable rule 3). They
follow the existing QA-ENG namespace of engine-derived checks and carry
`layer`, `automation: "full"`, `appliesTo`, `rule`, `errorCategory`, and
`ruleSource`.

### Data flow

```
registrar validate <agr.json>
  1. locate agr.schema.json + load ConfigBundle (4 config files)
  2. SchemaValidationLayer (gate)
       any blocking fail? -> assemble report with schema checks only, STOP
  3. deserialize AgrDocument from the (now schema-valid) AGR text
  4. build SemanticContext(Agr, Config, mode, registry: null)
  5. run the 5 semantic layers, aggregate their checks[]
  6. OverallResultPolicy(checks, mode, warningThreshold) -> overall result
  7. write validation-report.json
       configVersions now carries agr.schema.json + the 4 config hashes
```

`ValidationService` orchestrates steps 1-7. The schema layer keeps its existing
`SchemaValidationInput` record; the semantic layers share `SemanticContext`.

### CLI

No new verb. `registrar validate` simply runs more layers. Exit codes are
unchanged (0 for pass or pass-with-warnings, 1 for fail, 2 for usage/IO error).
The stdout summary line gains a warn count alongside the existing fail count.

## Mode semantics

- **draft:** unresolved client-completion placeholders and unknown bracket
  tokens downgrade to `warn`; a null `reviewDate` is permitted. Controlled-
  document violations (section schema, drift aliases, cross-field metadata,
  missing required clauses) still fail closed.
- **promotion:** everything that warns in draft blocks. A null `reviewDate`,
  unresolved required placeholders, and unknown tokens all fail.

`warningThreshold`, when present, fails the run if the warn count exceeds it even
in draft.

## Testing

Per CLAUDE.md, every QA check ID gets at least one passing and one failing test
named after the check ID. Coverage:

- **Per-check pass/fail:** QA-ENG-02, QA-SPEC-07, QA-SF-05, QA-ENG-03, QA-ENG-04
  each get a passing and a failing test.
- **Gate behavior:** a schema-invalid AGR produces only schema checks and the
  semantic layers do not run (asserted by the absence of any semantic check ID
  in the report).
- **Config loading:** `ConfigLoader` parses all four files and reports their
  hashes; `ConfigLocator.FindConfig` resolves and throws symmetrically to
  `FindSchema`.
- **`AgrDocument`:** round-trips the golden fixture into the typed shape the
  layers consume.
- **`OverallResultPolicy`:** the same warn-producing AGR yields
  `pass-with-warnings` in draft and `fail` in promotion; `warningThreshold`
  exceeded yields `fail` in draft.

### Fixtures

- `fixtures/agr-sample-ds-draft.json` upgraded to fully generation-valid (the
  golden pass case).
- One invalid fixture per new failure category: section-title mismatch,
  placeholder drift alias, review-date out of interval, documentId-code vs
  artifactType mismatch, and a missing required clause. Narrower variations may
  be produced by in-test mutation of the golden fixture, following the Slice 1
  pattern, but each headline failure category gets a named fixture.

## Interpretation notes (ambiguities resolved explicitly)

- **QA-SF-05 interval anchor.** The Spec says the review date must be "within
  category default" without naming a baseline date. This design anchors the
  interval to *today* (via the existing `IClock`): `reviewDate` must be no later
  than `today + categoryIntervalMonths`, unless a `reviewDateOverride` is logged.
  If the documentation owner intends a creation/approval-date anchor instead,
  this layer changes; the interpretation is called out here so it is a conscious
  choice, not a hidden assumption.
- **Unknown vs known-drift placeholders.** Known drift aliases
  (`placeholder-dictionary.json` `knownDriftAliases`) always fail with the
  canonical suggestion, in both modes. Unknown bracket tokens warn in draft and
  fail on promotion, consistent with `reconciledAgainstPdxXlsx: false`.
- **Placeholder error category.** The config docs refer informally to a
  "PlaceholderViolation," but that term is not in the report schema's closed
  `errorCategory` enum. Placeholder checks therefore emit `MetadataViolation`,
  the closest member of the closed set; the remediation text carries the
  specifics (canonical token, jsonPath).

## Component boundaries summary

- `AgrDocument` - typed AGR; depends on nothing but System.Text.Json.
- `ConfigBundle` / `ConfigLoader` - typed config; depends on config files.
- `ISemanticLayer` / `SemanticContext` - the seam; depends on `AgrDocument`,
  `ConfigBundle`, `Check`.
- Each semantic layer - one check family; depends on `SemanticContext` and the
  `Check`/`Vocabulary` primitives only.
- `OverallResultPolicy` - pure function of checks + mode + threshold.
- `ValidationService` - orchestration; the only component that knows the gate
  ordering and wires the layers together.
