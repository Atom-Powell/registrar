# Operator GUI: Design

Date: 2026-08-01
Status: Approved, not yet planned
Supersedes: a proposed console "New AGR wizard", rejected during this brainstorm

## Why this exists

The Registrar's operator interface is a console menu. The users are ORG technicians
who are averse to a command line, and the program is being designed for ease of use
and accurate repetition by non-specialists. The owner ruled on 2026-08-01 that the
console is a transitional shell and the operator interface moves to a GUI. That
ruling is recorded in `CLAUDE.md` under "Stack and environment".

This spec covers the GUI, the Core work it requires, and the Governor-side change it
implies.

## What was rejected, and why it matters

Two designs were worked through and discarded before this one. Both are recorded
because the reasoning constrains what follows.

**An Adaptive Card intake form in Copilot Studio.** It collected the fifteen identity
and context fields. Three problems killed it. It duplicated `artifact-types.json` as
hardcoded choice lists, and the first draft already carried two stale type names and
eleven artifact types whose `sectionSchema.status` is `not-defined-in-spec` and which
therefore refuse generation. Adaptive Cards cannot express cross-field validation, so
the ID-code-must-match-artifactType rule (QA-ENG-03), the per-category review interval
(QA-SF-05), and derived classification would all have needed a second implementation
in Copilot Studio topic logic, untested and unversioned. And it addressed only the
cheap fields; the expensive failures live in `dataInputs.sections`.

**A console "New AGR wizard".** Correct instinct, wrong surface, given the audience.

The surviving principle from both: **the interface must never reimplement a rule the
engine already owns.** The GUI runs the real validation layers in-process. That is the
capability neither rejected design could have.

## Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | The GUI is a full AGR editor. The technician never sees JSON. | JSON round-tripping through a chat window is the part of today's flow that is hostile to the audience. |
| 2 | Governor content arrives as one paste per artifact, parsed by the app. | JSON survives as transport but is neither authored nor read by a human. One exchange, not seven. |
| 3 | The engagement folder is the unit of work. | Matches the existing I/O model. No app-managed index, so no second source of truth. |
| 4 | Blazor Hybrid: a WPF host with a `BlazorWebView`. | Native .NET 8, references Core and Validation directly, publishes as one self-contained exe through the existing `scripts/publish.ps1`. Config-driven dynamic forms are natural in Razor and painful in XAML. |
| 5 | The design system is generated from `config/style-rules.json`. | The brand is already machine-readable there and already renders the DOCX. Generating the stylesheet means the app cannot drift from its own output. |
| 6 | The CLI is retained, not deprecated. | It is what CI runs and what `eval-governor.ps1` drives. It stops being the operator's tool, which is different from stopping being useful. |
| 7 | The Governor becomes a parent agent with one child per artifact type. | A single agent holding all eight types is where confabulation happens. A child that has only TCL's seven section titles cannot invent AUP's structure. |

### Platform verification

WebView2 Evergreen Runtime 150.0.4078.105 confirmed present on an ORG workstation, so
there is nothing to install and no admin rights are needed. Distribution is unchanged:
zip from GitHub Releases, extract, run.

Two implementation notes that follow from the platform choice:

- Blazor Hybrid on a WPF host is a NuGet package (`Microsoft.AspNetCore.Components.WebView.Wpf`).
  It is **not** MAUI and must not pull the MAUI workload.
- WebView2 defaults its user-data folder to a directory beside the exe. If the app is
  extracted somewhere read-only or run from a network path, it fails to start and the
  window comes up blank. Point it at `%LOCALAPPDATA%` before the view initializes, and
  add a WebView2 check to `registrar doctor`.

## Architecture

```
Registrar.Core          + AgrDraft, AgrAuthoring, GovernorBrief, ImportParser
                        + BuildPipeline (extracted from BuildCommand)
Registrar.Validation    + cached-config overload on ValidationService
Registrar.Rendering     unchanged
Registrar.Cli           thin wrapper over BuildPipeline; otherwise unchanged
Registrar.App           new. WPF host + BlazorWebView. References Core,
                        Validation, Rendering. Owns screens and state, nothing else.
Registrar.Tests         + tests for the new Core types and QA-ENG-08
```

