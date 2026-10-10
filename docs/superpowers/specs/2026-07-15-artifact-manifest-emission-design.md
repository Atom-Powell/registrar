# artifact-manifest.json emission - design

Date: 2026-07-15
Status: approved (design), pending implementation plan
Scope: The second and final half of the "output-package completion" thread.
Emit `artifact-manifest.json` on `build` when a DOCX is produced.
`generation-log.json` shipped 2026-07-15 (slice 1); this closes the output
package.

## Problem

The v2 output package is specified as five files. Four are now emitted (DOCX,
`validation-report.json`, `promotion-payload.json`, `generation-log.json`).
`artifact-manifest.json` is the last deferred file (CLAUDE.md "Output package";
ROADMAP post-v2 backlog). CLAUDE.md defines it as "resolved metadata, section
inventory, dependency map, registry references, artifact SHA-256."

Grounding against the current code changed the expected shape of the work:

- **Section inventory needs no renderer change.** `ArtifactRenderer.Render`
  iterates `agr.DataInputs.Sections` 1:1, in order (ArtifactRenderer.cs:57-64);
  it injects no config-defined section structure at render time. A faithful
  section inventory is therefore derivable directly from the AGR sections.
- **The manifest does not need `RenderResult`.** The artifact hash is already on
  the merged report (`ArtifactSha256`), sections come from the AGR, dependencies
  from the AGR, and the registry reference from the promotion payload. The
  builder is a pure function of `(mergedReport, agr, payload)`.
- **Dependencies are currently dropped.** The AGR carries a top-level
  `dependencies` array (`{dependencyId, kind, blocking}`) and an
  `evidenceReferences` object, but `AgrDocument` parses neither (AgrDocument.cs).
  A dependency map requires teaching the parser to read `dependencies`.
- **Registry data is exposed only at promotion.** `ValidationOutcome` is
  `(ValidationReport Report, PromotionPayload? Payload)` (ValidationOutcome.cs).
  The resolved `RegistryRecord` is not surfaced to the CLI; the only registry
  reference available without new plumbing is `payload.registryUpdate`, present
  at promotion and absent in draft.

## Decisions

Three scoping decisions were settled during brainstorming:

1. **Section inventory: lean.** Per section, record `number`, `title`,
   `blockCount`, and the `clauseIds` inserted in that section (from its
   `clauseInsertion` blocks). No per-block-type breakdown, descriptorLine,
   internalOnly flags, or per-clause SHA.
2. **Dependencies and registry: echo, no resolution status.** Parse and echo the
   AGR's declared `dependencies` (`dependencyId`, `kind`, `blocking`). Include a
   `registryReference` from `payload.registryUpdate` at promotion; `null` in
   draft. No per-dependency resolved/unresolved map (the validation report
   already reports dependency-resolution failures).
3. **Emission gating: DOCX produced.** Emit only when a DOCX is actually written
   (pass, or pass-with-warnings in draft) - mirroring where `artifactSha256` is
   stamped. The manifest describes a real artifact, so no DOCX means no manifest.
   This is tighter than `generation-log.json` (which emits whenever rendering was
   attempted).

Sub-decisions, approved:

- **Self-contained file** with an `executionId` correlation key, like the other
  package files.
- **Author a schema** `schemas/artifact-manifest.schema.json` with a conformance
  test, matching the other four output files.

## Content

`artifact-manifest.json` fields:

| Field | JSON name | Source |
| --- | --- | --- |
| Manifest schema version | `manifestVersion` | constant `"1.0.0"` (EngineInfo) |
| Execution correlation id | `executionId` | `mergedReport.ExecutionId` |
| Document id | `documentId` | `mergedReport.DocumentId` |
| Engine version | `engineVersion` | `mergedReport.EngineVersion` |
| Timestamp (UTC) | `timestampUtc` | `mergedReport.TimestampUtc` (same IClock instant) |
| Resolved metadata | `identity`, `context` | AGR (echo) |
| Section inventory | `sections[]` | AGR `dataInputs.sections` |
| Dependency map | `dependencies[]` | AGR `dependencies` (newly parsed) |
| Registry reference | `registryReference` | `payload.registryUpdate` or null |
| Artifact hash | `artifactSha256` | `mergedReport.ArtifactSha256` |

`identity` (echo of `AgrIdentity`): `artifactType`, `documentId`, `version`,
`classification`, `approvalStatus`, `layer`.

`context` (subset of `AgrContext`): `owner`, `audience`, `preparedBy`, `client`,
`engagement`, `reviewDate`. (`reviewDateOverride` is not echoed; the manifest is
a delivered-artifact descriptor, not an approvals record.)

`sections[]` element:

    { "number": 1, "title": "Purpose", "blockCount": 1, "clauseIds": [] }

