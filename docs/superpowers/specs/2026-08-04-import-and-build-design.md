# GUI-8: Import screen and Build wiring

Date: 2026-08-04
Status: Approved, not yet planned
Roadmap unit: GUI-8 in `docs/ROADMAP.md`
Parent design: `docs/superpowers/specs/2026-08-01-operator-gui-design.md`
Predecessors: `docs/superpowers/specs/2026-08-03-governor-brief-and-import-design.md`,
`docs/superpowers/specs/2026-08-04-editor-screen-design.md`

## Why this exists

GUI-8 is the last unit of the operator GUI, and it closes the loop. GUI-4 built both
directions of the Governor handoff and nothing calls them. GUI-7 built an editor whose
Build button is deliberately inert, with its enabled state computed, a `BuildWired` flag
defaulted to false, and a test already asserting what happens when that flag flips.

Without this unit a technician can author an artifact but cannot receive the Governor's
draft or produce a document, which means the GUI is an authoring surface for a compiler
it cannot run. The manual authoring path GUI-7 shipped is the fallback; Import is the
path the program was designed around.

## Four decisions taken while designing this unit

1. **The output package gets one owner.** Deciding which of the five files land, and
   when, is governance logic that currently exists only inside `Registrar.Cli`. A GUI
   that wrote its own would state that rule twice. It is extracted so both callers use
   it.
2. **Import applies content, never identity.** `ImportParser.Parse` returns a whole
   `AgrDraft`, so a pasted reply can carry an artifact type, Document ID, classification,
   or owner that disagrees with the draft already open. Only sections and the placeholder
   map are applied. A disagreement is reported.
3. **A build writes into a per-artifact subfolder.** Four of the five output filenames
   are fixed, and an engagement folder holds many artifacts.
4. **Import is a screen, not a panel.** It has a genuine two-step shape, and the editor
   is already three panes wide.

## Two corrections to the parent design

**`ImportParser` returns more than the parent design assumed.** That document describes
it as turning a reply into "`sections[]` plus a structured result". What GUI-4 actually
built returns an entire `AgrDraft` plus findings (`src/Registrar.Core/Import/ImportResult.cs`).
That is a better surface, but it means importing into an open draft is a merge rather
than an append, and the parent design never says what wins. This spec answers it: content
wins nothing it did not author, and identity is not content.

**The parent design's data flow implies a flat output layout.** It says outputs "land in
the folder as they do today", which is true of the CLI, where a folder conventionally
holds one AGR. The Engagement screen is built for the opposite: it lists every artifact
in a folder, and the fixtures folder holds 38. Written flat, a second build would
overwrite the first build's validation report, promotion payload, generation log, and
manifest, all four of which carry fixed names. Worse, `EngagementScan` globs `*.json`
non-recursively (`src/Registrar.Core/Engagement/EngagementScan.cs`), so those four files
would themselves be scanned as artifacts and appear in the operator's list as unreadable
rows, four per build. The per-artifact subfolder resolves both, and because the scan does
not recurse, the package stays out of the artifact list entirely.

## What moves, and why it is the same move GUI-3 already made

GUI-3 extracted `BuildPipeline` out of `BuildCommand` so a GUI could reuse the build
orchestration. It stopped at the decisions and left the writing behind. That was correct
for GUI-3, which had no caller for the writing half; it is not correct now.

`BuildPipeline.Run` returns everything in memory and touches no file system, by design,
and its own comment says so. What remains in `BuildCommand` is roughly seventy lines that
decide which of those in-memory products become files:

- the validation report always,
- the promotion payload when the outcome carries one,
- the generation log whenever the pipeline reached rendering, emitted before any outcome
  branch so it records a withheld DOCX as faithfully as a produced one,
- the DOCX only when eligible,
- the manifest only alongside a DOCX, because a manifest describes an artifact that was
  actually written.

`CLAUDE.md` specifies that package, so those are engine rules sitting in the CLI.

**`OutputPackage` in `Registrar.Pipeline`.** It takes a `BuildResult`, a target directory,
and the output filename, and returns a record naming every file written, every file
withheld with the reason, and any IO failure. It converts IO failure into that record
rather than throwing, the same shape `DraftSettler` uses, because the caller needs to
report a partial package rather than catch an exception and guess.

It lives in `Registrar.Pipeline` because it must see `BuildResult`, which lives there, and
`Registrar.Core` cannot reference `Registrar.Pipeline` without inverting the dependency.