`Registrar.App` enforces no rules of its own. Everything it displays comes from Core
or Validation.

### New Core types

All four are pure functions, testable with no UI.

- **`AgrDraft`** is the work-in-progress model. Every field nullable, no `minItems`.
  Deliberately not an `AgrDocument`: a half-filled artifact cannot satisfy the AGR
  schema, and pretending otherwise poisons everything downstream.
- **`AgrAuthoring`** converts `AgrDraft` to `AgrDocument` and back, applying config
  defaulting: classification, layer, `audienceDefault`, review date from the category
  interval, output filename, and section titles from `orderedSections`.
- **`GovernorBrief`** turns an `ArtifactTypeConfig` into the text a child agent
  receives. Generated from config, so it carries the correct section titles, required
  clause IDs with canonical names, and the radio-pattern instruction when
  `usesRadioOptionPattern` is set.
- **`ImportParser`** turns a child agent's reply into `sections[]` plus a structured
  result describing what matched, what is missing, and what could not be read. It
  returns a result object, never an exception.

### Two required refactors

**`ValidationService` needs a cached-config overload.** Today
`Validate(agrText, schemaPath, registryCsvText)` re-reads the schema from disk and
re-runs `ConfigLoader.Load` on every call. That is fine at one call per build and not
fine at one call per keystroke. The semantic layers themselves are pure and operate on
parsed objects, so only the entry point changes. A test must assert the overload
produces reports identical to the path-based call.

**`BuildCommand` orchestration moves to Core as `BuildPipeline`.** `BuildCommand.Run`
returns exit codes and writes to `TextWriter`, orchestrating through `CommandSupport`.
That is CLI shape; the app cannot use it without referencing `Registrar.Cli` and
parsing exit codes. `BuildPipeline` returns a result object and `BuildCommand` becomes
a thin wrapper. `BuildCommandTests` passing unchanged is the guard that the extraction
was faithful.

Both refactors are direct consequences of the `CLAUDE.md` rule that operator workflow
logic must not live where a GUI would have to reimplement it.

## Screens

**Engagement.** Open a folder, see every artifact and its state: Draft, Ready, or
Built. Answers "what does this client still owe."

**New artifact.** Four questions: type, Document ID, client, engagement. Everything
else derived. A review panel shows each resulting field tagged with its source (you,
config, registry, derived), so the technician can see they supplied four things and
the system supplied ten.

**Editor.** Three panes. Identity left, section content centre, live validation right.
Section titles locked and generated from config. Header carries the mode toggle,
Import from Governor, and Build, where Build is disabled while blocking issues exist
with the reason visible without hovering.

**Import.** A paste target, then a report of what lined up, before anything is written.

Three block editors carry most of the design weight: prose with character linting as
you type, clause insertion as a dropdown off `clause-library.json` with the canonical
text shown read-only, and a radioQuestion editor enforcing the four-option floor.

## Data flows

**Startup.** Load `ConfigBundle` and the AGR schema once for the process lifetime.
Load `CsvIdRegistry` if a path is set.

**Create.** Type selected, `AgrAuthoring.Defaults(typeConfig, clock)` produces derived
values, four inputs land on top, result written as an `AgrDraft`.

**Document ID.** `IIdRegistry` gains a read-only enumeration method so the wizard can
list IDs already issued for the chosen type with their status. This never mints:
rule 5 stands, and reading what Registry Owner issued is not minting. When no
registry is loaded, fall back to typing with pattern and type-agreement checks.

**Import.** `GovernorBrief.For(typeConfig)` to the clipboard, reply pasted back,
`ImportParser.Parse` returns sections plus a match report. Nothing is written until
accepted. Importing with known problems is allowed, because the editor keeps showing
them and Build stays blocked.

**Live validation.** On edit, debounced, `AgrAuthoring.ToDocument(draft)` either
produces an `AgrDocument` or reports which fields are still empty. If it converts, the
real layers run in-process and the returned `Check[]` populates the rail.

