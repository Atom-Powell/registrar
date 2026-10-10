# GUI-7: Editor screen

Date: 2026-08-04
Status: Approved, not yet planned
Roadmap unit: GUI-7 in `docs/ROADMAP.md`
Parent design: `docs/superpowers/specs/2026-08-01-operator-gui-design.md`
Predecessors: `docs/superpowers/specs/2026-08-02-core-authoring-foundation-design.md`,
`docs/superpowers/specs/2026-08-03-governor-brief-and-import-design.md`

## Why this exists

GUI-6 gave the Registrar a window, but it is a window onto artifacts a technician
cannot yet create or change. Every Core piece the authoring loop needs is already in
place: `AgrDraft` and `AgrAuthoring` from GUI-3, `GovernorBrief` and `ImportParser`
from GUI-4, design tokens from GUI-5, `EngagementScan` and `DraftFile` from GUI-6.
Nothing calls them. `AgrDraft` says so in its own source: draft creation "is a GUI
responsibility that does not exist in this repo yet."

GUI-7 is that responsibility. It closes the loop from an empty engagement folder to a
draft that is one Build away from a controlled document, without a technician ever
hand-editing JSON.

## Five decisions taken while designing this unit

1. **Artifact creation belongs to GUI-7.** The parent design lists four screens and
   the work decomposition assigns three of them. No unit owned New artifact, so the
   editor would have shipped able to open drafts that nothing could produce. Creation
   folds into this unit and is recorded as an amendment to the parent design's table,
   not as a ninth unit.
2. **Three block editors and a read-only fallback.** Prose (`paragraph`, `h2`, `h3`,
   `bulletList`), clause insertion, and radio question are editable. The remaining
   four types (`table`, `warnCallout`, `aggregationTable`, `copyReadyMessage`) render
   read-only and are preserved verbatim on every save. Showing a block a technician
   cannot edit is honest; hiding it would be the silent degradation the parent
   design's error-handling principle forbids. A new block can only be added in one of
   the three editable kinds, so the editor never creates content it cannot then edit.
3. **The draft autosaves.** The debounce that drives live validation also writes the
   file. There is no Save button, no unsaved-changes state, and no way to close the
   window and lose an hour. The draft is the source of truth, so a stale file on disk
   is the risk worth engineering against.
4. **Build is present and inert.** The header renders the mode toggle, Import, and
   Build, and computes Build's enabled state and its blocking reason from the
   validation rail. Clicking Build does nothing in this unit. Deciding when Build is
   legal is the hard part and it lands here, in front of the rail that justifies it;
   GUI-8 wires the button to `BuildPipeline`.
5. **The jsonPath registry is deleted, not built.** See below.

## Four corrections to the parent design

**The registry does not need to exist.** The parent design calls the
path-to-field registry "the fiddliest part of the design" and names it so it does not
get improvised: each input registers its own `jsonPath` as the form is generated, and
the rail looks up that registry. But if an input's DOM id is derived deterministically
from its pointer, the lookup is the derivation. There is no dictionary to populate
during render, nothing to rebuild when the form re-renders, and nothing to drift. The
rail focuses `FieldPath.ElementId(check.JsonPath)` directly.

**`Registrar.App` is reachable by tests.** GUI-6 concluded that a `net8.0` test project
cannot reference a `net8.0-windows` one, so nothing in `Registrar.App` is worth
testing. The first half is true and the second overstates it:
`tests/Registrar.App.Tests` targets `net8.0-windows`, references `Registrar.App`, and
already runs bUnit against `Engagement.razor`. Rules still belong in Core, because the
fast suite is cheaper and covers more, but "unreachable by the suite" is not the
reason.

**The four-option floor is a config value, not a rule.** The parent design describes
"a radioQuestion editor enforcing the four-option floor." Four is the current value of
`radioOptionPattern.minimumOptions` in `config/style-rules.json`, and
`RadioQuestionLayer` reads it from `context.Config.RadioPattern.MinimumOptions`. The
editor reads the same value. A literal `4` in the UI would violate rule 3 and would
disagree with QA-SF-10 the moment config changed.

**Section indices are the document's, not the draft's.** `AgrAuthoring.ToDocument`
matches draft sections to config titles by title, emits them in `orderedSections`
order, and omits any section with no blocks. A check reading
`/dataInputs/sections/2/blocks/0` therefore names a position in the converted
document, which is not the position in `draft.Sections`. Building the form over the
draft array in its own order would send every rail click past the first incomplete
section to the wrong block. The form is built over the same ordered projection
`ToDocument` produces.