`BuildCommand` becomes a mapping from that record to exit codes and stderr. **`BuildCommandTests`
must pass completely unchanged**; that is the guard the extraction was faithful, exactly as
it was for `BuildPipeline`.

### The CLI is a fallback, so the extraction is pinned before it starts

Owner constraint, 2026-08-04: **the CLI must remain intact as a fallback in case the GUI does
not work out.** That raises the bar on this extraction from "well tested" to "provably
behavior-preserving", because the CLI is not merely another caller, it is what the program
falls back to.

The existing coverage is already strong. `BuildCommandTests` holds 27 tests amounting to a
characterization suite for exactly what this extraction touches: DOCX written or withheld,
exit codes 0, 1 and 2, promotion payload emission across six artifact types, the generation
log present on a withheld render and absent on a pre-render failure, and `artifactSha256`
stamped or left null.

What it does not assert is the **complete file set**. Each test checks that the files it cares
about exist; none checks that nothing extra appeared and nothing quietly vanished. An
extraction that emitted one additional file, or dropped one on a branch no test names, would
pass all 27.

So the first task of this unit, before `BuildCommand` is touched at all, adds that assertion
against the unmodified CLI: for each build outcome, the exact set of file names the run
produces. It has to pass on today's code, unedited, which is what makes it a characterization
test rather than a statement of intent. It then has to keep passing through the extraction,
alongside all 27 existing tests, none of which may be edited.

If any of them needs an edit to pass, the extraction changed CLI behavior and is reworked
rather than accommodated.

The subfolder name is a separate function, tested, that the GUI calls. The writer itself is
location-agnostic and the CLI keeps passing its flat directory, so **CLI behavior does not
change at all**.

## The one new rule

**`ImportMerge` in `Registrar.Core.Import`.** Given the open draft, a parsed draft, and the
artifact type, it returns the merged draft plus findings. It applies `dataInputs.sections`
and `dataInputs.placeholderMap`. It applies nothing else.

Every identity or context field the reply carries that disagrees with the open draft becomes
an `ImportFinding` rather than an edit. `ImportFinding` already exists and is already
deliberately not a `Check`, which is the right shape here for the same reason: a Governor
that echoed the wrong Document ID is a transport problem, not a governance violation by the
document.

This is `CLAUDE.md` rules 4 and 5 and the division of labor made executable. The Document ID
was issued by Registry Owner and chosen by the operator; a chat reply must not be able to
move it. Putting this in Core rather than in the Import screen is what lets the fast suite
prove it.

## Components

**`Registrar.Pipeline`**
- `OutputPackage.Write(BuildResult, string directory, string outputFileName)` returning
  `OutputPackageResult`.
- `OutputPackage.DirectoryNameFor(string documentId, string version)`.

**`Registrar.Core.Import`**
- `ImportMerge.Apply(AgrDraft open, AgrDraft imported, ArtifactTypeConfig type)`.

**`Registrar.App`**
- `BuildRunner`, a plain class beside `DraftSettler`: converts the draft, decides whether
  the outcome it holds describes that exact document and revalidates when it does not,
  loads the wordmark asset, calls `BuildPipeline.Run`, creates the subfolder, writes the
  AGR JSON intermediate, calls `OutputPackage.Write`, and returns what happened.
- `Import.razor` at `/import`.
- A build-result region in `Editor.razor`.
- `Registrar.App` gains a project reference to `Registrar.Pipeline`.

`Registrar.App` still enforces no rules of its own. `BuildRunner` is glue in the same sense
`DraftSettler` is: it sequences Core and Pipeline calls and holds no judgment about what is
valid.

## Data flow

**Brief out.** The editor header's Import button opens `/import`, which renders
`GovernorBrief.For(type, config)` with a copy control.

**Reply in.** The paste goes to `ImportParser.Parse`. The screen reports what lined up and
what did not, and **writes nothing**. Accept applies `ImportMerge`, returns to the editor,
and the editor settles as usual, which saves and revalidates. Back changes nothing.

**Repair.** When findings indicate transport damage, `ImportParser.Normalize` is offered as
an explicit action and is never automatic. When it reports collisions the repair is refused
whole and the screen says so, because a partial repair that silently dropped a placeholder
would leave the operator believing the document had been cleaned.

**Build.** Convert the draft to an `AgrDocument`. Reuse the settled `ValidationOutcome` only
when it describes that exact document; otherwise revalidate. `BuildPipeline.Run` supplies the
in-memory products, the subfolder is created, the AGR JSON intermediate is written there, and
`OutputPackage.Write` produces the package. The editor then shows the overall result, the
folder, every file written, and anything withheld with its reason.