The rail is clickable because every check carries a `jsonPath`. Rather than
hand-maintaining a path-to-field map, which would drift, each input registers its own
`jsonPath` as the form is generated from config, and the rail looks up that registry.
This is the fiddliest part of the design and is named here so it does not get
improvised.

**Build.** Draft converts to an `AgrDocument`, is written as a real AGR, and
`BuildPipeline` runs. Outputs land in the folder as they do today.

## Draft file lifecycle

Drafts are `<slug>.agrdraft`, not `<slug>.agr-draft.json`. `AgrPicker` globs `*.json`,
so anything ending in `.json` appears in the operator menu as a selectable AGR. A
distinct extension keeps drafts invisible to it with no change to `AgrPicker`.

**This inverts a model documented in `CLAUDE.md`.** Today the AGR JSON is the input a
human saves and the CLI consumes. For a GUI-authored artifact, the draft is the source
of truth and the AGR JSON becomes a build intermediate, regenerated on every build. An
AGR authored elsewhere can still be opened, which imports it into a draft.
`CLAUDE.md` should be amended when this ships.

## Error handling

The governing principle: **the app never silently degrades a governance control.**

| Condition | Behavior |
|---|---|
| Config or schema fails to load | App cannot run. Show a `Doctor`-style diagnostic, not a stack trace. |
| Registry missing or malformed | Degrade openly. ID picker falls back to typing, persistent banner states the registry check is skipped, mirroring `RegistryLayer`'s Skipped check. |
| Draft file corrupt | Never lose work. Move it aside with a timestamp, say where it went, start clean. |
| Paste unreadable | No state change. Reassuring copy, ask the agent to resend. |
| Blocking validation failures | Not an error. Normal operation with Build disabled and the reason visible. |
| WebView2 user-data folder unwritable | Prevented at startup; also surfaced by `doctor`. |

## Testing

Core carries the load and needs no UI:

- `AgrAuthoring` defaulting, one test per artifact type, asserting classification,
  layer, review interval, and section titles against config. Eight tests that would
  have caught every error in the rejected Adaptive Card.
- `AgrDraft` to `AgrDocument` round-trip.
- `GovernorBrief` content per type, including that the four-option instruction appears
  only where `usesRadioOptionPattern` is set.
- `ImportParser` against a clean reply, a mangled one, one missing a required clause,
  and one with a three-option question.
- QA-ENG-08 with a passing and a failing test, per the convention of naming tests
  after the check ID.
- `ValidationService` overload asserted identical to the path-based call.
- `BuildCommandTests` unchanged and green, guarding the `BuildPipeline` extraction.

For the GUI, bUnit component tests on the three block editors covering the four-option
floor, character-lint feedback, and clause text being genuinely non-editable. Not
end-to-end. The rules are tested in Core; these prove only that the UI honors them.

## Prerequisite: the layer mapping

`artifactIdentity.layer` is schema-required but nothing supplies it. `artifact-types.json`
carries `category` per type and no `layer`, so nothing can derive it and the Governor
guesses. It flows from the AGR into `artifact-manifest.json` via
`ArtifactManifestBuilder` and stops: no validation beyond the schema's four-value enum,
no rendering, and repository targeting keys off `category` and `approvalStatus`. It is
the one required identity field the engine does not re-validate.

ADR-002 defines the four layers over program artifacts (Governance Standard, Risk
Register, Assessment Report Template, Repository Architecture) and names none of the
eight generation targets, so there was no mapping to lift. The owner ratified the
following on 2026-08-01 as a new ruling:

| Type | Category | Layer | Reasoning |
|---|---|---|---|
| DS | GOV | Governance | Deployment posture and control framework |
| AUP | GOV | Governance | Policy instrument |
| RAF | RSK | Operational | ADR-002 places the AI Risk Register in Operational |
| UQR | OPS | Delivery | Client-facing engagement output |
| DCN | OPS | Delivery | Client-facing engagement output |
| TCL | TRN | Delivery | Client-facing engagement output |
| TAK | TRN | Delivery | Client-facing engagement output |
| PIG | INT | Administration | Internal framework and package control |

