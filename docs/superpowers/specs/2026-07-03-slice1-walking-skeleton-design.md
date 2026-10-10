# Slice 1: Walking Skeleton — `registrar validate` (schema layer)

Status: Approved
Date: 2026-07-03
Author: brainstormed with Claude Code

## Context

Generation Engine v2 (`registrar`) is a greenfield .NET 8 build. The repository
currently holds only `/config`, `/schemas`, `/fixtures`, and `/docs` — no source.
The full engine decomposes into five build slices (see `CLAUDE.md` build order);
each slice gets its own spec -> plan -> implementation cycle. This spec covers
**Slice 1 only**: the thinnest end-to-end slice that runs the CLI, validates one
AGR against `schemas/agr.schema.json`, and emits a conforming
`validation-report.json`.

Later slices (out of scope here): semantic validation layers (Slice 2), the
Open XML renderer (Slice 3), `build`/`hash-clauses`/promotion payload (Slice 4),
and DS+AUP end-to-end acceptance (Slice 5).

## Goal

`registrar validate <agr.json>` loads an AGR, validates it against the AGR JSON
Schema (draft 2020-12), and writes a `validation-report.json` that conforms to
`schemas/validation-report.schema.json`. This establishes the project structure,
config/schema loading, the first validation layer, the report contract, and CI —
the foundation everything else hangs off.

Non-goals: no semantic validation (registry, lifecycle, clause hashing,
placeholder resolution), no DOCX rendering, no `build` or `hash-clauses` verbs,
no promotion payload.

## Design principles honored

- **Fail closed** (rule 1): any blocking failure yields a non-zero exit and no
  downstream artifact.
- **Zero LLM calls** (rule 2): pure deterministic validation.
- **Configuration over code** (rule 3): the schema file is the authoritative
  contract; core logic hard-codes no AGR field constants.
- **Reports serve two readers** (rule 7): every failure carries `checkId`,
  `jsonPath`, `expected`/`actual`, `remediation`, and `ruleSource`.

## Architecture

### Projects

Matches CLAUDE.md's Core/Validation/Cli split; the Rendering project is deferred
to Slice 3.

- `src/Registrar.Core` — domain contracts:
  - POCOs mirroring `validation-report.schema.json`: `ValidationReport`, `Check`,
    and enums `Layer`, `CheckResult`, `Severity`, `ErrorCategory`, `Mode`,
    `OverallResult`.
  - `IClock` (returns `UtcNow`) so `timestampUtc` is injectable and tests are
    deterministic. A real `SystemClock` reads the wall clock at runtime — this is
    not an LLM call and does not violate the determinism rule; it is stamped for
    audit, not used for control flow.
  - Config-loading helper and a content-hash (SHA-256) helper.
  - `engineVersion` constant.
- `src/Registrar.Validation` — validation abstraction:
  - `IValidationLayer` interface (input -> `Check[]`), the seam Slice 2 extends.
  - `SchemaValidationLayer : IValidationLayer` wrapping JsonSchema.Net; maps each
    schema violation to a `Check`.
- `src/Registrar.Cli` — `registrar` console app:
  - `validate <agr.json>` verb via `System.CommandLine`, structured so `build`
    and `hash-clauses` verbs slot in later.
  - Orchestrates: read input -> load schema -> run layer -> assemble report ->
    write `validation-report.json` next to the input -> print console summary ->
    set exit code.
- `tests/Registrar.Tests` — xUnit.

### Dependencies

- `JsonSchema.Net` (json-everything, MIT) — draft 2020-12 support; evaluation
  output provides per-error instance location and failing keyword, used to fill
  `jsonPath`/`expected`/`actual`. Also validates the emitted report against
  `validation-report.schema.json` in tests.
- `System.Text.Json` — report serialization (no extra dependency).
- `System.CommandLine` — CLI verbs. Prerelease (3.0.0-preview.5) but the standard choice; accepted so
  the CLI surface is consistent as verbs are added.

Rejected alternatives: `Newtonsoft.Json.Schema` (no full 2020-12 support;
commercial license beyond a free quota); hand-rolling a validator (reimplements a
schema engine when the schema file is meant to be authoritative).

## Data flow

1. CLI parses `validate <path>` and reads the AGR file as text.
2. Config loader loads `agr.schema.json`; its SHA-256 is recorded as a config
   version for the report.
3. `SchemaValidationLayer` evaluates the AGR against the schema (JsonSchema.Net,
   draft 2020-12, hierarchical/detailed output).
4. Each violation becomes a `QA-SCHEMA-01` `Check`:
   - `layer` = input, `result` = fail, `severity` = blocking,
     `errorCategory` = SchemaValidationError,
   - `jsonPath` = instance location of the offending value,
   - `expected` = the failing keyword constraint (e.g. `pattern ^ORG-...$`),
   - `actual` = the offending value,
   - `remediation` = actionable instruction for the Governor,
   - `ruleSource` = `agr.schema.json`.
5. Report assembled (field sourcing below). `overallResult` = fail if any
   blocking check fails, else pass. (`pass-with-warnings` is modeled but not
   reachable until warning-producing layers exist.)
