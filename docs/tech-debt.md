# Deferred Follow-Ups

Tracked, non-blocking work carried forward from completed slices' code reviews.
None of these block a release; they are recorded here so they survive past the
scratch review ledger. Fold the relevant ones into a future slice's plan rather
than resolving them silently.

## From Slice 6 (AGR Input Validation Layer, QA-ENG-05)

Cheap test-coverage gaps worth closing in a future slice:

1. **Multiple offenses in one field are not directly asserted.** The layer
   emits exactly one `QA-ENG-05` `Check` per offending field, listing all
   offenses (up to five) in `Actual`. That collapse is structurally correct but
   only ever exercised with zero or one offense per field; the `FormatActual`
   cap-at-five test builds `Offense` records directly rather than routing
   through `Scan`. Add a case: one field containing two prohibited characters,
   asserting `checks.Count == 1` and both code points present in `Actual`.
   File: `tests/Registrar.Tests/InputHygieneLayerTests.cs`.

2. **JSON-Pointer `~1` slash-escape path is untested.** The placeholder-offense
   test uses a key with no slash, so `InputHygieneLayer.EscapeToken`'s slash
   handling never runs, even though the fixture contains slash-bearing keys
   (for example `[CLIENT TENANT NAME / TENANT ID]`). Point the test at a
   slash-bearing key and assert the escaped pointer. The same escape is also
   untested in the pre-existing `PlaceholderLayer.PointerFor`.

Deferred until a natural trigger:

3. **JSON-Pointer escaper extraction - still deferred (trigger reasoning
   corrected by Slice 7).** The original note tied this extraction to "when the
   post-render QA-SF-08 content scan lands (the second consumer of
   `CharacterLint`)." QA-SF-08 landed in Slice 7 (`Registrar.Rendering.PostRenderScan`)
   and is indeed the second `CharacterLint` consumer - but it does NOT trigger the
   escaper extraction, because it emits document-origin locators (clause id /
   section title) in the check message with a null `jsonPath`; it produces no
   `placeholderMap`-key JSON pointers. So `InputHygieneLayer.EscapeToken` and
   `PlaceholderLayer.PointerFor` still have exactly two consumers - rule-of-three
   is unmet, and the extraction stays deferred until a third pointer-emitting
   consumer appears. The dead `contentLint.prohibitedCharacters` exemplar block in
   `config/style-rules.json` is likewise not read by QA-SF-08 (it uses
   `approvedGlyphAllowlist` via `CharacterPolicy`), so it remains harmless
   documentary and is left in place.

## From Slice 7 (Body-Section Rendering)

Deferred work recorded so it survives past the scratch review ledger. None of
these block a release.

