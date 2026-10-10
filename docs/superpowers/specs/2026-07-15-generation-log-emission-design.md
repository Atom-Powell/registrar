# generation-log.json emission - design

Date: 2026-07-15
Status: approved (design), pending implementation plan
Scope: The first half of the "output-package completion" thread. Emit
`generation-log.json` on `build`. `artifact-manifest.json` is deferred to a
follow-on slice.

## Problem

The v2 output package is specified as five files but emits three (DOCX,
`validation-report.json`, `promotion-payload.json`). `generation-log.json` and
`artifact-manifest.json` are deferred (CLAUDE.md "Output package"; ROADMAP
post-v2 backlog; tech-debt items 11 and the manifest note).

Concretely, `ArtifactRenderer` returns `RenderResult.ClauseAudits` - the
per-clause insertion record (`clauseId`, expected/actual SHA-256, verified,
tokens-resolved) - and nothing persists it. `generation-log.json` is the
intended sink (tech-debt item 11). This slice closes that gap.

`artifact-manifest.json` is out of scope here: it requires new data that does
not exist yet (a section inventory the renderer does not return, plus a
dependency map and registry references), so it becomes its own slice.

## Decisions

Three structural decisions were settled during brainstorming:

1. **Scope:** ship `generation-log.json` first; `artifact-manifest.json`
   follows as a separate slice. The log is the named sink for `ClauseAudits`
   and needs little new plumbing; the manifest needs new renderer output.
2. **Emission gating:** write the log whenever rendering was *attempted* - i.e.
   the run passed pre-render validation and `ArtifactRenderer.Render` ran. This
   covers a clean DOCX and a post-render failure (e.g. QA-SF-08 withholding the
   DOCX) alike, and skips pure pre-render validation failures, where nothing was
   generated and `validation-report.json` already tells the whole story. This is
   also exactly where `ClauseAudits` exist - they are produced only by rendering.
3. **Timing / determinism:** satisfy the spec's "timing" element with the single
   run timestamp already computed for the report (via `IClock`). No wall-clock
   stage durations. Under a fixed clock the log is byte-stable and therefore
   testable by comparison, consistent with the engine's determinism-as-governance
   property.

Two smaller sub-decisions, approved:

- **A. Self-contained file.** Config versions and timestamp are restated from
  the report rather than referenced, so `generation-log.json` stands alone like
  the other package files. `executionId` is the correlation key back to
  `validation-report.json` and `promotion-payload.json`.
- **B. Author a schema.** Add `schemas/generation-log.schema.json` and a
  conformance test, matching how the other three output files each conform to a
  schema.

## Content

`generation-log.json` maps CLAUDE.md's four listed elements onto existing data
plus the rescued `ClauseAudits`, with a small self-contained header:

| Field | JSON name | Source |
| --- | --- | --- |
| Log schema version | `logVersion` | constant (e.g. `"1.0.0"`), mirrors `reportVersion` |
| Execution correlation id | `executionId` | `report.ExecutionId` |
| Document id | `documentId` | `report.DocumentId` |
| Engine version | `engineVersion` | `report.EngineVersion` |
| Requested mode | `requestedMode` | `report.RequestedMode` |
| Run outcome | `outcome` | post-render merged `report.OverallResult` |
| Timestamp (UTC) | `timestampUtc` | `report.TimestampUtc` (same `IClock` instant) |
| Config versions loaded | `configVersions` | `report.ConfigVersions` |
| Rules applied | `rulesApplied` | distinct `checkId`s from `report.Checks` |
| Clause insertions | `clauseInsertions[]` | `RenderResult.ClauseAudits` |

`clauseInsertions[]` element shape (1:1 with `ClauseInsertionAudit`):

    { "clauseId": "CL-001",
      "expectedSha": "<hex>",
      "actualSha": "<hex>",
      "verified": true,
      "tokensResolved": 3 }

CLAUDE.md element mapping: config versions loaded -> `configVersions`; rules
applied -> `rulesApplied`; clause insertions -> `clauseInsertions`; timing ->
`timestampUtc`.

Notes:

- `rulesApplied` is a compact list of which rules ran (the distinct `checkId`
  set), not the full per-check detail; that detail lives in
  `validation-report.json`, reachable via `executionId`.
- `clauseInsertions` is an empty array on a run that rendered a document with no
  clause-insertion blocks. It is never absent when the log is emitted, because
  the log is only emitted after rendering was attempted.

## Components

New:

- `Registrar.Core.Model.GenerationLog` - the record, with `[JsonPropertyName]`
  attributes throughout and a `logVersion` constant, mirroring
  `ValidationReport`.
- `Registrar.Core.Model.ClauseInsertion` - the DTO shape for a
  `clauseInsertions[]` element, mapped 1:1 from `ClauseInsertionAudit`.
  (`ClauseInsertionAudit` stays the renderer-side type; `ClauseInsertion` is the
  serialized shape, keeping the wire contract independent of the internal type.)
