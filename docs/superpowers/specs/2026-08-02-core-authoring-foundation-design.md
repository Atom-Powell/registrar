# GUI-3: Core Authoring Foundation

Date: 2026-08-02
Status: Approved, not yet planned
Roadmap unit: GUI-3 in `docs/ROADMAP.md`
Parent design: `docs/superpowers/specs/2026-08-01-operator-gui-design.md`

## Why this exists

Everything in the operator GUI rests on this unit. GUI-4, GUI-6, GUI-7, and GUI-8 all
consume it, and none of it has a user interface, so all of it is testable as plain
functions.

Today the engine can only be driven by handing a finished AGR file to the CLI. The GUI
holds a partially-filled artifact in memory and needs three things the engine does not
currently offer: a model for work in progress, a conversion from that model to a valid
AGR, and a way to run the build without going through a console command that returns
exit codes.

## Two corrections to the parent design

The parent spec is wrong on one point and silent on another. Both were found while
designing this unit and both would have surfaced mid-implementation.

**`BuildPipeline` cannot live in `Registrar.Core`.** The parent spec says the
`BuildCommand` orchestration "moves to Core as `BuildPipeline`". That is impossible.
The project graph is:

```
Core          no project references
Validation -> Core
Rendering  -> Core
Cli        -> Core, Validation, Rendering
```

`Validation` and `Rendering` are siblings and neither references the other. Orchestrating
a build means doing both, so hosting it in Core would require Core to reference its own
dependents. A new `Registrar.Pipeline` project sits above both and is consumed by `Cli`
today and `Registrar.App` at GUI-6.

**An AGR serializer is required and was never mentioned.** Schema validation operates on
raw JSON text, not on a parsed `AgrDocument`. `AgrParser` reads; nothing writes. So the
GUI cannot validate anything it authors until an `AgrSerializer` exists. Neither the
parent spec nor the roadmap names it.

## Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | `AgrDraft` reuses the existing AGR tree rather than mirroring it | The sections and blocks a technician edits are already the right shape. A full nullable mirror would add roughly six record types that must be kept in step with the AGR model forever, when the schema is already what defines those shapes. |
| 2 | Incompleteness is reported as jsonPath-addressed gaps | The editor screen already needs every input to register its own `jsonPath` so the validation rail can jump to the offending field. Using the same vocabulary for "not filled in yet" and "fails a check" means one click-to-field mechanism serves both. |
| 3 | `BuildPipeline` is pure: loaded inputs in, in-memory results out | No file access, no exit codes, no console. The CLI keeps path resolution and writing; the GUI writes where it likes. GUI-2's `EmitKnowledgeCommand.LoadSource` already established this pattern. |
| 4 | `BuildPipeline` lives in a new `Registrar.Pipeline` project | See above. It is the layer that composes validation and rendering, which has no home today, which is precisely why the logic ended up inside a CLI command. |
| 5 | The existing path-based `ValidationService.Validate` is reimplemented in terms of the new overload | One code path rather than two that drift. The existing signature keeps working, so no caller changes. |

## Architecture

```
Registrar.Core        + AgrDraft, AgrDraftOverrides
                      + AgrAuthoring, AuthoringResult, AuthoringGap
                      + AgrSerializer

Registrar.Validation  + EngineContext
                      + ValidationService overload taking EngineContext
                      + IIdRegistry.All()

Registrar.Rendering   unchanged

Registrar.Pipeline    NEW. References Core, Validation, Rendering.
                      BuildPipeline, BuildResult

Registrar.Cli         BuildCommand becomes a thin wrapper over BuildPipeline
```

`EngineContext` belongs in `Registrar.Validation`, not in `Pipeline`. It holds the loaded
schema, which is a Validation type, and `ValidationService` consumes it, so Validation
cannot reach up to Pipeline for it.

## The draft model

```
AgrDraft
  ArtifactType?     string
  DocumentId?       string
  Client?           string
  Engagement?       string
  Mode              string, defaults to draft
  Sections          IReadOnlyList<AgrSection>, reused from the AGR model
  PlaceholderMap    IReadOnlyDictionary<string, string?>
  Overrides?        AgrDraftOverrides

AgrDraftOverrides
  Version?  Owner?  PreparedBy?  ReviewDate?  ApprovalStatus?  PackageName?
```

`PlaceholderMap` is in the draft because the technician supplies those values and nothing
derives them.

`Overrides` is the escape hatch. Every field null, which is the normal case, means "use
what config derives." It exists so a deliberate departure from a derived value is
representable and visible, rather than requiring the derived fields to be editable in
the main model.