`number` is the 1-based section index (matching the renderer's ordering).
`clauseIds` lists the `clauseId` of every `clauseInsertion` block in the section,
in order; empty when the section inserts no clauses.

`dependencies[]` element (echo, 1:1 with the AGR array; empty array when the AGR
declares none, i.e. `dependencies` is null or absent):

    { "dependencyId": "ORG-MST-SCL", "kind": "clause-library", "blocking": true }

`registryReference` (promotion only; `null` in draft):

    { "documentId": "ORG-GOV-DS-016", "fromStatus": "Draft",
      "toStatus": "In Review", "version": "v1.0" }

`artifactSha256`: the SHA-256 of the emitted DOCX. Because the manifest is
emitted only when a DOCX is produced, this is always populated (never null) in a
written manifest.

## Components

New:

- `Registrar.Core.Model.AgrDependency` - `{dependencyId, kind, blocking}` with
  `[JsonPropertyName]` attributes; added to `AgrDocument` as
  `[JsonPropertyName("dependencies")] IReadOnlyList<AgrDependency>? Dependencies`.
  Additive and nullable: `AgrParser` deserializes `AgrDocument` with default
  options and ignores unmapped fields, so this begins reading an array the AGR
  already carries without affecting any existing parse.
- `Registrar.Core.Model.ArtifactManifest` and its nested records
  (`ManifestIdentity`, `ManifestContext`, `ManifestSection`,
  `ManifestDependency`, `ManifestRegistryReference`) - all with
  `[JsonPropertyName]` attributes and a `manifestVersion` constant, mirroring
  `ValidationReport` / `GenerationLog`.
- `Registrar.Core.ArtifactManifestSerializer` - `ToJson` / `FromJson`, reusing
  `ReportSerializer.Options`.
- `Registrar.Core.ArtifactManifestBuilder` - pure assembly:
  `Build(ValidationReport mergedReport, AgrDocument agr, PromotionPayload? payload)
  -> ArtifactManifest`. Section inventory from `agr.DataInputs.Sections`
  (clauseIds from each section's `clauseInsertion` blocks); dependencies from
  `agr.Dependencies ?? empty`; `registryReference` from `payload?.RegistryUpdate`.
- `EngineInfo.ManifestVersion` (`const string`, `"1.0.0"`).
- `schemas/artifact-manifest.schema.json` - JSON Schema in the same style as the
  other output-file schemas.

Changed:

- `Registrar.Cli.BuildCommand` - inside the existing `if (docxEligible)` branch,
  after the DOCX is written successfully, build the manifest from `mergedReport`,
  the parsed `agr`, and `merged.Payload`, and write `artifact-manifest.json` to
  the AGR's directory. Guarded by the same `IOException` /
  `UnauthorizedAccessException` handling as the sibling writes (exit 2). Placing
  the write after the DOCX write ties it to "a DOCX was produced": it is
  unreachable on any pre-render or post-render failure path.

Unchanged by design:

- `ArtifactRenderer` / `RenderResult` - the manifest needs no renderer output.
- `ValidationOutcome` - no new field; the builder reads `Report` and `Payload`.

## Data flow

    build <agr> --mode ...
      -> validate, pre-render gating           (fail / not-eligible: no DOCX, NO manifest)
      -> render, PostRenderMerge               (merged report, artifactSha256 stamped)
      -> write validation-report.json [+ promotion-payload.json]
      -> write generation-log.json             (rendering was attempted)
      -> if docxEligible:
           write DOCX
           -> ArtifactManifestBuilder.Build(mergedReport, agr, merged.Payload)
           -> write artifact-manifest.json      (emitted here, only with a DOCX)

## Determinism

`timestampUtc` reuses the merged report's timestamp (one `IClock` instant per
run); every other field is a pure function of the AGR, the report, and the
payload. No wall-clock, no GUIDs. For fixed inputs and a fixed clock the
serialized manifest is byte-identical run to run, asserted by a dedicated
two-run BuildCommand test (the same way `generation-log.json` is covered). Note
that `FixtureTestRunner` compares only `validation-report.json` and
`promotion-payload.json`; it does not compare `generation-log.json` or
`artifact-manifest.json`, and extending it to do so is a separate coverage
improvement, out of scope here.

## Error handling

- The manifest write uses the same `catch (IOException or
  UnauthorizedAccessException)` pattern as the report/payload/DOCX writes,
  emitting `error: cannot write artifact manifest: <message>` to stderr and
  returning exit code 2.
- `ArtifactManifestBuilder.Build` is total: an AGR with no `dependencies` yields
  an empty `dependencies` array; a null `payload` yields a null
  `registryReference`; a section with no clause blocks yields an empty
  `clauseIds`.

## Testing

- Unit (`ArtifactManifestBuilder`): identity/context echoed; section inventory
  maps 1:1 with correct `number` / `title` / `blockCount` and per-section
  `clauseIds`; dependencies echoed (and empty when the AGR declares none);
  `registryReference` populated from a promotion payload and null when payload is
  null; `artifactSha256` carried from the report.
- Unit (`AgrParser`): an AGR JSON with a `dependencies` array now round-trips
  into `AgrDocument.Dependencies` (previously dropped); absent `dependencies`
  yields null.
- Unit (`ArtifactManifestSerializer`): `ToJson` / `FromJson` round-trip; schema
  conformance of the emitted JSON against
  `schemas/artifact-manifest.schema.json`.
- Integration (`BuildCommand`): a passing draft build (AUP) writes
  `artifact-manifest.json` with the expected sections and clause references and a
  null `registryReference`; a passing promotion build (DS, with the registry
  fixture) writes it with a populated `registryReference`; a pre-render
  validation failure writes NO manifest.
- Determinism: the same fixture under a fixed clock yields a byte-identical
  manifest across two runs (a dedicated BuildCommand test; the fixture runner
  does not compare the manifest).

## Out of scope (YAGNI)

- `evidenceReferences` parsing and emission.
- Per-dependency resolution status (resolved / unresolved).
- Per-block-type breakdown, descriptorLine, internalOnly, or per-clause SHA in
  the section inventory.
- Any `ArtifactRenderer` / `RenderResult` change.
- Surfacing the resolved `RegistryRecord` for a draft-mode registry reference.
