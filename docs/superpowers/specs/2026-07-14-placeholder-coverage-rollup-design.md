# Placeholder Coverage Rollup (REG-TD-007)

Date: 2026-07-14
Status: approved, ready for implementation plan

## Problem

`PlaceholderLayer` (QA-SPEC-07) reports per offending token only: a drift alias, an
unknown key, an unresolved null, or a missing required token. It emits no coverage
summary, so a governance reviewer (or the Governor, reading the validation report)
cannot see at a glance how complete the placeholder set is. The Governor raised this
as REG-TD-007 during 2026-07-14 smoke testing; it is recorded as tech-debt item 29.
The inputs for a summary are already in scope inside `PlaceholderLayer.Validate`.

## Non-goals

- No new report structure. No change to `schemas/validation-report.schema.json`, no
  `reportVersion` bump. The rollup rides in the existing `checks[]` array as an
  informational check, the same shape `PromotionLayer` and `RegistryLayer` already
  emit (`Result = pass`, `Severity = info`).
- No render-time reference scan. The layer runs pre-render, so it cannot prove a
  provided token is truly unreferenced (a non-required token may still feed clause
  text or a body block at render). The rollup therefore reports "beyond the required
  set", not "unused", and never claims a token is waste.
- Not a gate. The coverage check never fails and never warns. It cannot change
  `overallResult` and does not contribute to the failing/warning counts. It is
  additive signal only.

## Design

`PlaceholderLayer.Validate` emits exactly one additional check on every run, in both
draft and promotion mode, alongside the per-token findings it already produces.

The check shape:

- `CheckId` = `QA-SPEC-07` (unchanged; same layer, same rule source)
- `LayerName` = `content`
- `Result` = `pass` (`CheckResultValue.Pass`)
- `SeverityLevel` = `info` (`Severity.Info`)
- `ErrorCategory` = null. `Check.ErrorCategory` is a nullable `string?`, and
  `PromotionLayer.Pass` leaves it unset for its pass/info check; the coverage check
  does the same (an informational check names no error category).
- `JsonPath` = `/dataInputs/placeholderMap`
- `Expected` / `Actual` = null (a summary asserts no single expected-vs-actual value)
- `RuleSource` = the layer's existing `ORG-MST-SPEC-001 Section 9 item 7`
- `Message` = the coverage summary (format below)

### The four counts

All computed from data already in `PlaceholderLayer.Validate`, all certain pre-render.
Let `required` be the artifact type's `RequiredPlaceholders` (an empty set when the
config value is null, as it is for UQR, TCL, and PIG), `provided` be the
`placeholderMap` keys, and `canonical` be the canonical token set the layer already
builds.

- `requiredCount` = count of `required`
- `providedCount` = count of `provided`
- `missingCount` = count of tokens in `required` that are not keys of `provided`
  (the same set already emitted as fail checks)
- `beyondRequiredCount` = count of `provided` keys that ARE canonical tokens and are
  NOT in `required`. Drift-alias keys and unknown keys are deliberately excluded from
  this count: each is already its own finding, and counting them here would restate an
  existing failure as if it were extra coverage.

### Message format

One line, deterministic, naming the tokens for the two counts a reviewer acts on:

    Placeholder coverage for <documentId>: <requiredCount> required, <providedCount> provided, <missingCount> missing, <beyondRequiredCount> beyond the required set. Missing: <sorted missing tokens or "none">. Beyond required: <sorted beyond-required tokens or "none">.

Token lists are sorted with `StringComparer.Ordinal` so the output is stable across
runs and platforms. When a list is empty it reads `none`. Example for a complete DS:

    Placeholder coverage for ORG-GOV-DS-702: 10 required, 10 provided, 0 missing, 0 beyond the required set. Missing: none. Beyond required: none.

Example with one extra canonical token and one missing required token:

    Placeholder coverage for ORG-GOV-DS-702: 10 required, 10 provided, 1 missing, 1 beyond the required set. Missing: [OUT OF SCOPE USES]. Beyond required: [SUPPORT CONTACT].

### Placement in the check list

The coverage check is appended after the per-token findings, so a reader sees the
specific failures first and the summary last. Order within a layer's returned list is
preserved into the report, so appending is sufficient; no sort is needed.

## Data flow

Unchanged pipeline. `PlaceholderLayer.Validate(SemanticContext)` already receives
everything: `context.Agr.Identity.ArtifactType`, `context.Agr.DataInputs.PlaceholderMap`,
`context.Config.ArtifactTypesByCode` (carrying `RequiredPlaceholders`), and
`context.Config.CanonicalTokens`. The new counts are computed from those and one
`Check` is appended to the returned list. No new inputs, no new config, no signature
change.

## Error handling

- Artifact type not in `ArtifactTypesByCode`, or `RequiredPlaceholders` null: treat
  `required` as the empty set. `requiredCount` = 0, `missingCount` = 0, and every
  provided canonical token counts as beyond-required. The coverage check is still
  emitted (a type with no required placeholders still has meaningful provided and
  beyond-required counts).
- Empty `placeholderMap`: `providedCount` = 0, `beyondRequiredCount` = 0, and
  `missingCount` = `requiredCount`. The check is still emitted.
- The coverage check is independent of mode. It is identical in draft and promotion;
  only the per-token findings around it are mode-gated.

## Testing

Unit tests in the existing `PlaceholderLayer` test file, asserting the coverage check
specifically (find it by `Result == pass && Severity == info`, since the per-token
findings share the checkId):

1. **Complete DS**: a placeholderMap with exactly the 10 DS required tokens, all
   resolved. Coverage check present; message reports `10 required, 10 provided, 0
   missing, 0 beyond`; `Missing: none`; `Beyond required: none`.
2. **Missing one required**: drop one required token. `missingCount` = 1 and the
   dropped token is named in the message. (The existing missing-token fail check must
   also still be present; assert both.)
3. **Beyond the required set**: add one extra canonical token not required by the type.
   `beyondRequiredCount` = 1 and the token is named. Confirm a drift alias added
   instead does NOT increment `beyondRequiredCount` (it is its own fail).
4. **Null required placeholders (UQR)**: an artifact type whose `RequiredPlaceholders`
   is null. `requiredCount` = 0, `missingCount` = 0, and provided canonical tokens all
   count as beyond-required. Coverage check still emitted.
5. **Does not change overall result**: a run whose only checks are passes plus this
   coverage check computes `OverallResult.Pass`, confirming the info check is inert to
   `OverallResultPolicy`.

The golden fixtures gain this one deterministic check line on every run; regenerate
all fixture goldens once via `registrar test --all --update` and confirm the run is
idempotent afterward (a second `--all` matches). The full xUnit suite must stay green.

## Follow-up

Mark tech-debt item 29 resolved. Item 30 (cross-run history) and item 31 (declined
proposals) are unaffected.
