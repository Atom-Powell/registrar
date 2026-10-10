# radioQuestion Rendering and TCL End-to-End - Design

**Status:** Approved (design)
**Date:** 2026-07-14
**Predecessor:** Rendering Polish (Direction B), 2026-07-08
**Scope:** First deferred block type; TCL generation target; validation-report artifact hash

## Goal

Make TCL (Training Checklist) generate.

TCL is declared a generation target in `config/artifact-types.json`
(`generationTarget: true`, `usesRadioOptionPattern: true`, `sectionSchema.status:
"defined"`), but it cannot produce a document today. Any block it needs is a
`radioQuestion`, which fails closed under the Unsafe Block Exclusion Rule. TCL has
no fixture and has never been rendered end to end.

This slice builds the `radioQuestion` block renderer, implements the already
catalogued QA-SF-10 check that governs it, and renders TCL end to end in draft and
promotion. It also closes an unrelated gap found while scoping the work: the
validation report's `artifactSha256` field is hardcoded `null` and never computed.

Nothing here invents structure. The AGR schema already defines the `question`
object; `config/style-rules.json` already specifies the radio pattern; and
`config/qa-checklist.json` already assigns QA-SF-10 to it. The engine simply never
read any of it.

## Non-goals (locked)

- **No new artifact types or section schemas.** TCL's seven sections are already
  defined in config. Rule 6 is untouched.
- **The other three deferred block types stay deferred.** `warnCallout`,
  `aggregationTable`, and `copyReadyMessage` remain excluded and continue to fail
  closed. In particular `aggregationTable` has no defined semantics anywhere and
  building it would mean inventing structure.
- **No AGR schema change.** The `question` object already exists in
  `schemas/agr.schema.json` with `number`, `prompt`, `required`, and `options`
  (`minItems: 4`). Only the C# model lacked a counterpart.
- **No runtime LLM call** (rule 2). No new governance decision in the engine.

## The three findings that shaped this design

**1. `radioOptionPattern` is dead config.** `ConfigLoader.cs:29` deserializes only
the `visualIdentity` node of `style-rules.json` into `StyleRules`.
`radioOptionPattern` is a sibling top-level key, so no code can currently read it.
Binding it is a prerequisite, which makes this slice not purely additive.

**2. The AGR schema is flat, so a malformed radio block passes schema validation.**
`$defs.contentBlock` is one object with every field optional and no `if`/`then` per
`blockType`. A block with `blockType: "radioQuestion"` and no `question` object at
all is schema-valid and would reach the renderer as a null dereference. This is the
same hole `docs/tech-debt.md` item 12 records for a null `clauseId`, where the
lenient warn-severity choice is explicitly called too lenient. This design does not
repeat it: QA-SF-10 fails closed in both modes.

**3. The descriptor line's wording exists nowhere.** `style-rules.json` says
`"descriptorLine": "Italic gray line above each question stating question number,
single-answer instruction, and required or optional"`. That is a description of
what to render, not a template. Meanwhile `agr.schema.json` states that the
renderer applies the descriptor line and "the Governor supplies only labels." So
the engine must compose that sentence, and its wording has to live somewhere.

## Decision: the descriptor line is a config format string

Per rule 3 (configuration over code), the wording becomes versioned data in
`config/style-rules.json`, changeable in a template pack without a rebuild:

    "radioOptionPattern": {
      "glyph": ...,                    (existing key, unchanged)
      "descriptorLineFormat": "Question {number}. Select one answer. {requirement}",
      "requiredLabel": "Required",
      "optionalLabel": "Optional",
      ...
    }

The three descriptor keys are the only additions. The existing `glyph` key already
holds the radio glyph as a JSON unicode escape and is not touched.

Rendering as (italic descriptor in `secondaryGray`; the letter `O` below stands in
for the glyph, since this spec is kept ASCII-only):

    Question 3. Select one answer. Required
    O  Yes, reviewed and acknowledged
    O  Yes, partial review, follow-up scheduled
    O  No, blocked by access or timing
    O  Other or Not applicable

