# Rendering Polish (Direction B) - Design

**Status:** Approved (design)
**Date:** 2026-07-08
**Predecessor:** Slice 9 (v2 acceptance closeout); first post-v2 slice
**Scope:** Cosmetic; DS and AUP render targets only

## Goal

Make the generated DOCX read as an authored consulting deliverable rather than a
script dump, without changing any governance behavior, validation semantics, or
document content. The engine already applies the ORG brand identity (Montserrat
fonts, navy/blue palette, embedded cloud logo) per run; what it lacks is the
typographic *detail* that separates authored from generated output: vertical
rhythm, a visible heading hierarchy, and table breathing room.

This slice adds that detail as **config-driven typography** in the existing Open
XML renderer. It is the "Direction B (structured editorial)" option from the
sample set reviewed on 2026-07-08.

Every change is a standard Open XML paragraph or table property whose values live
in new `config/style-rules.json` keys, so the visual pack remains a config
release (rule 3). No outside tool, no seed `.docx`, no third-party template
engine, and no `styles.xml` are introduced (stack constraints in CLAUDE.md). No
runtime LLM call is added (rule 2).

## Non-goals (locked)

- **No new characters in the document.** Spacing, sizes, and rules are formatting,
  not glyphs. The QA-SF-08 post-render character scan and the em/en-dash
  prohibition are untouched. See "The tab-character catch" below for the one place
  this constraint shaped the design.
- **No governance, validation, or QA-check semantics change.** No new checkId, no
  change to any layer's pass/fail behavior, no lifecycle/registry/clause impact.
- **No structural invention (rule 6).** Section schemas, block types, and the
  universal closing elements are unchanged. This slice restyles what already
  renders; it adds no sections, block types, or artifact types.
- **No first-column table emphasis.** The sample's Direction B bolded the first
  table column in navy. Dropped by decision: not every table's first column is a
  key, and auto-emphasizing it could misrepresent governed data. Excluded entirely
  (not even a disabled flag) to keep the surface minimal.
- **DS and AUP only.** UQR, TCL, TAK, RAF, DCN remain out of scope per the v2
  acceptance contract; this slice does not touch them.

## Scope decisions (locked)

1. **All values are new `style-rules.json` keys; core logic gains no constants.**
   The renderer reads spacing, heading sizes, rule weight, section-number color,
   table cell margins, and logo height from config. No point size, color, or
   measurement is hardcoded in a renderer. This preserves rule 3 and keeps the
   look a config-pack concern.

2. **Inline paragraph/table properties, not a styles part.** The current renderer
   builds the body from direct-formatted runs with no `StyleDefinitionsPart`. This
   slice keeps that model and attaches `SpacingBetweenLines`, `ParagraphBorders`,
   and `TableCellMargin` inline. Introducing `styles.xml` is a larger architectural
   change with no benefit at this document scale and is explicitly not done here.

3. **Two heading tiers, styled distinctly.** There are two heading kinds today:
   - **Section titles** are composed by `SectionRenderer` as
     `"{index + 1}. {Title}"` and rendered at `typeScale.titlePt` (16pt) navy.
   - **`h2` / `h3` blocks** come verbatim from AGR `block.Text` and both render at
     `typeScale.subtitlePt` (11pt) via `HeadingBlock`, separated only by color.

   The flat spot is h2 == h3. The fix gives h2 its own size (13pt) so size, not
   only color, carries level. Section titles keep 16pt and gain the left accent bar
   and spacing.

4. **Section number styled in brand blue, kept as `"N."`.** The renderer-composed
   section number is emitted as its own run in `sectionNumber.color` (brand blue),
   with the title following in navy. The number retains its trailing period
   (`"1."`) rather than a tab-separated `"1"` - see the tab-character catch.

5. **Section separation is a left accent bar, not a horizontal rule.** A pale
   full-width horizontal hairline under each heading was rejected in review: it
   reads as generic AI/Copilot default output. The adopted treatment is a short
   vertical brand-blue bar to the LEFT of each section title (rendered as a thick
   left `ParagraphBorders` border with a matching left indent - the idiomatic Open
   XML "blockquote bar" construct). The masthead carries no separator rule at all;
   it is set off by spacing only. This also removes the grey line under the logo
   block that the rule previously produced.