6. Report serialized to `validation-report.json` next to the input.
7. Console prints a human-readable summary.
8. Process exits with the fail-closed exit code.

## Report field sourcing

| Field | Source |
|-------|--------|
| `reportVersion` | constant `"1.0.0"` |
| `executionId` | AGR `sourceWorkflowContext.executionId` when parseable, else `"unknown"` |
| `documentId` | AGR `artifactIdentity.documentId` when parseable, else `"unknown"` |
| `requestedMode` | AGR `validationPolicy.mode` when parseable, else `"draft"` |
| `overallResult` | `fail` if any blocking check fails, else `pass` |
| `engineVersion` | `Registrar.Core` constant |
| `configVersions` | `{ "agr.schema.json": <sha256> }` — content hash, since config files carry no version field; deterministic and audit-reproducible |
| `timestampUtc` | `IClock.UtcNow` (RFC 3339 / ISO 8601 date-time) |
| `artifactSha256` | `null` (no DOCX in this slice) |
| `checks` | schema violations mapped as above; empty on a clean pass |

## Error handling and exit codes

- Clean (schema-valid) -> report written, exit `0`.
- Schema violation(s) -> report written, exit `1`.
- Malformed-JSON AGR (unparseable) -> one `QA-SCHEMA-01` fail with `jsonPath`
  null and a message identifying the parse error, report written, exit `1`.
- Missing/unreadable input file, or missing/unreadable `agr.schema.json` ->
  message to stderr, exit `2`, **no report** (nothing valid to report on).

No path produces a downstream artifact on a blocking failure (fail closed).

## Config change

Add a `QA-SCHEMA-01` entry to `config/qa-checklist.json` so the report's
"`checkId` from qa-checklist.json" contract holds:

- `checkId`: `QA-SCHEMA-01`
- `layer`: `input`
- `automation`: `full`
- `appliesTo`: `all`
- `rule`: "AGR conforms to agr.schema.json (draft 2020-12): required fields
  present, enums/patterns/const satisfied, no additional properties."
- `errorCategory`: `SchemaValidationError`
- `ruleSource`: `agr.schema.json`

## Testing

xUnit, following CLAUDE.md's rule that every checkId gets at least one passing and
one failing test named after the checkId.

- `QA-SCHEMA-01_pass` — `fixtures/agr-sample-ds-draft.json` -> `overallResult` =
  pass, zero fail checks.
- `QA-SCHEMA-01_fail` — new `fixtures/agr-sample-ds-invalid-schema.json` (e.g.
  `documentId` breaking the pattern) -> a fail check with the correct `jsonPath`,
  `expected`, and `actual`.
- `ReportConformsToSchema` — the emitted `ValidationReport`, serialized, validates
  against `schemas/validation-report.schema.json`.
- `MalformedJson_producesSingleFail` — non-JSON input -> exactly one
  `QA-SCHEMA-01` fail, exit code 1.
- Clock is injected with a fixed instant so report output is byte-stable in tests.

## New fixtures

- `fixtures/agr-sample-ds-invalid-schema.json` — a copy of the valid DS fixture
  with one deliberate schema violation, for the failing test.

## CI

GitHub Actions: `dotnet build` + `dotnet test` on the solution, against synthetic
fixtures only (no client data, per CLAUDE.md). Execution of real engagements
never happens on hosted runners.

## Acceptance criteria

1. `registrar validate fixtures/agr-sample-ds-draft.json` exits 0 and writes a
   `validation-report.json` with `overallResult` = pass and zero fail checks.
2. `registrar validate fixtures/agr-sample-ds-invalid-schema.json` exits 1 and the
   report contains a `QA-SCHEMA-01` fail with accurate `jsonPath`/`expected`/`actual`.
3. Every emitted report validates against `validation-report.schema.json`.
4. `dotnet test` passes; each behavior above is covered by a named test.
5. Malformed JSON and missing-file paths behave per the exit-code table.

## Known follow-ups (resolved)

Non-blocking items surfaced during the Slice 1 code reviews and consciously
deferred at the time. All five were resolved in commit `ee0cc56` before Slice 2
work began; the full suite (29 tests) passes. Retained here for traceability.

1. Resolved. `SchemaConstraint.Lookup` (Registrar.Validation) now calls
   `Uri.UnescapeDataString` on the schema-location fragment before
   `JsonPointer.TryParse`, with isolated `SchemaConstraintTests` covering the
   happy path, percent-encoded segments, empty fragment, unparsable pointer, and
   missing keyword.
2. Resolved. `AgrFactsTests` now includes a non-string field case
   (`documentId: 42`) asserting the degrade-to-fallback tuple, guarding against a
   future narrowing of the catch clause.
3. Resolved. `ValidateCommand.Run` (Registrar.Cli) guards the report *write*
   (`File.WriteAllText`) with `catch (IOException or UnauthorizedAccessException)
   -> exit 2`, symmetric with the read path; covered by an ACL-denied-directory
   test.
4. Resolved. `ReportSerializerTests` disposes the `JsonDocument` in the
   schema-conformance test via `using` (CA2000).
5. Resolved. `Malformed_json_exits_1_with_single_fail_check` now also asserts
   `overallResult == "fail"` and the `checkId`.