Everything else is computed on conversion and never stored: classification, layer,
audience, review date, package name, output filename, and the section titles themselves.

## Authoring

```
AgrAuthoring.Defaults(ArtifactTypeConfig type, IClock clock)
    -> the derived values, for the review screen

AgrAuthoring.ToDocument(AgrDraft draft, ConfigBundle config, IClock clock)
    -> AuthoringResult

AgrAuthoring.FromDocument(AgrDocument agr)
    -> AgrDraft
```

```
AuthoringResult
  Document  AgrDocument?                  null when incomplete
  Gaps      IReadOnlyList<AuthoringGap>   empty when complete

AuthoringGap
  JsonPath  "/context/client"
  Message   "Client legal name is required."
```

A gap is not a validation failure. It is a field a technician has not filled in yet, so
it deliberately does not reuse `Check`: that type carries `checkId`, `ruleSource`, and
`errorCategory`, none of which apply, and forcing it would blur a governance violation
into an unfinished form.

## The build pipeline

```
BuildPipeline.Run(
    string agrText,          serialized, for schema validation
    AgrDocument agr,         parsed, for rendering
    EngineContext engine,    config and schema, loaded once
    string? registryCsv,
    byte[] logoBytes,
    IClock clock)
  -> BuildResult

BuildResult
  Report          ValidationReport
  Payload         PromotionPayload?
  DocxBytes       byte[]?
  GenerationLog   GenerationLog?
  Manifest        ArtifactManifest?
  DocxEligible    bool
```

Both `agrText` and `agr` are passed because schema validation needs the text and
rendering needs the parsed document, and parsing twice would be wasteful and could
diverge.

The pipeline preserves `BuildCommand`'s existing ordering decisions, which are load
bearing and were reasoned about when they were written:

- Eligibility is decided before the report is written, so `artifactSha256` never
  attests to a document that was not produced.
- The generation log is produced for every run that reached rendering, whether or not
  a post-render check withholds the DOCX.
- A pre-render failure returns a report and nothing else.

Only one report is ever written per run today, either the pre-render one or the merged
one, so returning a single final result preserves behavior rather than changing it.

## Registry enumeration

`IIdRegistry` gains `IReadOnlyCollection<RegistryRecord> All()`. `CsvIdRegistry`
implements it from the dictionary it already holds. This is what lets the GUI show a
technician the Document IDs that Registry Owner has already issued for the chosen
artifact type, instead of asking them to type one.

It reads only. Rule 5 stands: the engine never mints an ID, and enumerating what the
registry already contains is not minting.

## Testing

Routine coverage: per-type defaulting the way GUI-1 did it, a gap for each missing field
carrying the correct `jsonPath`, `All()` returning every row, the `ValidationService`
overload producing reports identical to the path-based call, and `BuildCommandTests`
passing unchanged as the guard that the extraction was faithful.

Two tests carry most of the value:

**Serializer output must be schema-valid.** For every fixture: parse it, serialize it
back, and run schema validation on the result. If `AgrSerializer` emits anything the
schema rejects, the GUI can never build anything. Thirty-seven fixtures is broad coverage
of the shape space for almost no effort.

**Round trip must be lossless.** For every fixture: parse, `FromDocument` to a draft,
`ToDocument` back, and compare to the original. This is the test that catches the draft
model silently dropping a field, which is the specific failure mode of choosing a thin
draft over a full mirror. If it fails, the draft shape is wrong, and learning that here
is far cheaper than learning it in GUI-7.

Note the round trip cannot be byte-exact for a document whose stored values differ from
what config derives, because `ToDocument` recomputes derived fields. The test asserts
equality for fixtures whose values agree with config, and any fixture that legitimately
diverges must be represented through `Overrides` and still round trip. If a fixture
cannot round trip through `Overrides`, that is a finding about the draft model, not a
test to weaken.

## Out of scope

- `GovernorBrief` and `ImportParser`. Those are GUI-4.
- Any UI. `Registrar.App` does not exist until GUI-6.
- Changing `ConfigBundle.Hashes` or any validation rule.

## Note for GUI-4

`GovernorBrief` was specified in the parent design as deriving per-type facts from config
for a Governor agent. GUI-2 already built exactly that as `DrafterProjection` in
`Registrar.Core.Governor`. GUI-4 should reuse it rather than write a second derivation,
which was the original argument for putting GUI-2's logic in Core rather than in a
PowerShell script.
