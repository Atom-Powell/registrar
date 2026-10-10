# warnCallout block rendering - design

Date: 2026-07-15
Status: approved (design), pending implementation plan
Scope: The first of the three still-deferred block types. Render `warnCallout`.
`copyReadyMessage` and `aggregationTable` remain deferred to their own slices.

## Problem

`BlockRenderer` throws `NotSupportedException` on `warnCallout`,
`aggregationTable`, and `copyReadyMessage` (the Unsafe Block Exclusion Rule), and
`BlockTypeSupportLayer` (QA-ENG-06) fails any AGR carrying one before rendering.
`radioQuestion` was the first of the four to land (PR #25). This slice lands
`warnCallout`, the next-cleanest, because its governance enforcement already
exists and is live.

Grounding against the current code:

- **No new gate is needed.** `WarnCalloutLayer` (QA-SF-09) is already implemented
  and registered in `ValidationService` (ValidationService.cs:73). It blocks a
  `warnCallout` block in client-facing output at promotion (client-facing read
  from the CONFIGURED artifact classification, never the Governor's). Today
  QA-ENG-06 rejects `warnCallout` first, so QA-SF-09's warn-callout branch is
  unreachable; enabling rendering makes it reachable.
- **Block types are not section-schema-gated.** `SectionSchemaLayer` (QA-ENG-02,
  SectionSchemaLayer.cs) validates that section TITLES match the artifact type's
  schema exactly and in order; it does not constrain which block types a section
  contains. So a `warnCallout` can appear in any section's blocks without any
  artifact-types.json change.
- **The palette is pre-provisioned.** `config/style-rules.json` `visualIdentity`
  already defines `warnRedBorder` (#C0504D) and `warnRedFill` (#FBEEEC), dead
  config today, exactly as `radioOptionPattern` was before `radioQuestion`.
- **The schema already permits it.** `warnCallout` is in the `blockType` enum;
  the block carries `text` (no dedicated title field).

## Behavior

Once landed, a `warnCallout` renders as a styled callout box wherever it is
legal:

- Draft, any artifact type: renders.
- Promotion, non-client-facing artifact (e.g. RAF, classification "Controlled
  Signoff", with a defined section schema and existing fixtures): renders.
- Promotion, client-facing artifact (DS/AUP/UQR/TCL/TAK/DCN): QA-SF-09 fails
  closed, no document. (Already enforced; now reachable.)

## Decisions

- **Config-driven label.** The block carries only `text`, so the callout's
  heading is a fixed, config-driven label (default "Warning"), not block data.
  It lives in `config/style-rules.json` and is bound into the config bundle,
  mirroring `radioOptionPattern`.
- **Reuse the existing warn palette.** The renderer references the existing
  `warnRedBorder` / `warnRedFill` color keys; no new colors.
- **Token residue applies.** `warnCallout.text` is Governor-supplied prose
  rendered verbatim, so it is scanned for `[TOKEN]` residue by QA-ENG-07, exactly
  like a paragraph.

## Components

New:

- `Registrar.Core.Config.WarnCalloutStyle` (or similar record): `{ Label }`,
  deserialized from a new `warnCallout` object in `config/style-rules.json`.
  Bound in `ConfigLoader` (ConfigLoader.cs, next to `radioPattern`) and exposed
  as `ConfigBundle.WarnCallout`, mirroring `ConfigBundle.RadioPattern`.
- `config/style-rules.json` gains a `warnCallout` object: `{ "label": "Warning" }`.
  (The colors are referenced by the existing `warnRedBorder` / `warnRedFill`
  keys in `visualIdentity.colors`; no new color entries.)
- `Registrar.Rendering.Blocks.WarnCalloutBlock` - `Render(string text,
  WarnCalloutStyle warn, StyleRules style) -> Table`. A full-width bordered table
  (borders `warnRedBorder`, single cell shaded `warnRedFill`) with a bold label
  line (the configured label) followed by a body paragraph of `text`. Structure
  mirrors `ConfigCalloutRenderer.Render`.

Changed:

- `Registrar.Rendering.RenderContext` - carry the `WarnCalloutStyle` (or the
  whole style already carried plus the new config), so `BlockRenderer` can render
  the callout. (RadioPattern is already carried on `RenderContext` as `ctx.Radio`;
  add `ctx.Warn` the same way.)
- `Registrar.Rendering.Blocks.BlockRenderer` - add `case "warnCallout"`: render
  via `WarnCalloutBlock`, return the block's `text` as `ScanText` (so QA-SF-08's
  post-render character scan covers the rendered runs), no clause check/audit.
- `Registrar.Validation.BlockTypeSupportLayer` - add `"warnCallout"` to
  `SupportedBlockTypes` (and to the `Expected` message string listing the
  supported set).
- `Registrar.Validation.TokenResidueLayer` - add a `case "warnCallout"` that
  yields `("/text", block.Text)` so QA-ENG-07 scans warn-callout prose for
  unresolved tokens.

Unchanged by design:

- `WarnCalloutLayer` (QA-SF-09) - already blocks warn callouts at client-facing
  promotion; this slice only makes that path reachable.
- `ArtifactRenderer` section-assembly loop - it already iterates blocks and
  collects `ScanText`/checks/audits per block; `warnCallout` flows through
  unchanged once `BlockRenderer` handles it.

## Fixtures and tests

- **Retarget the QA-ENG-06 fixture.** `fixtures/agr-sample-ds-unsupported-block.json`
  currently uses a `warnCallout` block to trigger QA-ENG-06. Once `warnCallout`
  is supported it would no longer fail there, so switch that fixture's offending
  block to a still-unsupported type (`aggregationTable` or `copyReadyMessage`) and
  regenerate its golden. The QA-ENG-06 test keeps its meaning.
- Unit (`WarnCalloutBlock`): renders a table with the warn borders/fill, the
  configured label as a bold run, and the text; a config test that
  `style-rules.json` `warnCallout.label` binds to `ConfigBundle.WarnCallout`.
- Unit (`BlockRenderer`): `case "warnCallout"` returns the box and the text as
  `ScanText`; no longer throws.
- Unit (`BlockTypeSupportLayer`): `warnCallout` is now in `SupportedBlockTypes`
  (no QA-ENG-06 failure for it).
- Unit (`TokenResidueLayer`): a `warnCallout` whose `text` carries a `[TOKEN]`
  residue produces a QA-ENG-07 failure.
- Integration (`BuildCommand`): a DS draft fixture containing a `warnCallout`
  renders a DOCX (previously QA-ENG-06 fail); a RAF promotion fixture (Controlled
  Signoff, not client-facing) with a `warnCallout` renders it at promotion. The
  client-facing promotion block is already covered by `WarnCalloutLayerTests`.
- Determinism: the golden-file fixture runner byte-compares the rendered DOCX
  (artifactSha256) for any new fixture that produces a document.

## Determinism

The renderer remains a pure function of (AGR + config): the callout box is built
from the block `text` and static config (label, color keys), no clock or GUIDs.
The warn palette and label come from config, so a v1.x style change stays a
config release.

## Out of scope (YAGNI)

- `copyReadyMessage` and `aggregationTable` rendering (their own slices).
- Any section-schema (`artifact-types.json`) change - block types are not
  schema-gated.
- A per-block severity/label (Warning vs Caution vs Note) - the schema block
  carries only `text`; the label is a single fixed config value this slice.
- Configurable warn colors beyond the existing `warnRedBorder` / `warnRedFill`.
