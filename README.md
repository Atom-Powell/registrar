# The Registrar

A governed artifact compiler for AI governance programs at managed service providers. The Registrar
consumes **Artifact Generation Requests (AGRs)** produced by the Governor and
emits controlled DOCX deliverables alongside machine-readable manifests and
validation reports. It is not a general-purpose document authoring tool; it is a
deterministic compiler that renders and enforces, and never makes a governance
decision.

## What it does

The Governor, a Microsoft 365 Copilot declarative agent running in the MSP
tenant, owns judgment: findings, risk classification, clause selection,
narrative content, and GO/NO-GO logic. It can only read knowledge and talk. It
emits an AGR describing the artifact to produce.

The Registrar owns rendering and enforcement. It re-validates every field, builds
the DOCX from scratch in code (no seed templates), inserts clause text by
reference from a versioned library, and fails closed on any controlled-document
violation. It never trusts the Governor.

An operator is the transport and the trigger: they save the AGR JSON from the
Governor chat, run the engine, review the validation report, and paste any
failures back into the Governor chat for regeneration. That human checkpoint is a
governance control, not friction to be automated away.

The operator window is a front end over the same engine as the CLI. It never
edits an AGR, and the operator's transport role above applies unchanged to both.

```
  Governor (Copilot agent)         Operator            registrar (this engine)
  judgment, narrative,   ── AGR ──▶  save,    ── CLI ──▶  re-validate every field,
  clause selection            JSON   run,               render DOCX from config,
                                     review    ◀── report ── fail closed on violation
        ▲                                              │
        └────────── paste failures for regeneration ◀──┘
```

## Design principles

These are binding. Changing any of them is an ADR-and-owner-review decision, not
a refactor.

1. **Fail closed.** A controlled-document violation (missing or invalid Document
   Control Header field, unregistered ID, illegal lifecycle transition, clause
   paraphrase) never produces a deliverable. There is no partial output.
2. **Zero LLM calls at runtime.** Determinism is a governance property the MSP
   sells to clients. Nothing on the generation path calls a model.
3. **Configuration over code.** Artifact structure, vocabularies, clauses,
   placeholders, style rules, and QA checks live in `/config` and `/schemas` as
   versioned data. Core logic carries no artifact constants, clause text, or
   repository paths. A new template pack ships as a config release.
4. **Clause insertion is by reference only.** The AGR carries a `clauseId`; the
   engine inserts canonical text from `config/clause-library.json` and proves it
   with SHA-256. Governor-supplied clause prose is ignored by design, so
   paraphrase drift is structurally impossible.
5. **The ID Registry is authoritative state.** The engine verifies the Document
   ID exists in the registry in the expected status before rendering and refuses
   promotion on any metadata/registry disagreement. It never mints IDs;
   Registry Owner owns the ID Registry.
6. **Do not invent structure.** Artifact types whose `sectionSchema.status` is
   `not-defined-in-spec` refuse generation with a `ConfigurationError` until a
   section schema is authored and approved.
7. **Validation reports serve two readers**, the operator and the Governor.
   Every failure carries `checkId`, `jsonPath`, expected/actual, a remediation
   phrased as an instruction the Governor can execute, and a `ruleSource`.
8. **Output hygiene.** US English; no emojis; no em/en dashes or decorative
   unicode in generated documents (enforced by QA-SF-08).

## The AGR contract

The AGR (`schemas/agr.schema.json`, draft 2020-12) is the interface between the
Governor and the engine. It is deliberately strict (closed enums, no
additional properties, exact patterns) so Governor drift surfaces as a
`SchemaValidationError` rather than silent acceptance. A request carries:

- `artifactIdentity`: type, Document ID, version, classification, approval
  status, and layer.
- `context`: owner, audience, prepared-by, review date and any override.
- `sourceWorkflowContext`: the Governor session that produced the request.
- `dataInputs`: the placeholder map plus the ordered sections and content
  blocks to render (paragraphs, tables, radio questions, clause insertions,
  callouts, and more).
- `renderingOptions` and `validationPolicy`: package/output naming, styling
  profile pin, and the `draft` vs `promotion` mode.

**Schema-valid does not mean generation-valid.** After schema validation passes,
the engine's semantic layers enforce registry sync, lifecycle transition
legality, clause hashes, and placeholder resolution.

## Validation modes

- **draft**: style deviations and unresolved client-completion placeholders
  downgrade to warnings; controlled-document violations still fail closed.