The rendered glyph is U+25CB. It is already on the `approvedGlyphAllowlist`, so it
passes the QA-ENG-05 character policy unchanged.

**Placeholder syntax is `{number}`, not `[NUMBER]`, and this is deliberate.** The
QA-ENG-07 token-residue scan matches the bracketed upper-case token shape
(`config/style-rules.json` `contentLint.tokenResidue.pattern`). A bracketed
descriptor token would be flagged as unresolved placeholder residue in rendered
prose. Braces sidestep the collision.

Alternatives rejected: a Governor-supplied `descriptorLine` per block (contradicts
the schema's own note that the Governor supplies only labels, and lets client-facing
wording drift per request, which is the drift this engine exists to prevent); and
hard-coding the sentence in C# (violates rule 3, since changing it would need a
rebuild rather than a config release).

## Components

### Config binding

New `RadioOptionPattern` record in `src/Registrar.Core/Config`, bound from
`styleRoot["radioOptionPattern"]` in `ConfigLoader`, exposed on `ConfigBundle`
alongside `Style`.

It is deliberately **not** folded into `StyleRules`, which maps 1:1 to the
`visualIdentity` node; preserving that correspondence keeps the config-to-model
mapping legible.

Fields bound: `glyph`, `glyphColor`, `optionsIndented`, `minimumOptions`,
`standardOptionSet`, plus the three new descriptor fields. `minimumOptions` is read
from config and never appears as a literal `4` in code.

`descriptorLine` (the prose one) and `prohibitedRenderings` are **not** read at
runtime and stay documentary. A comment will say so, rather than leaving them
looking wired.

### Model

`AgrBlock` gains `Question`; a new `AgrQuestion` record mirrors the existing schema
object (`Number`, `Prompt`, `Required`, `Options`). No schema change.

### Validation: QA-SF-10 (`RadioQuestionLayer`)

A new `ISemanticLayer` emitting **blocking** checks in both draft and promotion:

| Condition | Result |
|---|---|
| `radioQuestion` block with null `question` | Fail (blocking) |
| empty or whitespace `prompt` | Fail (blocking) |
| `options.Count < minimumOptions` (from config) | Fail (blocking) |

Each check carries `checkId: QA-SF-10`, a `jsonPath` to the offending block,
expected/actual, a remediation phrased as an instruction the Governor can execute,
and `ruleSource: ORG-MST-WFE-001 4.5 item 10` (matching the catalog entry, which
already types QA-SF-10 as `layer: content`, `appliesTo: usesRadioOptionPattern`).

The rule's second half (no checkboxes, plain bullets, dash lists, or numbered lists)
is guaranteed **by construction**: `RadioQuestionBlock` is the only code path that
can emit a radio question, and it references no numbering definition. This is
asserted by unit test rather than scanned at runtime. A post-render scan was
considered and rejected: it cannot produce Governor-actionable remediation (the
failure would be document-shaped, not request-shaped), and it would guard against a
bug in our own renderer, which is what tests are for. QA-SF-08 scans post-render
because clause *text* can drift after resolution; radio *structure* cannot.

### Renderer: `RadioQuestionBlock`

One static class in `src/Registrar.Rendering/Blocks/`, matching the existing
one-class-per-block-type pattern.

- Descriptor paragraph: composes `descriptorLineFormat` with `{number}` and
  `{requirement}` (resolving to `requiredLabel`/`optionalLabel`); italic,
  `secondaryGray`, caption size.
- One paragraph per option: two runs (glyph run in `glyphColor`, label run in
  black), indented when `optionsIndented`. No numbering reference.

Supporting changes:

- `OpenXmlHelpers.StyledRun` takes a `bold` flag but has no italic parameter; it
  needs one for the descriptor line.
- `BlockRenderer` gains a `radioQuestion` case. Its `BlockRenderResult` scan text is
  the descriptor, prompt, and all option labels concatenated, so rendered radio
  prose flows through the QA-SF-08 character scan and the QA-ENG-07 token-residue
  scan like any other body text.
