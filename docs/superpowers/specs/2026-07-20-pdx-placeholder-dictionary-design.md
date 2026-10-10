# PDX: Placeholder Dictionary reference document

Design spec. Status: approved for planning. Date: 2026-07-20.

## Goal

Generate the Placeholder Dictionary (PDX, doc code ORG-MST-PDX) as a controlled
internal reference DOCX whose body is a projection of
`config/placeholder-dictionary.json`, so the human-readable token reference is
drift-proof against the config the engine actually validates against. This is the
twin of the Shared Clause Library (SCL) slice delivered 2026-07-17: same
config-as-source-of-truth model, same config-sourced `aggregationTable` mechanism.

This slice also resolves a standing provenance question. The canonical
`ORG - Placeholder Dictionary - v1.2.xlsx` (ORG-MST-PDX) was never among the
program's source documents and is not present anywhere in the repository or the
ORG workspace. Rather than author a new spreadsheet as a second home for the same
fact, the owner has ruled that `config/placeholder-dictionary.json` is the
canonical source. The generated PDX document becomes the human-readable view of
that config, exactly as the SCL document is the view of the clause library. This
makes token drift structurally impossible rather than policed, and it retires the
long-standing "reconcile against the xlsx" gate by removing its premise.

## Scope

In scope:

- Render PDX as a controlled DOCX via the existing config-sourced
  `aggregationTable` block, projecting the placeholder dictionary.
- A token completeness audit that verifies the canonical set against every token
  actually referenced in the repository, reported as an evidence table before the
  dictionary is declared canonical.
- Author the PDX section schema and flip its `generationTarget` to true.
- Declare `config/placeholder-dictionary.json` canonical: drop the
  `-extracted-subset` version suffix and replace the dead reconciliation flag with
  honest provenance.
- The enforcement wiring that makes `placeholderDictionary` a live aggregation
  source and keeps a token-bearing reference document legal.

Out of scope (YAGNI, deliberately deferred):

- Per-token authored definitions. The owner chose render-what-config-has: the
  document shows only the fields config carries today (token, drift aliases,
  usage). A definitions column is a possible later enrichment slice and would
  require authoring judgment content for every token.
- Wiring any flag to gate behavior at runtime. The former
  `reconciledAgainstPdxXlsx` flag has no live consumer; giving it one is invented
  scope.
- Migrating the placeholder dictionary out of the interim JSON into any external
  system. Config in git is the versioned home per engine rule 3.

## Design

### The token completeness audit (reconciliation, reshaped)

With config declared canonical, "reconciliation" is no longer a diff against an
external workbook. It becomes a completeness audit against the only ground truth
that exists in the repository: every `[TOKEN]` actually referenced across fixtures,
the config's own drift aliases, and the docs that quote spec sections. The source
specs (ORG-MST-SPEC-001, ORG-MST-WFE-001) are, like the xlsx, not present in the
repo; they survive only as citations, so they cannot be swept directly.

The audit sweeps those repository sources, diffs the distinct tokens against the
canonical set in `config/placeholder-dictionary.json`, and records the result as an
evidence table in the implementation plan. An early sweep of fixtures found 18
distinct tokens, all already inside the canonical 19 (the extras being a known
drift alias and a deliberate bad-token test fixture), so the expected delta is
near zero. If the sweep surfaces a genuinely missing canonical token, it is
brought to the owner for a ruling, not added silently: adding a canonical token is
a judgment call, not an engine default. The audit is a documentation deliverable,
not code.

### The config-sourced aggregationTable block, placeholderDictionary source

The mechanism already exists from the SCL slice. `AggregationTableBlock` today
renders only `clauseLibrary`; it hard-fails any other source with an
`InvalidOperationException` on the reasoning that QA-ENG-06 blocks a non-live
source before the renderer is reached. This slice adds a `placeholderDictionary`
branch.

AGR block shape (unchanged form, new source value):

    { "blockType": "aggregationTable", "aggregationSource": "placeholderDictionary" }

The block carries only the source. The renderer projects the placeholder
dictionary from config at render time and builds a table with columns:

    Token | Drift Aliases | Usage

