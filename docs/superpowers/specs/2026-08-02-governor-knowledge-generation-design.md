# GUI-2: Governor Knowledge Generation

Date: 2026-08-02
Status: Approved, not yet planned
Roadmap unit: GUI-2 in `docs/ROADMAP.md`

## Why this exists

The Governor is live. ORG technicians consult it. Its authoritative reference,
`docs/governor-contract.md`, is hand-derived: its own header says "Derived from
`schemas/agr.schema.json`, `config/artifact-types.json` ... as of 2026-07-14," and
nothing in `scripts/` generates it. It has since drifted. Section 2 still says the
engine can generate eight artifact types and instructs the agent to never offer SCL or
PDX, while `config/artifact-types.json` has marked both `defined` and
`generationTarget: true` since 2026-07-17.

`scripts/build-travel-pack.ps1` copies that file into the Governor's knowledge pack. So
a technician can today be confidently told the wrong thing by the agent that exists to
prevent exactly that.

The drift went unnoticed for a reason worth recording. `docs/governor-instructions.md`
states: "If they ever disagree, the contract sheet wins, because it is generated from
config and this is not." That claim is false, and the whole doc chain has been
operating on the belief that a generated ground truth already existed.

This unit makes it true.

## Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Generate one knowledge file per **agent role**, not per artifact type | The Studio Governor is a parent with children split by function, not by artifact. Every child needs all ten types, so a per-type split would not match how knowledge is attached. |
| 2 | Four projections now: intake, drafter, payload, correction | Their config sources are stable and owned by this thread. |
| 3 | The Archivist projection is deferred to its own unit | Its sources are `config/library-baseline.json` and `config/repository-targets.json`, which the GovLib workstream is actively changing. Generating against a moving target means regenerating on every GovLib landing and two workstreams editing the same config. |
| 4 | Derivation lives in `Registrar.Core`, exposed by a CLI verb | GUI-4's `GovernorBrief` derives the same per-type facts from the same config for the same reader. A PowerShell generator would force GUI-4 to either duplicate the logic in C# or shell out to a script from inside a GUI. |
| 5 | `docs/governor-contract.md` is deleted, not kept alongside | Leaving a hand-maintained 25k contract sheet next to four generated files gives two sources of truth for the same facts, which is the failure mode being fixed. |
| 6 | Generated files are committed, with a test that regenerates and byte-compares | A generated file committed without a staleness guard is a hand-maintained file with extra steps. The repo already uses this pattern for fixture goldens. |
| 7 | Per-agent **instruction text** is out of scope | Instructions are capped at 8000 characters by Studio and are a different size budget with five outputs of their own. Its own unit. |

### What is deliberately not generated

`docs/governor-briefing.md` and `docs/governor-payload-correction.md` stay
hand-written and untouched.

The briefing is orientation prose explaining why the two systems divide labour as they
do. The payload-correction doc is a protocol: a failed check is not a bug to argue
with, it is an instruction; fix the exact field, do not restructure, resubmit. Neither
is derivable from config, and generating judgment prose from data would produce
something worse than what exists.

The correction handler needs both that protocol and the generated check catalogue.
They are complementary, not duplicative.

## Prerequisite: bind the config that already exists

Most of the facts these projections need are in the repo and have never been read.
This is the same situation as `radioOptionPattern` and `contentLint`, both of which sat
dead in `style-rules.json` until a slice needed them.

**Vocabulary.** `ConfigLoader` parses `config/vocabularies.json` only for approval
statuses, transitions, and review intervals. Unbound and needed: the Document ID
pattern, the version pattern, the classification values, the `frameworkLayer` values,
`approvalStatus.prohibitedAliases`, and the category list.

**QA rule text.** `QaCatalogEntry` is `(CheckId, RuleSource, ErrorCategory)`. The
`rule` field, which is the entire content the correction handler needs, is not bound.
`QaCatalog` also exposes only `Lookup` and `Contains`, with no enumeration, so nothing
can iterate the catalogue.

**The AGR schema.** Required fields, the `outputFileName` pattern, and the `blockType`
enum live in `schemas/agr.schema.json` and are not part of `ConfigBundle` at all. They
are schema facts, not config facts, so they are passed alongside rather than folded in.

## Architecture

```
src/Registrar.Core/Config/
  ConfigBundle.cs        + Vocabulary
  ConfigModels.cs        + VocabularyConfig
  ConfigLoader.cs        bind the vocabulary fields
  QaCatalog.cs           + Rule, Layer, AppliesTo on the entry; + enumeration

src/Registrar.Core/Governor/
  KnowledgeSource.cs     record (ConfigBundle Config, JsonNode AgrSchema)
  IKnowledgeProjection.cs
  IntakeProjection.cs
  DrafterProjection.cs
  PayloadProjection.cs
  CorrectionProjection.cs

src/Registrar.Cli/
  EmitKnowledgeCommand.cs

docs/governor/           intake.md drafter.md payload.md correction.md
                         generated, committed
```

`IKnowledgeProjection` is one method, `string Render(KnowledgeSource source)`, plus a
`FileName`. Projections are pure: no IO, no clock, no ambient state. The CLI verb owns
reading config, writing files, and nothing else. That split is what lets the whole
generation path be tested without touching the file system, and what lets GUI-4 call
`DrafterProjection` directly.

