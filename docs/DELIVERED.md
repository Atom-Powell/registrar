# The Registrar - Delivered

Append-only record of shipped work. Nothing here is a plan. Remaining work lives in
`docs/ROADMAP.md`; deferred non-blocking follow-ups live in `docs/tech-debt.md`.

This file exists because delivered history and remaining work were previously mixed
into the same sections of `ROADMAP.md` and the same list in `CLAUDE.md`, which meant
neither could function as a plan of record. Separating them is what lets the roadmap
be short enough to hold still.

**Slice numbering note.** Slices 1 through 9 below are the v2 build and are the only
numbered slices in this repo. Work after v2 acceptance is named, not numbered. The
operator GUI spec uses a separate `GUI-n` labelling that is unrelated to these.

---

## Delivered

| # | Slice | Date | Status | What shipped |
|---|-------|------|--------|--------------|
| 1 | Walking skeleton | 2026-07-03 | Merged | `registrar validate`; AGR JSON Schema validation; config/schema loading; validation-report contract; CI. |
| 2 | Semantic validation | 2026-07-04 | Merged | Config-driven semantic checks over the AGR (vocabulary, metadata, section schema, placeholder, clause reference, review date) folded into the report. |
| 3 | Registry + lifecycle | 2026-07-04 | Merged | ID Registry made authoritative (CSV/xlsx adapter); registry agreement checks; lifecycle state machine enforcing legal approval-status transitions (rule 5). |
| 4 | Promotion payload | 2026-07-04 | Merged | `promotion-payload.json` on a passing promotion-mode validate; QA-SPEC-18 assisted registry-update check. |
| 5 | Renderer foundation | 2026-07-05 | Merged | DOCX renderer and `registrar build`; universal chrome (header table + logo, Document Control Header, Configuration callout, footer); render-on-pass. |
| 6 | Input validation | 2026-07-05 | Merged | QA-ENG-05 input-hygiene layer: prohibited-character lint of Governor free text and placeholder values before render, fail closed in both modes. |
| 7 | Body-section rendering | 2026-07-05 | Merged (PR #5) | Prose-subset block renderers (paragraph, h2, h3, bulletList, table, clauseInsertion); section assembly; clause insertion by reference + SHA-256; post-render QA-SF-08 scan; QA-ENG-06 block-type support. DS and AUP render end-to-end. |
| 8 | Content lint | 2026-07-07 | Merged | HeadingStructureLayer (QA-SF-07 no-empty-h1 / no-heading-level-skips), WarnCalloutLayer (QA-SF-09 client-facing warn-callout promotion gate), TokenResidueLayer (QA-ENG-07 body-prose token residue), and a QA-ENG-06 supported-set drift guard. |
| 9 | v2 acceptance closeout | 2026-07-07 | Merged | DS/AUP promotion build tests, failure-path coverage matrix (docs/failure-path-coverage.md), LifecycleTransitionError fixture, and provenance-only documentation of the reconciledAgainstPdxXlsx release gate. Meets the adopted v2 Definition of Done. |

## Delivered after v2 acceptance

Work merged since the v2 finish line was met. Not slices against the v2 contract;
recorded here so the board reflects `main`.

| Slice | Date | Status | What shipped |
|-------|------|--------|--------------|
| Rendering polish (PR #16) | 2026-07-08 | Merged | Direction B typography: shared paragraph/run formatting helpers, vertical rhythm, distinct h2/h3 sizes with keep-with-next, section title with blue number and left accent bar, horizontal-rule tables, borderless Document Control Header with micro-labels, end-to-end logo-size guard. All new keys config-bound. |
| Test-warning cleanup (PR #17) | 2026-07-08 | Merged | Silenced the eight CS8602 nullable-dereference warnings in renderer and build-command tests. Build output is clean. |
| Takeover safety (PR #18, #19) | 2026-07-11 | Merged | Section schemas authored for DCN, TAK, and RAF (the last undefined generation targets), QA-SF-09 internal-only-note detection, the master Section 7.9 QA-checklist reconciliation, and promotion-mode tests for all three types. |
| radioQuestion rendering / TCL end-to-end | 2026-07-14 | Merged | `RadioQuestionBlock` renders the U+25CB radio pattern; QA-SF-10 (`RadioQuestionLayer`) blocks a malformed question in both draft and promotion; `radioOptionPattern` bound into `ConfigBundle.RadioPattern` (was dead config); TCL fixtures and registry entries added, TCL now renders end to end in draft and promotion; `artifactSha256` is now computed and stamped in the validation report on a produced document, which required reordering `BuildCommand` so DOCX eligibility is decided before the report is written. `radioQuestion` is the first of the four deferred block types to land; three remain (`warnCallout`, `aggregationTable`, `copyReadyMessage`). |
| Governor eval + payload harness | 2026-07-15 | Merged (PRs #30-#37) | Eval test cases, answer rubric, and Governor eval assets; the `travel-pack` bundle/payload builder and payload-generation eval; Governor payload-serialization fix and type-name grounding. Scores Studio model candidates via the CLI without ever calling a model. |
| Output-package completion | 2026-07-15 | Merged (PRs #38-#40) | `generation-log.json` emission (closes the `RenderResult.ClauseAudits` sink), `artifact-manifest.json` (metadata, section inventory, dependency map, artifact SHA-256), and validation hardening (`EngineInfo.SchemaVersion` guard, strict `reviewDateOverride` calendar parse, `ClauseReferenceLayer` null-`clauseId` now blocking). The v2 output package is now the full five files. |
| warnCallout rendering | 2026-07-15 | Merged (PR #41) | `WarnCalloutBlock` renders a config-styled warn-red callout; QA-ENG-06 accepts it and QA-ENG-07 scans its text; QA-SF-09 still gates it out of client-facing output at promotion. Second of the four deferred block types. |
| Governor publish checklist | 2026-07-16 | Merged (PR #42) | Publish gate and self-install steps for the Studio Governor. |
| SCL reference doc / aggregationTable | 2026-07-17 | Merged (PR #44) | `AggregationTableBlock` renders the clause library as a config-sourced table (projected from `clause-library.json` at render time); QA-ENG-06 enforces source-validity (`clauseLibrary` live, `placeholderDictionary` reserved), QA-ENG-07 exempts it. Makes the Shared Clause Library generable as a reference document. Third of the four deferred block types. |
| copyReadyMessage rendering | 2026-07-17 | Merged (PR #45) | `CopyReadyMessageBlock` renders a config-styled neutral callout; QA-ENG-06 accepts it and QA-ENG-07 SCANS its text (operator-facing verbatim text must fail closed on an unresolved token); DCN's Suggested Delivery Message adopts it. Last of the four deferred block types: the Unsafe Block Exclusion Rule now excludes nothing. |
| Docs: known gaps, Steward, operations guide | 2026-07-17..18 | Merged (PRs #46-#48) | CLAUDE.md known-gaps reconciliation; the Steward intake-agent knowledge set and instructions; the program operations guide. |
| PDX (Placeholder Dictionary) reference document | 2026-07-20 | Merged (PR #49) | `placeholderDictionary` is now a live `aggregationTable` source (QA-ENG-06 accepts it); the PDX section schema is authored and its `generationTarget` flips to true, so PDX renders end to end in draft and promotion. The `reconciledAgainstPdxXlsx` gate is retired rather than wired: the canonical xlsx it named was never among the program source documents, so the repo owner declared `config/placeholder-dictionary.json` the authoritative placeholder source after a repository-wide token sweep verified its 19-token canonical set complete. |

## v2 status

The v2 acceptance closeout is complete. Slice 9 closed the remaining gaps
identified after Slice 8 (AUP promotion coverage, failure-path coverage, and
release-gate documentation), and the bounded v2 finish line defined by
`docs/v2-acceptance-roadmap.md` and `docs/v2-coding-agent-directive.md` is met.
Further work is the post-v2 backlog below.


## Delivered from the post-v2 backlog

Recorded here rather than in the roadmap, because a plan of record should not carry
completed rows.

- **Output-package completion. Complete 2026-07-15.** `artifact-manifest.json` and
  `generation-log.json` both ship. The output package is the full five files.
- **Remaining block types. Complete 2026-07-17.** All ten AGR block types render.
  The Unsafe Block Exclusion Rule excludes nothing and `BlockRenderer`'s default
  branch is a guard for unknown types.
- **Remaining generation targets. Complete for authored targets.** Section schemas
  exist for DS, AUP, UQR, TCL, TAK, RAF, DCN, PIG, SCL, and PDX. Types still marked
  `not-defined-in-spec` continue to refuse generation under rule 6.
- **Placeholder dictionary reconciliation. Retired 2026-07-20.** The canonical PDX
  xlsx could not be located; the owner declared `config/placeholder-dictionary.json`
  canonical and the `reconciledAgainstPdxXlsx` flag was removed rather than wired.

## Delivered 2026-08-01

- **Client-name scrub (PR #50).** Two real ORG client names and a real M365 tenant
  domain replaced across 53 files. Goldens regenerated; the U+2019 fixture that
  exercises QA-ENG-05 kept its curly apostrophe.
- **QA-ENG-08, framework layer validation (PR #54, GUI-1).** `layer` is declared per
  artifact type in `config/artifact-types.json` and enforced by `LayerAgreementLayer`,
  blocking in both modes. It was the one required identity field the engine accepted
  on trust. (Opened as PR #52, which a stacked squash merge auto-closed; recreated
  and merged as #54.)

## Delivered 2026-08-02

- **Governor knowledge generated from config (PR #55, GUI-2).** Four projections in
  `Registrar.Core.Governor` derive the knowledge each Governor agent role needs
  straight from config: `IntakeProjection`, `DrafterProjection`, `PayloadProjection`,
  and `CorrectionProjection`. Their output is committed as `docs/governor/*.md` and
  guarded by `GeneratedKnowledgeIsCurrentTests`, so config and the Governor's
  knowledge cannot drift apart silently. `governorScope` now marks the eight artifact
  types the Governor authors, distinguishing them from the ten the engine can render.
  `.gitattributes` pins the generated files to LF; without it `core.autocrlf` breaks
  the byte-compare guard on a fresh Windows clone.
- **Core authoring foundation (PR #56, GUI-3).** The engine can now be driven by
  something other than a finished AGR file, which is what every later GUI unit rests
  on. `AgrDraft` plus `AgrAuthoring` (`Defaults`, `ToDocument`, `FromDocument`) model
  work in progress and convert it to a valid AGR, reporting unfilled fields as
  jsonPath-addressed gaps rather than as validation failures. `AgrSerializer` writes
  an AGR back out, which schema validation needs and which nothing previously did.
  `BuildPipeline` lands in a new `Registrar.Pipeline` project, above Validation and
  Rendering, taking loaded inputs in and returning in-memory results with no file
  access or exit codes; `BuildCommand` becomes a thin wrapper over it. `EngineContext`
  and a `ValidationService` overload let config and schema load once. `IIdRegistry`
  gains a read-only `All()` so a technician can pick from the IDs Registry Owner
  has already issued instead of typing one. Rule 5 is untouched: enumerating the
  registry is not minting.
- **A `build` on an unparseable AGR now writes a validation report (PR #56).** Found
  during GUI-3 review. `CommandSupport.WriteOutputs` runs before `AgrParser.Parse`,
  so a document that fails schema validation still produces the report the operator
  and the Governor need in order to fix it. Previously it produced nothing, which
  defeated rule 7 exactly when the report mattered most.

## Delivered 2026-08-03

- **GovernorBrief and ImportParser (PR #58, GUI-4).** Both directions of the Governor
  handoff. `GovernorBrief` is extracted from `DrafterProjection` rather than derived a
  second time, so the clipboard brief and the generated agent knowledge are one code
  path. `ImportParser` recovers an AGR from a chat paste, populates `AgrDraft`
  tolerantly so a partial reply still opens, and scans the whole payload for prohibited
  characters. That scan closes a real gap: QA-ENG-05 covers context fields and
  placeholder values but not section body text, which was caught only after rendering
  by QA-SF-08.
- **Design system generated from config (PR #59, GUI-5).** `registrar emit-tokens`
  derives CSS custom properties from `config/style-rules.json`, so the operator GUI and
  the DOCX deliverables cannot drift onto different palettes. A `screen` block adds the
  two colors that are correct in print and fail WCAG on a monitor: `danger #A3282B`,
  a new brand value approved because warn red is reserved by QA-SF-09, and
  `border #7E8B9C`. `Contrast` measures WCAG ratios in the engine and a test asserts
  every screen token meets the threshold for its role.

## Delivered 2026-08-04

- **`Registrar.App` shell and Engagement screen (PR #60, GUI-6).** A WPF host with a
  Blazor surface, and the first screen: point it at an engagement folder and see what
  is in it. The registry export is optional and its absence is stated on screen rather
  than hidden, mirroring the Skipped check `RegistryLayer` already emits.
- **Editor screen (PR #61, GUI-7; fixes in PR #62).** Section titles arrive locked from
  config, clause text is chosen by name and inserted by reference, and validation runs
  as the operator types. `FieldPath` makes the pointer dialect a single owner, so a
  check's `jsonPath` and an authoring gap's resolve to the same element and the rail can
  focus the field a finding is about.

  Two things this unit established are worth more than the screen. First, a green suite
  proved nothing about the WPF host: every bUnit fixture was pre-normalized, so a draft
  that was never normalized on open passed twelve task reviews and was caught only by
  running the app. Second, `SaveError="saveError"` shipped to main as a literal string,
  because Razor treats a quoted attribute on a string parameter as a literal rather than
  an expression, and every draft therefore showed a permanent save failure while saving
  worked. Both were found by launching the application, not by testing it.
- **Import screen and Build wiring (PR #63, GUI-8; follow-ups in PR #66).** The Governor
  handoff in both directions and the button the whole GUI exists for. The import shows a
  report before anything reaches the draft, so a reply meant for a different artifact
  costs the operator nothing. Build freshness is decided by comparing the serialized
  document and registry text, not by a timer. `OutputPackage.DirectoryNameFor` is
  containment-gated because its caller deletes the folder recursively before writing.

  The CLI was held intact throughout as the fallback, on the owner's instruction, and a
  characterization suite for the CLI's output file set was written before the extraction
  rather than after it.
- **Copilot Studio setup checklist (PR #65).** `docs/copilot-studio-setup.md`, the
  configuration procedure GOV-1 follows to stand the Governor back up on the
  generated per-role knowledge. Written ahead of GOV-1 rather than during it, because
  the unit is gated on tenant access rather than on this repo and the procedure does
  not need that access to be written.
- **First-artifact walkthrough (PR #68, DOC-1).** `docs/first-artifact-walkthrough.md`
  takes a technician who has never opened the Registrar from an empty folder to a
  promotion build. Written against the screens as they actually are, with the Governor
  step marked as contract-verified rather than observed, since that agent is not yet
  stood up. This closes the operator-facing half of the roadmap's endpoint; the
  remaining half is GOV-1.

## Delivered 2026-08-05

- **Test suite temp-directory cleanup (PR #70, closes issue #69).** The suite created
  temp directories and never removed them: 18,450 attributable directories and 10,591
  rendered DOCX files, 55 MB, accumulated in three days on one developer machine. All
  32 `CreateTempSubdirectory` call sites across 17 files now route through a shared
  `TempDir` helper that registers each directory and deletes it when the run ends.
  Two details are load-bearing rather than incidental: every directory carries a
  `registrar-test-` prefix, because the default 8.3 name is indistinguishable from any
  other application's scratch space and was why the historical residue could only be
  identified by sniffing file contents; and cleanup does not depend on the
  `ProcessExit` handler, which does not run if the test host is killed, so `TempDir`
  also sweeps abandoned directories older than an hour on first use.
  `scripts/clean-test-temp.ps1` clears the pre-existing residue and defaults to a dry
  run. The claim verified was not a green suite but a count: zero `registrar-test-*`
  directories remain after a full run.

  The correct pattern already existed in a few of the newer GUI tests. It had simply
  never been applied anywhere else, which is how an inconsistent convention survives
  until someone counts.

## Delivered 2026-08-26

- **GUI-7 follow-ups, tech-debt items 72 through 76.** The five review notes parked in
  PR #61 and recorded in `docs/tech-debt.md` on 2026-08-05, taken together rather than
  folded into a later slice. None was a correctness defect in the shipped path, which is
  why they had waited; each is now closed with a test that would have caught it.

  `DraftCreation` gives the config-supplied Document ID pattern a 250 ms match timeout, so
  a hand-edited backtracking pattern becomes a refusal naming `agr.schema.json` instead of
  a hung window (72). `ValidationRail` assigns the probed signature and the set it produced
  together, only once the probe returns, with a separate in-flight signature keeping the
  probe loop shut, so a render arriving mid-probe can no longer pair the current item set
  with the previous answer (73). The duplicate-section gap is addressed at the duplicated
  title's own section rather than the collection, so the rail can focus it; the stray-title
  gap deliberately keeps the collection pointer, having no element to point at (74). The
  New artifact panel's Source column renders `AgrAuthoring.DefaultSources` instead of
  spelling `config`, `schema default` and `derived` in markup, with a drift guard that was
  verified to fire against the pre-fix markup rather than merely pass against the new (75).
  `Editor.DebounceMs` became a parameter so the one test asserting inside the autosave
  window holds it open instead of racing a wall clock (76).

  Suite 784 passing in Release, 0 warnings.

## Delivered 2026-10-08

The authoring GUI (GUI-6 to GUI-8) was judged too complex and replaced by a thin
one-page window over the same engine. Design:
`docs/superpowers/specs/2026-10-08-simple-operator-window-design.md`.

- **SW-1, file-based run extracted into Pipeline (PR #78).** `AgrFileRun` in Pipeline
  now owns the validate-or-build run for an AGR file. `ValidateCommand` and
  `BuildCommand` became wrappers that replay its recorded output, and the helper types
  they shared moved with it. `CliOutputStreamTests` pin the CLI text, so the refactor is
  proven not to change what the CLI prints. Suite 704 + 87.
- **SW-2, authoring screens retired (PR #79).** The Engagement, Editor and Import
  screens, `DraftSettler`, `BuildRunner` and their tests were removed, about 4,000 lines.
  The shell was kept. The Core authoring code (GUI-3 and GUI-4) is left in place as dead
  code, recorded as tech-debt 78. Suite 710 + 3.
- **SW-3, the one-page operator window (PR #80).** One page: working folder, optional
  registry, the file to check, Check and Build, the result, and a health check. New
  pieces are `AgrMode`, `GuardedRun`, `AgrRunner`, `DoctorPresenter`,
  `OutputPackage.PackageFileNames`, an opt-in `excludeOutputs` on `AgrPicker` that hides
  the tool's own output files from the list, and the registry path in the shared
  settings. Drag and drop was deferred; **Add a file...** covers it. Suite 733 + 25.
- **SW-4, operator docs for the simple window (PR #81).** README, OPERATOR, CLAUDE.md,
  the first-artifact walkthrough and the operations guide describe the window as shipped,
  and the roadmap amendment records SW-1 to SW-4 and the open endpoint question.
  Documentation only, so the suite is unchanged.

  Found along the way and not fixed: a promotion-payload.json left over from an earlier
  run can remain after a post-render failure. It predates this work and is recorded as
  tech-debt 77.

## Delivered 2026-10-08, later

- **ORG branding of the operator window (PR #82).** The window was given the ORG look
  from a Claude Design handoff: the ORG logo beside the title, ghost and primary
  buttons, a solid red **Copy failures for the Governor** button, status banners with a
  distinct icon for pass, warning and fail, and the notice and error callouts. Colors
  stay driven by config: two screen tokens, `surface` and `dangerHover`, were added to
  `style-rules.json`, and `AppCssTests` fail the build if `app.css` ever contains a
  literal color or a token that does not exist. Three contrast pairs the engine did not
  measure are now pinned. No copy, order or behavior changed. Suite 736 + 30.

  Not done: the board's dimmed section and spinner while a run is in progress, a vector
  logo (the existing 52x54 raster is used), and bundling Montserrat (the window falls back
  to Arial, as the DOCX deliverables do).

  Found along the way and not fixed: the config hashes in the goldens, the generated
  tokens and the Governor knowledge files are taken over the raw bytes of each config
  file, so they depend on line endings. CI runs on Windows and sees the CRLF bytes; an
  edit by a tool that writes LF breaks CI until the hashes are regenerated from the CRLF
  file. Normalizing line endings when hashing would fix it for good, but it is an engine
  change.

## First roadmap closed 2026-10-09

The first roadmap (endpoint adopted 2026-08-01) was closed by the owner and replaced by a
second one in `docs/ROADMAP.md`. Delivered under it: the layer mapping and QA-ENG-08
(GUI-1), generated per-role Governor knowledge (GUI-2), the Core authoring foundation and
the brief and import code (GUI-3 and GUI-4, now unused), the design tokens (GUI-5), the
operator window (GUI-6 to GUI-8, replaced by SW-1 to SW-4 on 2026-10-08), and the
first-artifact walkthrough (DOC-1). Not delivered: GOV-1 and GOV-2, which carry over to
the second roadmap with their IDs, and the endpoint's wording about never seeing JSON and
validating as they type, which was reworded to match the window that shipped. The first
roadmap's text is in git history at commit `d4e046b`.

## Delivered 2026-10-09

- **Config hashes no longer depend on line endings (tech-debt 49).** The config and schema
  files are hashed over their raw bytes, and the hashes are pinned in the validation
  reports, the generated stylesheet and Governor knowledge files, and the fixture goldens.
  Raw bytes include line endings, so a Windows checkout, a Linux or macOS clone, and a tool
  that writes LF each produced a different hash, and CI passed only because its runner
  happened to match the machine that generated the pins. `config/*.json` and
  `schemas/*.json` are now pinned to LF in `.gitattributes`, so every checkout hashes the
  same bytes, and all 43 pinned files were re-pinned once (38 goldens, 4 knowledge
  documents, the stylesheet). Only hash lines changed; no engine code changed and hashing
  is still over raw bytes. Existing clones refresh the files with
  `git checkout -- config schemas` (or a fresh clone). Suite 736 + 30.