It belongs per-type rather than per-category in `vocabularies.json`. Across these eight
it happens to be a pure function of category, but that breaks as soon as SOP or RBK get
section schemas, because ADR-002 puts Operational Runbooks in the **Governance** layer
while UQR and DCN in the same OPS category are Delivery.

Work: add `layer` to `ArtifactTypeConfig` and to each entry in `artifact-types.json`,
add QA-ENG-08 (next free check ID) failing any AGR whose layer disagrees with config,
add its `qa-checklist.json` entry, and update `governor-contract.md`.

## Also required, independent of the GUI

**Generate the Governor's knowledge from config.** `docs/governor-contract.md` states
it was "derived from ... as of 2026-07-14" and is hand-maintained. Nothing in `scripts/`
generates it, and it is already stale: it says eight generable types and "never offer
SCL, PDX" while config marks both `defined`. For a new technician, an agent that
confidently supplies wrong section titles is worse than no agent.

With parent and child agents this becomes concrete: generate one knowledge file per
artifact type from config, each becoming a child agent's knowledge. The parent triages
and routes. Eight small generated files instead of one large hand-maintained one, which
also mitigates the 2026-07-28 Copilot Studio paste-mangling problem, since a shorter
file is less likely to be wrecked and a generated file can be regenerated rather than
repaired.

## Work decomposition

This spec is a design record, not one implementation plan. It is too large for a
single plan and decomposes into the eight units below. Each gets its own plan in
`docs/superpowers/plans/` and ships working, tested software.

**On the naming.** These are labelled GUI-1 through GUI-8 and are **unrelated to the
repo's v2 slice numbering**. Slices 1 through 9 are the v2 build, delivered
2026-07-03 through 2026-07-07 and long since merged; the repo stopped numbering after
v2 acceptance and has named its work since. A plain "slice 6" is ambiguous in this
repo, so these carry a prefix. Do not renumber them and do not reuse the prefix for
anything outside this spec.

| ID | Unit | Depends on | Notes |
|---|---|---|---|
| GUI-1 | Layer mapping and QA-ENG-08 | none | Smallest, fully independent of the GUI. Closes the one required identity field the engine does not re-validate. Ship first. |
| GUI-2 | Generate Governor knowledge per artifact type | none | Docs and scripts only. Unblocks the Copilot Studio parent/child work and fixes a contract sheet that is already stale. |
| GUI-3 | Core authoring foundation | none | `AgrDraft`, `AgrAuthoring`, the `ValidationService` cached-config overload, the `BuildPipeline` extraction, `IIdRegistry` enumeration. No UI. Entirely testable, and every later unit rests on it. |
| GUI-4 | `GovernorBrief` and `ImportParser` | GUI-3 | Still no UI. Completes the Core surface the app needs. |
| GUI-5 | Design system generation | none | `screen` block in `style-rules.json`, `scripts/build-design-tokens.ps1`, generated stylesheet, golden test guarding regeneration. |
| GUI-6 | `Registrar.App` shell and Engagement screen | GUI-3, GUI-5 | WPF host, `BlazorWebView`, WebView2 user-data folder fix, `doctor` check, folder open, artifact list. Proves the platform end to end on the smallest screen. |
| GUI-7 | Editor screen | GUI-6 | The large one. Identity pane, config-generated section panels, the three block editors, live validation rail with `jsonPath` registry. |
| GUI-8 | Import screen and Build wiring | GUI-4, GUI-7 | Closes the loop. |

GUI-1, GUI-2, GUI-3, and GUI-5 have no dependency on each other and no dependency on
any GUI decision, so they can proceed while the Claude Design work on screens is still
running.

**This list is the endpoint for the operator GUI.** Completing GUI-1 through GUI-8
finishes it. Work discovered along the way is recorded as an amendment below, not by
inserting a new unit into the table. Adding scope is an owner decision.

### Amendments

- **2026-08-04, GUI-7.** The New artifact screen had no owning unit: the decomposition
  assigned Engagement to GUI-6, Editor to GUI-7, and Import to GUI-8, leaving creation
  unassigned, so the editor would have shipped able to open drafts that nothing could
  produce. Creation ships in GUI-7. The eight units remain the endpoint.
