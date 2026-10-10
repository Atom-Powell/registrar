# The Registrar: A Briefing for The Governor

**Audience:** The Governor (the Microsoft 365 Copilot declarative agent that produces
Artifact Generation Requests) and anyone tuning the Governor's instructions.
**Purpose:** Explain what The Registrar is, how the two systems divide labor, the
exact contract the Governor must satisfy, and the rules that decide whether a request
passes or fails. **State of this document:** current through Slice 8 (content lint),
2026-07-07.

This is an orientation, not the authoritative contract. The authoritative sources are
`schemas/agr.schema.json` (request shape), `config/qa-checklist.json` (checks), and
the `/config` data files (vocabularies, artifact types, clause library, placeholder
dictionary, style rules). When this briefing and those files disagree, the files win.

---

## 1. What The Registrar is

The Registrar is a deterministic, offline artifact compiler for the ORG AI Governance
Program. It consumes an **Artifact Generation Request (AGR)** - a JSON document the
Governor produces - and emits a controlled DOCX deliverable plus machine-readable
manifests and validation reports. It runs as a console application (`registrar`) inside
the MSP network. It makes **zero LLM calls at runtime**: given the same AGR and config,
it always produces the same result. Determinism is a governance property the MSP sells
to clients, so the engine never improvises.

## 2. Division of labor (do not blur this)

- **The Governor owns judgment.** Findings, risk classification, clause selection,
  narrative content, GO/NO-GO logic. The Governor runs in the tenant, can only read
  knowledge and talk, and cannot call APIs, flows, or the engine.
- **The engine owns rendering and enforcement.** It never makes a governance decision,
  and it never trusts the Governor. Every field in an AGR is re-validated against
  config. If the Governor asserts something the engine cannot verify, the engine fails
  the request rather than trusting the assertion.
- **The operator is the transport and the trigger.** A human saves the AGR JSON from
  the Governor chat, runs the CLI, reviews the validation report, and pastes any
  failures back into the Governor chat for regeneration. This human checkpoint is a
  governance control, not friction to be automated away.

## 3. The workflow loop

1. The Governor produces an AGR (JSON) in chat.
2. The operator saves the AGR to a local path and runs the engine:
   - `registrar validate <agr.json>` - checks only, no document.
   - `registrar build <agr.json> --mode draft|promotion` - checks, then renders on pass.
3. The engine writes a **validation report**. On pass it also writes the DOCX.
4. If the report has failures, the operator pastes them back into the Governor chat.
   Each failure is phrased as an instruction the Governor can execute.
5. The Governor regenerates a corrected AGR. Repeat until the report passes.

The Governor's job is to produce an AGR that passes on the first or second try. This
briefing exists so the Governor knows the rules before it generates.

## 4. The AGR: what the Governor produces

An AGR is a single JSON object. Its major sections (see `schemas/agr.schema.json` for
the full, authoritative shape):

- **`artifactIdentity`** - `artifactType` (e.g. `DS`, `AUP`), `documentId`
  (`ORG-[CATEGORY]-[CODE]-[NNN]`), `version`, `classification`, `approvalStatus`,
  `layer`.
- **`context`** - `owner`, `audience`, `preparedBy`, optional `client`, `engagement`,
  `reviewDate`, `reviewDateOverride`.
- **`dataInputs`**:
  - `placeholderMap` - a map from canonical bracketed tokens (for example
    `[CLIENT ORGANIZATION NAME]`) to their resolved values.
  - `sections` - an ordered list; each section has a `title`, optional
    `descriptorLine`, and an ordered list of `blocks`.
  - A block has a `blockType` and the fields that type needs (see Section 6).
- **`validationPolicy`** - `mode` (`draft` or `promotion`) and optional
  `warningThreshold`.
- **`renderingOptions`** - `packageName`, `outputFileName`.

The Governor supplies the content and structure; the engine supplies the styling,
chrome (header table, Document Control Header, Configuration callout, footer), and all
enforcement.

## 5. The rules the Governor must follow (this is the important part)

These are the behaviors that most often decide pass versus fail. Each maps to one or
more checks in `config/qa-checklist.json`.

### 5.1 Insert clauses by reference, never by prose (QA-ENG-04, QA-SF-11)
Controlled clause text lives in `config/clause-library.json`. To include a clause, the
Governor emits a `clauseInsertion` block carrying only the `clauseId` (for example
`CL-006`). **The engine ignores any clause prose the Governor writes and inserts the
canonical text itself, proving it with SHA-256.** Paraphrasing a clause is structurally
impossible: do not try. If a clause is required for the artifact type, it must be
present as a `clauseInsertion` block.

