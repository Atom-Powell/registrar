# Slice 6: AGR Input Validation Layer (Free-Text Hygiene) - Design

**Status:** Approved (design)
**Date:** 2026-07-05
**Predecessor:** Slice 5 (renderer foundation + universal chrome)

## Goal

Close the documented gap where Governor-supplied free text reaches DOCX chrome
with no prohibited-character check. Governor-supplied strings (the chrome fields
plus every `dataInputs.placeholderMap` value) are substituted into the deliverable
verbatim, so an em dash or smart quote in a client name can currently ride into a
promotion document undetected. This slice adds a pre-render input-validation layer
that **blocks** (never normalizes) any string containing a prohibited character,
failing closed in both draft and promotion. The allowed/prohibited set is driven
from `style-rules.json` `contentLint`, so accented names (for example "Pena" with
an n-tilde) stay valid.

## Slice numbering (resolved)

The Slice 5 design doc originally forward-referenced body-section rendering as
"Slice 6", conflicting with the project constitution (CLAUDE.md known-gaps) and
commit `97c86e2`, which assign Slice 6 to this input-validation layer. Resolved in
favor of the constitution: **this is Slice 6; body-section rendering becomes
Slice 7.** The Slice 5 design and plan docs have been updated to say Slice 7, so
the development history is unambiguous before this slice is committed.

## Scope decisions (locked)

1. **Coverage: chrome fields + all placeholder values.** The linted set is the six
   chrome fields - `context.owner`, `context.audience`, `context.preparedBy`,
   `context.client`, `context.engagement`, `renderingOptions.packageName` - plus
   every value in `dataInputs.placeholderMap`. This is the full set of unchecked
   free text the engine substitutes into the deliverable today. Clause text is out
   of scope by construction: it is inserted by reference from config and is already
   clean (rule 4). Optional fields (`client`, `engagement`) are linted only when
   present.
2. **Fail closed in both modes.** A prohibited codepoint is always blocking, draft
   and promotion alike. Unlike unresolved placeholders or unknown tokens (which are
   mode-gated because they carry legitimate draft-time ambiguity), the allowed
   character set is definitive, so there is no reason to soft-pedal it in draft.
   This matches how a known drift alias already fails unconditionally and honors
   rule 1 (fail closed) and QA-SF-08's unconditional "anywhere".
3. **New input-layer checkId.** The check is emitted under a new catalog entry,
   `QA-ENG-05` (next free id in the input-layer QA-ENG family), not by reusing
   QA-SF-08. QA-SF-08 remains catalogued as a content-layer, post-render text-run
   scan that a later render slice can still implement honestly; this input-layer
   check records the relationship through the catalog's citation-based
   `alsoSatisfies` field. See "Check identity" below.
4. **Policy from config, field list in code.** The allowed-character policy (glyph
   allowlist) is data in `style-rules.json` `contentLint`. The list of AGR fields
   to lint is engine logic tied to the AGR contract shape, so it lives in code -
   making it config would be premature abstraction against an unknown second
   consumer (YAGNI).

## Character policy (normative)

The authoritative rule is a codepoint range check, not a blocklist of known-bad
characters; that is what makes it complete rather than whack-a-mole. Scanning is by
`System.Text.Rune`, so a supplementary-plane codepoint (for example an astral-plane
emoji encoded as a UTF-16 surrogate pair) is treated as one offense at one position,
not two.

**Allowed characters** are printable Basic Latin (`U+0020`-`U+007E`), printable
Latin-1 Supplement excluding invisible formatting codepoints (`U+00A1`-`U+00FF`,
excluding `U+00AD`), plus any explicitly approved glyphs from config (currently
`U+25CB`, the open-circle radio glyph). Control characters, NBSP (`U+00A0`), soft
hyphen (`U+00AD`), and all other codepoints are prohibited.

Consequences of this policy:

- Em dash (`U+2014`), en dash (`U+2013`), ellipsis (`U+2026`), and smart quotes
  (`U+2018`/`U+2019`/`U+201C`/`U+201D`) are all >= `U+2013`, so the range check
  rejects them without naming them.
- Accented Latin names (n-tilde `U+00F1`, e-acute `U+00E9`, etc.) fall in
  `U+00A1`-`U+00FF` and remain valid.
- All C0 controls (`U+0000`-`U+001F`), DEL (`U+007F`), and C1 controls
  (`U+0080`-`U+009F`) fall outside the printable ranges and are prohibited -
  including newlines and tabs in these single-line fields.
- NBSP and soft hyphen sit inside the Latin-1 block but are invisible and are
  common copy/paste corruption, so they are carved out explicitly.

**Sourced vs. implementation (governance trail).** This distinction is recorded so
the ADR trail stays honest: sourced where sourced, engine design where engine
design.