Drift aliases render as the comma-joined `knownDriftAliases` list (empty cell when
none). Usage renders the token's `usage` tag (`footer`, `header table`) or an
empty cell when the token carries none. Because the AGR never carries the token
data, the document cannot diverge from config.

The placeholder dictionary is already loaded into `ConfigBundle.CanonicalTokens`
for use by the validation layer (`PlaceholderLayer`). It is not yet plumbed to the
renderer, so this slice adds it to `RenderContext`.

### Section schema and the governance gate

PDX is `sectionSchema.status: "not-defined-in-spec"` and `generationTarget: false`
today, sitting as a stub next to the fully defined SCL entry. This slice authors an
`orderedSections` list and flips `generationTarget` to true in
`config/artifact-types.json`. Per engine rule 6, this is a documentation-owner
decision recorded in the config release, not an engine default.

Proposed PDX ordered sections, mirroring SCL:

1. Purpose
2. How Placeholders Work (token rules: square brackets, uppercase, the
   drift-alias reporting behavior where the engine names the canonical
   replacement and never silently auto-corrects, unresolved-in-client-facing
   prohibited, unresolved-in-draft warned)
3. Placeholder Dictionary (the aggregation section; holds the single
   `aggregationTable` block with `aggregationSource: "placeholderDictionary"`)
4. Version and Change Notes

### Declaring config canonical, and the reconciliation flag

`config/placeholder-dictionary.json` currently frames itself as a verified subset
extracted from the specs, carries `dictionaryVersion: "v1.2-extracted-subset"`,
and holds `reconciledAgainstPdxXlsx: false` with a note describing it as
provenance-only and read by no code.

This slice declares the file canonical:

- `dictionaryVersion` drops the `-extracted-subset` suffix (becomes `v1.2`).
- `reconciledAgainstPdxXlsx` and its note are removed and replaced by an honest
  provenance note. The former flag would falsely imply a reconciliation event if
  flipped to true; instead the note records what actually happened: the canonical
  source is this file, the ORG-MST-PDX xlsx was never located, the owner declared
  this file authoritative on 2026-07-20, and completeness was verified by the
  repository sweep recorded in the plan. No code reads either field, so this is a
  pure documentation and provenance change.
- The `$comment` provenance block at the head of the file is rewritten to state
  the file is canonical rather than a subset awaiting reconciliation.

### Enforcement

- **QA-ENG-06 (block-type support).** `placeholderDictionary` is added to
  `BlockTypeSupportLayer.LiveAggregationSources`. The "reserved until the PDX
  slice" language in the failing-source remediation message is updated, since the
  reservation is now lifted. The supported-set drift-guard test is updated in the
  same change so the live-source set cannot widen silently.

- **QA-ENG-07 (token residue), scoped exemption.** A Placeholder Dictionary is by
  definition a table of bracketed tokens; its rows contain `[CLIENT ORGANIZATION
  NAME]` and every other canonical token verbatim. QA-ENG-07 exists to catch a
  deliverable that failed to resolve a token into its final value, so unmodified
  it would reject the very document whose purpose is to display the tokens. The
  `placeholderDictionary` aggregation is therefore exempt from token-residue
  scanning, exactly as the `clauseLibrary` aggregation already is. The exemption
  is narrow: it covers only the aggregation's rendered runs, never the Document
  Control Header and never surrounding prose, where residue still fails closed.
  This is the reason PDX is safe where operator-authored prose (a
  `copyReadyMessage`) is not. Implementation note: `aggregationTable` is already
  exempt by omission (it is absent from the `TokenResidueLayer` scan switch,
  independent of source), so `placeholderDictionary` needs no production change
  here; the slice adds a test that pins the exemption for the placeholderDictionary
  source so a future scan-switch edit cannot silently start flagging PDX rows.

- **QA-SF-08 (prohibited characters)** stays fully in force and is expected to
  pass. Canonical tokens are printable Basic Latin and already satisfy the
  engine's output-hygiene rule.

### ID Registry dependency (external blocker)

