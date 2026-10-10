# The Registrar (Generation Engine v2)

Governed artifact compiler for the ORG AI Governance Program. Consumes Artifact
Generation Requests (AGRs) produced by the Governor, a Microsoft 365 Copilot
declarative agent, and emits controlled DOCX deliverables plus machine-readable
manifests and validation reports. This file is the architectural constitution:
Claude Code should treat every rule here as binding unless the repo owner
explicitly amends it.

## Division of labor (do not blur this)

- The Governor owns judgment: findings, risk classification, clause selection,
  narrative content, GO/NO-GO logic. It runs in the MSP tenant and can only
  read knowledge and talk. It cannot call APIs, flows, or this engine.
- The engine owns rendering and enforcement. It NEVER makes a governance
  decision, and it NEVER trusts the Governor. Every field is re-validated.
- The operator is the transport and the trigger: saves the AGR JSON from the
  Governor chat, runs the CLI, reviews the validation report, pastes failures
  back into the Governor chat for regeneration. This human checkpoint is a
  governance control, not friction to be automated away.
- The operator window (`Registrar.App`) is a front end over the same engine as
  the CLI. It never edits an AGR: the AGR JSON saved from the Governor chat is
  the file, and the operator's transport role above applies unchanged to both
  surfaces.

## Non-negotiable engine rules

1. Fail closed. A controlled-document violation (missing/invalid Document
   Control Header field, unregistered ID, illegal lifecycle transition,
   clause paraphrase) never produces a deliverable. No partial output.
2. Zero LLM calls at runtime. Determinism is a governance property the MSP
   sells to clients. Adding any model call to the generation path is a
   design change requiring an ADR and owner review, not a refactor.
3. Configuration over code. Artifact structure, vocabularies, clauses,
   placeholders, style rules, and QA checks live in /config and /schemas as
   versioned data. Core logic contains no artifact constants, no repository
   paths, no clause text. A v1.3 template pack must be a config release.
4. Clause insertion is by reference only. The AGR carries clauseId; the
   engine inserts canonical text from config/clause-library.json and proves
   it via SHA-256. Governor-supplied clause prose is ignored by design so
   paraphrase drift is structurally impossible.
5. The ID Registry is authoritative state. The engine verifies the Document
   ID exists in the registry in the expected status before rendering and
   refuses promotion on any metadata/registry disagreement. The engine never
   mints IDs; Registry Owner owns the ID Registry.
6. Do not invent structure. Artifact types whose sectionSchema.status is
   "not-defined-in-spec" in config/artifact-types.json refuse generation with
   a ConfigurationError. Authoring a section schema is a documentation-owner
   decision, not an engine default.
7. Validation reports are for two readers: the operator and the Governor.
   Every failure carries checkId, jsonPath, expected/actual, remediation
   phrased as an instruction the Governor can execute, and ruleSource citing
   the governing document section.
8. Output hygiene: US English; no emojis; no em/en dashes or decorative
   unicode in generated documents (enforced by QA-SF-08, but do not write
   them into config strings or templates either).

## Stack and environment

- .NET 8, C#, Open XML SDK (DocumentFormat.OpenXml). DOCX is built from
  scratch in code from config-defined structure. No seed .docx, no
  third-party template engines.
- Target OS Windows 11; dev shell PowerShell 7.x. Do not assume Linux paths
  in scripts or docs.
- Delivery: a console app (registrar) and an operator window (Registrar.App),
  distributed together via GitHub Releases and executed inside the MSP network.
  Execution never happens on GitHub-hosted runners: client engagement data must
  not leave the tenant boundary. CI builds and tests with synthetic fixtures
  only. The CLI is retained in full as the fallback and the scripting surface;
  it is not to be reduced as the window grows.
- I/O: reads AGR JSON from a local path (typically a OneDrive-synced
  SharePoint library folder); writes the output package next to it.
  SharePoint Graph API integration is a later phase behind an interface;
  v2.0 is file-system only.

## Output package (per run)

The v2 output package is five files. Nothing in the output package remains
deferred.

1. Generated DOCX (only on pass, or pass-with-warnings in draft mode)
2. validation-report.json - conforms to schemas/validation-report.schema.json;
   emitted on every run
3. promotion-payload.json - registry update payload and repository target,
   conforms to schemas/promotion-payload.schema.json (emitted only when
   validation passes in promotion mode)
4. generation-log.json - config versions loaded, rules applied, clause
   insertions, timing; conforms to schemas/generation-log.schema.json;
   emitted whenever rendering was attempted
5. artifact-manifest.json - resolved metadata, section inventory,
   dependency map, registry references, artifact SHA-256; conforms to
   schemas/artifact-manifest.schema.json; emitted only when a DOCX is
   produced