6. **Design-lead optimization pass (beyond the minimum fix).** Five refinements
   were adopted to move the output from "fixed" to "considered," each staying
   config-driven and governance-safe: (a) a systematic spacing scale
   (before > after; section > h2 > h3); (b) horizontal-rules-only body tables via
   `tableBorders`; (c) a modernized, borderless Document Control Header with
   uppercase gray micro-labels (`dchLabel`) - the DCH was the most spreadsheet-like
   element on the page; (d) pagination controls (`KeepNext` on headings,
   `WidowControl` on body) for real multi-page output; (e) confirming the footer
   stays borderless. Items (c) and (d) touch universal chrome / all block
   renderers and slightly widen the change surface beyond the section-body
   components, which is intended.

## Config additions (`config/style-rules.json`)

Added under `visualIdentity` (data only; the `$comment` documenting the block's
provenance is extended to cover these keys):

- `spacing` (a systematic scale; for every heading, space-BEFORE exceeds
  space-AFTER so the heading binds to the text beneath it, and section > h2 > h3):
  - `bodyAfterPt: 6`, `lineSpacingPct: 132` (~1.32)
  - `sectionTitleBeforePt: 16`, `sectionTitleAfterPt: 6`
  - `h2BeforePt: 10`, `h2AfterPt: 4`
  - `h3BeforePt: 8`, `h3AfterPt: 3`
  - `bulletAfterPt: 3`
- `headingScale`: `h1Pt: 16`, `h2Pt: 13`, `h3Pt: 11`
  - (`h1Pt` is the section title; it equals the existing `typeScale.titlePt`. The
    section title reads its size from `headingScale.h1Pt` so all heading sizes have
    one home.)
- `sectionAccent`: `widthHalfPoints` (thick, ~18 = ~2.25pt bar), `color` (brand
  blue `#005CB9`), `indentTwips` (space between bar and title text). Rendered as a
  thick left paragraph border plus a matching left indent - a short vertical accent
  bar to the left of each section title. Replaces the rejected full-width hairline.
- `sectionNumber`: `color` (brand blue `#005CB9`)
- `tableCellMargin`: `topTwips`, `bottomTwips`, `leftTwips`, `rightTwips`
  (cell padding; ~40/40/90/90 twips as a starting point)
- `tableBorders`: which sides body tables draw - `outer: false`,
  `insideHorizontal: true`, `insideVertical: false` (horizontal-rules-only tables;
  the navy header fill, zebra, and cell padding carry the rest). `widthHalfPoints` /
  `color` reuse the neutral border spec.
- `pagination`: `keepHeadingWithNext: true` (section titles and h2/h3 emit
  `KeepNext` so a heading never strands at a page foot) and `widowControl: true`
  (body paragraphs emit `WidowControl`). Real multi-page behavior.
- `dchLabel`: Document Control Header field-label treatment - `color`
  (`secondaryGray`), `uppercase: true`, `characterSpacingTwips` (letter-spacing).
  Turns the boxed navy-bold label into an uppercase gray micro-label over the value.
- `wordmark.heightIn`: **0.4 -> 0.55** (logo size-up; existing key, new value)

New records/fields mirror these in `Registrar.Core.Config` (`StyleRules` and
friends) so the config binds strongly-typed, consistent with existing
`TypeScale` / `FontSet` records.

## Renderer changes (one focused edit per component)

- **`OpenXmlHelpers` (or a small new helper).** Add a way to attach a
  `ParagraphProperties` carrying `SpacingBetweenLines` (before/after/line + line
  rule) built from the config spacing values, so no component composes spacing
  numbers itself. This is the shared primitive the block renderers call.

- **`SectionRenderer`.** Emit the section title as two runs: a number run
  (`"N."`, `sectionNumber.color`, bold) and a title run (`Title`, navy, bold) at
  `headingScale.h1Pt`. Attach section-title spacing plus the left accent bar: a
  left `ParagraphBorders` border at `sectionAccent.widthHalfPoints`/`color` and a
  left `Indentation` of `sectionAccent.indentTwips` so the text clears the bar. No
  horizontal rule. The combined `InnerText` remains `"N. Title"`, identical to
  today, so the post-render scan unit is unchanged.

- **`HeadingBlock`.** Use `headingScale.h2Pt` / `h3Pt` for size (currently both
  `subtitlePt`); keep the existing color rule (h3 accent blue, h2 navy). Attach
  h2/h3 spacing.

- **`ParagraphBlock`.** Attach body spacing (`bodyAfterPt`, `lineSpacingPct`).

- **`BulletListBlock`.** Attach `bulletAfterPt` spacing to each item paragraph
  (alongside the existing numbering properties).

