# Slice 8: Content Lint - Design

**Status:** Approved (design)
**Date:** 2026-07-07
**Predecessor:** Slice 7 (body-section rendering, QA-SF-08 / QA-ENG-04 / QA-ENG-06)

## Goal

Add the first-class content-lint enforcement that Slice 7 deliberately deferred.
Slice 7 shipped the prose-subset body renderer without the structural and
policy checks that make heading-bearing bodies and the deferred block types safe
to render. This slice supplies those gates: heading-structure validation, the
client-facing warn-callout gate, and detection of unresolved `[TOKEN]` residue
left in Governor-supplied body prose. It also closes a Slice 7 test-coverage gap
by tying the supported-block-type allowlist to the renderer's actual capability.

None of these checks gate the current DS and AUP path - their bodies are plain
`paragraph` + `clauseInsertion`, with no headings and (when authored correctly)
no residual tokens. They are the required prerequisite for a future slice to
render heading-bearing bodies and the deferred block types (`warnCallout`,
`radioQuestion`, `aggregationTable`, `copyReadyMessage`) without a fail-closed
gap. QA-ENG-07 is the one check that can bite a real DS/AUP request today, since
nothing currently inspects body prose for placeholder residue.

## Scope decisions (locked)

1. **Three checks, all pre-render semantic layers.** The three code checks are
   `ISemanticLayer` implementations reading the validated AGR model, one checkId
   per layer, matching the established convention (`RegistryLayer` = QA-SF-03,
   `InputHygieneLayer` = QA-ENG-05, `BlockTypeSupportLayer` = QA-ENG-06). No new
   post-render scan and no `ArtifactRenderer` or `BuildCommand` change is
   required.

2. **Pre-render is correct because body prose is verbatim (normative).** Only
   `clauseInsertion` blocks substitute placeholders (via `ClauseResolver`). The
   `paragraph`, `h2`, `h3`, `bulletList`, and `table` renderers emit
   `block.Text` / `Items` / `Rows` unchanged, so the AGR source text is byte-for-byte
   the rendered output for every block these checks inspect. Running pre-render
   therefore loses no fidelity and gains a real JSON pointer for each finding
   (remediation the Governor can act on) instead of the post-render `null`
   `jsonPath` + string locator that QA-SF-08 must use.

