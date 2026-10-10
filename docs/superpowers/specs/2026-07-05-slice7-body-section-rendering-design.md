# Slice 7: Body-Section Rendering - Design

**Status:** Approved (design)
**Date:** 2026-07-05
**Predecessor:** Slice 6 (AGR input-validation layer, QA-ENG-05)

## Goal

Render the numbered body sections of a governed artifact - the content between
the Configuration callout and the footer that Slice 5 deliberately stopped short
of. Slice 5 produced a well-formed document with universal chrome only, because
the `no-empty-h1` rule forbids a heading with no body and bodies require the
block renderers this slice introduces. Slice 7 adds those block renderers plus
per-section assembly, driven entirely by the validated AGR and config, and
renders the DS and AUP artifact types end-to-end as the acceptance milestone.

Clause text is inserted by reference from `config/clause-library.json` and proven
with SHA-256 (rule 4). The post-render QA-SF-08 character scan is folded in as the
honest "anywhere" enforcement over rendered runs, reusing the `CharacterLint`
primitive built in Slice 6.

## Scope decisions (locked)

1. **Prose-subset blocks only.** Six of the schema's ten block types render:
   `paragraph`, `h2`, `h3`, `bulletList`, `table`, `clauseInsertion`. These are
   the block types a DS or AUP body can realistically hold. Deferred:
   `warnCallout`, `radioQuestion`, `aggregationTable`, `copyReadyMessage` (see
   Deferred work).
2. **Unsafe Block Exclusion Rule.** Slice 7 does not render any block type whose
   safety in client-facing output depends on an enforcement check that does not
   yet exist. `warnCallout` is excluded on exactly this basis: the
   `no-warn-callouts-in-client-facing` check (QA item 9) is not implemented, and
   DS and AUP are both Client-Facing, so rendering a warn callout unguarded would
   be a fail-closed gap. The block dispatcher throws `NotSupportedException` on any
   out-of-scope block type so partial or unsafe support cannot be smuggled in
   later. Such a block can never legitimately reach the dispatcher (schema
   validation restricts the enum and section assembly only feeds validated blocks);
   the throw makes the rule structural rather than implied.
3. **Placeholder resolution in clause text only (normative).** Render-time
   `[TOKEN]` substitution from `dataInputs.placeholderMap` applies to canonical
   clause text and nothing else. Governor-supplied body text is rendered verbatim
   as supplied; the engine never rewrites Governor content. A token whose map
   value is present is substituted; a token that is absent or null is left as a
   literal `[TOKEN]`, which the clause spec explicitly permits and which
   `PlaceholderLayer` already blocks at promotion (null values fail). Residue can
   therefore survive only in draft, by design - that residue is the QA-SF-09
   concern deferred to Slice 8.
4. **Fold in QA-SF-08 (post-render character scan).** Now that bodies render -
   chiefly resolved clause text, which the input layer does not cover - the
   post-render "anywhere" scan over rendered runs is implemented, reusing
   `CharacterLint` and a `CharacterPolicy` built from `ContentLint`. This forces a
   BuildCommand flow reorder (see Post-render model).
5. **All structural and warn content-lint enforcement is deferred** to a dedicated,
   first-class Content-Lint slice (Slice 8): `no-empty-h1`,
   `no-heading-level-skips`, `no-warn-callouts-in-client-facing`, and QA-SF-09
   (token residue). Slice 8 is a required successor, not optional. Section assembly
   in Slice 7 is a faithful renderer, not a gatekeeper for these rules.
6. **Universal closing elements are deferred.** "Distribution and Follow-up" and
   the "Prepared-by and companion-artifact closing line" appear only as prose
   labels in `artifact-types.json` `universalStructure`; neither has defined content
   or structure in config. Rule 6 (do not invent structure) forbids fabricating
   them. Deferred pending ORG-MST-SPEC-001 Section 4.1 content; flagged for the
   documentation owner's ruling.

## Character policy reuse

QA-SF-08 reuses, unchanged, the Slice 6 primitives: `CharacterLint` (pure scanner,
`(string, CharacterPolicy) -> IReadOnlyList<Offense>`) and `CharacterPolicy` (built
from `ContentLint`, encoding the allowed printable Basic Latin / printable Latin-1
Supplement ranges plus the `approvedGlyphAllowlist`). QA-SF-08 is the second
consumer of `CharacterLint`, which validates the primitive's reuse design.

