# Slice 5: Renderer Foundation and Universal Chrome - Design

**Status:** Approved (design)
**Date:** 2026-07-05
**Predecessor:** Slice 4 (promotion payload + QA-SPEC-18)

## Goal

Stand up the DOCX renderer and the `registrar build` command. `build` runs the full
validation pipeline and, on a pass, renders a real `.docx` containing the
universal chrome - header table (with the ORG cloud mark), the Document Control
Header, the Configuration callout, and the footer - built from scratch with the
Open XML SDK and driven entirely by config. This is the first of several render
slices; body-section rendering (block renderers, section assembly) is Slice 7
(Slice 6 is the AGR input-validation layer).

## Scope decisions (locked)

1. **Universal chrome only, no body sections.** Section headings cannot render
   yet: the `no-empty-h1` content-lint rule (style-rules.json) forbids a heading
   with no body, and bodies require the block renderers (paragraph, table, radio,
   clause) that are Slice 7. Slice 5 renders page setup + header table + DCH +
   Configuration callout + footer, and stops. The result is a well-formed Word
   document that proves the plumbing; it is not yet a client deliverable.
2. **`build` = validate + render-on-pass.** `build` reuses `ValidationService`
   exactly like `validate`, writes `validation-report.json` (and
   `promotion-payload.json` when applicable), and only then renders. Fail closed:
   a fail produces no `.docx`.
3. **Real logo now.** The ORG cloud mark is embedded as an Open XML image part
   from `assets/org-logo.png` (52x54 px, committed). Its filename and display
   height are referenced from `style-rules.json` so a template pack can swap it
   without code changes.
4. **`artifact-manifest.json` and `generation-log.json` stay deferred.**

## DOCX emission rule

Per CLAUDE.md output-package item 1, the DOCX is produced only when
`overall == pass`, OR `overall == pass-with-warnings AND mode == draft`. A
promotion run with warnings does NOT produce a DOCX.

Interpretation note: this is intentionally stricter than the promotion-payload
gate shipped in Slice 4 (which emits the payload on any non-fail promotion). A
promotion-with-warnings can therefore yield a payload but no DOCX - the registry
may be updated while the client deliverable is withheld until the warnings clear.
This follows the constitution's explicit output-package definition; if the
documentation owner intends the two gates to match, that is a separate change.

## Non-goals (deferred, not silently dropped)

- Body-section rendering: block renderers (paragraph, h2/h3, table,
  radioQuestion with U+25CB, clauseInsertion, aggregationTable, copyReadyMessage,
  warnCallout, bulletList) and per-artifact section assembly -> Slice 7.
- `artifact-manifest.json` (needs the DOCX SHA-256 and section inventory) and
  `generation-log.json` -> later slice.
- A higher-resolution logo and any logo image beyond the committed 52x54 asset.
- Post-render content-lint enforcement against the rendered runs (QA-SF-08 etc.
  scan the rendered document); Slice 5 renders hygienic chrome by construction
  and asserts it in tests, but the rendered-document lint pass is a render-slice
  concern once bodies exist.

## Architecture

A new `Registrar.Rendering` project holds the Open XML rendering; `Registrar.Cli`
references it and gains a `build` verb. Rendering is driven by the validated
`AgrDocument`, the `ConfigBundle` (now carrying style rules), and the committed
logo asset.

### New and changed components

- **`Registrar.Rendering` project** with a `DocumentFormat.OpenXml` package
  reference (the one third-party dependency CLAUDE.md sanctions). Reference graph:
  `Cli -> Rendering -> Core`; `Rendering -> Core`.
- **`assets/org-logo.png`** - the committed brand asset (52x54 px).
- **`style-rules.json` addition** - a `visualIdentity.wordmark` entry:
  `{ "asset": "org-logo.png", "heightIn": 0.4 }`.
- **`StyleRules` config model + `ConfigBundle.Style`** - load
  `style-rules.json`'s `visualIdentity` (fonts, colors, borders, page,
  footerTemplate, wordmark) into a typed `StyleRules` record; hash the file into
  `configVersions`. The renderer reads style values from here - no hardcoded
  fonts, colors, or sizes in rendering code (rule 3).
- **`AgrDocument.AgrContext` extension** - add `Owner`, `Audience`, `PreparedBy`,
  `Client`, `Engagement` (the DCH row 2, header title stack, and callout need
  them; Slice 2 mapped only `reviewDate`/`reviewDateOverride`).
- **`ConfigLocator.FindAsset(baseDir, fileName)`** - sibling to `FindConfig`,
  resolving `assets/<fileName>` from the repo root.