- **Framework policy (sourced).** The prohibition itself - "no em dash, en dash,
  or decorative Unicode anywhere in the file" - and the positive style surface of
  standard punctuation only, plus the explicit `U+25CB` allowance, are established
  by three governing documents, not one: ORG-MST-SPEC-001 Section 4.2 (normative
  style rules), ORG-MST-WFE-001 Section 4.5 item 8 (the short-form QA scan that
  makes the prohibition operational and enforced "anywhere"), and ORG-MST-UG
  Section 4 (the Usage Guide punctuation rule). These are explicit rule sources.
- **Engine design (implementation, not framework policy).** The `contentLint`
  config block, the `QA-ENG-05` catalog id, the choice to validate at the input
  layer pre-render rather than post-render, the by-`Rune` scanning, and the
  specific carve-outs of NBSP, soft hyphen, and control characters are Generation
  Engine v2 design decisions. They are consistent with the framework's strict
  fail-closed QA posture but are not line-item quotes from the template pack, and
  must not be represented as framework policy. Shifting detection earlier (input
  vs. post-render) is a valid pre-render control over an already-existing rule, not
  a new rule.

The glyph allowlist applies uniformly across all linted fields. A client name will
never legitimately contain `U+25CB`, but keeping one policy keeps the `CharacterLint`
primitive reusable for the future post-render body scan, where the glyph is
legitimate. A per-field allowlist is not warranted.

## Architecture (Approach 1: reusable scanner + thin layer)

Four components, smallest surface that does the job:

1. **`ContentLint` config record + loader wiring** (`Registrar.Core`). `ConfigLoader`
   currently deserializes only the `visualIdentity` node of `style-rules.json` into
   `StyleRules`; the `contentLint` node is ignored. Add a `ContentLint` record,
   deserialized from the `contentLint` node, exposed on `ConfigBundle.ContentLint`.
   It carries `approvedGlyphAllowlist` and the named prohibited exemplars (used to
   build friendly offense messages). No new config file, so no new hash entry - it
   rides `style-rules.json`'s existing hash.