## Architecture

### 1. Inputs: model and config extensions

Both changes are additive and follow existing patterns.

**Model (`AgrDocument.cs`).** Extend `AgrBlock` (currently only `BlockType` +
`ClauseId`) with the schema fields the six render types need, all optional:

- `Text` (`string?`) - paragraph, h2, h3
- `Items` (`IReadOnlyList<string>?`) - bulletList
- `ColumnHeaders` (`IReadOnlyList<string>?`) and
  `Rows` (`IReadOnlyList<IReadOnlyList<string>>?`) - table
- `ClauseId` - already present

Extend `AgrSection` with `DescriptorLine` (`string?`) - the optional single
italic-gray intro line the schema already defines. The `question` and radio fields
stay unmapped (those blocks are out of scope). No `required` members change, so
existing parse paths are unaffected.

**Config (`ConfigBundle` + `ConfigLoader`).** The loader already deserializes the
full clause library but exposes only `ClauseIds` (a set). Add `ClausesById`
(`IReadOnlyDictionary<string, ClauseRecord>`) carrying `CanonicalText`,
`CanonicalTextSha256`, `ContainsPlaceholders`. No new file, no new hash entry - it
rides the existing `clause-library.json` load.

### 2. Block renderers

One focused static renderer per block type, mirroring the Slice 5 chrome pattern
(static class, returns Open XML elements, reads styling from `StyleRules`, reuses
`OpenXmlHelpers`). A single `BlockRenderer` dispatcher maps `blockType` to renderer;
`RenderContext` bundles `StyleRules`, the clause map, and the resolved
`placeholderMap`.

- **`ParagraphBlock`** - body paragraph: Montserrat Regular, `bodyPt` (10).
- **`HeadingBlock`** (h2, h3) - h2: Montserrat SemiBold, `subtitlePt` (11),
  `headingNavy`; h3: `accentBlue` (per `colorUsage` "H3 sub-headings"). The section
  title H1 is owned by section assembly, not a block.
- **`BulletListBlock`** - renders items via Word list formatting
  (`NumberingProperties` referencing a bullet definition in a
  `NumberingDefinitionsPart`), not a literal bullet glyph. This is deliberate:
  U+2022 is outside the allowed character set and not in the allowlist, so a literal
  bullet in a run would fail QA-SF-08; Word's numbering renders the bullet as list
  formatting, keeping run text clean.
- **`TableBlock`** - `columnHeaders` to a header row (`headingNavy` fill, bold),
  `rows` to body rows with `tableRowAltFill` on alternates and `borderNeutral`
  borders at the config half-point width. Reuses `OpenXmlHelpers.Grid`.
- **`ClauseInsertionBlock`** - see Clause insertion.

`ArtifactRenderer` adds a `NumberingDefinitionsPart` (one abstract bullet
definition) unconditionally so assembly stays simple.

### 3. Section assembly

**`SectionRenderer`** turns one `AgrSection` into its Open XML sequence, in order:

1. **Numbered H1 heading** - `"{n}. {title}"`, `n` 1-based from position.
   Montserrat SemiBold, `titlePt` (16), `headingNavy`. `SectionSchemaLayer`
   (QA-ENG-02) already guarantees the AGR sections equal the type's `orderedSections`
   exactly and in order, so assembly trusts position = schema order and does not
   re-validate.
2. **Descriptor line** (if non-null) - single italic-gray line, `secondaryGray`,
   `captionPt` (9). The schema caps it at one, so "no-stacked-italic-descriptors"
   holds by construction.
3. **Blocks** - each block through the `BlockRenderer` dispatcher, in order.

`ArtifactRenderer` loops the validated sections through `SectionRenderer` between
the Configuration callout and the section properties. The two universal closing
elements stay out (deferred).

### 4. Clause insertion (governance-critical)

`ClauseInsertionBlock`, in order:

1. **Reference lookup.** Resolve `block.ClauseId` against `ConfigBundle.ClausesById`.
   The Governor never supplies clause prose - `AgrBlock` carries no clause-text
   field, only `clauseId` - so paraphrase drift is structurally impossible, not
   merely checked. Unknown clauseId cannot reach here: `ClauseReferenceLayer`
   (QA-ENG-04) already fails closed on it.
2. **Integrity proof (SHA-256).** Recompute `Hashing.Sha256Hex` over the UTF-8 of
   the raw `CanonicalText` (tokens intact - that is what the stored hash covers) and
   compare to `CanonicalTextSha256`. Mismatch is a blocking post-render finding; no
   DOCX.
3. **Token resolution.** Only after the hash verifies, resolve `[TOKENS]` from the
   resolved `placeholderMap`, per the normative rule in scope decision 3.
4. **Render** the resolved text as one body paragraph (Montserrat Regular,
   `bodyPt`). Current library clauses are single-paragraph; treating a clause as one
   paragraph is a design judgment, with multi-paragraph splitting deferred until a
   clause carries a break.

**Contract wording** (normative, for the spec doc and implementation):

- `ClauseInsertionBlock` SHALL resolve clause content exclusively by clause
  identifier from the approved shared clause library and SHALL NOT accept inline
  clause prose from Governor-produced AGR content.
- `ClauseInsertionBlock` SHALL verify canonical clause text integrity prior to
  placeholder substitution; verification failure SHALL terminate the deliverable
  (no DOCX).
- A missing or null clause identifier at render time SHALL terminate the
  deliverable (no DOCX).
- Clause insertion verification metadata SHALL be returned from rendering for
  downstream audit logging.

**Null `clauseId` at render.** `ClauseReferenceLayer` only warns (not blocks) on a
`clauseInsertion` with a null `clauseId`, so in draft such a block can reach the
renderer. Ruling: this is a fail-closed condition - a governed clause must never
vanish from a deliverable. It is emitted as a blocking post-render finding (no
DOCX), not silently skipped. The validation warn severity is too lenient for this
surface and should be promoted to blocking in a later validation pass; Slice 7 does
not wait for that change to fail closed.

### 5. Post-render model and BuildCommand reorder

Folding in QA-SF-08 forces a sequencing change: it inspects the rendered document,
which does not exist when Slice 5's `build` writes the report. The report write must
move after render for the build path.

**Unified post-render finding model.** The renderer always runs to completion (no
early termination for governed conditions) and returns:

    RenderResult {
        byte[] Bytes;
        IReadOnlyList<Check> PostRenderChecks;
        IReadOnlyList<ClauseInsertionAudit> ClauseAudits;
    }

`PostRenderChecks` collects, without short-circuiting:

- **QA-SF-08 offenses** - walk every `Text` element (`Descendants<Text>()`), run
  each through `CharacterLint`. This is the honest "anywhere" scan: chrome
  (hygienic by construction), body text (already input-linted by QA-ENG-05), and
  the real new exposure, resolved clause text.
- **Clause integrity** - SHA mismatch and null `clauseId`, as blocking checks.

Any blocking check discards the bytes: no DOCX. A block that cannot render (null
clause) is simply absent from the discarded bytes; the blocking check is what
guarantees fail-closed, not a thrown exception. Thrown exceptions are reserved for
genuine faults (IO, malformed OpenXml), not governed conditions. Completing the scan
and reporting all findings at once (rather than terminating on the first) gives the
operator and Governor a complete report to regenerate against in one cycle.

**Blocking semantics (normative).** "Blocking" is not a new predicate or a new
`Check` member. A check is blocking exactly when `Result == fail` and
`Severity == blocking` - the identical condition `OverallResultPolicy.Compute`
already applies. The post-render merge is literally: append `PostRenderChecks` to
the outcome's `checks` and re-run `OverallResultPolicy.Compute`; if that yields
`overall == fail`, the bytes are discarded. Any `IsBlocking` shorthand in this
document means that existing condition, so post-render findings gate the deliverable
through the same policy as every other layer's checks - no divergent blocking logic
is introduced.

`ClauseInsertionAudit` fields: `clauseId`, `expectedSha`, `actualSha`, `verified`,
`tokensResolved`. Produced now; persistence format and sink (`generation-log.json`)
deferred to the logging slice.