- **promotion**: everything blocks, and a `promotion-payload.json` is emitted.

There is no mode in which a missing or invalid Document Control Header produces
output.

## Output package (per run)

The v2 output package is five files. Nothing in it remains deferred; the last
two landed on 2026-07-15.

1. Generated DOCX, only on pass, or pass-with-warnings in draft mode.
2. `validation-report.json`, conforming to
   `schemas/validation-report.schema.json`; emitted on every run.
3. `promotion-payload.json`, the registry update payload and repository target,
   conforming to `schemas/promotion-payload.schema.json`, emitted only when
   validation passes in promotion mode.
4. `generation-log.json`, the config versions loaded, rules applied, clause
   insertions and timing; emitted whenever rendering was attempted.
5. `artifact-manifest.json`, the resolved metadata, section inventory,
   dependency map, registry references and artifact SHA-256; emitted only when
   a DOCX is produced.

## Running The Registrar

For everyday use, run the window: `Registrar.App.exe`, launched by
`RegistrarApp.cmd`. It is one page with no navigation, and it does what the CLI
does. Step 1, choose your working folder. Step 2, optionally choose an ID registry
file (a draft skips the registry check without one; a promotion build fails
without one). Step 3, pick the AGR `.json` file to check, or use **Add a file...**.
Then press **Check** to validate only, or **Build** to validate and render the
DOCX if it passes. The result shows PASS, PASS WITH WARNINGS or FAIL, then one
block per failing or warning check. After a run, **Open document**, **Open
report**, **Open folder** and **Copy failures for the Governor** appear when they
apply. The **Health check** button at the bottom runs the same self-check as
`registrar doctor`. The window never edits an AGR; the mode is whatever the AGR
declares, and a line under the list shows it (or says "Mode unknown. Press Check and
the report will say what is wrong with this file." if it cannot be read). `docs/first-artifact-walkthrough.md` is the start-to-finish path for
someone who has never opened it.

The CLI is retained in full as the fallback and as the scripting surface. It is
not a reduced version of the window; every verb below still works. Double-click
`Registrar.cmd`, or run `registrar menu` from a terminal, to get the interactive
menu: pick an AGR from your working folder, choose validate or build, and read
the result. Failing checks can be copied to the clipboard formatted for the
Governor chat; a produced document can be opened directly.

The packaged release is self-contained: it needs no .NET installation. Both
executables and their launchers live in a `runtime/` folder; keep the `config/`,
`schemas/`, and `assets/` folders as its siblings, one level up (they are the
versioned template pack, editable without a rebuild). The engine locates them by
walking up from `registrar.exe`, so the folder shape must be preserved. The
package ships an `OPERATOR.md` quickstart at its top level for the person running
it: the folder-shape rule, the everyday loop, and the promotion and signoff flow.

For scripting or CI, the direct verbs remain:

    registrar validate <agr.json> [--registry <registry.csv>]
    registrar build <agr.json> --mode draft|promotion [--registry <registry.csv>]
    registrar test --all

To build a release locally: `pwsh scripts/publish.ps1` (produces `dist/registrar.zip`).

## CLI

```
registrar validate <agr.json>                     schema + semantic validation only
registrar build    <agr.json> --mode draft|promotion   validate then render the package
registrar menu                                    interactive operator menu
registrar test     --fixture <name> | --all [--update]   run fixtures, diff outputs vs goldens
registrar doctor   [--registry <registry.csv>]    runtime self-diagnostics (config/schemas/clauses)
registrar emit-knowledge                          regenerate docs/governor/*.md from config
registrar emit-tokens                             regenerate the CSS design tokens from style-rules.json
```

`registrar doctor` checks the installation without running a build: config files load,
the three schemas parse, the placeholder dictionary covers every required placeholder,
and every clause's canonical text still matches its recorded SHA-256. Pass `--registry
<csv>` to also confirm an ID Registry export loads. Exit 0 means healthy; exit 1 means a
diagnostic failed. Clause-hash verification lives here rather than in a separate verb:
the `hash-clauses` verb named in `CLAUDE.md`'s build order was never implemented, and
`doctor` covers what it was for.

`emit-knowledge` and `emit-tokens` are generators, not operator verbs. Their output is
committed (`docs/governor/*.md` and the app's CSS custom properties) and guarded by
tests that byte-compare the committed files against a fresh generation, so config and
its downstream projections cannot drift apart silently.

`registrar test` runs each fixture through the full build pipeline under a frozen
clock and diffs the emitted `validation-report.json` and `promotion-payload.json`
against committed goldens in `fixtures/expected/<name>/`. The rendered DOCX is not
diffed (its bytes are not stable); DOCX stays under structural assertions in the
xUnit suite. `--update` regenerates the goldens after an intended behavior change;
review the diff before committing.

## Repo layout

```
/src        engine source. Core has no project references; Validation and
            Rendering are siblings above it; Pipeline composes both; Cli and
            App (a net8.0-windows WPF host with a Blazor surface) sit on top
/schemas    agr.schema.json, validation-report.schema.json,
            promotion-payload.schema.json
/config     vocabularies, artifact-types, clause-library,
            placeholder-dictionary, style-rules, qa-checklist
/fixtures   synthetic sample AGRs (valid + one per failure category)
/tests      xUnit; every QA check ID gets a passing and a failing test
/docs       reference specifications and ADRs
```

## Stack and environment

- .NET 8 / C# with the Open XML SDK (`DocumentFormat.OpenXml`). DOCX is built
  from scratch from config-defined structure: no seed `.docx`, no third-party
  template engine.
- Target OS Windows 11; dev shell PowerShell 7.x. The operator window is WPF
  hosting Blazor over WebView2, so it is Windows-only by construction; the
  headless projects are not.
- Delivered as a console app and a window via GitHub Releases, executed inside
  the MSP network. Client engagement data must not leave the tenant boundary, so
  execution never happens on GitHub-hosted runners; CI builds and tests against
  synthetic fixtures only.
- I/O is file-system only in v2.0: the engine reads an AGR from a local path
  (typically a OneDrive-synced SharePoint folder) and writes the output package
  next to it. SharePoint Graph API integration is a later phase behind an
  interface.

## Generation targets

Section schemas are authoritative for ten types: **DS** (Deployment Summary),
**AUP** (Acceptable Use Policy), **UQR** (User Quick Reference), **TCL**
(Training Checklist), **TAK** (Training Acknowledgment), **RAF** (Risk Acceptance
and Sign-off Form), **DCN** (Delivery Cover Note), **PIG** (Package Inventory and
Executive Guide), **SCL** (Shared Clause Library), and **PDX** (Placeholder
Dictionary). Every current generation target has one; the remaining artifact
types in `config/artifact-types.json` carry
`sectionSchema.status: "not-defined-in-spec"` and refuse generation with a
`ConfigurationError` until the documentation owner authors theirs (rule 6).

The Governor authors eight of the ten. SCL and PDX are reference documents
projected from config at render time (`aggregationTable`), so they have no
Governor-authored body; `governorScope` in `config/artifact-types.json` is what
marks the distinction.

All ten AGR block types render, the last two as of 2026-07-17. The Unsafe Block
Exclusion Rule therefore excludes nothing today, and `BlockRenderer`'s default
branch is a guard against an unknown or future type rather than a live gate.
Rendering DS and AUP end-to-end, in draft and promotion, was the v2 acceptance
milestone and is met.

## Known gaps

These are tracked deliberately and must not be silently resolved:

- The universal closing elements (Distribution and Follow-up; the
  prepared-by/companion-artifact line) are not rendered. They have no defined
  content in config, and rule 6 forbids inventing it.
- The ID Registry lives in a workbook until the SharePoint list migration; the
  registry adapter is swappable behind an interface.
- Document Control Header fields beyond approval status (classification, owner,
  review date) are not exposed as DOCX document or custom properties, so a
  SharePoint library cannot drive columns from them. Which fields should be is
  an owner decision, open since PR #26.
- `config/placeholder-dictionary.json` was meant to be reconciled against a
  canonical PDX workbook that was never located among the program source
  documents. On 2026-07-20 the repo owner declared the config file authoritative
  instead, after a repository-wide sweep verified its token set complete, and
  the `reconciledAgainstPdxXlsx` flag was retired rather than wired. Unknown
  tokens still warn in draft and fail on promotion via `PlaceholderLayer`
  mode-gating, which that ruling did not change.

Deferred non-blocking follow-ups from completed slices live in
`docs/tech-debt.md`. Remaining planned work lives in `docs/ROADMAP.md`, and
shipped work in `docs/DELIVERED.md`.

## Reference material

Architectural sources live in `/docs/reference`: the Generation Engine v2 system
specification (`Arti-Generator.docx`), the Master Template System Specification
and Usage Guide v1.2, and ADR-001 through ADR-003. `CLAUDE.md` is the
architectural constitution and governs any change to this engine.