3. **Reuse QA-SF-07 for the heading rules; do not mint a new id.** QA-SF-07 in
   `config/qa-checklist.json` already states both rules verbatim ("Every H1 has
   body content ... Heading hierarchy has no level skips") and has no code layer
   bound. Binding a code layer to a short-form id is established practice
   (QA-SF-03/04/05/08 all do it). The `no-empty-h1` and `no-heading-level-skips`
   entries under `contentLint.prohibitedPatterns` in `style-rules.json` are the
   config-side descriptors of the same two rules.

4. **QA-ENG-07 is a new engine-family id.** The Slice 7 design called the
   token-residue check "QA-SF-09"; that id was already taken by the warn-callout
   check, so the token-residue check is catalogued as **QA-ENG-07** (per
   tech-debt item 7). QA-SF-10/11/12 are also framework-reserved, so an
   engine-family id is the correct home.

5. **QA-ENG-07 scans non-clause blocks only.** Clause blocks are governed by
   `ClauseResolver` (SHA-256 verification, QA-SF-11) and legitimately retain
   `[TOKEN]` placeholders that resolve at render time; scanning them would
   produce false positives. QA-ENG-07 inspects `paragraph`, `h2`, `h3`,
   `bulletList`, and `table` text only.

6. **QA-SF-07 and QA-ENG-07 block in both modes; QA-SF-09 is promotion-only.**
   Heading-structure defects and token residue are structural faults in a
   controlled document, so they fail closed in draft and promotion (rule 1),
   matching QA-ENG-05/QA-ENG-06. QA-SF-09 is a promotion gate by its own
   catalogued wording ("at promotion") and by classification
   ("classification=Client-Facing").

7. **QA-SF-09 keys off the configured classification, not the AGR field.** The
   authoritative classification is the artifact type's value in
   `config/artifact-types.json` (`Client-Facing`), not the Governor-supplied
   `artifactIdentity.classification`. The engine never trusts the Governor
   (division-of-labor rule), and `MetadataLayer` already reconciles the two.

## Check identity and reporting

Each layer emits `Check` records via the shared `Vocabulary.cs` constants (no
magic strings). Common fields: `Result` = `fail`, `SeverityLevel` = `blocking`,
`ErrorCategory` = `MetadataViolation` (consistent with QA-ENG-05 and QA-SF-08;
`PromotionPolicyError` was considered for QA-SF-09 and rejected for consistency).

### HeadingStructureLayer -> QA-SF-07

- `CheckId = "QA-SF-07"`, `LayerName = Layer.Content`, `RuleSource = "ORG-MST-WFE-001 4.5 item 7"`.
- A section is an implicit H1 (rendered `"{n}. {title}"`); `h2`/`h3` blocks carry
  explicit levels via `BlockType`.
- **no-empty-h1:** a section whose `blocks` contain no non-heading content block
  (i.e. every block is `h2`/`h3`, or the section has zero blocks) fails.
  `JsonPath = "/dataInputs/sections/{s}"`. `Expected` = "at least one content
  block (paragraph, bulletList, table, or clauseInsertion)", `Actual` = "heading-only"
  or "no blocks".
- **no-heading-level-skips:** an `h3` block with no preceding `h2` within the
  same section (an H1 -> H3 jump) fails.
  `JsonPath = "/dataInputs/sections/{s}/blocks/{b}"`. `Expected` = "an h2 heading
  before this h3", `Actual` = "h3 without a preceding h2".
- Out of scope (recorded, not claimed): QA-SF-07's "and, where required by the
  section schema, a subsection or table" clause is section-schema enforcement
  owned by QA-ENG-02 (`SectionSchemaLayer`); this layer implements only the two
  heading rules the catalog and roadmap name.

### WarnCalloutLayer -> QA-SF-09

- `CheckId = "QA-SF-09"`, `LayerName = Layer.Content`, `RuleSource = "ORG-MST-WFE-001 4.5 item 9"`.
- Runs its body only when the artifact type's configured classification is
  `Client-Facing` **and** `context.Mode == Mode.Promotion`; otherwise returns no
  checks.
- Any block with `BlockType == "warnCallout"` fails.
  `JsonPath = "/dataInputs/sections/{s}/blocks/{b}"`, `Expected` = "no warnCallout
  blocks in client-facing output at promotion", `Actual` = "warnCallout".
- QA-ENG-06 also fails on the same `warnCallout` block today (it is not yet a
  rendered type); both checks firing is correct and harmless. QA-SF-09 is the
  gate that remains once a future slice removes `warnCallout` from QA-ENG-06's
  unsupported set.

### TokenResidueLayer -> QA-ENG-07 (new)

- `CheckId = "QA-ENG-07"`, `LayerName = Layer.Content`, `RuleSource = "ORG-MST-SPEC-001 Section 9 item 7 (placeholder-resolution completeness, body prose)"`. The exact citation is confirmed against the spec in the plan.
- For each non-clause block, gather scan text (`paragraph`/`h2`/`h3` -> `Text`;
  `bulletList` -> `Items`; `table` -> `ColumnHeaders` + `Rows`) and match against
  the configured token-residue pattern. Each offending block fails.
  `JsonPath = "/dataInputs/sections/{s}/blocks/{b}"` (with `/text`, `/items`, or
  the table field appended where a single sub-field localizes the offense),
  `Expected` = "no unresolved bracketed placeholder tokens in body prose",
  `Actual` = the matched token(s).
- **Config-driven** (rule 3): the pattern is data, not code. It is added to
  `config/style-rules.json` under `contentLint.tokenResidue.pattern` and read
  into an extended `ContentLint` record. The pattern matches the canonical
  placeholder shape - a bracketed, upper-case token such as
  `[CLIENT TENANT NAME / TENANT ID]` - so lowercase editorial brackets (for
  example `[see appendix]`) are not flagged. The exact regex is finalized in the
  plan; a starting point is `\[[A-Z0-9][A-Z0-9 ,./()&'-]*\]`.
- **Why this is not already covered:** QA-SPEC-07 (`PlaceholderLayer`) inspects
  only `dataInputs.placeholderMap` keys and values, never section block text;
  QA-ENG-05 (`InputHygieneLayer`) lints characters and treats `[` and `]` as
  legal Basic Latin; QA-SF-08 scans rendered runs for prohibited characters,
  not bracketed ASCII. A token that appears only in body prose is unmanaged
  residue that renders literally into the DOCX.

## QA-ENG-06 supported-set self-verify (test-only, tech-debt item 16)

`BlockTypeSupportLayer.SupportedBlockTypes` (`paragraph`, `h2`, `h3`,
`bulletList`, `table`, `clauseInsertion`) is asserted by comment to mirror
`BlockRenderer.Render`'s case labels, but nothing enforces it - the two could
drift silently, re-opening the crash the layer exists to prevent. This slice adds
a test that ties them behaviorally:

- Expose `SupportedBlockTypes` as `internal` and add `InternalsVisibleTo` for
  the test project (or expose a public read-only view).
- For each type in the set, build a minimal `AgrBlock` and call
  `BlockRenderer.Render`, asserting it does not throw.
- For each of the four deferred types (`warnCallout`, `radioQuestion`,
  `aggregationTable`, `copyReadyMessage`), assert `BlockRenderer.Render` throws
  `NotSupportedException`.

No production behavior changes; this is a drift guard.

## Configuration changes

- `config/style-rules.json`: add `contentLint.tokenResidue.pattern` (regex
  string). The existing `contentLint.prohibitedPatterns` entries `no-empty-h1`
  and `no-heading-level-skips` become documented-as-consumed by
  `HeadingStructureLayer` (their `$comment` / documentary status is updated;
  no structural change to those entries).
- `config/qa-checklist.json`: add the QA-ENG-07 entry
  (`layer: "content"`, `automation: "full"`, `appliesTo: "all"`,
  `errorCategory: "MetadataViolation"`, rule text describing body-prose token
  residue, `ruleSource` per above).
- `src/Registrar.Core/Config/ContentLint.cs`: add a `TokenResiduePattern`
  member (or a nested `TokenResidue` record) with `ConfigLoader` reading the new
  key. Keep `ApprovedGlyphAllowlist` unchanged.

## Error handling

- Layers are pure and return `IReadOnlyList<Check>`; no I/O, no exceptions on the
  validation path. A malformed/absent `tokenResidue.pattern` in config is a
  `ConfigurationError` surfaced at load time (fail closed), consistent with how
  other required config values are handled by `ConfigLoader`.
- Aggregation is unchanged: blocking fails flow through `OverallResultPolicy`
  into `PostRenderMerge` and the `BuildCommand` gates, withholding the DOCX and
  (in promotion) the payload with no new wiring.

## Testing

Per repo convention every checkId gets at least one passing and one failing test
named after the checkId, plus focused component tests. All three layers are
unit-testable in isolation via `SemanticTestSupport` with `Registry: null`
(they do not touch the registry).

- **QA-SF-07** (`QA_SF_07_passes` / `QA_SF_07_fails_empty_h1` /
  `QA_SF_07_fails_heading_skip`, in `HeadingStructureLayerTests`): a well-formed
  DS -> no finding; a section with only heading blocks -> one empty-h1 finding;
  a section with an `h3` and no preceding `h2` -> one heading-skip finding.
- **QA-SF-09** (`QA_SF_09_passes` / `QA_SF_09_fails`, in `WarnCalloutLayerTests`):
  a `warnCallout` in a Client-Facing artifact at promotion -> one finding; the
  same in draft, or in a non-client-facing type, or with no `warnCallout` ->
  no finding.
- **QA-ENG-07** (`QA_ENG_07_passes` / `QA_ENG_07_fails`, in
  `TokenResidueLayerTests`): clean body prose -> no finding; a `[TOKEN]` in a
  paragraph (and one in a bullet / table cell) -> a blocking finding per block,
  in both draft and promotion; a `[TOKEN]` inside a `clauseInsertion` block ->
  no finding (excluded).
- **QA-ENG-06 self-verify** (in `BlockTypeSupportLayerTests` or a new
  `BlockRendererSupportTests`): supported types render without throwing;
  deferred types throw `NotSupportedException`.
- **Catalog** (`QaChecklistCatalogTests`): assert `QA-ENG-07` is present and that
  `TokenResidueLayer.CheckId` exists in the catalog (mirroring the existing
  per-layer assertions).
- **Fixtures:** single-defect copies of `agr-sample-ds-draft.json`, each isolating
  exactly one defect - `agr-sample-ds-empty-h1.json`, `agr-sample-ds-heading-skip.json`,
  `agr-sample-ds-token-residue.json`, `agr-sample-ds-warn-callout.json`. The
  warn-callout fixture uses promotion mode; layer tests inject mode via
  `SemanticTestSupport.Context(..., Mode.Promotion)` and need no registry record.

## Non-goals (deferred, not silently dropped)

- **Internal-only-note detection** (the second half of QA-SF-09). No AGR data
  model represents an internal note, so this is un-implementable now; recorded in
  `docs/tech-debt.md`. QA-SF-09 covers `warnCallout` blocks only in this slice.
- **Rendering any deferred block type.** This slice supplies the gates; enabling
  `warnCallout`/`radioQuestion`/`aggregationTable`/`copyReadyMessage` rendering
  is a later slice.
- **The section-schema-driven subsection/table requirement of QA-SF-07** - owned
  by QA-ENG-02.
- **Post-render token scanning.** Unnecessary while body prose is verbatim; if a
  future slice introduces body-prose placeholder substitution, revisit whether
  QA-ENG-07 needs a post-render companion.

## Component boundaries summary

| Unit | Responsibility | Reads | Emits |
|------|----------------|-------|-------|
| `HeadingStructureLayer` | Heading structure of section bodies | `AgrDataInputs.Sections[].Blocks[]` | QA-SF-07 checks |
| `WarnCalloutLayer` | Client-facing warn-callout promotion gate | sections + configured classification + mode | QA-SF-09 checks |
| `TokenResidueLayer` | Bracketed token residue in non-clause prose | non-clause block text + `contentLint.tokenResidue.pattern` | QA-ENG-07 checks |
| `ContentLint` (extended) | Config carrier for the token pattern | `style-rules.json` | `TokenResiduePattern` |
| QA-ENG-06 self-verify test | Prevent allowlist/renderer drift | `BlockTypeSupportLayer` + `BlockRenderer` | test assertions |