- `BlockTypeSupportLayer.SupportedBlockTypes` gains `radioQuestion`, and its
  `Expected` message string updates.
- `config/qa-checklist.json`: the QA-ENG-06 rule text currently names
  `radioQuestion` among the block types "not yet built" and must be updated, or the
  catalog will contradict the engine.

### artifactSha256

`ValidationService.cs:117` hardcodes `ArtifactSha256 = null`, and nothing sets it
afterward. `PostRenderMerge.Apply` does not touch it. The report therefore
advertises an artifact hash and always emits nothing, even on a successful render.

The fix exposes an ordering problem in `BuildCommand`. `WriteOutputs` (which
serializes `validation-report.json`) runs at line 67; the DOCX is not written until
line 84; and the `docxEligible` decision sits between them at line 76. The report is
written before the engine knows whether a document will exist.

**The hash must describe a document that was actually produced.** Stamping it on a
run that emitted no DOCX would be a false attestation, which is precisely the class
of thing this engine exists to prevent. So:

1. Compute `docxEligible` **before** `WriteOutputs`.
2. If eligible, stamp `Sha256(rendered.Bytes)` into the merged report.
3. Otherwise leave `ArtifactSha256` null.
4. `Hashing` gains a `byte[]` overload (it is string-only today).

Pre-render failure paths continue to emit `null`, correctly. This is a small reorder
of the shared build path rather than a rewrite, but it *is* a behavior change to a
path DS and AUP also use, so it gets dedicated tests.

## Testing

Per the repo rule that every QA check ID gets at least one passing and one failing
test named after the checkId:

- `RadioQuestionLayerTests`: QA-SF-10 passes on a well-formed question; fails on a
  null `question`, on fewer than `minimumOptions` options, and on an empty prompt.
  Theory across draft and promotion, asserting blocking in both.
- `RadioQuestionBlockTests`: glyph is U+25CB; glyph run carries `glyphColor`;
  descriptor run is italic and `secondaryGray`; options are indented; and the
  produced XML contains **no numbering reference** (the by-construction guarantee).
- QA-ENG-06 drift guard (`BlockRendererTests`): updated so `radioQuestion` moves
  from the deferred set to the supported set, keeping
  `BlockTypeSupportLayer.SupportedBlockTypes` and `BlockRenderer`'s dispatch in
  lockstep.
- `BuildCommand` TCL end to end: draft and promotion.
- artifactSha256: present and correct on a passing build; null on a failing one.

## Fixtures

Mirroring what DCN, TAK, and RAF already have:

- `fixtures/agr-sample-tcl-draft.json` and `fixtures/agr-sample-tcl-promotion.json`,
  covering TCL's seven defined sections and its required clauses CL-003 and CL-004.
- `fixtures/agr-sample-tcl-radio-short-options.json` as the QA-SF-10 failure case.
- `ORG-TRN-TCL-001` added to `fixtures/registry-sample.csv` (classification
  Client-Facing, status Draft, v1.0), without which promotion cannot pass the
  registry check.

## Risks

- **Shared-path change.** The `BuildCommand` reorder touches the path DS and AUP
  use. Mitigated by the existing DS/AUP end-to-end tests, which must stay green.
- **Config surface growth.** Three new keys in `radioOptionPattern`. They are
  additive and defaulted nowhere in code, so a config pack missing them fails loudly
  at load rather than rendering a blank descriptor.
- **`no-stacked-italic-descriptors`.** `style-rules.json` lists this content-lint
  rule ("one italic gray intro line per heading maximum"). It is documentary and
  unimplemented. A per-question descriptor line is per question, not per heading, so
  it does not violate the rule as written; noting it here so a future content-lint
  slice does not mistake radio descriptors for stacked ones.

## Out of scope, recorded

- `warnCallout`, `aggregationTable`, `copyReadyMessage` rendering.
- UQR and PIG fixtures (both have defined section schemas but no end-to-end
  exercise; neither needs a new block type, so both are cheap follow-ups).
- `artifact-manifest.json` and `generation-log.json` emission.