The engine never mints IDs (rule 5). Before the real PDX document can be produced,
Registry Owner must register ORG-MST-PDX-NNN in the ID Registry in the expected
status. Everything in this slice is built and tested against the fixture registry;
production output of the standing PDX document waits on that one registry entry and
is not itself a code deliverable.

## Deliverables

1. **Audit**
   - Token completeness evidence table (sweep result and canonical-set diff)
     recorded in the implementation plan; any genuinely missing token escalated to
     the owner before the dictionary is declared canonical.
2. **Config**
   - `config/artifact-types.json`: PDX `orderedSections` authored;
     `generationTarget: true`.
   - `config/placeholder-dictionary.json`: declared canonical; `dictionaryVersion`
     de-suffixed; `reconciledAgainstPdxXlsx` and note replaced with a provenance
     note; head `$comment` rewritten.
   - `config/qa-checklist.json`: QA-ENG-06 rule text updated to admit
     `placeholderDictionary` as a live aggregation source, if the catalog text
     enumerates live sources.
3. **Core model**
   - Add a `Usage` field to `CanonicalToken` (today it binds only `Token` and
     `KnownDriftAliases`; the `usage` tag in config is not yet modeled), so the
     renderer can project the Usage column. It is optional with an empty default,
     matching the config where most tokens carry no usage tag.
4. **Renderer**
   - `RenderContext`: add a `PlaceholderDictionary` member, populated from
     `ConfigBundle.CanonicalTokens` where the context is built.
   - `AggregationTableBlock`: add the `placeholderDictionary` branch projecting
     `Token | Drift Aliases | Usage`.
5. **Validation**
   - QA-ENG-06: `placeholderDictionary` added to the live-source set; remediation
     text updated; drift-guard test updated.
   - QA-ENG-07: no production change (aggregationTable is exempt by omission); a
     test pins the exemption for the `placeholderDictionary` source.
6. **Fixtures**
   - `fixtures/agr-sample-pdx-draft.json` and
     `fixtures/agr-sample-pdx-promotion.json`, mirroring the SCL fixtures
     (metadata plus one `aggregationTable` section), with their expected outputs.
   - `ORG-MST-PDX-001` added to `fixtures/registry-sample.csv`.
7. **Docs**
   - `docs/ROADMAP.md`: PDX moved from stubbed to delivered.
   - `docs/tech-debt.md`: item 33 (PDX not generated) and the placeholder-
     reconciliation follow-up closed; the audit's evidence recorded.
   - `CLAUDE.md` known-gaps: note that `config/placeholder-dictionary.json` is now
     the canonical placeholder source and the xlsx reconciliation gate is retired.

## Testing

Keyed to check IDs per repo convention (each gets a passing and a failing test):

- **QA-ENG-06**: passing = `aggregationTable` with
  `aggregationSource: "placeholderDictionary"` is accepted; failing = an unknown
  or missing source still fails closed. The drift-guard test asserts the live set
  is exactly `{clauseLibrary, placeholderDictionary}`.
- **QA-ENG-07 carve-out**: passing = bracketed tokens inside the rendered
  placeholder table do not trip residue; failing = a bracketed token in
  surrounding prose still trips it. The pair proves the exemption is scoped to the
  aggregation, not the whole document.
- **Config fidelity**: rendered token rows (token, joined aliases, usage) match
  `config/placeholder-dictionary.json` exactly, guarding that the renderer pulls
  from config and cannot silently diverge.
- **End-to-end**: build PDX in draft and promotion; assert every canonical token
  in config renders as a row, and that output stays byte-deterministic.

## Consequences and follow-ups

- Closes tech-debt item 33: PDX is generable and `placeholderDictionary` is a live
  aggregation source. Both config-sourced aggregation sources are now live; the
  reserved-source path in QA-ENG-06 is retired for placeholderDictionary.
- Retires the placeholder-dictionary reconciliation gate by removing its premise.
  `config/placeholder-dictionary.json` is canonical; there is no external xlsx to
  reconcile against.
- Production of the standing PDX document depends on ORG-MST-PDX-NNN existing in
  the ID Registry, which Registry Owner owns.
- A per-token definitions column remains available as a future enrichment slice if
  the documentation owner later wants the reference to carry authored meanings.
