# Slice 4: Promotion Payload and QA-SPEC-18 - Design

**Status:** Approved (design)
**Date:** 2026-07-04
**Predecessor:** Slice 3 (ID Registry and lifecycle enforcement)

## Goal

On a passing promotion-mode validate that has a registry, emit
`promotion-payload.json` - the registry-update payload and repository target the
operator applies before final delivery - and wire QA-SPEC-18, the assisted
registry-update check. This closes the third registry check (after QA-SF-03 and
QA-SF-04) and produces the fifth engine output file (CLAUDE.md output package
item 5).

Note: the Slice 3 spec's deferred-items note referred to this check as
"QA-SF-18"; the correct catalog ID is QA-SPEC-18 (there is no QA-SF-18). Slice 4
uses QA-SPEC-18.

## Scope decisions (locked)

1. **promotion-payload.json + QA-SPEC-18 only.** Renderer-free. The
   artifact-manifest.json, generation-log.json, and the DOCX renderer are
   deferred to later slices.
2. **Repository target from a config map.** A new `config/repository-targets.json`
   maps category to a controlled-documents repository path, consistent with
   configuration-over-code. The actual category-to-path values trace to ADR-001
   and should be ratified by the documentation owner; the file ships with
   provisional values and a `reconciledAgainstAdr001: false` flag (mirroring the
   placeholder-dictionary's `reconciledAgainstPdxXlsx` pattern).
3. **Payload validates against a schema.** `schemas/promotion-payload.schema.json`
   is authored so the emitted payload is checked the same way the report is.
4. **QA-SPEC-18 is `pass`-with-attestation.** "Assisted" means the engine verifies
   what it can and surfaces an operator attestation line. On a promotion run with
   a coherent registry, QA-SPEC-18 emits an explicit `pass` check carrying the
   attestation, rather than passing by absence - an affirmative audit record that
   the update was computed and the operator must apply it.

## Non-goals (deferred, not silently dropped)

- **Version-supersede chains.** When a new version is generated, flipping the
  prior version's registry entry to Superseded is a Registrar concern; the
  payload records this artifact's own status transition and version, and notes
  supersede as out of engine scope.
- **Post-update verification.** QA-SPEC-18's "verifies post-update state" needs a
  registry write-back path (SharePoint list); in the file-system-only v2 the
  engine emits the payload and the operator attests. No auto-verify.
- **artifact-manifest.json, generation-log.json, the DOCX renderer.**

## Architecture

Slice 3 established the registry (`IIdRegistry`, `RegistryRecord`) and the
release-tier layers. Slice 4 adds one more release-tier layer, a payload builder,
a payload model + schema, a config map, and a change to how `ValidationService`
returns its result so the CLI can write a second output file.

### New and changed components

- **`config/repository-targets.json`** - `{ "reconciledAgainstAdr001": false,
  "targetsByCategory": { "GOV": "...", "TRN": "...", "RSK": "...", "OPS": "...",
  "INT": "...", "MST": "..." } }`. Provisional paths derived from the ADR-002
  layer names; values pending ADR-001 ratification.
- **`ConfigBundle` extension** - load `RepositoryTargetsByCategory`
  (`IReadOnlyDictionary<string,string>`) and hash the file into `configVersions`.
- **`schemas/promotion-payload.schema.json`** - the payload contract (see Payload
  shape).
- **`PromotionPayload`** model (`src/Registrar.Core/Model/`) - a typed, serializable
  record matching the schema.
- **`PromotionPayloadSerializer`** (or reuse `ReportSerializer.Options`) - emits
  indented JSON with the report's serializer settings.
- **`PromotionPayloadBuilder`** (`src/Registrar.Validation/`) - builds a
  `PromotionPayload` from the validated `AgrDocument`, the matching
  `RegistryRecord`, the config map, and the `IClock`.
- **`PromotionLayer`** (QA-SPEC-18, release tier, `ISemanticLayer`) - emits the
  assisted check (see Check behavior).
- **`ValidationOutcome`** - `sealed record ValidationOutcome(ValidationReport
  Report, PromotionPayload? Payload)`. `ValidationService.Validate` now returns
  this. `Payload` is non-null only when mode is promotion and the overall result
  is pass (which, in promotion with a registry, guarantees the ID exists, the
  transition is legal, and the repository target resolves).
- **`ValidateCommand`** - writes `validation-report.json` as today; additionally
  writes `promotion-payload.json` next to it when `outcome.Payload` is non-null.

### Payload shape

```
{
  "payloadVersion": "1.0.0",
  "executionId":   "...",           // sourceWorkflowContext.executionId
  "documentId":    "ORG-GOV-DS-016",
  "artifactType":  "DS",
  "version":       "v1.0",
  "generatedUtc":  "2026-07-03T12:00:00.0000000+00:00",  // IClock
  "registryUpdate": {
    "documentId": "ORG-GOV-DS-016",
    "fromStatus": "Draft",          // registry current
    "toStatus":   "In Review",      // AGR approvalStatus (target)
    "version":    "v1.0"
  },
  "repositoryTarget": "Controlled Documents/Governance",  // config map by category
  "attestation": {
    "checkId": "QA-SPEC-18",
    "statement": "Operator must apply this registry update to the PMC Registry sheet and confirm post-update state before final delivery.",
    "requiresOperatorSignoff": true
  }
}
```

### QA-SPEC-18 check behavior (`PromotionLayer`)

- **draft** -> `skipped` (no promotion, no payload).
- **promotion, registry absent** -> `skipped` (QA-SF-03 already fails the run).
- **promotion, registry present but Document ID not found** -> `skipped`
  (QA-SF-03 owns the existence failure).
- **promotion, registry present, ID found, repository target missing for the
  category** -> blocking `RegistrySyncError` (fail closed: cannot emit a complete
  payload).
- **promotion, registry present, ID found, repository target resolves** ->
  explicit `pass` check whose message/remediation carries the attestation line.

Because the config map covers all six categories, the missing-target path is a
config-regression guard, not an expected operating state.

### Data flow

```
registrar validate <agr.json> [--registry <path>]   (mode from the AGR)
  ... schema gate + Slice 2 layers + Slice 3 registry layers ...
  + PromotionLayer (QA-SPEC-18)
  -> OverallResultPolicy -> overall
  if mode == promotion and overall == pass:
       PromotionPayloadBuilder -> PromotionPayload
  -> ValidationOutcome(report, payload?)
  CLI: write validation-report.json (always, on exit 0/1)
       write promotion-payload.json  (only when payload != null)
```

Exit codes unchanged (0 pass / pass-with-warnings, 1 fail, 2 usage/IO).

## Interpretation notes

- **Payload emitted only on overall pass in promotion.** A `pass-with-warnings`
  promotion still emits the payload (warnings do not block promotion); a `fail`
  never does. Draft never does. Fail closed.
- **Category source.** The repository target is keyed by the category segment of
  the Document ID (the `ORG-<CATEGORY>-<CODE>-<NNN>` shape the schema guarantees),
  consistent with how ReviewDateLayer and MetadataLayer already read the category.
- **`ValidationOutcome` return change** touches existing Slice 1-3 service and CLI
  tests, which read `.OverallResult`/`.Checks` off the return value directly; they
  become `.Report.OverallResult`/`.Report.Checks`. Mechanical, and included in the
  plan.

## Testing

Per CLAUDE.md, every QA check ID gets a passing and a failing test named after it;
QA-SPEC-18 gets its pass (promotion, attestation) and fail (missing repository
target) cases, plus the draft-skipped case.

- **ConfigBundle:** `RepositoryTargetsByCategory` loads; the file hash lands in
  `configVersions`.
- **PromotionPayloadBuilder:** builds the correct fields (identity, from/to
  status, target, attestation) from a validated AGR + registry record.
- **Payload schema conformance:** an emitted payload validates against
  `promotion-payload.schema.json`.
- **PromotionLayer:** draft skipped; promotion pass-with-attestation; missing
  repository target -> blocking RegistrySyncError.
- **ValidationService:** promotion + pass -> `Outcome.Payload` non-null with the
  right fields; draft -> null; promotion + fail -> null.
- **CLI:** a passing promotion run writes `promotion-payload.json`; a draft run
  does not; a failing promotion run does not.
- **Existing tests:** updated for `ValidationOutcome`.

### Fixtures

- `fixtures/agr-sample-ds-promotion.json` - the golden DS AGR with
  `validationPolicy.mode: "promotion"`, a `reviewDate` within the GOV interval,
  and `approvalStatus: "In Review"` (a legal Draft -> In Review transition against
  the sample registry). Passes all checks so the payload is emitted.
- Reuses `fixtures/registry-sample.csv` (ORG-GOV-DS-016 = Draft).

## Component boundaries summary

- `PromotionPayload` / schema - the payload contract; depends on nothing.
- `PromotionPayloadBuilder` - assembles the payload; depends on `AgrDocument`,
  `RegistryRecord`, `ConfigBundle`, `IClock`.
- `PromotionLayer` - QA-SPEC-18 assisted check; depends on `SemanticContext`.
- `ValidationOutcome` - report + optional payload wrapper.
- `ValidationService` - orchestration; the only component that decides when a
  payload is built.
- `ValidateCommand` - writes the two output files.