**BuildCommand flow (final):**

    1. ValidationService.Validate -> outcome
    2. pre-render overall == fail? write report, no DOCX, exit 1
    3. docx-eligible? render in memory -> RenderResult
    4. merge PostRenderChecks into outcome, recompute overall
    5. write validation-report.json (validation + QA-SF-08 + clause integrity)
    6. still docx-eligible? write DOCX : withhold
    7. exit per final overall

QA now happens after render but before artifact emission, so the report always
reflects the final deliverable state.

**QA-SF-08 finding locator.** Post-render findings have no natural `jsonPath` - they
are properties of rendered output, not an AGR input coordinate, and the report
schema declares `jsonPath` as `["string","null"]` ("when applicable"). QA-ENG-05
already provides precise per-input-field character remediation at the input layer;
QA-SF-08 is the defense-in-depth net over what the input layer cannot see (chiefly
clause text). Findings therefore carry a document-origin locator in `message` -
naming the `clauseId` when the offending run is a clause insertion, else the
containing section title - with `jsonPath` null and remediation pointing at
source/config reconciliation. Run-to-source-block provenance tagging is deferred as
unnecessary for QA and remediation.

**QA-SF-08 catalog entry** - a new row in `config/qa-checklist.json`: `checkId`
`QA-SF-08`, layer `content`, post-render, blocking, scope "all rendered text runs",
ruleSource `ORG-MST-WFE-001 Section 4.5 item 8`. This citation is confirmed against
the Slice 6 work: the QA-ENG-05 catalog entry's `alsoSatisfies` list already names
`ORG-MST-WFE-001 4.5 item 8` as QA-SF-08's governing source (the short-form QA scan
that enforces the character prohibition "anywhere"), so the two checks cite the same
line by construction rather than by independent transcription.

**Locator sufficiency (intentional).** Document-origin locators (clauseId, else
section title) are intentionally sufficient for Slice 7. They satisfy the QA and
remediation need: a post-render offense's fix is a source/config correction, for
which naming the clause or section is enough to act. Precise run-to-source-block
provenance tagging is deferred by design, not by omission - QA-ENG-05 already
provides exact per-input-field character remediation at the input layer, so QA-SF-08
does not need to reconstruct input coordinates to be actionable.

## Error handling

- Governed conditions (QA-SF-08 offense, clause SHA mismatch, null clauseId) are
  blocking checks, never exceptions. The blocking check discards the rendered bytes,
  so no DOCX ships - fail closed.
- Config integrity: a clause SHA mismatch indicates the config text no longer matches
  its recorded digest (tampering or drift). It fails closed as a blocking check; the
  committed library always matches, so this is a defense-in-depth path.
- Exceptions are reserved for IO faults and malformed Open XML.

## Testing

Per repo convention every checkId gets at least one passing and one failing test
named after the checkId, plus focused component tests.

- **Block renderers** (reopen output, assert structure/text/color): paragraph body
  font/size; heading h2 navy / h3 accentBlue; bulletList via Word numbering with no
  literal U+2022 in any run; table header fill, alt-row fill, borders.
- **Clause insertion governance:** valid clause -> canonical text with tokens
  resolved, SHA verified, `ClauseAudit.verified == true`; SHA mismatch (tampered
  test clause) -> blocking `PostRenderCheck`, no DOCX, `verified == false`; null
  clauseId at render -> blocking check, no DOCX; null/absent token value -> literal
  `[TOKEN]` survives (draft-acceptable); by-reference is structural (the model
  carries no clause-prose field).
- **QA-SF-08** (`QA_SF_08_passes` / `QA_SF_08_fails`): clean DS render -> no finding;
  a prohibited char in a rendered run -> one blocking finding naming the
  clause/section, no DOCX.