**On reusing the outcome.** `BuildPipeline.Run` takes an outcome rather than computing one,
deliberately, so a GUI that validates continuously does not re-validate on every press and so
one clock read stands behind the report. That optimization is only sound if the outcome
describes the document being built. `BuildRunner` must confirm that rather than assume it,
because the alternative is a validation report attesting to a document that was never
validated, which is a false attestation of exactly the kind the pipeline already refuses to
make about artifact hashes.

Confirming it must not rely on "no edit happened since", which is a claim about wall-clock
timing and debounce state rather than about the document. `DraftSettler` already serializes
the converted document with `AgrSerializer.ToJson` to feed `ValidationService.Validate`. It
therefore holds the exact text that was validated, and the check is a string comparison:
`BuildRunner` converts the draft, serializes it, and reuses the outcome only when that text
equals the text the held outcome was produced from. Anything else revalidates. This makes
the freshness question decidable from the two values themselves, with no timer involved, and
it is directly testable.

**The AGR JSON intermediate** is written into the same subfolder as the package, named for the
Document ID with a `.json` extension. It goes there rather than beside the draft for the same
reason the package does: `EngagementScan` globs `*.json` in the engagement folder, so an AGR
written flat would appear in the operator's artifact list as a second row for an artifact they
already have open, which is the duplicate-row confusion this layout exists to avoid.

## Error handling

The parent design's table stands. This unit adds:

| Condition | Behavior |
|---|---|
| Paste recovers nothing | No state change. The draft is untouched and the screen says the reply could not be read. |
| Paste recovers content with findings | Import is allowed. The rail keeps showing the problems and Build stays blocked, per the parent design. |
| Reply carries identity that disagrees | Reported, never applied. |
| `Normalize` reports collisions | Repair refused whole, nothing changed, and the screen says why. |
| Package write fails partway | Report exactly which files landed. A partial package must never read as complete: a validation report without its document is evidence of something that did not happen. |
| Wordmark asset missing | A build error naming the asset, not a crash. |
| Subfolder exists from a previous build | Replaced wholesale, never merged, so a stale file from an earlier run cannot masquerade as part of this one. |

## Testing

Fast `net8.0` suite:

- `OutputPackage`'s emit matrix: pre-render failure writes the report only; reaching
  rendering adds the generation log even when the DOCX is withheld; eligibility adds the
  DOCX and the manifest together; promotion mode with `PassWithWarnings` withholds the
  DOCX and therefore the manifest.
- `OutputPackage` reporting a partial write when a file cannot be written.
- `DirectoryNameFor` per artifact type and version.
- `ImportMerge`: sections and placeholder map applied; a disagreeing Document ID reported
  and not applied; a disagreeing artifact type reported and not applied.
- A file-set characterization test, written first against the unmodified CLI, asserting the
  exact set of files each build outcome produces. Written before the extraction, passing on
  today's code, and unedited afterward.
- `BuildCommandTests` unchanged and green, guarding the extraction. None of its 27 tests may
  be edited to accommodate the change.

bUnit in `Registrar.App.Tests`:

- A paste writes nothing before Accept, asserted on the draft's content rather than on a
  screen state.
- Back leaves the open draft byte-identical.
- Accept applies sections and leaves identity alone.
- `BuildRunner` reuses an outcome whose serialized document matches, revalidates when it
  does not, and reports a partial package honestly. The revalidation case must be driven by
  changing the document rather than by waiting, so the test proves the comparison and not a
  timer.

## Out of scope

- Anything that does with the promotion payload beyond writing it. Handing it to Technology
  Success to update the ID Registry stays a human step.
- SharePoint or Graph integration. The registry adapter stays the interim workbook behind
  its interface.
- Any change to `AgrPicker`, `EngagementRow`, `agr.schema.json`, or any validation layer.

## Amendments this unit requires elsewhere

1. The parent design's Data flows section describes outputs landing flat in the folder.
   It gains a note that a GUI build writes a per-artifact subfolder, and why.
2. The parent design describes `ImportParser` as returning `sections[]`. It gains a note
   that it returns an `AgrDraft`, and that identity from a reply is reported rather than
   applied.
3. `docs/ROADMAP.md` marks GUI-8 done, which completes GUI-1 through GUI-8 and the operator
   GUI as scoped.