## The pointer contract

`AuthoringGap.JsonPath` and `Check.JsonPath` already speak one dialect: RFC 6901 JSON
Pointer, rooted at the AGR document, for example `/artifactIdentity/documentId` and
`/dataInputs/sections/2/blocks/0/blockType`. No layer emits JSONPath (`$.a.b`) and none
emits a relative path. That agreement is what lets one rail carry both kinds of item.

`FieldPath` becomes the single owner of that dialect. It exposes builders rather than
string concatenation at call sites, and `ElementId(pointer)` for the DOM. A test
asserts the pointers `FieldPath` builds are identical to the ones the layers emit for
the same field, so a layer that changes its path spelling breaks a test rather than
quietly breaking rail navigation.

## Architecture

Five new Core types. All pure, all testable in the fast `net8.0` suite, none aware of
Blazor.

**`FieldPath`** owns the pointer dialect and `ElementId`.

**`EditorForm.For(draft, config)`** returns the ordered section panels the editor
renders. Each panel carries its config title (fixed, never editable, per QA-ENG-02),
its document index, and its blocks. Each block carries its pointer, its editor kind
(`Prose`, `Clause`, `Radio`, `ReadOnly`), and the `AgrBlock` itself. Sections with no
content still appear, because an empty section is where a technician authors, and it
is simultaneously what `ToDocument` reports as a gap.

**`DraftEdits`** holds every mutation as `AgrDraft -> AgrDraft`: set prose text, add a
block, remove a block, reorder within a section, set a clause id, edit a question
prompt or option, set required, toggle `internalOnly`. Razor calls these and decides
nothing. Every mutation rule is therefore testable without a UI.

**`DraftCreation.New(...)`** produces a draft from the five human inputs (artifact
type, Document ID, client, engagement, owner) plus the derived review date from
`AgrAuthoring.Defaults` and a generated `sourceWorkflowContext`. It also names the
`.agrdraft` file. `AgrDraft`'s own comment is explicit that `ReviewDate` is derived
once, at creation, and never recomputed on save; this is the one place that derivation
happens.

**`EditorRail`** merges into a single ordered list. When the draft does not convert,
the rail holds `AuthoringGap`s. When it converts, the rail holds `Check`s. Never both,
because a gap means there is no document to validate. It also computes the
Build-blocked reason as a sentence, so the header can state it without a hover.

## Screens and components

`MainWindow` currently registers only `AddWpfBlazorWebView`. It gains `EngineContext`
and `IClock` as singletons, so config and the compiled schema load once for the process
lifetime as the parent design requires, and an `AppState` service holding the open
draft, its path, and the current form and rail.

- `NewArtifact.razor` at `/new`. Artifact type comes from a list of types whose
  `SectionSchema.Status` is `defined`; rule 6 refuses the rest, so offering them would
  be a lie the engine then has to tell. Document ID is a picker over
  `IIdRegistry.All()` filtered to the chosen type, falling back to a typed field with
  a pattern and type-agreement check when no registry is loaded. Client, engagement,
  and owner are typed. A review panel tags each resulting field with its source (you,
  config, registry, derived).
- `Editor.razor` at `/editor`. Three panes: `IdentityPane` left, `SectionPanel` list
  centre, `ValidationRail` right, under an `EditorHeader`. `IdentityPane` edits what
  `AgrDraft` treats as human input (client, engagement, owner, review date) and
  displays what config derives (artifact type, Document ID, classification, layer,
  audience, version, approval status, prepared by, output file name) as read-only text
  tagged with its source. A derived value is changed only through
  `AgrDraftOverrides`, behind a deliberate control, which is what that record exists
  for: a departure from a derived value should be representable and visible rather
  than indistinguishable from typing in a box.
- `ProseBlock`, `ClauseBlock`, `RadioBlock`, `ReadOnlyBlock`.

Every input carries `id="@FieldPath.ElementId(pointer)"`. Rail items focus by that id
through a small JS module; an anchor would scroll but not focus, and focus is what
makes the rail usable with a keyboard.