2. **`CharacterPolicy`** (`Registrar.Validation`). Built once from `ContentLint`.
   Exposes an `IsAllowed(Rune)` predicate encoding the allowed ranges (a code
   constant per the spec's fixed definition) plus the config allowlist. If a future
   template pack ever needs different ranges, that is a code change plus an ADR, not
   a pure config release - an accepted consequence of the spec pinning the ranges.

3. **`CharacterLint`** (`Registrar.Validation`). A pure static scanner:
   `(string text, CharacterPolicy policy) -> IReadOnlyList<Offense>`, where
   `Offense` is `(Rune Codepoint, int Position)` with a 1-based position. No AGR
   knowledge; independently testable; the exact primitive a future post-render
   QA-SF-08 content scan reuses.

4. **`InputHygieneLayer : ISemanticLayer`** (`Registrar.Validation`). Builds its
   `CharacterPolicy` from `context.Config.ContentLint`, enumerates the six chrome
   fields (in code) and loops `dataInputs.placeholderMap` values, calls
   `CharacterLint`, and emits one `Check` per offending field. Registered first in
   `ValidationService`'s layer list (input-before-content ordering), outside the
   registry-gated block since it needs no registry. Runs only after schema
   validation passes, like the other semantic layers; the required chrome fields are
   therefore guaranteed present, and optional fields are linted only when non-null.

This split mirrors `PlaceholderLayer`'s separation of concerns and the framework's
stated preference for reusable primitives over duplicated logic (ADR-003
"promote reuse before creation").

## Check identity and reporting

**One `Check` per offending field** (a field with three bad characters produces one
check that lists them, not three checks). Each check carries:

- `checkId`: `QA-ENG-05`
- `layerName`: `input`
- `result`: `fail`; `severity`: `blocking` (both modes)
- `errorCategory`: `MetadataViolation` - matches the sibling input-field check
  QA-ENG-03; the report schema pins `errorCategory` to a fixed enum with no
  content/style member, and a bespoke category is not worth a schema revision here.
- `jsonPath`: the field pointer - `/context/owner`, `/context/audience`,
  `/context/preparedBy`, `/context/client`, `/context/engagement`,
  `/renderingOptions/packageName`, or `/dataInputs/placeholderMap/<key>` with
  RFC 6901 `~0`/`~1` escaping (reusing `PlaceholderLayer`'s escaping helper).
- `expected`: "only printable Basic Latin, printable Latin-1 Supplement excluding
  NBSP and soft hyphen, or an approved glyph".
- `actual`: the offending codepoint(s) rendered as `U+2014 (em dash) at position 12`,
  1-based, capped at the first five with an "and N more" suffix so a pathological
  field cannot bloat the report.
- `remediation`: Governor-executable, for example "Replace the em dash (U+2014) in
  context.owner with a standard hyphen, or remove it."
- `ruleSource`: `ORG-MST-SPEC-001 Section 4.2`.

**Catalog entry** - a new `QA-ENG-05` row in `config/qa-checklist.json`:

    { "checkId": "QA-ENG-05", "layer": "input", "automation": "full",
      "appliesTo": "all",
      "rule": "Chrome free-text fields (context.owner/audience/preparedBy/client/
               engagement, renderingOptions.packageName) and every
               dataInputs.placeholderMap value contain only allowed characters:
               printable Basic Latin, printable Latin-1 Supplement excluding NBSP
               and soft hyphen, plus approved glyphs. Blocks in draft and promotion.",
      "errorCategory": "MetadataViolation",
      "ruleSource": "ORG-MST-SPEC-001 Section 4.2",
      "alsoSatisfies": ["ORG-MST-WFE-001 4.5 item 8",
                        "ORG-MST-SPEC-001 Section 9 item 10",
                        "ORG-MST-UG Section 4"] }

The `ruleSource` anchors to the normative style rule (Spec 4.2); the
`alsoSatisfies` field holds the additional rule-source citations - the Workflow
Example short-form QA scan that enforces the prohibition "anywhere"
(ORG-MST-WFE-001 4.5 item 8, which is exactly QA-SF-08's source), the full
Pre-Delivery QA item (ORG-MST-SPEC-001 Section 9 item 10), and the Usage Guide
punctuation rule (ORG-MST-UG Section 4). Together these record both the "also
satisfies QA-SF-08" intent and the full provenance across all three governing
documents, within the existing citation-based convention.

## Error handling

- The layer never trusts the Governor and never throws on input content: any string
  value is scannable, and a null optional field is skipped, not faulted.
- Config load: if the `contentLint` node is missing or malformed, that is a
  `ConfigurationError` surfaced by the config loader (the engine cannot lint without
  its policy), consistent with how the engine treats other missing config. The
  committed `style-rules.json` always carries `contentLint`, so this is a
  defense-in-depth path, not an expected one.
- Fail closed on the render path: because the layer emits a blocking `fail`, the
  existing `OverallResultPolicy` yields `overall == fail`, and `build` produces no
  DOCX. No new gating logic is required in `BuildCommand`.

## Testing

Per repo convention every checkId gets at least one passing and one failing test
named after the checkId.

- **`CharacterLint` primitive (pure unit tests):** clean ASCII -> none; "Pena" with
  n-tilde (`U+00F1`) -> none; `U+25CB` -> none; em dash / en dash / ellipsis / smart
  quote -> one offense at the correct position; astral-plane emoji -> one offense
  (by `Rune`, not two); NBSP -> offense; soft hyphen -> offense; newline and tab ->
  offense; empty string -> none.
- **`CharacterPolicy`:** built from config; allowlist honored; ranges enforced.
- **`InputHygieneLayer` (fixture tests, named after the checkId):**
  `QA_ENG_05_passes` on a clean AGR emits no QA-ENG-05 failure; `QA_ENG_05_fails` on
  an em dash in `context.client` emits one blocking QA-ENG-05 fail at
  `/context/client`, asserted in both draft and promotion (parameterized); an
  offense in a `placeholderMap` value is reported at the correct escaped pointer;
  multiple bad fields produce multiple checks.
- **New fixture:** `fixtures/agr-sample-ds-prohibited-char.json` - a DS AGR with a
  smart quote in the client name - as the failing-category fixture; the existing
  valid DS fixture is the passing case.
- **End-to-end (`BuildCommand`):** build against the prohibited-char fixture fails
  closed, emits no DOCX, and the report contains the QA-ENG-05 failure. This is the
  acceptance proof that the render-path gap is closed.

## Non-goals (deferred, not silently dropped)

- Post-render QA-SF-08 text-run scan of the rendered document. This slice validates
  the AGR inputs before rendering; the honest content-layer scan over rendered runs
  remains available for a later render slice and shares the `CharacterLint`
  primitive.
- Linting narrative prose in section blocks (beyond placeholder values). Coverage
  was deliberately scoped to chrome fields plus placeholder values - the free text
  the engine substitutes verbatim today.
- A dedicated content/style `errorCategory` enum member (would be a report-schema
  revision; reusing `MetadataViolation` avoids it).
- Making the linted field list config-driven (premature abstraction until a second
  consumer exists).
- Normalization/auto-correction of prohibited characters. The engine blocks and
  reports; the Governor regenerates. Never silently rewrite Governor content.