- `Registrar.Core.GenerationLogSerializer` - `ToJson` / `FromJson`, reusing the
  exact `JsonSerializerOptions` as `ReportSerializer` (indented,
  `WhenWritingNull`, `UnsafeRelaxedJsonEscaping`).
- `Registrar.Core.GenerationLogBuilder` - pure assembly:
  `Build(ValidationReport mergedReport, IReadOnlyList<ClauseInsertionAudit> audits)
  -> GenerationLog`. Derives `rulesApplied` as the distinct `checkId` set from
  `mergedReport.Checks`, maps audits to `ClauseInsertion`, carries the header and
  provenance fields from `mergedReport`.
- `schemas/generation-log.schema.json` - JSON Schema for the emitted file,
  authored in the same style/draft as the other output-file schemas.

Changed:

- `Registrar.Cli.BuildCommand` - after `merged` is finalized and the report is
  written (after the `WriteOutputs(merged, ...)` call, currently line 81-82) and
  *before* the `overallResult == Fail` early return (line 84) and the
  `docxEligible` DOCX write, build the log from `merged.Report` +
  `rendered.ClauseAudits` and write `generation-log.json` to the AGR's directory.
  Placing the write ahead of every outcome branch is what makes it fire for both
  a clean DOCX and a post-render failure; it is unreachable on a pre-render
  validation failure (rendering never ran) - matching the gating decision. The
  write is inline in `BuildCommand` (it needs `rendered`, which
  `CommandSupport.WriteOutputs` does not receive), guarded by the same
  `IOException`/`UnauthorizedAccessException` handling as the sibling writes and
  returning exit code 2 on failure.

Unchanged by design:

- `ArtifactRenderer` / `RenderResult` - no new renderer output is needed;
  `ClauseAudits` already flows to `BuildCommand`.
- `CommandSupport.WriteOutputs` - keeps writing report + payload only; the log
  write lives where `rendered` is in scope.

## Data flow

    build <agr> --mode ...
      -> CommandSupport.Validate            (schema + semantic validation)
      -> pre-render gating                   (Fail / not-eligible: no render, NO log)
      -> ArtifactRenderer.Render             (produces RenderResult incl. ClauseAudits)
      -> PostRenderMerge.Apply               (merged report, post-render checks)
      -> WriteOutputs                        (validation-report.json [+ promotion-payload.json])
      -> GenerationLogBuilder.Build          (merged report + ClauseAudits)
      -> write generation-log.json           (emitted here on: clean DOCX OR post-render fail)
      -> write DOCX                          (only when docxEligible)

## Determinism

`timestampUtc` reuses the report's timestamp (one `IClock` instant per run) and
`configVersions` come from the report; no wall-clock durations are recorded. So
for a fixed clock and fixed inputs the serialized log is byte-identical run to
run, and can be asserted by byte comparison in tests, the same property the DOCX
and report already hold.

## Error handling

- The log write is wrapped in the same `catch (IOException or
  UnauthorizedAccessException)` pattern as the report/payload/DOCX writes,
  emitting `error: cannot write generation log: <message>` to stderr and
  returning exit code 2.
- `GenerationLogBuilder.Build` is total over a valid merged report: an empty
  `checks` list yields an empty `rulesApplied`; an empty audit list yields an
  empty `clauseInsertions`. No throw path is expected from assembly.

## Testing

- Unit (`GenerationLogBuilder`): `ClauseAudits` map 1:1 to `clauseInsertions`;
  `rulesApplied` is the de-duplicated `checkId` set; header/provenance fields are
  carried from the report; empty audits -> empty array.
- Unit (`GenerationLogSerializer`): `ToJson` / `FromJson` round-trip; null-field
  omission and escaping match the report serializer's behavior.
- Schema conformance: the emitted JSON validates against
  `schemas/generation-log.schema.json` (mirrors the existing report/payload
  schema-conformance tests).
- Integration (`BuildCommand`): a passing build (a DS or TCL fixture with a
  clause insertion) writes `generation-log.json` with the expected insertions and
  `outcome` = pass; a pre-render validation-failure fixture (e.g. the prohibited
  character or invalid-schema fixture) writes NO `generation-log.json`.
- Post-render-failure branch: no fixture triggers a post-render-only failure,
  because inputs are already linted pre-render by QA-ENG-05 and the only
  post-render-only defect (a character introduced during rendering) cannot be
  authored into an AGR. That branch is therefore covered structurally - the log
  write is placed before every outcome branch in `BuildCommand`, so any run that
  reached rendering emits the log regardless of the merged result - rather than
  by an end-to-end fixture. Noted here so the absence is a decision, not a gap.
- Determinism: the same fixture under a fixed clock yields a byte-identical log
  across two runs.

## Out of scope (YAGNI)

- `artifact-manifest.json` and its inputs (section inventory, dependency map,
  registry references) - the follow-on slice.
- Per-stage wall-clock durations - rejected as non-deterministic.
- Persisting `generation-log.json` on `validate` (no render) or on pre-render
  validation failures - the log is a record of a generation attempt.
- Cross-run history / repeat-failure detection (tech-debt item 30) - a separate
  architecture decision.