- **2026-08-04, GUI-7.** The `jsonPath` registry described under Data flows is not
  built. An input's DOM id is derived from its pointer (`FieldPath.ElementId`), so the
  lookup is the derivation and there is no map to populate or keep in step.
- **2026-08-04, GUI-7.** The error-handling rule "Move it aside with a timestamp, say
  where it went, start clean" for draft file corruption departs from the parent design.
  When a technician explicitly opens a draft that cannot be read, the app reports the
  error and touches nothing. Moving a file the operator just pointed at is a worse
  failure than refusing to open it, and EngagementScan already lists unreadable files,
  so the condition is visible either way. Unsolicited draft relocation belongs to a
  future unit if one ever scans folders on its own initiative rather than at the
  operator's request.
- **2026-08-04, GUI-8.** `ImportParser` returns an entire `AgrDraft`, not `sections[]`
  as the Data flows section describes. Importing into an open draft is therefore a
  merge, and identity carried by a reply is reported rather than applied, because a
  chat reply must not move a Document ID that Registry Owner issued.
- **2026-08-04, GUI-8.** A GUI build writes its output package into a per-artifact
  subfolder rather than flat. Four of the five output filenames are fixed, an
  engagement folder holds many artifacts, and `EngagementScan` globs `*.json`
  non-recursively, so a flat build would both overwrite an earlier build's evidence
  and add four unreadable rows to the operator's artifact list per build. The CLI's
  flat layout is unchanged.
- **2026-08-04, GUI-8.** An exception escaping a Blazor event handler in this WPF host
  crashes the process because the app lacks `DispatcherUnhandledException` and
  `ErrorBoundary` handlers. GUI-8 closed this at the Build call site by catching
  broadly there and rendering the failure as an ordinary build result. Home.razor's
  Rescan and OpenRow handlers, and NewArtifact.razor's Create, already catch broadly
  and report. But this coverage is ad hoc; nothing enforces it and nothing prevents
  the next handler from being written without it. The owner has since ruled that the
  app-wide net ships in GUI-8 rather than waiting: this list is the endpoint for the
  operator GUI, so there is no later unit to inherit it, and deferring to a unit that
  will not exist is how a known crash path becomes permanent. Both mechanisms were
  added, because they cover different ground and do not double-report an exception
  the boundary actually handles: an `ErrorBoundary` in `Routes.razor` renders
  `ScreenError` for anything Blazor raises inside the component tree, keeping the
  window and offering a way back to the engagement list, and a
  `DispatcherUnhandledException` handler in `App.OnStartup` is the last net for what
  reaches the WPF dispatcher outside it, showing the same class of message and
  marking the exception handled. An exception the boundary handles is never
  rethrown, so it never reaches the dispatcher handler that way; this is not an
  absolute split, though, since `ErrorBoundaryBase` itself rethrows when its error
  content throws, and again once `MaximumErrorCount` is exceeded, and either path
  does reach the dispatcher handler. The conclusion still holds in both cases,
  because a rethrown exception was never actually handled by the boundary. Neither
  swallows: both name the underlying failure, per rule 7. The ad hoc local catches
  stay, and Import's two handlers gained one, because a message beside the control
  the operator just used
  is better than a screen-level one. The global net is the floor, not the ceiling.

## Open items

1. `CLAUDE.md` needs amending when this ships, for the draft-as-source-of-truth
   inversion described above.
2. A `--org-danger` color for destructive actions is genuinely new and not derivable.
   Warn red is reserved: `colorUsage` restricts it to internal notes and
   remove-before-client-use callouts, and QA-SF-09 enforces that meaning. Needs owner
   approval as a new brand value, added to config.
3. Two `style-rules.json` colors are correct in print and fail as interactive UI:
   warn red on the warn fill measures 4.12:1 against a 4.5:1 text threshold, and
   `borderNeutral` on white measures 1.82:1 against a 3:1 control-boundary threshold.
   Screen-only replacements are needed in a `screen` block alongside `visualIdentity`.
4. Whether `Registrar.App` ships as one exe with the CLI or as a separate artifact.