`ProseBlock` runs `CharacterLint.Scan(text, CharacterPolicy.FromConfig(config.ContentLint))`
as the technician types and shows offenses immediately rather than waiting for the
debounce. That is the same policy `InputHygieneLayer` enforces for QA-ENG-05, so the
editor cannot disagree with the check that will later fail the build.

`ClauseBlock` is a dropdown over `config.Clauses`. The selected clause's
`CanonicalText` renders read-only beneath it. Rule 4 says clause insertion is by
reference and Governor prose is ignored by design; showing the canonical text
uneditable is that rule made visible to the operator.

`RadioBlock` enforces `config.RadioPattern.MinimumOptions` and shows the option count
against it.

The Engagement screen gains an open action per row. The screen already knows the
folder and `EngagementRow` carries the file name, so the path is a `Path.Combine` and
`EngagementRow` does not change. A `.agrdraft` opens directly. An existing AGR opens
through `AgrAuthoring.FromDocument` and is written beside it as a draft.

## Data flow

**Startup.** `EngineContext.Load` once. A failure here is fatal and shows a
`doctor`-style diagnostic, per the parent design's error table.

**Create.** Five inputs, `DraftCreation.New`, `DraftFile.Write` into the engagement
folder, navigate to the editor.

**Edit.** A component calls a `DraftEdits` function; `AppState` replaces the draft.
One 400 ms debounce then does three things in order: write the draft (temp file, then
move over the original), rebuild the form, and validate.

**Validate.** `AgrAuthoring.ToDocument` first. Gaps go straight to the rail. A document
that converts is serialized with `AgrSerializer.ToJson` and passed to
`ValidationService.Validate(agrText, engineContext, registryCsvText)`, the cached-config
overload GUI-3 added for exactly this call rate.

**Mode.** The toggle sets `draft.Mode` and revalidates, so a technician can see what
promotion would add before attempting it.

**Build.** The header computes enabled state and reason from the rail. The click is
inert in this unit.

## Error handling

The parent design's table stands. Three cases it does not cover:

| Condition | Behavior |
|---|---|
| Draft write fails (file locked, sync client holding it) | The draft stays in memory and a banner names the path and the error. Autosave that quietly stops saving is worse than no autosave. |
| Validation throws | An engine-error item appears in the rail. A blank rail must never be ambiguous between "nothing is wrong" and "we did not check". |
| Draft contains an unsupported block type | Rendered read-only and preserved verbatim through every save. Visible and unlosable. |

The second is the same failure GUI-6 fixed on the Engagement screen, where a mistyped
folder reported that the folder was empty.

## Testing

The fast `net8.0` suite carries the load:

- `FieldPath` pointers asserted identical to the pointers the layers emit for the same
  field.
- `EditorForm` per artifact type: panels match `orderedSections` in order, and indices
  match the projection `ToDocument` produces for the same draft, including a draft
  whose sections are out of order and one with an incomplete section.
- `DraftEdits` round-trips, including that an unsupported block survives an unrelated
  edit and a save.
- `DraftCreation` defaults per artifact type, and that `ReviewDate` is set once at
  creation rather than recomputed.
- `EditorRail` merge behavior and its Build-blocked sentence, including the case where
  the draft does not convert.

bUnit in `Registrar.App.Tests` proves only that the UI honors rules already tested in
Core:

- `ProseBlock` surfaces a lint offense (named for QA-ENG-05).
- `ClauseBlock` renders canonical text with no editable control bound to it.
- `RadioBlock` blocks below the minimum and reads the number from config rather than a
  literal (named for QA-SF-10).
- An unsupported block renders visibly rather than being dropped.
- Rail item targets match `FieldPath.ElementId`.

## Out of scope

- Wiring Build to `BuildPipeline`, and the Import screen. Both are GUI-8.
- Editors for `table`, `warnCallout`, `aggregationTable`, and `copyReadyMessage`.
  `aggregationTable` in particular carries no editable content: it holds only an
  `aggregationSource` and the renderer projects config at render time.
- Any change to `EngagementRow`, `AgrPicker`, or the CLI.

## Amendments this unit requires elsewhere

1. The parent design's work decomposition table gains a note that New artifact ships
   in GUI-7. The table's eight units remain the endpoint.
2. `CLAUDE.md` needs the draft-as-source-of-truth amendment the parent design lists as
   open item 1: for a GUI-authored artifact the `.agrdraft` is the source of truth and
   the AGR JSON becomes a build intermediate. This unit is where that becomes true, so
   the amendment lands with it.