### 5.2 Use canonical placeholder tokens, and do not leave tokens in body prose (QA-SPEC-07, QA-ENG-07)
Placeholder tokens must be canonical tokens from the placeholder dictionary. Two
distinct rules:
- **Placeholder map:** every token used should be a known canonical token; unknown
  tokens warn in draft and fail on promotion. A token intentionally left for the client
  to complete may be null in `placeholderMap` in draft only.
- **Body prose is rendered verbatim and is NOT placeholder-substituted.** Only clause
  text resolves tokens at render time. If the Governor writes a bracketed token such as
  `[CLIENT ORGANIZATION NAME]` into a paragraph, bullet, heading, or table cell, it will
  ship literally into the document. **QA-ENG-07 fails any such residue in both draft and
  promotion.** Put dynamic values into the prose as their resolved text, or carry them
  through a clause; never leave a raw token in body content.

### 5.3 Character hygiene (QA-ENG-05, QA-SF-08)
Free-text fields and placeholder values may contain only printable Basic Latin,
printable Latin-1 Supplement (excluding non-breaking space and soft hyphen), and the
approved glyph allowlist. **Prohibited: em dashes, en dashes, the ellipsis character,
smart quotes, emojis, and any decorative unicode.** Use a plain hyphen, straight quotes,
and three periods. Accented names are fine. This blocks in both modes.

### 5.4 Heading structure (QA-SF-07)
Each section is an implicit H1. Two rules, enforced in both modes:
- **No empty H1:** every section must contain at least one non-heading content block
  (paragraph, bulletList, table, or clauseInsertion). A section made only of `h2`/`h3`
  headings, or with no blocks, fails.
- **No heading-level skips:** an `h3` must not appear without a preceding `h2` in the
  same section. Do not use `h3` as a decorative subtitle under an H1.

### 5.5 Section titles and order (QA-ENG-02)
For an artifact type whose section schema is defined, the section titles must match the
schema exactly and in order: no additions, omissions, or reordering. The Governor cannot
invent sections. If a type has no defined section schema, the engine refuses to generate
it at all (see Section 8).

### 5.6 Warn callouts in client-facing output (QA-SF-09)
Client-facing artifacts must contain zero warn (red) callouts at promotion. `warnCallout`
blocks render as of 2026-07-15 (see Section 6); QA-SF-09 is what keeps them out of
client-facing output at promotion, not a rendering gap.

### 5.7 Identity, registry, and lifecycle (QA-SF-03, QA-SF-04, QA-ENG-03, QA-SPEC-18)
- The `documentId` must follow `ORG-[CATEGORY]-[CODE]-[NNN]` with a valid category/code
  pairing, and must already exist in the ID Registry in the expected status. **The
  engine never mints IDs;** Registry Owner owns the registry. The Governor must use
  an ID that already exists.
- The embedded category and code must match the `artifactType` and the type's configured
  category.
- `approvalStatus` must be one of the controlled values, must match the registry state,
  and the requested transition must be legal per the lifecycle transition map.
- `reviewDate` must be ISO 8601 and within the category default, or carry a logged
  documentation-owner override.

## 6. Block types

The engine currently renders these block types:

| blockType | Fields the Governor provides |
|---|---|
| `paragraph` | `text` |
| `h2`, `h3` | `text` |
| `bulletList` | `items` (string array) |
| `table` | `columnHeaders` (string array), `rows` (array of string arrays) |
| `clauseInsertion` | `clauseId` (canonical clause text is inserted by the engine) |
| `radioQuestion` (landed 2026-07-14) | `question` (`number`, `prompt`, `required`, `options` - four-option minimum; the engine applies the U+25CB glyph and layout) |
| `warnCallout` (landed 2026-07-15) | `text` (the label is config-driven, not Governor-supplied); QA-SF-09 still fails any of these in client-facing output at promotion |

These block types are defined in the schema but **not yet renderable** (QA-ENG-06 fails
closed on any of them, in both modes): `aggregationTable`, `copyReadyMessage`. Do not
use them until a future engine version enables them.

## 7. Modes: draft vs promotion

- **Draft** tolerates warnings: a pass-with-warnings result still produces a DOCX so the
  operator can review a work in progress. Unknown placeholder tokens and intentionally
  retained (null) placeholders are permitted in draft.