6. **Content-Lint slice (required successor - "Slice 8").** The prose-subset
   body renderer ships without structural/warn content-lint enforcement, which
   is deferred to a dedicated content-lint slice. That slice implements: the
   already-catalogued **QA-SF-09** (zero warn/red callouts in client-facing
   output at promotion), **no-empty-h1** and **no-heading-level-skips** (both in
   `style-rules.json` `contentLint.prohibitedPatterns`), and a new engine check
   **QA-ENG-07** (rendered-body token residue - see item 7). It is a hard
   prerequisite for rendering the deferred blocks safely and for promoting any
   body with heading structure; it does NOT gate the current DS/AUP path (their
   bodies are plain paragraph + clause).

   **Resolved by Slice 8.** Shipped as three pre-render `ISemanticLayer`
   implementations: `HeadingStructureLayer` (QA-SF-07, `no-empty-h1` /
   `no-heading-level-skips`), `WarnCalloutLayer` (QA-SF-09, promotion-only,
   client-facing classification), and `TokenResidueLayer` (QA-ENG-07 - see
   item 7). Rendering the deferred block types safely and promoting any
   heading-bearing body are now unblocked. Internal-only-note detection (the
   second half of QA-SF-09's original scope) is now **resolved** (2026-07-11):
   `contentBlock` carries an optional `internalOnly` boolean, and
   `WarnCalloutLayer` (QA-SF-09) fails any `internalOnly` block in a
   client-facing artifact at promotion, alongside the existing warnCallout gate.
   Because the flag rides on an otherwise-supported block, an internal note still
   renders in internally-classified artifacts. The `style-rules.json`
   `contentLint.prohibitedPatterns` entries `no-warn-callouts-in-client-facing`,
   `no-empty-h1`, and `no-heading-level-skips` are documentary duplicates of
   these now-enforced checks (the layers read the layer-specific config, not
   this array).

7. **QA-ENG-07 Token Residue Detection (renamed from the design's "QA-SF-09";
   renumbered from QA-ENG-06 once that id was implemented in Slice 7 review as
   the block-type-support check).** The Slice 7 design named the deferred
   rendered-body token-residue check "QA-SF-09". That id was already taken:
   `config/qa-checklist.json` defines QA-SF-09 as the warn-callout check (item 6
   above), and QA-SF-10/11/12 are also framework-assigned. The token-residue
   check is therefore an engine-family id, **QA-ENG-07**: detect unresolved
   `[TOKEN]` residue left in rendered body prose (QA-ENG-05 lints input free
   text and placeholder values but not stray tokens in Governor-supplied body
   text, and QA-SF-08's character scan does not flag bracketed ASCII). Semantic
   check, distinct from QA-SF-08. Deferred to the content-lint slice.

   **Resolved by Slice 8.** Shipped as `TokenResidueLayer`, reading the
   config-driven pattern at `style-rules.json` `contentLint.tokenResidue.pattern`
   and scanning non-clause block fields (`text`, bullet items, table cells);
   `clauseInsertion` blocks are excluded by design (canonical clause text is
   SHA-verified separately, never scanned for residue).

8. **Deferred block types.** `warnCallout`, `radioQuestion`, `aggregationTable`,
   and `copyReadyMessage` are not rendered; `BlockRenderer` throws
   `NotSupportedException` on them (the Unsafe Block Exclusion Rule). Each lands
   with the artifact type or enforcement that makes it safe (warnCallout needs
   QA-SF-09; radioQuestion targets TCL; aggregationTable needs normalized
   findings/risks handling; copyReadyMessage targets DCN).

   **Resolved for `radioQuestion` (2026-07-14).** `RadioQuestionBlock` renders
   the U+25CB radio pattern, `RadioQuestionLayer` (QA-SF-10, already catalogued
   in `config/qa-checklist.json`) blocks a malformed question in both draft and
   promotion, and TCL (its target artifact type) now renders end to end. Three
   block types remain deferred and still fail closed: `warnCallout`,
   `aggregationTable`, `copyReadyMessage`.

   **Resolved for `warnCallout` (2026-07-15).** `WarnCalloutBlock` renders it
   as a config-styled warn-red callout box, `QA-ENG-06` (`BlockTypeSupportLayer`)
   accepts it, and `QA-ENG-07` (`TokenResidueLayer`) scans its text. `QA-SF-09`
   (`WarnCalloutLayer`) is unchanged and continues to gate it out of
   client-facing output at promotion. Two block types remain deferred and
   still fail closed: `aggregationTable`, `copyReadyMessage`.

   **Resolved for `aggregationTable` (2026-07-17, SCL slice).** `AggregationTableBlock`
   renders the clause library as a config-sourced table (the block carries only an
   `aggregationSource`; the renderer projects `clause-library.json` at render time, so
   it cannot diverge from config). `QA-ENG-06` accepts it and enforces source-validity
   (`clauseLibrary` only live; `placeholderDictionary` reserved for the PDX slice).
   `QA-ENG-07` exempts it (config-sourced, legitimately carries token slots). One block
   type remains deferred: `copyReadyMessage`.

   **Resolved for `copyReadyMessage` (2026-07-17). ITEM 8 NOW FULLY CLOSED.**
   `CopyReadyMessageBlock` renders it as a config-styled neutral callout box,
   `QA-ENG-06` accepts it, and `QA-ENG-07` SCANS its text (unlike `aggregationTable`:
   copyReadyMessage carries operator-facing authored text copied verbatim to the
   client, so an unresolved token must fail closed). The DCN's Suggested Delivery
   Message adopts it. All ten AGR block types now render; the Unsafe Block Exclusion
   Rule excludes nothing.

9. **QA-SF-11 overlap (citation refinement, no code change).**
   `config/qa-checklist.json` catalogues QA-SF-11 as the post-render clause-verbatim
   check (rendered clause text matches the canonical SHA-256). Slice 7's
   render-time clause SHA verification ships under `ClauseResolver` with checkId
   **QA-ENG-04** (the clause-reference family). The two describe the same
   governance intent from different layers; a future pass may reconcile the
   citation (cite QA-SF-11 for the render-time proof, or record the relationship
   via `alsoSatisfies`). Behavior is correct today; this is provenance tidiness.

10. **Universal closing elements not rendered.** "Distribution and Follow-up" and
    the "Prepared-by and companion-artifact closing line" appear only as prose
    labels in `artifact-types.json` `universalStructure`; neither has defined
    content/structure in config. Rule 6 (do not invent structure) forbids
    fabricating them. Deferred pending ORG-MST-SPEC-001 Section 4.1 content and a
    documentation-owner ruling.

11. ~~**Clause audit sink.**~~ RESOLVED 2026-07-15. `ArtifactRenderer` returns
    `RenderResult.ClauseAudits` (per-clause id, expected/actual SHA, verified,
    tokens-resolved). `generation-log.json` now persists them: it is emitted
    by `BuildCommand` whenever rendering was attempted, conforming to
    `schemas/generation-log.schema.json`. `artifact-manifest.json` remains
    deferred.

12. ~~**ClauseReferenceLayer null-`clauseId` severity is too lenient.**~~ RESOLVED
    2026-07-15. A `clauseInsertion` block with a null `clauseId` is now a blocking
    QA-ENG-04 failure in `ClauseReferenceLayer`, caught at validation rather than
    relying on `ClauseResolver` to fail closed at render. This was the only
    warn-severity check that survived promotion (`PlaceholderLayer` is blocking at
    promotion), so promoting it made "pass-with-warnings at promotion" unreachable:
    the `agr-sample-ds-promotion-warn` fixture and its dedicated
    `ValidationServiceTests` case were retired, and pass-with-warnings coverage moved
    to the new draft fixture `agr-sample-ds-draft-warn` (warned by an unknown
    placeholder token, which is `ModeGated` to a warning in draft).

13. **Single-paragraph clause assumption.** `ClauseResolver`/`BlockRenderer`
    render a clause as one body paragraph. Current library clauses are
    single-paragraph; revisit if a clause ever carries a hard break.

14. **Footer not post-render scanned by QA-SF-08.** The QA-SF-08 scan collects
    `ScanUnit`s from chrome (header/DCH/callout) and body sections, but the footer
    lives in a separate `FooterPart` outside the body scan-unit collection. Its
    only dynamic content is `renderingOptions.packageName`, which QA-ENG-05
    already input-lints; the rest is the hand-vetted footer template. The
    catalogued QA-SF-08 rule says "any text run," so a future pass could extend
    the scan to the footer part for completeness. Low exposure.

15. **No end-to-end test exercises a post-render check withholding the DOCX.**
    `PostRenderScan` (QA-SF-08) and `PostRenderMerge` are unit-tested, and
    `BuildCommand` wires them, but no fixture drives the full build path to a
    post-render blocking failure. There is no clean way to fixture one: prohibited
    characters in Governor text are caught pre-render by QA-ENG-05, and clauses are
    SHA-verified clean config, so QA-SF-08 has no natural trigger, and
    `BuildCommand.Run` takes a file path with no seam to inject a synthetic
    `RenderResult`. The fail-closed path is verified by composition and code
    inspection; a dedicated seam or a deliberately corrupt-clause test fixture
    would close the gap.

16. **QA-ENG-06 supported-set test does not self-verify against the renderer.**
    `BlockTypeSupportLayerTests.QA_ENG_06_passes` exercises only `paragraph` and
    `clauseInsertion` (the block types present in the DS fixture), so the layer's
    correctness rests on its supported-set matching `BlockRenderer.Render`'s
    dispatch cases - a match confirmed by inspection today, but not asserted by a
    test. A literal-block-list test (one case per supported type, plus a drift
    guard comparing the two sets) would make the invariant self-verifying rather
    than re-derived on each review. Fold into the Slice 8 content-lint work.

    **Resolved by Slice 8.** `BlockRendererTests.QA_ENG_06_supported_set_matches_renderer_capability`
    asserts `BlockTypeSupportLayer.SupportedBlockTypes` (now exposed as a public
    `IReadOnlySet<string>`) is exactly `{paragraph, h2, h3, bulletList, table,
    clauseInsertion}`, renders one sample block per supported type without a
    `NotSupportedException`, and asserts each deferred type (`warnCallout`,
    `radioQuestion`, `aggregationTable`, `copyReadyMessage`) is both absent from
    the set and rejected by the renderer.

## From Slice 9 (v2 Acceptance Closeout, 2026-07-07)

Slice 9 closed the v2 acceptance closeout: DS/AUP promotion build tests, the
failure-path coverage matrix (`docs/failure-path-coverage.md`), the
`LifecycleTransitionError` fixture, and the `reconciledAgainstPdxXlsx`
release-gate documentation (plus its unknown-token test) all shipped. Two
items remain intentionally unwired for v2, not oversights:

- The four reserved-unreachable error categories (`RenderingError`,
  `PromotionPolicyError`, `DependencyResolutionError`,
  `SharePointIntegrationError`) have no emitter in v2's DS/AUP scope; they are
  documented in the failure-path coverage matrix as reserved for future
  slices, not fixtured.
- `reconciledAgainstPdxXlsx` in `config/placeholder-dictionary.json` stays
  `false` and inert; no code reads it yet. Flipping it and wiring its
  behavior is post-v2 config/infrastructure work (see the Post-v2 backlog in
  `docs/ROADMAP.md`).

  **Resolved 2026-07-20 (PDX slice).** The flag is retired rather than wired.
  The `ORG-Placeholder-Dictionary-v1.2.xlsx` it was meant to reconcile against
  was never among the program source documents and could not be located, so
  there was nothing to reconcile against. The repo owner declared
  `config/placeholder-dictionary.json` the authoritative placeholder source
  on 2026-07-20 (its 19-token canonical set was verified complete by a
  repository sweep; see
  `docs/superpowers/plans/2026-07-20-pdx-placeholder-dictionary.md`), and
  `reconciledAgainstPdxXlsx` and its provenance-only note were removed from
  the config. The runtime release gate is unchanged: `PlaceholderLayer`
  (QA-SPEC-07) mode-gating still warns on unknown or unresolved tokens in
  Draft and fails on Promotion, independent of the removed flag.

## From QA checklist reconciliation (2026-07-11)

24. **Table header text color is hard-coded, not config-driven.**
    `src/Registrar.Rendering/Blocks/TableBlock.cs:60` sets the body-table header
    run color to the string literal `"#FFFFFF"` (bold white on the navy
    `headingNavy` fill). Every other color in that render path is read from
    `config/style-rules.json`, so per engine rule 3 (style rules live in config)
    the white header text should come from a `visualIdentity` key (e.g.
    `tableHeaderText`) rather than a literal. Cosmetic/purity only - the
    white-on-navy result is correct and satisfies master Section 7.9 Domain G;
    surfaced during the QA-checklist reconciliation
    (`docs/qa-checklist-reconciliation.md` Section 5).

## Pre-existing (not introduced by Slice 6; present since Slice 5)

4. **Eight CS8602 nullable-dereference warnings in test code.** In
   `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs` (lines 33, 47, 57,
   73, 79, 100) and `tests/Registrar.Tests/BuildCommandTests.cs` (lines 43,
   111). The build is otherwise clean (0 errors). Worth a cleanup pass so test
   output stays pristine; a warning is easy to lose a real one behind.

   **Resolved (PR #17, 2026-07-08).** `dotnet build` reports 0 warnings, 0 errors.

5. **Build-command test temp directories are never cleaned up.**
   `BuildCommandTests` uses `Directory.CreateTempSubdirectory()` and leaves the
   directories behind. Test-run litter, not a correctness issue.

## From the operator-menu feature (interactive CLI + launcher)

Deferred, non-blocking follow-ups from the operator-menu code reviews and the
whole-branch review. The final review confirmed none of these block merge.

17. **AgrPicker display sort is culture-sensitive.**
    `src/Registrar.Cli/AgrPicker.cs` sorts folder files with `OrderBy(p => p)`
    (default culture-sensitive comparer), inconsistent with the
    `OrdinalIgnoreCase` dedup used in the same method. It affects only display
    order (dedup is correct), but is a latent locale/Turkish-I trap. One-word
    fix: `OrderBy(p => p, StringComparer.OrdinalIgnoreCase)`.

18. **Thin test coverage on presenter/clipboard branches.**
    `GovernorClipboardText` has no test for the `(document)` jsonPath fallback,
    the mode-in-header, the expected/actual line, or warn-exclusion;
    `MenuCommand` has no test for the post-run open-document / open-report
    paths (only copy-failures and the shell-failure path are asserted). All
    correct by inspection; add fast-follow tests to pin them.

19. **ReportPresenter cosmetics.** The expected/actual line prints a blank
    field when only one of expected/actual is set, and only the per-check
    header line is colored (sub-lines are neutral). Both are readable as-is;
    tidy if a polish pass happens.

20. **Passing-build output path not printed.** The design says the presenter
    reports the DOCX path (and payload path in promotion) on a passing build,
    but `ReportPresenter` only consumes a `ValidationReport`, which carries no
    output path. The operator can still open the document via the post-run
    "d" action, so this is a cosmetic spec gap, not a functional one.

21. **OperatorShell.OpenFile does not dispose the returned Process.** A small
    handle leak per open; negligible for an occasional-use tool. Add a `using`
    or discard.

22. **Menu output-dir derivation vs CommandSupport.** `MenuCommand`'s
    `ReadReport`/`FindDocx` use `Path.GetDirectoryName(agrPath)` while
    `CommandSupport.Validate` writes to `Path.GetDirectoryName(Path.GetFullPath(agrPath))`.
    These diverge only for a relative/bare `agrPath`; `AgrPicker` yields
    absolute paths in practice, so low risk. Normalize with `GetFullPath` to
    remove the latent mismatch.

23. **SettingsStore.Save on a bare-filename path.** `Directory.CreateDirectory(Path.GetDirectoryName(_path)!)`
    would throw `ArgumentException` (outside the IO catch filter) if `_path`
    had no directory component. Unreachable via shipped entry points
    (`DefaultPath()` and the menu always pass dir-having paths), but the guard
    is cheap: `var d = Path.GetDirectoryName(_path); if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);`
    plus adding `ArgumentException` to the filter.

## From radioQuestion / TCL (2026-07-14)

Deferred work and known limitations from the slice that rendered `radioQuestion`
and unblocked TCL end to end. None of these block a release.

25. ~~**The rendered DOCX is not byte-deterministic across runs.**~~ RESOLVED
    2026-07-14. The same AGR and config now produce byte-identical DOCX files, so
    `artifactSha256` actually proves the artifact and is compared exactly in the
    golden harness (the `FixtureTestRunner.Normalize` exclusion is gone, which
    means a regression here now breaks the goldens rather than hiding in them).

    The original entry named two causes and missed one. Actual causes were three:
    (a) the Open XML SDK mints a **random relationship ID** for every part added
    without an explicit one, and those IDs are written into `document.xml` and the
    `.rels` parts; (b) `PackageProperties` emits core properties as
    `package/services/metadata/core-properties/{fresh-guid}.psmdcp`; (c)
    `System.IO.Packaging` stamps every zip entry's `LastWriteTime` with the wall
    clock and exposes no way to set it.

    Fixed by naming every relationship (`RelationshipIds`), writing a real
    `docProps/core.xml` via `CoreFilePropertiesPart`, and repacking the archive
    with a fixed zip epoch (`DeterministicPackage`). `ArtifactRenderer` no longer
    takes an `IClock` at all: the document is a pure function of (AGR + config),
    and generation time lives in `validation-report.json` `timestampUtc`, where
    run metadata belongs. The DOCX therefore carries no created/modified date by
    design.

    Two things fell out of this worth knowing. The `.psmdcp` form that
    `PackageProperties` wrote is **not** the part Word or SharePoint read as
    document properties, so the Title and Creator the engine had been setting were
    invisible to every consumer; they now land in `docProps/core.xml`, along with
    `cp:contentStatus` (the approval status). And the old entry's claim that this
    was "container metadata, not content" was too generous: a hash that changes
    every run cannot serve as proof of an artifact, which is the job
    `artifactSha256` exists to do.

    Follow-up (not done): the Document Control Header fields beyond approval status
    (classification, owner, review date) are still not exposed as document or custom
    properties, so a SharePoint library cannot drive columns from them. Adding them
    as custom properties is a separate decision.

26. **`style-rules.json` `radioOptionPattern.descriptorLine` (prose) and
    `prohibitedRenderings` remain documentary** and are read by no code. The
    prohibited-renderings rule (no checkboxes, plain bullets, dash lists,
    numbered lists) is instead guaranteed by construction: `RadioQuestionBlock`
    emits no numbering properties at all, and a unit test asserts it.

27. **The radio question's `prompt` is carried in the render scan text but is
    not rendered as its own paragraph.** TCL fixtures introduce each question
    with a preceding `paragraph` block. If the documentation owner wants the
    prompt rendered by the block itself, that is a follow-up.

28. **The eval harness bounds coverage in ways worth stating.** `scripts/eval-governor.ps1`
    scores four prompts times three runs per model, which is enough to separate a habit
    from a fluke but is not a benchmark. It measures one deployment (Copilot Studio, with
    the attached knowledge) doing one job, and the results do not generalize past that.
    The `.refused` marker for the trap prompt is a convention, not a validated format; a
    per-model `refusals.txt` manifest would be equivalent. Model slugs are operator-chosen
    strings and nothing enforces their spelling, so comparing one round to a later one
    requires the slugs to match by hand.

29. ~~**Placeholder coverage rollup (Governor-requested, real gap).**~~ RESOLVED
    2026-07-14. `PlaceholderLayer` (QA-SPEC-07) now emits an informational coverage check
    on every run (result `pass`, severity `info`): required / provided / missing /
    beyond-the-required-set counts, naming the tokens for missing and beyond. It rides in
    the existing `checks[]` array with no schema change, and is inert to `overallResult`
    and the failing/warning counts. It reports "beyond the required set" rather than
    "unused" because the layer runs pre-render and cannot prove a provided token is
    unreferenced. Original entry: `PlaceholderLayer` reported per offending token only
    (drift alias, unknown key, unresolved null, missing required token) with no coverage
    summary. Raised by the Governor as REG-TD-007 during 2026-07-14 smoke testing.

30. **Cross-run validation history (Governor-requested, real gap, needs a decision
    first).** The engine keeps no state between runs: `ValidationService.Validate` is a
    pure function of (AGR + schema + registry) and reads nothing from prior runs. Two
    Governor requests depend on adding a history store: repeat-failure detection (flag the
    same `checkId` + `jsonPath` + `actual` recurring N times, REG-TD-005) and a validation
    dataset export across runs (CSV/JSON/Excel, REG-TD-010). Both presuppose the same
    persistence surface as the deferred `generation-log.json` (item 11). Before building
    either, the owner should decide whether validation stays stateless by design, the way
    rendering is now an explicit pure function (item 25). Not a bug; an architecture
    decision.

31. **Governor-proposed changes DECLINED by the constitution (recorded so they are not
    re-raised as bugs).** The Governor cannot read the engine's code, only its validation
    reports, so its 2026-07-14 smoke-test register proposed several changes that each
    violate a named CLAUDE.md rule. They are declined by design, not deferred:
    - REG-TD-001 (add a normalization phase that strips U+200B / U+200C / U+200D / U+FEFF
      before validating) and REG-TD-004 (an `autoRepairSafeCharacters` option that
      sanitizes smart quotes / NBSP / zero-width / BOM and continues). Both are the quiet
      correction rule 1 (fail closed, no partial output) and the zero-trust-of-Governor
      rule exist to prevent: the engine would ship a mutated field instead of forcing a
      Governor regeneration loop. `CharacterPolicy` already rejects these codepoints by
      design.
    - REG-TD-009 (engine computes the review date instead of validating the Governor's).
      `ReviewDateLayer` already knows the per-category interval and validates against it.
      Authoring the date crosses from enforcement into judgment, which CLAUDE.md assigns
      to the Governor.
    Four other register items (REG-TD-002 severity/layer/phase, REG-TD-003 summary,
    REG-TD-006 field-level repair text, REG-TD-008 rule traceability) describe fields the
    report already carries; they were the Governor reading a subset of the report rather
    than the whole check. Addressed by `docs/governor/correction.md` (generated from
    config) and `docs/governor-instructions.md`, not by engine work.

## From SCL reference document / aggregationTable (2026-07-17)

32. **The SCL aggregation displays each clause's recorded SHA-256 without recomputing it
    at render time.** `AggregationTableBlock` projects `ClauseEntry.CanonicalTextSha256`
    (the hash stored in `clause-library.json`) into the SHA-256 column verbatim. If the
    config ever held a text/hash mismatch, the Shared Clause Library reference document
    would faithfully display the inconsistent pair, undercutting the document whose whole
    purpose is to be the trustworthy hash reference. This is not a governance hole: the
    clause-insertion path enforces recorded-vs-actual integrity via `ClauseResolver` /
    QA-SF-11, so a mismatched clause fails closed when actually inserted. But recomputing
    `Sha256Hex(CanonicalText)` in the aggregation block (and either asserting equality or
    displaying the computed value) is cheap and would make the reference self-certifying.
    Flagged by the Slice 2 whole-branch review; left deferred to keep the block minimal.

33. **The PDX (Placeholder Dictionary) reference document is not yet generated.** Slice 2
    shipped SCL only. `placeholderDictionary` is a recognized `aggregationSource` enum value
    in `agr.schema.json` but is not a live render source (QA-ENG-06 fails it closed), PDX's
    `generationTarget` stays `false`, and no PDX section schema is authored. The PDX slice is
    blocked on reconciling `config/placeholder-dictionary.json` against the canonical
    `ORG-Placeholder-Dictionary-v1.2.xlsx` (`reconciledAgainstPdxXlsx: false` today); a PDX
    view generated now would faithfully render a known-incomplete subset. Once reconciled, the
    PDX slice reuses the aggregationTable mechanism, adds `placeholderDictionary` as a live
    source, authors the PDX section schema, and flips PDX `generationTarget`.

    **Resolved 2026-07-20 (PDX slice).** The blocking premise did not hold: the
    `ORG-Placeholder-Dictionary-v1.2.xlsx` this item was waiting on was never among the
    program source documents and could not be located in the repository or the ORG workspace.
    A repository-wide token sweep (recorded in
    `docs/superpowers/plans/2026-07-20-pdx-placeholder-dictionary.md`) confirmed the 19-token
    canonical set in `config/placeholder-dictionary.json` is complete, and the repo owner
    ruled that file the authoritative placeholder source rather than continuing to wait on a
    reconciliation that has no source document to reconcile against. `placeholderDictionary`
    is now generable via the existing aggregationTable mechanism as a live source; PDX is no
    longer blocked on this item.

34. **`ClauseEntry.Name` is optional with a silent empty-string default.** A clause missing
    `name` in `clause-library.json` renders a blank Name cell in the SCL aggregation rather
    than failing closed. This matches the pre-existing style of its sibling properties
    (`CanonicalText`, `CanonicalTextSha256`) and current config populates `name` on all ten
    clauses, so it is not an active gap. If the SCL Name column is later deemed load-bearing,
    a config-load assertion that clause names are non-empty would restore the fail-closed
    posture.

## From layer mapping / QA-ENG-08 (2026-08-01)

35. **PIG has no end-to-end fixture exercising QA-ENG-08.** PIG's `Administration`
    layer is covered by `ConfigLoaderTests` (`Generable_types_declare_their_framework_layer`
    and the new `Types_with_a_defined_section_schema_declare_a_layer`), but no fixture
    drives a PIG AGR through `LayerAgreementLayer` or the full validation pipeline, so the
    row is proven only at the config layer, never through the pipeline. DS is the only
    artifact type with a dedicated layer-mismatch fixture
    (`agr-sample-ds-layer-mismatch`). Adding a PIG fixture (matching and/or mismatched
    layer) would close this gap.

## From the core authoring foundation (2026-08-02, GUI-3)

36. **Reopening a draft pins all eight overrides, so later edits leave derived values
    stale.** `AgrAuthoring.FromDocument` records every derived field that departs from
    what config would produce, which is correct for a faithful round trip. The
    consequence is that changing the client or the version afterwards does not
    recompute `outputFileName` or `packageName`, because both are now pinned. The
    editor screen must clear the relevant override when the field it derives from is
    edited. This is a GUI-7 requirement, not an engine defect, and it is recorded here
    because nothing in the code says it.
37. **There is no `AgrAuthoring.NewDraft` factory.** The contract that a newly created
    artifact receives a derived review date lives only in a comment. Deferred
    deliberately: GUI-6 is the consumer that should shape that API, and adding it
    speculatively before there is a caller is the YAGNI trap. Whoever builds the
    Engagement screen should add it and move the contract into code.
38. **A draft carrying two sections with the same title silently drops the second.**
    `AgrAuthoring` rebuilds sections by matching ordinal config titles, taking the first
    occurrence deterministically. Unreachable through intended paths, since the GUI
    generates sections from config and `FromDocument` reads them from an already valid
    AGR, and it cannot crash. It is recorded because it is the same silent-content-loss
    class as the round-trip defects found during GUI-3 review, and worth one guard
    whenever someone is next in that code.
39. **The path-based `ValidationService.Validate` ignores the `schemaPath` it is
    handed.** Reimplementing it over the `EngineContext` overload means it re-derives
    the repo root and calls `EngineContext.Load`, which re-locates the schema through
    `ConfigLocator`. Not reachable today: every call site obtains `schemaPath` from
    `ConfigLocator` in the first place, so the reconstruction resolves to the identical
    file. A future caller passing an arbitrary `schemaPath` would get a
    `FileNotFoundException` instead of reading the file it named. Either honor the
    parameter or remove it.
40. **Two unused `using` directives remain in `ValidationService.cs`.** Left in place
    deliberately during the `EngineContext` extraction, whose whole review was about
    proving a mechanical move had zero side effects. Safe to remove.
41. **`EngineContext.Load` reads files.** A sanctioned exception to the pipeline's
    no-IO constraint rather than a defect: the `EngineContext` record itself is pure,
    and a GUI can construct one directly without touching the filesystem. Recorded so
    the exception is visible rather than discovered.

## From GovernorBrief and ImportParser (2026-08-03, GUI-4)

42. **`ImportParser.Bind` recovers no elements from a list one bad element spoiled.**
    List deserialization in `System.Text.Json` is atomic, so a single section block
    missing a required property discards every section in the paste. Final review made
    that honest rather than silent: the loss now carries an `Unreadable` finding at the
    member's own pointer, naming the property the deserializer refused. The better fix
    is per-element tolerant binding, so the readable blocks survive and only the bad one
    is dropped. It was deliberately not attempted at the merge gate because it is a
    larger change with its own risks and is not needed for the result to be honest.
43. **A key offense and a value offense at the same pointer are distinguished only by
    their message.** The character scan now covers object keys as well as values, and a
    key offense carries the pointer the value would use, because that pointer already
    names the key. An Import screen grouping findings by `JsonPath` will therefore see
    two entries on one path. Recorded so GUI-8 renders the message rather than assuming
    a pointer is unique.

    The re-review noted this branch answered the same ambiguity question the other way
    one commit earlier: `Unreadable` was made a distinct `ImportFindingKind` rather than
    reusing `Malformed`, precisely so two findings on one result stay machine-separable.
    A key and a value offense share both `JsonPath` and `Kind`, so `(JsonPath, Kind)`,
    the natural grouping key for a validation rail, cannot tell them apart. RFC 6901
    cannot point at a member's name, so there is no clean pointer fix; a
    `ProhibitedCharacterInKey` kind or a small `Target` discriminator on `ImportFinding`
    would resolve it. Parked at the merge gate rather than fixed, because the branch had
    already had its one fix wave and the consequence is confined to how GUI-8 groups a
    list. Decide it when the Import screen is built, not before.
44. **A key offense says "resend this value without it".** `ImportParser.Describe`
    composes its remediation sentence around the word "value", which was accurate while
    the scan covered only values. Now that it covers keys too, that sentence is wrong for
    a property name. One string, parameterized alongside its subject.
45. **A key offense can say "Repair is available." for a repair that will be refused.**
    Availability is decided per code point from `transportRepairs`, but `Normalize`
    refuses the whole document when two repaired keys would collide. So an operator can
    be told repair is available for a key, invoke it, and get the paste back unchanged.
    The spec's promise that an operator is never told repair will clear a problem it will
    not clear holds for repair-mapping availability, which is what that sentence is
    about; the collision gate is a separate, later refusal. Detecting it at scan time
    needs cross-key analysis the scan does not do today. Recorded rather than fixed so
    the gap is visible instead of resurfacing as a surprise at the button press.
46. **`Bind` is atomic for dictionaries too, and entry 42 names only lists.**
    `placeholderMap` and `optionalSectionToggles` bind through
    `Bind<Dictionary<...>>`, so one bad value discards the whole map exactly as one bad
    element discards a whole list. Any per-element tolerance work under entry 42 must
    cover both shapes.
47. **`Every_committed_fixture_imports_with_no_character_findings` filters to
    `ProhibitedCharacter`,** so it would stay green if the fixture corpus began emitting
    the new `Unreadable` kind. The re-review confirmed out of band that no committed
    fixture emits one today, by checking all twelve `Bind` targets against the schema's
    required sets, but the suite itself does not carry that guard. Widening the filter
    would.
## From design system generation (2026-08-03, GUI-5)

48. **A new color added to `visualIdentity.colors` reaches the generated stylesheet
    unmeasured.** `Every_screen_color_is_at_least_visible_on_white` walks only
    `Screen.Colors`, but `TokenProjection` exports both blocks, so the block that
    auto-propagates into the stylesheet is the unchecked one. Note that the naive fix
    does not work: extending the blanket test over `Style.Colors` fails today, and
    correctly, on five document colors that measure below the 3:1 blanket on white:
    `borderNeutral` at 1.82:1, `headerRightCellFill` at 1.17:1, `warnRedFill` at
    1.13:1, `dchFill` at 1.09:1, and `tableRowAltFill` at 1.05:1. Those are document
    colors chosen for print and they are supposed to be there. The shape that
    composes is a test enumerating every exported token and requiring each to be
    classified as screen-safe foreground, background-only, or document-only, failing on
    an unclassified one. A new color then has to be classified before it can ship, and
    the same classification would let the generator annotate the CSS per token instead of
    only per group.
49. **LIVE DEFECT, not a deferred polish item (unlike its neighbors in this list): the
    provenance SHA is reproducible only on a CRLF checkout.**
    `config/style-rules.json` has no EOL attribute in `.gitattributes` and the repo runs
    `core.autocrlf=true`, so the hash pinned in the generated stylesheet, the knowledge
    documents, and the fixture goldens is the hash of the CRLF working copy. The LF blob
    in the index hashes differently. This predates this branch and already affects 38
    fixture goldens and 4 generated knowledge documents; the stylesheet makes 43 pinned
    files in total. CI is green only because `windows-latest` defaults to
    `autocrlf=true`. A contributor cloning with `core.autocrlf=false`, which is the
    Linux and macOS default, gets an unmodified tree that fails roughly four xUnit
    tests: the two golden-comparison tests in `TestCommandTests` (covering the 38
    fixture goldens), the single looping `[Fact]` in `GeneratedKnowledgeIsCurrentTests`
    (covering the 4 knowledge documents), and one test in
    `GeneratedTokensAreCurrentTests` (covering the stylesheet). The real fix is a
    repo-wide `config/*.json text eol=lf` plus one re-pin of every affected file, which
    is an owner decision about repo-wide normalization rather than something a feature
    unit should take.
    **Resolved 2026-10-09:** `config/*.json` and `schemas/*.json` are now pinned to LF in
    `.gitattributes`, so every checkout (Windows with autocrlf, Linux, macOS, or a tool that
    writes LF) hashes the same bytes, and every pinned hash (38 goldens, 4 knowledge
    documents, the stylesheet) was re-pinned once to the LF value. Hashing is unchanged: it
    is still over raw bytes. A file edited by hand on a deployed machine and saved with
    CRLF will hash differently from the shipped one, which is the intended behavior for an
    audit hash.
50. **`--out` means a directory for `emit-knowledge` and a file for `emit-tokens`.**
    `EmitKnowledgeCommand` writes several files so its `--out` is a target directory;
    `EmitTokensCommand` writes one file so its `--out` is a target path. The option name
    is the same and the CLI help text is the only thing that distinguishes them. Passing
    a path that does not yet exist to `emit-tokens` does not error: it creates a file
    with that name and exits 0, indistinguishable from passing an ordinary filename.
    Passing an *existing* directory does error today, by accident rather than by
    design: `File.WriteAllText` throws `UnauthorizedAccessException`, which the command
    catches and reports as a generic error on exit code 2, not a message that names the
    real problem. The proposed fix of rejecting an existing directory is therefore
    largely already in place; what remains is replacing the accidental
    `UnauthorizedAccessException` with a purpose-built check and a message that says the
    path is a directory. Renaming one of the two `--out` options so they stop sharing a
    name that means two different things is still worth doing on its own.
51. **`TokenProjection` constrains `visualIdentity.colors` key naming and nothing in
    config says so.** `TokenName` requires lowerCamelCase and rejects anything else, so
    a `$comment` key added inside either colors object makes `emit-tokens` throw. Adding
    a `$comment` is idiomatic elsewhere in this repo, including in the `screen` block
    immediately beside these, so the trap is a live one. Either skip `$`-prefixed keys in
    `Collect` or document the constraint in the config file itself.
52. **"The renderer never reads the screen block" is documented but not enforced.** The
    claim lives in the `$comment` on `screen` and in the design spec, and it is currently
    true, but nothing fails if a future renderer change reads `config.Screen`.
    `SourceHygieneTests` is the precedent for a source-scanning invariant in this repo,
    and the same shape here would be a test asserting that no file under
    `src/Registrar.Rendering` references `Screen`.
53. **`Contrast.Parse` accepts strings it should reject, in two different ways.**
    `Component` uses `NumberStyles.HexNumber`, which permits leading and trailing
    whitespace, so `"#FF 000"` parses as a color rather than throwing. Separately, the
    length check is the only validation the method owns: a six-character non-hex string
    reaches `int.Parse` and surfaces a `FormatException` rather than the
    `ArgumentException` the method raises for every other malformed input. Both are the
    same defect, that the parser trusts `int.Parse` to be the validator, and both should
    be fixed together with a single explicit hex-digit check. Not currently reachable
    with bad data, since every caller passes a config value that `style-rules.json`
    already constrains.
54. **The byte-compare guards use `File.ReadAllText`, which silently strips a UTF-8
    BOM.** A committed copy carrying a BOM would compare equal to a BOM-less fresh
    render, so the guard would pass while the bytes on disk differ from what the
    generator produces. This is exactly the class of drift the guards exist to catch, and
    it applies both to `GeneratedTokensAreCurrentTests` and to the pre-existing
    `GeneratedKnowledgeIsCurrentTests`. `File.ReadAllBytes` compared against the UTF-8
    encoding of the render would close it for both.
55. **The repo-root resolution snippet is now duplicated a fourth and fifth time.**
    `Directory.GetParent(Path.GetDirectoryName(ConfigLocator.FindSchema(...))!)!.FullName`
    appears verbatim in `EmitKnowledgeCommand`, `EmitTokensCommand`, and
    `SemanticTestSupport`, and in a near-identical form in `CommandSupport`,
    `DoctorCommand`, `TestCommand`, and `ValidationService`. One `ConfigLocator.RepoRoot`
    helper would replace all of them. It was not added here because touching
    `ConfigLocator` is out of this unit's scope and the change should be made once,
    across all call sites, rather than in a feature branch.
56. **No white or surface token is emitted, although every contrast assertion measures
    against white.** The stylesheet carries foreground and fill colors but no page
    background, so GUI-6 has nothing to write for the surface the whole palette was
    measured against. The next unit will hardcode `#FFFFFF`, which would be the first
    hand-maintained color in the application and the first crack in the "one config, one
    palette" property this unit exists to establish. Adding a surface color to config is
    a palette decision for the owner, not a generator change.
57. **The token-shape guard over-rejects a trailing single capital.** The
    `LowerCamelCase` regex requires at least one lowercase or digit character after every
    capital, which is what forbids consecutive capitals, but it also refuses a key ending
    in a single capital. A future key like `shadowOffsetX` derives cleanly to
    `--org-shadow-offset-x` and is rejected anyway. No current config key hits this, and
    the fail-closed direction is the safe one, so it is recorded rather than loosened:
    the fix is to allow a final capital only in the last position, which is a small regex
    change plus the test that proves it still rejects `brandURLColor`.
58. **`Each_group_is_sorted_and_the_render_is_stable` counts declarations by line prefix,
    so a comment line can be mistaken for one.** The test identifies a custom-property
    declaration by trimming a line and checking whether it starts with `--org-`. That is
    fine for the declarations themselves, but the group comments legitimately name tokens
    in prose, and a wrapped comment line beginning with a token name is counted as a
    declaration. This is not hypothetical: it fired during the record correction on
    2026-08-03, when the document-group comment listed the four text-validated tokens and
    the list wrapped. The workaround was to keep those four names on one long unwrapped
    line, which is why that line is formatted the way it is. The real fix is to identify
    a declaration by something a comment cannot imitate, for example requiring the line to
    end in a semicolon as well, or tracking whether the parser is inside a comment.
    Until then, anyone editing the group comments must avoid starting a line with
    `--org-`, and nothing says so at the site.
59. **The document-group comment says colors were chosen for print twice.** The correction
    on 2026-08-03 added a sentence about not assuming a document color is screen-safe,
    directly after an existing sentence making the same point. The guidance is accurate,
    just redundant. Worth a trim whenever `TokenProjection`'s comment text is next edited;
    it costs a regeneration, so it is not worth a commit of its own.
60. **A transient single-test failure was observed once and never reproduced, and the
    prime suspect is the only test that mutates process-wide state.**
    `A_bare_filename_writes_to_the_current_directory` in
    `tests/Registrar.Tests/Cli/EmitTokensCommandTests.cs` sets `Environment.CurrentDirectory`
    to a temporary directory for its duration and restores it in a `finally`. xUnit runs
    test classes in parallel by default, so that mutation is visible to every other class
    for the length of the test. It was accepted during review on the grounds that no other
    test in the assembly reads the current directory, which is true of the test code but
    says nothing about what the runtime or the SDK does underneath it.
    On 2026-08-03 a full-suite run reported 480 passed and 1 failed. Five consecutive runs
    immediately afterwards were clean at 481, and the failing test name was not captured,
    so the cause is unproven and this entry is a lead rather than a diagnosis. Recording it
    because a flaky suite that nobody wrote down becomes a suite people learn to re-run.
    The durable fix is to stop mutating process state: assert the path resolution that
    `EmitTokensCommand` performs rather than exercising it through a real current-directory
    change, or serialize the test against every other class rather than against none.
    **DIAGNOSED 2026-08-04.** It recurred during GUI-6, and this time the test name was
    captured: it is `A_bare_filename_writes_to_the_current_directory`, the suspect named
    above. The mechanism is now clear from the code. The test assigns
    `Environment.CurrentDirectory`, which is process-wide, then in its `finally` restores
    it and immediately calls `Directory.Delete(tempDir, recursive: true)`. xUnit runs test
    classes in parallel, so for the duration of this test every other running test that
    creates a file by a relative path creates it inside `tempDir`. The recursive delete
    then races those writes and throws `IOException`. Two independent observations plus
    the code agree, so this is no longer a lead.
    The durable fix is still to stop mutating process state: have the test assert the path
    resolution `EmitTokensCommand` performs rather than exercising it through a real
    current-directory change. Deleting the temp directory less eagerly would hide the
    symptom while leaving the interference, so it is not the fix.
    This lives in already-merged GUI-5 code and is deliberately not fixed inside GUI-6,
    whose plan does not touch it.
## From the app shell and Engagement screen (2026-08-04, GUI-6)

Numbering note: this entry was committed as a second `55` while the list already ran
to `60`. It is renumbered here so the list has one entry per number again; nothing
else about it changed except the addition of the final paragraph.

61. **The shared-payload publish is safe by coincidence of build order, not by
    construction.** `scripts/publish.ps1` publishes `Registrar.Cli` and then
    `Registrar.App` into the same `runtime/` folder so one copy of `config/`,
    `schemas/` and `assets/` serves both. The Task 6 review established that the second
    publish genuinely overwrites the first's copies of the roughly 494 shared runtime
    files, `hostfxr.dll` and `coreclr.dll` among them; the arrangement works only
    because both projects resolve the same win-x64 runtime pack from the same installed
    SDK in the same script run, so the overwritten bytes are identical. Nothing
    structurally prevents a version skew between the two projects from making the last
    writer win with different bytes, and the failing symptom would be one binary loading
    a runtime it was not built against, which is hard to attribute.
    One part of it IS structurally sound and should not be disturbed: the CLI publishes
    single-file, so its `deps.json` and `runtimeconfig.json` are embedded in the
    executable rather than written loose, and the app's are distinctly named. That is
    what rules out the more dangerous collision.
    If the two projects ever target different runtime versions, publish them into
    separate subfolders and give each its own walk-up path to the shared data, rather
    than relying on the bytes matching.
    The final review added that the guard is nearly free and does not need the folder
    split to be decided first. `publish.ps1` can hash one shared runtime file, for
    example `hostfxr.dll`, immediately after the first publish and again after the
    second, and throw when the two differ. That is about three lines, it turns "safe by
    coincidence of build order" into "safe by construction", and it fails at publish time
    with an obvious message rather than at run time with a binary loading a runtime it
    was not built against.

62. **A corrected AGR reports Built before it has ever been built.** The expected output
    name is derived from artifact type, client and version only, so two AGRs for the same
    artifact derive the same DOCX name. Save `ds-corrected.json` beside an already-built
    `ds.json` and the corrected one finds the existing DOCX and reports `Built`; both rows
    read `Built`. This over-claims, which is the unsafe direction: the screen tells the
    operator a correction has been rendered when the DOCX on disk is still the old one.
    Deriving the name rather than pattern-matching the DOCX is deliberate and should
    stay, since it keeps the screen and the renderer using one rule. The fix is a design
    choice for the owner between correlating on Document ID, comparing the AGR's and the
    DOCX's timestamps, or detecting that two AGRs claim the same output name and flagging
    the collision rather than resolving it. Nothing here is a code-level default.

63. **`EngagementScan` discards `ImportParser`'s findings and so reproduces the exact lie
    `ImportParser` exists to prevent.** The scan calls `ImportParser.Parse(text, config)`
    and keeps only `.Draft`, dropping `.Findings`. Entry 42 records that `Bind` is atomic
    for lists: one malformed block makes deserialization of the whole `sections` list
    fail, and every section is discarded. `ImportParser` emits a finding saying precisely
    that, which is the whole reason it returns findings rather than a draft alone. The
    scan drops it, so the row reads something like "7 field(s) still to fill. First:
    Purpose and Scope has no content yet" for a document whose sections were supplied and
    then thrown away. The direction is fail-safe, since it under-claims rather than
    over-claims, but the Detail line instructs the operator to redraft a body that
    already exists. Surfacing findings belongs with the Import screen, which is where a
    finding has somewhere to be shown and acted on; a state between `Draft` and
    `Unreadable`, or a Detail line that says content was recovered incompletely, is the
    shape to consider there.

64. **`Home.razor` reloads the whole `ConfigBundle` and reparses `agr.schema.json` on
    every Open click.** `EngineContext` exists precisely so config is loaded once and
    reused. The screen instead walks up for the repo root, calls `ConfigLoader.Load`, and
    reparses the schema from disk each time the button is pressed. It is correct and it
    is wasteful, and it compounds with entry 65: the reload happens synchronously on the
    UI thread before the scan even starts.
    Obsolete (2026-10-08, SW-2): the code this item describes was removed when the authoring screens were retired.

65. **The folder box does not respond to Enter, and the scan is synchronous with no busy
    indicator.** Typing a path and pressing Enter does nothing; the operator has to reach
    for the Open button, which is not what a single-field form leads them to expect. The
    scan then runs on the UI thread: `EngagementScan.Run` reads every candidate file in
    the folder, and on a Files-On-Demand OneDrive or SharePoint folder, which is the
    deployment target, each read hydrates the file from the cloud. The window freezes for
    the duration with no progress, no spinner and no disabled button, and a technician
    with no feedback clicks again. The fix is an `@onkeydown` handler on the input plus
    moving the scan off the UI thread behind a busy state; both belong with GUI-7, which
    is the unit that makes this screen something an operator lives in.
    Obsolete (2026-10-08, SW-2): the code this item describes was removed when the authoring screens were retired.

66. **A `.agrdraft` can reach `Ready` with "Complete and ready to build." while nothing in
    this release can build one.** `Ready` means `AgrAuthoring.ToDocument` converted the
    draft with zero gaps, which is true for a `.agrdraft` as much as for a `.json` AGR.
    The CLI's build path takes a file and runs it through `AgrParser.Parse`, which expects
    the AGR document shape, not the draft shape, so pointing `registrar build` at a
    `.agrdraft` fails. The Detail line therefore promises something the shipped payload
    cannot deliver. It is unreachable today because nothing in the repo writes a
    `.agrdraft`: GUI-6 reads the format and never emits it. The editor makes it reachable
    the moment it saves one, so the Import screen or the editor must close it, either by
    teaching the build path to accept a draft or by saying, in the Detail line, that a
    draft has to be exported before it can be built.

67. **Two guards are unreachable from their only caller, and both become reachable at the
    same moment.** `DraftFile.Read` does not catch `ArgumentException`, which
    `File.ReadAllText` throws for a path containing a NUL character, and
    `WebViewEnvironment.UserDataFolder` accepts a relative root and quietly produces a
    relative user-data path. Neither is reachable today: the scan only ever hands
    `DraftFile.Read` a path that `Directory.EnumerateFiles` produced, and the only
    production caller of `UserDataFolder` passes `Environment.SpecialFolder
    .LocalApplicationData`, which is always absolute. Both stop being theoretical as soon
    as a path comes from somewhere else, which for both means the first screen that lets a
    technician type or pick one. Fix them together then, rather than hardening one caller
    at a time.
68. **`EngagementScan.Run` catches only `DirectoryNotFoundException`, so an ACL-denied
    folder escapes it, and the class's own comment says that condition is ordinary.**
    The guard added for the vanished-mid-scan race catches `DirectoryNotFoundException`
    from the lazy `Directory.EnumerateFiles`. A folder whose ACL denies list access
    throws `UnauthorizedAccessException` instead, which does not derive from
    `IOException` and is not caught, on a folder `Directory.Exists` had just reported as
    present. Fifteen lines below, `TryRead`'s own comment calls that exception "an
    ordinary condition in a synced SharePoint folder, the deployment target" and catches
    it for exactly that reason, so the file contradicts itself.
    The consequence today is degraded but visible rather than silent: `Home.razor`
    catches it and shows a red error line, though the state sentence beneath then reads
    "Choose an engagement folder" after the operator has chosen one (see entry 70).
    It is not untestable, which is the reason it was left. `EngagementScanTests` already
    manipulates ACLs deterministically for the file-level version of this property.
    Denying `ListDirectory` on the directory rather than `ReadData` on a file gives
    `Directory.Exists == true` with `EnumerateFiles` throwing, using the pattern already
    in that file. The test would fail today.
    Note (2026-10-08, SW-2): the screens named here were removed in SW-2; the Core defect in `EngagementScan.Run` still applies.
69. **`App.xaml.cs` calls `WebViewEnvironment.UserDataFolder` unguarded during startup,
    so a broken profile crashes the application rather than declining to open it.**
    `UserDataFolder` throws `ArgumentException` when
    `Environment.GetFolderPath(LocalApplicationData)` returns empty. `OnStartup` calls it
    with no guard, so the exception propagates out of application startup. The doctor
    check for the same condition reports Warn and says "the app window will not open",
    which is generous: the app does not fail to open a window, it terminates. Predates
    the fix wave that introduced the Warn wording. Catching it in `OnStartup` and showing
    a diagnostic, in the spirit of the design's "show a Doctor-style diagnostic, not a
    stack trace", is the fix.
70. **Three of the four folder-state classes have no styling, and the error path
    contradicts itself.** `Engagement.razor` emits `prompt`, `missing`, `empty` and
    `summary`; only `missing` has a CSS rule, so the other three are dead hooks
    distinguished by their sentence alone, which is sufficient for the finding they were
    added for but not for a styled screen. Separately, when the scan throws, `Home.razor`
    resets the state to `NoFolderChosen`, so the screen shows "Choose an engagement
    folder to see what this client still owes" beneath a red error line, after the
    operator has chosen one. The error line carries the truth and this is better than the
    stale rows it replaced, but the state sentence contradicts the visible input box.
    Obsolete (2026-10-08, SW-2): the code this item describes was removed when the authoring screens were retired.
71. **No page background is declared, so every contrast figure the screen relies on is
    measured against a canvas nobody set.** `app.css` sets `color` on `body` and no
    `background`, which was the correct refusal: the four fills the token file exports
    are element fills with declared document meanings, and promoting one to page canvas
    would invent a meaning config never declared. White is therefore inherited from the
    user agent while `screen.$comment` in `style-rules.json` measures every ratio against
    it. A zero-color mitigation exists and was not taken: `:root { color-scheme: light; }`
    pins both the user-agent canvas and the native form-control colors without a hex
    literal or an invented token. Worth adding before the editor screen introduces more
    inputs.
    Note (2026-10-08, SW-2): the editor screen was removed in SW-2; the advice still applies, now to SW-3's inputs.

## From the Editor screen (2026-08-04, GUI-7)

Recorded here on 2026-08-05. These five were parked in PR #61's description and never
reached this file, which is the exact failure mode this document exists to prevent: a
PR body is the scratch review ledger, not the durable record. A sixth parked item from
that PR, the add-button labels reading as engine identifiers, was fixed in PR #62 and
is not carried forward. Each of the five below was re-verified against `main` before
being written down; none is a correctness defect in the shipped path.

**Items 72 through 76 are all RESOLVED (2026-08-26).** They were taken together as one
change rather than folded into a later slice, because none is a correctness defect in the
shipped path and none needed a slice of its own. Each kept its number.

72. ~~**`DraftCreation` runs a config-supplied `Regex` with no match timeout.**~~
    RESOLVED 2026-08-26. `Regex.IsMatch` takes a 250 ms match timeout, and
    `RegexMatchTimeoutException` becomes a refusal that names `agr.schema.json` as the
    cause rather than the operator's Document ID, since the pattern is not something they
    can fix from that screen. Two tests cover it; neither asserts elapsed time, because a
    wall-clock assertion here would be the same defect as item 76.

    Original: **`DraftCreation` runs a config-supplied `Regex` with no match timeout.**
    `src/Registrar.Core/Authoring/DraftCreation.cs:52` calls `Regex.IsMatch(documentId,
    pattern)` where `pattern` comes from `config/artifact-types.json`. Config is a
    versioned trusted input, so this is not an injection surface, but rule 3 makes
    config editable without a rebuild, and a catastrophically backtracking pattern
    edited in by hand would hang the app with no diagnostic. A `TimeSpan` overload
    turns that into an exception someone can read.

73. ~~**`ValidationRail` sets its probe flag before awaiting, so a stale `unreachable`
    set can survive one render.**~~ RESOLVED 2026-08-26. The probed signature and the set
    it produced are now assigned together as one `settled` pair, only once the probe
    returns, and the markup reads that pair rather than the loop guard. A separate
    `inFlight` signature keeps the probe/`StateHasChanged` loop shut, which is the property
    the old ordering existed to preserve. Covered by a test that leaves the second probe
    pending and asserts the previous set's answer is not applied to the items on screen.

    Original: **`ValidationRail` sets its probe flag before awaiting, so a stale
    `unreachable` set can survive one render.** `probed = signature` is assigned before the JS
    interop probe is awaited, which is what stops the probe/`StateHasChanged` loop, but
    it also means a render arriving during the await sees the new signature against the
    old set. The visible consequence is bounded: one render in which a rail item is
    drawn as a button that does not navigate, or as text when it would have. It
    self-corrects on the next probe.

74. ~~**The duplicate-section gap points at a non-navigable pointer.**~~ RESOLVED
    2026-08-26. The gap is addressed at `FieldPath.Section(i)` for the duplicated title's
    own position in the built document, which is where the surviving instance lands and so
    where the merge has to happen. The stray-title gap above it deliberately keeps the
    collection pointer: a stray title is not one of the type's sections and has no element
    to point at. Both cases are now tested.

    Original: **The duplicate-section gap points at a non-navigable pointer.**
    `AgrAuthoring.cs:162` reports the gap at `/dataInputs/sections`, the collection
    rather than an indexed element, so `FieldPath` cannot resolve it to an element and
    the rail cannot focus the field the finding is about. The finding itself is right
    and worth having; only its address is unusable.

75. ~~**The New artifact review panel spells the source vocabulary in markup.**~~
    RESOLVED 2026-08-26. `AgrAuthoring.DefaultSources` owns the provenance, declared
    directly above the `Defaults` method whose body each entry describes, and the panel
    renders it. A drift guard fails the build if the Source column ever spells one of the
    three origins itself; it was verified to fire against the pre-fix markup rather than
    merely passing against the new one.

    Original: **The New artifact review panel spells the source vocabulary in markup.**
    `NewArtifact.razor:81-88` hardcodes `config`, `schema default` and `derived` in the
    Source column. The values beside them are read from Core, as the file's own header
    comment promises; the provenance labels are not. If a default's origin ever changes,
    the screen will confidently state the old one.
    Note (2026-10-08, SW-2): the drift guard described here was removed with the New artifact panel.

76. ~~**One editor test asserts across the autosave debounce window.**~~ RESOLVED
    2026-08-26. `Editor.DebounceMs` is a parameter defaulting to the shipped 400 ms, and
    the test holds the window open instead of racing it. The application supplies no value,
    so the operator's window is unchanged. Fixed by making the debounce injectable, as the
    item prescribed, rather than by lengthening a wait.

    Original: **One editor test asserts across the autosave debounce window.**
    `EditorShellComponentTests.cs:172-187` deliberately does not wait for the 400 ms
    debounce, because the window is what the test is about. That makes it a timing
    assertion on a wall clock, and a future flake candidate on a loaded CI machine. It
    is not flaky today. Fixing it means making the debounce injectable rather than
    lengthening a wait, which would only move the threshold.

## From SW-1 (2026-10-08)

77. **A promotion build can leave a stale `promotion-payload.json` beside a FAIL report.**
    Found by reading, not executed. In a promotion build with a registry, the pre-render
    pass writes `promotion-payload.json`. If post-render validation then fails,
    `PostRenderMerge` nulls the payload (`src/Registrar.Validation` `PostRenderMerge.cs`,
    around line 21), so the merged write writes only the report. The registry-update
    payload from the earlier pass stays on disk beside a FAIL report, and `Written` lists
    it. This contradicts the CLAUDE.md output-package rule that the payload is emitted
    only when validation passes in promotion mode, and no test covers the path. Fix
    direction: `OutputPackage` should delete the payload file when the merged payload is
    null. Needs owner sign-off because it changes CLI output, which the SW-1 ruling froze.

## From SW-2 (2026-10-08)

78. **The Core authoring code is now dead code, with tests, awaiting its own deletion
    PR.** SW-2 retired every screen that used it. Still present under
    `src/Registrar.Core`: `AgrAuthoring`, `DraftEdits`, `EditorForm`, `EditorRail`,
    `DraftCreation`, `FieldPath`, `EngagementScan`, the `.agrdraft` `DraftFile` format,
    `GovernorBrief` and `ImportParser`. Their tests remain in `tests/Registrar.Tests`:
    `Authoring/` (AgrAuthoringTests, DefaultSourceProvenanceTests, DraftCreationTests,
    DraftEditsTests, EditorFormTests, EditorRailTests, FieldPathTests), `Engagement/`
    (DraftFileTests, EngagementScanTests), `Governor/GovernorBriefTests.cs` and `Import/`
    (ImportParserTests, ImportRoundTripTests, ImportMergeTests, JsonCandidatesTests).
    Verified by grep that none of these names is referenced by `Registrar.App`,
    `Registrar.Cli` or `Registrar.Pipeline` code; the one hit is a comment in
    `src/Registrar.Pipeline/OutputPackage.cs` that mentions `DraftCreation.FileName`.
    Deleting them is a separate PR with its own review, because some Core types may be
    shared with surviving code (check `SemanticTestSupport.cs` and the comment above before
    cutting). `fixtures/ORG-GOV-AUP-004.agrdraft`, and any other `.agrdraft` fixture, go
    with the format.