## The four files

**`intake.md`** serves the Steward. The artifact catalogue: code, full name, category,
classification, description, `audienceDefault`, review interval, and whether the type
is generable. No section schemas, no clauses, no block types. It answers "which
artifact does this engagement need" and deliberately nothing else, because an intake
agent carrying structural detail is an intake agent that will start drafting.

**`drafter.md`** is the heavy one. Per generable type: the `orderedSections` titles
exactly and in order, `requiredClauses` as ID plus canonical name, `requiredPlaceholders`,
and the radio-pattern rule where `usesRadioOptionPattern` is set. Plus the shared
block-type list from the schema enum and the character policy from `contentLint`.

Note for the implementer: `usesRadioOptionPattern` is set on **two** types, TCL and
DQR. DQR is `not-defined-in-spec` and not generable, so it must not appear in
`drafter.md` at all. Iterate the generable set and read the flag within it; do not
iterate the flag. `usesRadioOptionPattern` is currently unbound on
`ArtifactTypeConfig` and needs binding along with the vocabulary fields.

**`payload.md`** serves the assembler. Envelope shape and required fields from the
schema, the Document ID pattern including its category and code pairings, the version
pattern, the three closed vocabularies, the legal lifecycle transitions, the
`outputFileName` pattern, and the standing prohibitions.

**`correction.md`** serves the correction handler. Every QA check as checkId, layer,
error category, rule text, and ruleSource. This is what turns a `validation-report.json`
into something actionable, and it is nearly free once `QaCatalog` enumerates.

Every fact in all four is read from config or the schema. None is transcribed.

## Determinism and the staleness guard

The guard is a test that renders all four projections and byte-compares them against
the committed files, failing with an instruction to run `registrar emit-knowledge`.

Two rules make that work:

**No generation timestamp.** A "generated on" line would fail the guard every day.
Nothing in a projection may read a clock.

**A provenance header carrying the config SHA-256 values** from `ConfigBundle.Hashes`,
which are already computed. This strengthens the guard rather than weakening it:
change `artifact-types.json` without regenerating, and the committed hash line no
longer matches the computed one, so the test fails on precisely the drift being
guarded. It also lets an operator looking at a file inside Copilot Studio tell whether
it matches the engine they are running.

## CLI

`registrar emit-knowledge [--out <dir>]`, defaulting to `docs/governor/`.

That is the whole surface. A `--check` flag was considered for CI and cut: the xUnit
guard already fails the build, so `--check` would be a second implementation of the
same gate.

## Travel pack

`scripts/build-travel-pack.ps1` copies a hardcoded document list into the pack's
`governor/` directory. Drop `governor-contract.md`, add the four generated files.

Its generated `UPLOAD-TO-GOVERNOR.md` currently describes attaching one contract sheet
to one agent. It must describe five agents and which files each receives. That file is
what an operator actually follows when standing the Governor up, so it is part of this
unit rather than a follow-up.

## Deleting the contract sheet

Five tracked files reference `docs/governor-contract.md`. One,
`docs/superpowers/specs/2026-07-17-copy-ready-message-design.md`, is a historical
design record and stays as written. The four live references to update:

- `docs/governor-instructions.md` - also remove the false claim that the contract sheet
  is generated from config, and point at the real generated files
- `docs/operations-guide.md`
- `docs/tech-debt.md`
- `scripts/build-travel-pack.ps1`

## Testing

Facts, not shape. A test asserting "the file has four headings" would pass over a file
full of wrong section titles.

- `drafter.md` contains all seven TCL section titles in order; contains `CL-003` with
  its canonical name; contains the four-option radio rule for TCL and not for DS.
- **Converse invariant:** every artifact type whose `sectionSchema.status` is `defined`
  appears in `drafter.md`. Without this, a future generable type is silently absent and
  the suite stays green. This is the failure mode GUI-1's review caught in the layer
  mapping.
- `intake.md` lists every artifact type in `artifact-types.json`, generable or not,
  since the Steward must be able to say "that one cannot be generated yet."
- `correction.md` contains every checkId in the catalogue, count-matched rather than
  spot-checked.
- Determinism: render twice, assert byte-identical.
- Character hygiene: the generated files are ORG documents and must be ASCII-clean
  under the same rule as the rest of the repo.
- The staleness guard itself.

## Out of scope

- The Archivist projection. Its own unit, after GovLib settles.
- Per-agent instruction text under the 8000-character Studio cap. Its own unit.
- Standing the rebuilt knowledge up in Copilot Studio. That is GOV-1.
- Running the eval harness against the result. That is GOV-2.

## Amendment to the roadmap

`docs/ROADMAP.md` needs two corrections when this lands:

1. GUI-2 is described as "Generate Governor knowledge **per artifact type** from
   config." It is per agent role. The Amendments log records the change.
2. The GOV-1 note says "The Governor already runs in Copilot Studio as a parent with
   **per-type** children." The children are split by function: intake, drafter, payload
   assembler, correction handler, and archivist.
