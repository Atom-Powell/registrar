# Slice 2: Shared Clause Library reference document

Design spec. Status: approved for planning. Date: 2026-07-17.

## Goal

Generate the Shared Clause Library (SCL) as a controlled reference DOCX whose body
is a projection of `config/clause-library.json`, so the human-readable clause
reference is drift-proof against the config the engine actually uses. This is the
first of three slices in the standing-documentation effort; it delivers the config-
derived reference document that most directly serves the "data reference / source of
truth" goal.

The user's ranked goals for standing docs are, in order: no clause/token drift,
single source of truth, repeatable regeneration, and the shared controlled form
(Document Control Header, classification, visual system). Approach A (config is the
machine source of truth; the document is a generated view) is the only option that
makes drift structurally impossible rather than policed, so it was chosen.

## Scope

In scope:

- Render SCL as a controlled DOCX via a new config-sourced `aggregationTable` block.
- Author the SCL section schema and flip its `generationTarget` to true.
- The enforcement changes that make a token-bearing reference document legal.

Out of scope (YAGNI, deliberately deferred):

- PDX (Placeholder Dictionary). Held until `config/placeholder-dictionary.json` is
  reconciled against the canonical `ORG-Placeholder-Dictionary-v1.2.xlsx`. Its config
  today carries `reconciledAgainstPdxXlsx: false` and is a documented subset, so a PDX
  view generated now would faithfully render a known-incomplete set. `placeholderDictionary`
  is built as a recognized enum value in the schema but is NOT a live render source in
  this slice: it fails closed until the PDX slice, and PDX's `generationTarget` stays false.
- `copyReadyMessage` block type. Remains deferred under the Unsafe Block Exclusion Rule.
- Slice 1 (reference exemplar set of the 8 deliverables) and Slice 3 (authored
  operating-procedure docs). Separate specs.

## Design

### The config-sourced aggregationTable block

The block mirrors `clauseInsertion`: it carries a reference, not content, and the
renderer pulls canonical data from the already-loaded `ConfigBundle` at render time.
Because the AGR never carries the clause text, the document cannot diverge from config.

AGR block shape:

    { "blockType": "aggregationTable", "aggregationSource": "clauseLibrary" }

The renderer reads `clause-library.json` from `ctx.Clauses` and builds a table with
columns: clause ID, name, canonical text, SHA-256. Only `clauseLibrary` is a live
source in this slice. `placeholderDictionary` is a recognized enum value that fails
closed until the PDX slice. Any other value, or a missing `aggregationSource`, fails
closed.

The resulting AGR is thin: metadata plus one section holding one `aggregationTable`
block. All content lives in config.

This lands the first half of the deferred `aggregationTable` block type (tech-debt
item 8), scoped to config projection only. It aggregates a defined, trusted config
source, not arbitrary AGR-supplied data, which keeps it within the intent of the
Unsafe Block Exclusion Rule.

### Section schema and the governance gate

SCL is `sectionSchema.status: "not-defined-in-spec"` and `generationTarget: false`
today. This slice authors an `orderedSections` list and flips `generationTarget` to
true in `config/artifact-types.json`. Per engine rule 6, this is a documentation-owner
decision recorded in the config release, not an engine default.

Proposed SCL ordered sections:

1. Purpose
2. How Clauses Are Inserted
3. Clause Library (the aggregation section; holds the single `aggregationTable` block)
4. Version and Change Notes

### Enforcement

- **QA-ENG-06 (block-type support).** `aggregationTable` moves from the unbuilt list to
  the supported set, narrowly: valid only with a recognized `aggregationSource`. An
  unknown or missing source fails closed. The supported-set drift-guard test is updated
  in the same change so the set cannot widen silently.

- **QA-ENG-07 (token residue), scoped exemption.** Clause canonical text contains
  placeholder tokens by design (for example CL-001 contains `[TOOL / CONNECTOR NAME]`,
  and `containsPlaceholders` is true for such clauses). QA-ENG-07 exists to catch a
  deliverable that failed to resolve a token into its final value, so unmodified it
  would reject the very document whose purpose is to display the tokens. Content
  emitted by an `aggregationTable` block is therefore exempt from token-residue
  scanning. The exemption is narrow: it covers only the aggregation's rendered runs,
  never the Document Control Header and never surrounding prose, where residue still
  fails closed. This is an explicit, tested carve-out, not a blanket "reference docs
  skip QA-ENG-07."

- **QA-SF-08 (prohibited characters)** stays fully in force and is expected to pass,
  since config text is already held to the engine's output-hygiene rule.

### ID Registry dependency (external blocker)

The engine never mints IDs (rule 5). Before the real SCL document can be produced,
Registry Owner must register `ORG-MST-SCL-NNN` in the ID Registry in the expected
status. Everything in this slice is built and tested against the fixture registry;
production output of the standing SCL document waits on that one registry entry and is
not itself a code deliverable.

## Deliverables

1. **Config**
   - `config/artifact-types.json`: SCL `orderedSections` authored; `generationTarget: true`.
   - `config/qa-checklist.json`: QA-ENG-06 rule text updated to admit config-sourced
     `aggregationTable`.
2. **Schema**
   - `schemas/agr.schema.json`: add `aggregationSource` property to the block
     definition, enum `["clauseLibrary", "placeholderDictionary"]`. (`aggregationTable`
     is already in the `blockType` enum.)
3. **Core model**
   - `src/Registrar.Core/Model/AgrDocument.cs`: add `AggregationSource` to `AgrBlock`.
4. **Renderer**
   - New `AggregationTableBlock` (config-sourced), dispatched from `BlockRenderer`,
     mirroring the `clauseInsertion` case and pulling from `ctx.Clauses`.
5. **Validation**
   - QA-ENG-06 supported-set plus source-validity enforcement.
   - Scoped QA-ENG-07 exemption for aggregation runs.
   - Updated supported-set drift-guard test.
6. **Fixtures**
   - A golden SCL AGR (metadata plus one `aggregationTable` section).
   - Its ID entry in the fixture registry.

## Testing

Keyed to check IDs per repo convention (each gets a passing and a failing test):

- **QA-ENG-06**: passing = `aggregationTable` with `aggregationSource: "clauseLibrary"`
  is accepted; failing = an unknown or missing source (including `placeholderDictionary`,
  deliberately not yet a live source) fails closed.
- **QA-ENG-07 carve-out**: passing = bracketed tokens inside rendered clause text do not
  trip residue; failing = a bracketed token in surrounding prose still trips it. The pair
  proves the exemption is scoped to the aggregation, not the whole document.
- **Config fidelity**: rendered clause rows and the SHA-256 column match
  `clause-library.json` exactly, guarding that the renderer pulls from config and cannot
  silently diverge.
- **End-to-end**: build SCL in draft and promotion; assert all clauses currently in the
  library render, and that output stays byte-deterministic.

## Consequences and follow-ups

- Lands half of `aggregationTable` (tech-debt item 8), scoped to config projection.
  Arbitrary-data aggregation and `placeholderDictionary` as a live source remain future
  work.
- The PDX slice is unblocked once the placeholder dictionary is reconciled against the
  canonical xlsx: it reuses this slice's mechanism, adds `placeholderDictionary` as a
  live source, authors the PDX section schema, and flips PDX `generationTarget`.
- Production of the standing SCL document depends on `ORG-MST-SCL-NNN` existing in the
  ID Registry.