- **Four component renderers** in `Registrar.Rendering`, each a focused unit taking
  the data it needs plus `StyleRules` and returning Open XML elements:
  - `HeaderTableRenderer` - a two-cell header table: the embedded logo image on
    the left, a three-line title stack on the right (artifact name; client or
    engagement; program line).
  - `DchRenderer` - the "DOCUMENT CONTROL HEADER" banner label plus a two-row,
    four-column table. Row 1: Document ID, Version, Classification, Approval
    Status. Row 2: Owner, Review Date, Audience, Prepared By. Values from the
    validated `AgrDocument`; a null draft review date renders "To be set on
    Approval".
  - `ConfigCalloutRenderer` - a bordered callout: Artifact (type name),
    Description (artifact type description), Audience, Settings + ID Pattern
    (artifact type id pattern).
  - `FooterRenderer` - the footer template with `[PACKAGE]`/`[ARTIFACT]`/
    `[VERSION]` resolved, `Page X of Y` via the Open XML PAGE/NUMPAGES fields, and
    the "ORG Consulting, LLC" close.
- **`ArtifactRenderer`** - assembles the `WordprocessingDocument`: US-Letter page
  setup with 1in margins, the four components in order (header table, DCH,
  callout, footer), core document properties stamped from `IClock`. Returns the
  document bytes. Depends on `AgrDocument`, `StyleRules`, the logo bytes, `IClock`.
- **`BuildCommand`** (`Registrar.Cli`) - `registrar build <agr.json> --mode
  draft|promotion [--registry <path>]`: runs `ValidationService.Validate`, writes
  the report (and payload), and on a DOCX-eligible outcome parses the AGR + loads
  config + reads the logo asset and calls `ArtifactRenderer`, writing the `.docx`
  under `renderingOptions.outputFileName`. Exit codes match `validate` (0 pass /
  pass-with-warnings, 1 fail, 2 usage/IO).

### Data flow

```
registrar build <agr.json> --mode draft|promotion [--registry <path>]
  1. ValidationService.Validate -> outcome (report + payload?)   [same as validate]
  2. write validation-report.json (+ promotion-payload.json when present)
  3. overall == fail? -> no docx, exit 1
  4. docx-eligible (pass, or draft pass-with-warnings)?
       parse AgrDocument; load ConfigBundle (StyleRules); read assets/org-logo.png
       ArtifactRenderer.Render(agr, style, logoBytes, clock) -> byte[]
       write <renderingOptions.outputFileName>
  5. exit 0
```

## Determinism and testing

- **Determinism:** the rendered `.docx` is a zip with internal timestamps, so
  byte-for-byte reproducibility is not a goal. Core document properties
  (created/modified) come from the injected `IClock`. Tests assert document
  CONTENT, not bytes.
- **Testing approach:** render a valid DS AGR to an in-memory or temp `.docx`,
  reopen it with `WordprocessingDocument`, and assert:
  - the DCH table cells equal the AGR values (Document ID `ORG-GOV-DS-016`,
    Version `v1.0`, Classification `Client-Facing`, Approval Status `Draft`,
    Owner, Review Date rendering, Audience, Prepared By);
  - the header title stack contains the artifact name and client/engagement;
  - the footer contains the package name, version, and "ORG Consulting, LLC";
  - an image part exists (the embedded logo) with a valid relationship;
  - no prohibited characters (em/en dash, ellipsis, emoji) appear in any run.
- **Fail-closed test:** `build` on a schema-invalid AGR writes the report, writes
  no `.docx`, and exits 1.
- **Config test:** `StyleRules` loads from `style-rules.json`; `ConfigLocator`
  `FindAsset` resolves the logo.
- Each component renderer gets a focused test that reopens its output and checks
  the expected cells/text.

## Interpretation notes

- **Title-stack wording.** The three header lines are assumed to be artifact name
  / client-or-engagement / a program line ("ORG AI Governance Program"). The exact
  wording may be specified in ORG-MST-SPEC-001; if it matters, it is a
  documentation-owner/Governor ruling, and the strings are config- or
  metadata-derived so a correction is a data change.
- **DOCX vs payload gate.** See "DOCX emission rule" above - the DOCX gate is
  stricter than the payload gate by constitutional definition.
- **Logo resolution.** The committed asset is 52x54 px, displayed at ~0.4in tall;
  adequate for a small header mark. A higher-resolution asset swaps in via the
  `style-rules.json` `wordmark.asset` reference with no code change.

## Component boundaries summary

- `StyleRules` - typed visual identity; depends on nothing.
- `HeaderTableRenderer` / `DchRenderer` / `ConfigCalloutRenderer` /
  `FooterRenderer` - each renders one chrome element from data + `StyleRules`.
- `ArtifactRenderer` - assembles the document; the only unit that knows element
  order and page setup.
- `BuildCommand` - orchestration; the only unit that knows the validate ->
  render-on-pass flow and the DOCX emission rule.
- `ConfigBundle`/`ConfigLoader` - extended to carry `StyleRules`.
- `AgrDocument.AgrContext` - extended with the metadata the chrome needs.