- **`TableBlock`.** Add a table-level `TableCellMargin` (`TblCellMar`) from
  `tableCellMargin`, and emit only the border sides enabled in `tableBorders`
  (horizontal insides on; verticals and outer box off). Keep the navy header fill
  and zebra. No first-column emphasis.

- **`DchRenderer`.** Drop the full gridline `TableBorders` (render the shaded panel
  borderless), and restyle field labels per `dchLabel`: `secondaryGray`, uppercase,
  with run `Spacing` (character spacing) - a micro-label over the value instead of a
  boxed navy-bold label. Universal chrome, so this refines the DCH for every artifact
  type, which is intended. The "Document Control Header" banner caption is retained.

- **Pagination (`SectionRenderer`, `HeadingBlock`, `ParagraphBlock`,
  `BulletListBlock`).** When `pagination.keepHeadingWithNext`, section-title and
  h2/h3 paragraphs emit `KeepNext`. When `pagination.widowControl`, body and bullet
  paragraphs emit `WidowControl`. Invisible on a single page; prevents orphaned
  headings and stray single lines in real multi-page output.

- **`FooterRenderer`.** No change: the footer is already borderless (no top rule),
  which matches the no-generic-hairline direction. Called out so it is not
  "polished" back into a rule.

- **`HeaderTableRenderer`.** Logo height already reads `style.Wordmark.HeightIn`,
  so the size-up is purely the config value change (0.55"). No separator rule is
  added: the masthead is set off from the DCH block by spacing only. The header
  table stays fully borderless, so no grey line appears under the logo block.

## The tab-character catch (design rationale)

A tab-separated section number (`"1"` + tab + title) would place a tab character
(U+0009, a C0 control character) into a `Text` run. The QA-SF-08 post-render scan
rejects C0/C1 control characters, so that would fail the document closed. The
design therefore keeps the number as `"N."` with the separating space, coloring
only the number run. Bonus: the scanned `InnerText` stays byte-identical to the
current output, so no existing scan-based test shifts. Visual separation between
number and title, if desired later, must come from paragraph indentation or a
`TabChar` element with a tab stop (formatting, not a text character) - noted for a
future pack, not done here.

## Testing

Rendering-structure unit tests (not QA-check-keyed, since this slice mints no
checkId) asserting the config-driven properties are emitted:

- Body/heading/bullet paragraphs carry `SpacingBetweenLines` with the configured
  before/after/line values.
- `HeadingBlock` emits `h2Pt` for h2 and `h3Pt` for h3 (distinct sizes).
- Section-title paragraph carries a LEFT `ParagraphBorders` border plus a left
  `Indentation` (the accent bar) at `sectionAccent` width/color, and the number
  run's color equals `sectionNumber.color`. No bottom/horizontal border is emitted
  on the section title or the header table.
- `TableBlock` emits `TableCellMargin` matching config, and only the border sides
  enabled in `tableBorders` (inside-horizontal present; inside-vertical and outer
  box absent).
- Section-title and h2/h3 paragraphs carry `KeepNext`; body and bullet paragraphs
  carry `WidowControl` (when the `pagination` flags are on).
- `DchRenderer` emits the field table without gridline borders, and field-label runs
  use `secondaryGray` with the configured character `Spacing`.
- The header logo `Extent.Cy` reflects `wordmark.heightIn = 0.55`.

Regression guards:

- The existing DS and AUP end-to-end render tests still pass unchanged (document
  opens, validates, and the QA-SF-08 scan passes), confirming no new character or
  structural regression.
- A test asserting the section-title `InnerText` is still `"N. Title"` (no tab, no
  new glyph), pinning the tab-character decision.

## Risks and mitigations

- **Font availability on the operator machine.** Montserrat may be absent;
  `RunFonts` already sets the family with Word's Arial fallback. Unchanged by this
  slice.
- **Config-model drift.** New `StyleRules` fields must be added to the config
  binder and to any config fixture used by tests; missing keys should fail loudly
  at load, consistent with the existing required-member records.
- **Value tuning.** The spacing/margin starting values (6pt after, 1.3 line, 40/90
  twip margins, 0.55" logo) are first-pass; they are config, so tuning is a config
  edit, not a code change. The plan should render a real DS and AUP and eyeball the
  output before finalizing values.

## Out of scope (deferred)

- `styles.xml` / document default styles.
- First-column table emphasis.
- Tab-stop-based number/title alignment.
- Any restyle of UQR/TCL/TAK/RAF/DCN, deferred block types, or the universal
  closing elements.
- Footer logo mark (considered in brainstorming, not adopted for this slice).