- **Promotion** is strict: warnings that are acceptable in draft become blocking, the
  document is only produced on a clean pass, and a `promotion-payload.json` (the registry
  update instructions) is emitted for the operator to apply.

Structural and safety violations (character hygiene, heading structure, token residue,
unregistered ID, illegal lifecycle transition, unsupported block type) fail closed in
**both** modes.

## 8. What the engine can produce today (through Slice 8)

- **Fully rendered end to end:** `DS` (Deployment Summary) and `AUP` (Acceptable Use
  Policy). Both have defined section schemas and render with full chrome and numbered
  body sections.
- **Section schema defined, not yet rendered:** `UQR`, `TCL`. The Governor can prepare
  for these, but the engine does not render them yet (TCL also needs the radio-option
  selection pattern).
- **Types without a defined section schema:** any artifact type whose
  `sectionSchema.status` is not `defined` in `artifact-types.json`. The engine refuses to
  generate these with a ConfigurationError until a documentation owner authors and approves
  a section schema. Section schemas are defined for `DS`, `AUP`, `UQR`, `TCL`, `PIG`,
  `DCN`, `TAK`, and `RAF`. **Rule: the engine does not invent structure.**
- **Enforcement in place:** schema validation, vocabulary and semantic checks, ID
  registry agreement, lifecycle state machine, clause insertion by reference with
  SHA-256, input character hygiene, heading structure, the client-facing warn-callout
  gate, and body-prose token-residue detection.

## 9. The output package (per run)

Emitted today:
- **Generated DOCX** - only on pass (or pass-with-warnings in draft mode).
- **`validation-report.json`** - always emitted; conforms to
  `schemas/validation-report.schema.json`.
- **`promotion-payload.json`** - the registry update payload and repository target,
  emitted only when validation passes in promotion mode.
- **`generation-log.json`** - config versions loaded, rules applied, clause insertions,
  timing; written whenever rendering was attempted, correlated to
  `validation-report.json` by `executionId`, conforming to
  `schemas/generation-log.schema.json`.
- **`artifact-manifest.json`** - resolved metadata, section inventory, dependency map,
  registry references, artifact SHA-256; emitted only when a DOCX is produced,
  correlated to the other package files by `executionId`, conforming to
  `schemas/artifact-manifest.schema.json`.

## 10. How to read and act on a validation report

Every failure in `validation-report.json` carries five fields the Governor should use:
- **`checkId`** - the rule that failed (for example `QA-ENG-07`).
- **`jsonPath`** - where in the AGR the problem is (for example
  `/dataInputs/sections/0/blocks/0/text`).
- **`expected`** and **`actual`** - what the engine required versus what it found.
- **`remediation`** - a plain instruction the Governor can execute to fix it.
- **`ruleSource`** - the governing document section, for traceability.

The Governor's regeneration should target the `jsonPath` and follow the `remediation`
verbatim. Fix the specific field; do not restructure the whole artifact.

## 11. Where the authoritative definitions live

- `schemas/agr.schema.json` - the AGR request contract.
- `schemas/validation-report.schema.json` - the report contract.
- `config/qa-checklist.json` - every check the engine runs, with its rule text and
  source.
- `config/artifact-types.json` - artifact types, categories, ID patterns, section
  schemas, required clauses and placeholders, classification.
- `config/clause-library.json` - canonical clause text and hashes.
- `config/placeholder-dictionary.json` - canonical placeholder tokens and known drift
  aliases.
- `config/vocabularies.json` - classifications, approval statuses, lifecycle transitions,
  review intervals.
- `config/style-rules.json` - visual identity and content-lint rules.

For what is already built, see `docs/DELIVERED.md`; for what remains and the endpoint
it runs to, see `docs/ROADMAP.md`; for deferred non-blocking follow-ups, see
`docs/tech-debt.md`.

---

### Quick reference: the ten things most likely to fail a request

1. Paraphrasing a clause instead of using its `clauseId`.
2. Leaving a raw `[TOKEN]` in paragraph, bullet, heading, or table text.
3. Em dashes, en dashes, smart quotes, or an ellipsis character anywhere in text.
4. A section with a heading but no body content, or an `h3` with no preceding `h2`.
5. Section titles that do not match the type's schema exactly and in order.
6. A `documentId` that is not in the ID Registry, or not in the expected status.
7. A category/code in the ID that does not match the `artifactType`.
8. An illegal `approvalStatus` transition.
9. Using a not-yet-renderable block type (`aggregationTable`, `copyReadyMessage`).
10. Requesting generation of a type whose section schema is not yet defined in
    `artifact-types.json`.
