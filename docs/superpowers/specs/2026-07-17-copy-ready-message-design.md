# copyReadyMessage block (the last deferred block type)

Design spec. Status: approved for planning. Date: 2026-07-17.

## Goal

Render `copyReadyMessage` as a distinct, config-styled callout box, and adopt it in the
Delivery Cover Note (DCN) so the suggested delivery message reads as a clear "copy this
exact text and send it" block instead of a plain paragraph. This lands the last of the
four originally-deferred block types; after it, all ten AGR block types render and nothing
in the block set remains stubbed.

This is an enhancement, not a gap-fill: the DCN already renders its delivery message as a
paragraph and works. The value is the copy-ready visual affordance for the operator.

## Scope

In scope:

- `CopyReadyMessageBlock` renderer + config styling.
- Enforcement: QA-ENG-06 admits it; QA-ENG-07 scans it (it carries authored text).
- DCN adoption: retarget the "Suggested Delivery Message" section to the new block.
- Governor-docs reconciliation (supersedes PR #43, see below).
- Fixtures, tests, golden regeneration.

Out of scope:

- No new artifact types or section schemas. DCN's section schema is unchanged; only the
  block inside "Suggested Delivery Message" changes from paragraph to copyReadyMessage.
- No new colors: the box reuses existing neutral `visualIdentity` keys.

## Design

### Renderer and config

`CopyReadyMessageBlock` mirrors the built `WarnCalloutBlock`: a full-width, bordered,
lightly filled table box with a bold config-driven label line above the authored message
text. It reuses existing neutral `visualIdentity` colors (border `borderNeutral`, fill a
light neutral such as `dchFill` or `headerRightCellFill` - final pick made in the plan;
no new colors), deliberately distinct from warn-red. The render is pure and deterministic
(no clock, no ambient state), like every other block.

Config: `style-rules.json` gains a `copyReadyMessage` object, minimally `{ "label": "..." }`,
bound through `ConfigLoader` into a `CopyReadyMessageStyle` record on `ConfigBundle`, then
threaded onto `RenderContext`, exactly as `WarnCalloutStyle`/`WarnCallout` are. The AGR
block carries only `text`; the label is config-driven, not Governor-supplied (same as
warnCallout).

`BlockRenderer.Render` gains a `copyReadyMessage` case. After this slice, the `default`
branch's `NotSupportedException` covers no remaining block type (all ten render); the
branch stays as a defensive guard for an unknown/future type.

### Enforcement, and the distinction from aggregationTable

- **QA-ENG-06 (BlockTypeSupportLayer)**: `copyReadyMessage` is added to
  `SupportedBlockTypes`. The deferred set becomes empty. The invariant test
  `QA_ENG_06_supported_set_matches_renderer_capability` gains a `copyReadyMessage` sample;
  its `deferred` loop becomes empty and its assertions are adjusted so the invariant still
  holds truthfully (supported set == renderable set; no type both unsupported and
  rendered).

- **QA-ENG-07 (TokenResidueLayer)**: `copyReadyMessage` is ADDED to `ScanFields` (the
  `text` case, alongside `paragraph`/`warnCallout`/`h2`/`h3`). This is the opposite of
  `aggregationTable`, which is exempt. Rationale: copyReadyMessage carries authored text
  the operator copies verbatim to the client, so an unresolved `[TOKEN]` in it would ship
  broken and must fail closed. This is the single most important correctness property of
  the slice.

- **QA-SF-09 (WarnCalloutLayer / internal-only gating)**: no change. It gates `warnCallout`
  and `internalOnly` blocks out of client-facing output at promotion. `copyReadyMessage` is
  neither, so it renders in the client-facing DCN as intended.

- **QA-SF-08 (post-render prohibited characters)**: unchanged; scans the rendered box text
  via the existing scan-unit path.

### DCN adoption

- The DCN fixtures (`agr-sample-dcn-draft.json`, `agr-sample-dcn-promotion.json`) have their
  "Suggested Delivery Message" section's single `paragraph` block replaced with a
  `copyReadyMessage` block carrying the same text.
- The DCN `description` in `config/artifact-types.json` is updated: it currently says the
  delivery message renders as a body paragraph and the distinct copyReadyMessage block is
  not required; it now reflects that the DCN uses copyReadyMessage.

Golden consequences:

1. The DCN goldens change for real (the delivery message now renders as a box, not a
   paragraph). Regenerated and inspected, not blindly accepted.
2. Because `config/artifact-types.json` is in the validation-report `configVersions` hash
   set, every OTHER fixture's golden gets a hash-only refresh (only the artifact-types.json
   hash line changes). `config/qa-checklist.json` and `config/style-rules.json` are NOT in
   that set, so editing them does not stale goldens - but the artifact-types.json edit does.
   (Note: verify style-rules.json is not in configVersions during planning; if it is, the
   refresh is still hash-only but the driver differs.)

### Governor-docs reconciliation (supersedes PR #43)

The block-type guidance in `docs/governor-instructions.md` is already stale on main: it
tells the Governor that `warnCallout` and `aggregationTable` "are not built" and to avoid
them, but both now render (merged). The paste block is also currently over the 8000-char
Studio cap (8261 LF / 8387 CRLF); PR #43 (open) trims it but does not fix the staleness.

This slice reconciles the whole section instead: with `copyReadyMessage` landed, all ten
block types render, so the "only seven render / three deferred / express a warning as an
ordinary paragraph, an aggregated table as an ordinary table, a delivery message as an
ordinary paragraph" text is deleted, and the DCN guidance becomes "emit copyReadyMessage
for the delivery message." The same reconciliation is applied to `docs/governor-contract.md`
(its "Forbidden" block-type list and DCN guidance).

Because the reconciliation DELETES the deferred-type workaround text, the instructions paste
block should drop comfortably under 8000. The plan MUST verify the final count is under the
cap (using the CRLF-pessimistic count, per the PR #43 precedent). This makes PR #43's
separate trim redundant; PR #43 is closed as superseded once this merges.

## Testing

- `CopyReadyMessageBlockTests`: renders a bordered box, the config label line, and the
  message text; deterministic.
- **QA-ENG-06**: passing (copyReadyMessage accepted) and the invariant test updated.
- **QA-ENG-07**: passing (a clean copyReadyMessage is not flagged) and FAILING (a
  `copyReadyMessage` whose text contains a bracketed `[TOKEN]` trips residue) - this pair
  proves the block is scanned, unlike aggregationTable.
- DCN end-to-end draft + promotion: builds pass, the delivery message renders as the box,
  byte-deterministic.
- Golden regeneration: DCN goldens (real change, inspected) + all others (hash-only from the
  artifact-types.json hash).
- Governor-instructions paste-block char count verified under 8000.

## Consequences and follow-ups

- After this slice, the block set is complete: all ten AGR block types render. The Unsafe
  Block Exclusion Rule no longer excludes anything; the `default` branch remains a guard.
- `docs/tech-debt.md` item 8 (render the remaining deferred block types) is fully closed.
- PR #43 is superseded and closed.