## Repo layout

    /src            engine source. Core has no project references; Validation
                    and Rendering are siblings above it and reference neither
                    each other nor Pipeline; Pipeline composes Validation and
                    Rendering; Cli and App sit on top. Anything that must both
                    validate and render belongs in Pipeline, not in Core.
                    App is the operator window (net8.0-windows, WPF hosting
                    Blazor). It is a peer of Cli, not a layer above it: no
                    engine logic may live there, and Cli must never reference
                    it. Whatever both need belongs in Core or Pipeline.
    /schemas        agr.schema.json, validation-report.schema.json
    /config         vocabularies, artifact-types, clause-library,
                    placeholder-dictionary, style-rules, qa-checklist
    /fixtures       synthetic sample AGRs (valid + one per failure category)
    /tests          xUnit; every QA check ID gets at least one passing and
                    one failing test named after the checkId

## Build order

1. Contracts: schema validation (agr.schema.json), vocabulary/semantic
   validation, lifecycle state machine, registry adapter (CSV/xlsx export of
   the ID Registry as interim source until the SharePoint list exists).
2. Validation layers: input -> content -> structural -> release, each
   producing checks[] entries; wire qa-checklist.json as the check catalog.
3. Renderer: universal components first (header table, DCH, Configuration
   callout, footer, warn callout), then block renderers (paragraph, tables,
   radio pattern with U+25CB), then section assembly per artifact-types.json.
4. CLI: registrar validate <agr.json>, registrar build <agr.json> --mode
   draft|promotion, registrar hash-clauses (recompute/verify clause hashes).
   Recorded 2026-08-05: hash-clauses was never implemented. The shipped verbs
   are validate, build, menu, test, doctor, emit-knowledge, emit-tokens, and
   clause-hash verification lives inside doctor. Left in the build order as
   written, since this section is the original plan of record.
5. Fixtures and tests keyed to QA check IDs; render DS and AUP end-to-end
   as the acceptance milestone (both have fully defined section schemas).

## Known gaps (do not silently resolve)

- config/placeholder-dictionary.json is now the canonical placeholder source.
  The ORG-MST-PDX xlsx it was historically meant to be reconciled against was
  never located; on 2026-07-20 the repo owner ruled this config file
  authoritative instead. Unknown tokens still warn in draft and fail on
  promotion via PlaceholderLayer (QA-SPEC-07) mode-gating, unchanged by this
  decision. The reconciledAgainstPdxXlsx flag it once carried is retired.
- Section schemas are now defined for every generation target: DS, AUP, UQR,
  TCL, PIG, DCN, TAK, RAF. RAF form fields such as the Form ID and
  originating-runbook reference are not yet placeholder-dictionary tokens;
  adding them is an optional follow-up.
- The universal closing elements (Distribution and Follow-up; the
  prepared-by/companion line) are not rendered. They are not defined in config
  and rule 6 forbids inventing them. Blocked on ORG-MST-SPEC-001 Section 4.1
  content and a documentation-owner ruling; tech-debt item 10.
- The ID Registry lives in a workbook until the SharePoint list migration; the
  registry adapter must be swappable behind an interface.
- SCL and PDX framework layers were extended to Administration during GUI-1
  rather than ratified with the other eight rows on 2026-08-01. Nothing was
  overridden, but both are now enforced by a blocking check (QA-ENG-08), so the
  owner confirmation is still wanted.

Design consequences of delivered work that the delivery record does not carry:

- All ten AGR block types render, so the Unsafe Block Exclusion Rule excludes
  nothing and BlockRenderer's default branch is now a guard for unknown or
  future types. Because every schema-enum block type is engine-supported,
  QA-ENG-06's block-type fail path is no longer reachable through the full
  pipeline (QA-SCHEMA-01 rejects an unknown blockType first); it is now
  defense-in-depth, stays unit-tested directly, and its aggregationSource
  sub-checks remain live and reachable.
- QA-ENG-07 exempts aggregationTable but scans copyReadyMessage, and the
  asymmetry is deliberate: an aggregationTable is projected from config at
  render time and legitimately carries the [TOKEN] slots a clause library
  documents, whereas a copyReadyMessage carries operator-facing text copied
  verbatim to the client, so an unresolved token there must fail closed.
- QA-SF-09 covers internal-only notes through an optional internalOnly flag on
  a content block: it fails any internalOnly block in a client-facing artifact
  at promotion, and renders normally in internally-classified artifacts since
  the flag rides on an otherwise-supported block.
- The engine's own source is held to the same escapes-only character rule as
  Governor input (QA-ENG-05, InputHygieneLayer) by SourceHygieneTests.
- QA-ENG-08 (LayerAgreementLayer) enforces artifactIdentity.layer per type
  rather than per category, because ADR-002 places Operational Runbooks in the
  Governance layer while UQR and DCN, in the same OPS category, are Delivery.
- Deferred, non-blocking follow-ups from completed slices are tracked in
  docs/tech-debt.md; shipped work is recorded in docs/DELIVERED.md and is not
  repeated here.