- **Section assembly:** numbered H1s ("1. Purpose" through "8. Version and Change
  Notes") in order, navy; descriptor line italic-gray when present, absent when null;
  blocks in order.
- **Model/config:** new `AgrBlock`/`AgrSection` fields parse; `ConfigBundle.ClausesById`
  exposes canonical text + SHA.
- **End-to-end acceptance (the milestone):** DS draft renders chrome + 8 numbered
  sections + resolved CL-006 + footer, `ClauseAudits` has CL-006 verified, no
  prohibited chars; AUP renders 9 numbered sections + CL-001/004/005/009 resolved.
  This requires a new `agr-sample-aup` fixture and its ID-registry record (only DS
  fixtures exist today) - a work item in this slice. BuildCommand: DS build writes a
  QA-SF-08-inclusive report and emits DOCX on pass; a blocking clause/character
  failure writes the report and withholds the DOCX (exit 1).
- **Regression + hygiene:** Slice 5 chrome and Slice 6 input tests still pass; the
  reordered BuildCommand still fails closed on a schema-invalid AGR; new source held
  to `SourceHygieneTests` (escapes only, no literal non-ASCII glyphs).

## Deferred work (recorded, not silently dropped)

To be reflected in `docs/tech-debt.md` and the CLAUDE.md known-gaps:

1. **Content-Lint slice (Slice 8, required successor).** Houses `no-empty-h1`,
   `no-heading-level-skips`, `no-warn-callouts-in-client-facing`, and QA-SF-09
   (token residue). Hard prerequisite for rendering the deferred blocks safely and
   for promoting any body with heading structure. Does not gate Slice 7 or DS/AUP
   promotion (their bodies are plain paragraph + clause).
2. **Deferred blocks:** `warnCallout`, `radioQuestion`, `aggregationTable`,
   `copyReadyMessage`, governed by the Unsafe Block Exclusion Rule (dispatcher throws).
3. **QA-SF-09 Token Residue Detection** - unresolved `[TOKEN]` residue in rendered
   body text; semantic, distinct from QA-SF-08's character scan. Deferred to Slice 8.
4. **Universal closing elements** - "Distribution and Follow-up" and the
   "Prepared-by/companion closing line"; not defined in config, rule 6 forbids
   inventing. Deferred pending ORG-MST-SPEC-001 Section 4.1 content.
5. **Clause audit sink** - `RenderResult.ClauseAudits` produced now;
   `generation-log.json` persistence deferred to the logging slice.
6. **ClauseReferenceLayer null-`clauseId` severity** - warn today, too lenient;
   promote to blocking in a future validation pass (render already fails closed).
7. **Multi-paragraph clause text** - single-paragraph assumption; revisit if a
   clause carries a break.
8. **artifact-manifest.json / generation-log.json** - still deferred from Slice 5.

**Correction to an existing tech-debt note.** Tech-debt item 3 ties the shared
JSON-Pointer escaper extraction (and deletion of the dead
`contentLint.prohibitedCharacters` exemplar block) to "when the post-render QA-SF-08
content scan lands (the second consumer of `CharacterLint`)." That reasoning
conflates two things. QA-SF-08 is the second `CharacterLint` consumer, but it scans
rendered runs with document-origin locators and emits no placeholderMap-key JSON
pointers - so it is not a new consumer of `EscapeToken`/`PointerFor`. The escaper
still has exactly two consumers (InputHygieneLayer, PlaceholderLayer); rule-of-three
is not met, so that extraction stays deferred. The dead `prohibitedCharacters`
exemplar block is likewise not read by QA-SF-08 (which uses `approvedGlyphAllowlist`
via `CharacterPolicy`), so it remains harmless documentary and is left in place. The
tech-debt note's trigger reasoning is corrected rather than acted on.

## Component boundaries summary

- `AgrBlock` / `AgrSection` - extended with optional body fields; depend on nothing.
- `ConfigBundle` / `ConfigLoader` - extended to carry `ClausesById`.
- `ParagraphBlock` / `HeadingBlock` / `BulletListBlock` / `TableBlock` /
  `ClauseInsertionBlock` - each renders one block type from data + `RenderContext`.
- `BlockRenderer` - dispatcher; the only unit that knows the block-type-to-renderer
  map and enforces the Unsafe Block Exclusion Rule.
- `SectionRenderer` - assembles one section (numbered H1, descriptor line, blocks).
- `ArtifactRenderer` - assembles the document, adds the numbering part, runs the
  post-render scan, returns `RenderResult`.
- `BuildCommand` - orchestration; the only unit that knows the validate ->
  render -> merge -> report -> emit flow and the DOCX emission rule.
