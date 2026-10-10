# Rename arti-gen to The Registrar

Date: 2026-07-06
Status: Approved (owner)

## Goal

Rename the tool formerly called "Generation Engine v2 (arti-gen)" to **The
Registrar** everywhere it is relevant, without disturbing two adjacent terms
that look similar but mean something else. This is a full, deep rename: product
name, CLI/exe identity, C# namespaces, project and directory names, the
solution file, schema `$id` URIs, and all docs (including dated historical
specs and plans).

## The two forms of the new name

- **Proper display name** -> `The Registrar`. Used in prose: README, CLAUDE.md,
  ROADMAP, tech-debt, CLI help text, design docs.
- **Code / path / CLI token** -> `Registrar` (PascalCase) and `registrar`
  (lowercase). Used in namespaces, folders, `.csproj`/`.sln`, the executable,
  the command token, and `$id` path segments.

## Mapping

| Old | New | Where |
|---|---|---|
| `namespace ArtiGen.Core` (and `.Cli`, `.Validation`, `.Rendering`, `ArtiGen.Tests`) | `Registrar.*` | all `.cs`, all `using` directives |
| `src\ArtiGen.Core\` etc., `tests\ArtiGen.Tests\` | `src\Registrar.Core\` etc., `tests\Registrar.Tests\` | folder renames via `git mv` |
| `ArtiGen.*.csproj`, `ArtiGen.sln` | `Registrar.*.csproj`, `Registrar.sln` | file renames + `.sln` project path/name entries (project GUIDs unchanged) |
| `AssemblyName arti-gen`, `arti-gen.exe`, `dist/arti-gen`, `arti-gen.zip` | `registrar`, `registrar.exe`, `dist/registrar`, `registrar.zip` | `.csproj`, `scripts/publish.ps1` |
| `Arti-Gen.cmd` | `Registrar.cmd` | launcher file + references in `publish.ps1`, README |
| `arti-gen validate/build/menu`, RootCommand `"arti-gen - governed artifact compiler"` | `registrar ...`, `"The Registrar - governed artifact compiler"` | `src\Registrar.Cli\Program.cs` |
| `https://ironwright.dev/schemas/arti-gen/...` | `https://ironwright.dev/schemas/registrar/...` | 3 schema `$id`s |
| `ArtiGen.Validation.CharacterPolicy` (in a config `$comment`) | `Registrar.Validation.CharacterPolicy` | `config/style-rules.json` |
| "Generation Engine v2 (arti-gen)", "`arti-gen` owns rendering...", etc. | "The Registrar" | README, CLAUDE.md, ROADMAP, tech-debt, historical specs/plans |
| CI build/test steps referencing `ArtiGen.sln` / paths | `Registrar.sln` / new paths | `.github/workflows/ci.yml` |

## Carve-outs (must NOT change)

A naive global find-replace would corrupt governance. Two terms are protected:

1. **`Arti-Generator`** (9 occurrences, including `Arti-Generator.docx`). This is
   the governing **specification document**, cited in `ruleSource` values and
   schema descriptions. Several are `const string` values asserted in tests and
   emitted verbatim in validation reports (engine rule 7: ruleSource cites the
   governing document). The replacement must use a negative-lookahead guard so
   `Arti-Gen`/`arti-gen` is only rewritten when NOT followed by `erator`:
   `Arti-Gen(?!erator)` and `arti-gen(?!erator)`. Known sites to leave intact:
   `README.md:187`, `config/vocabularies.json:50`, `config/qa-checklist.json`
   (both hits), `schemas/agr.schema.json:5` and `:135`,
   `src/*/SectionSchemaLayer.cs`, `src/*/BlockTypeSupportLayer.cs`, and the
   corresponding dated plan.

2. **`the Registrar` = Registry Owner** (the ID-minting entity; CLAUDE.md
   rule 5, line 41: "The engine never mints IDs; Registry Owner is the
   Registrar."). After the rename the tool is also "The Registrar", so this
   sentence would name two different things. Resolve the collision by rewording
   the one sentence to name the actor by function, e.g. "The engine never mints
   IDs; Registry Owner owns the ID Registry." This is the only judgment edit
   in the whole change; everything else is mechanical.

## Execution approach

Scripted mechanical replace plus a small number of hand-edited prose spots,
gated by build and test.

- A PowerShell pass rewrites file *contents* with the `(?!erator)` guard for the
  token forms, in this order of specificity to avoid double-rewrites:
  `ArtiGen` -> `Registrar`, then `Arti-Gen(?!erator)` -> `Registrar`, then
  `arti-gen(?!erator)` -> `registrar`.
- `git mv` renames the four `src` project folders, the `tests` folder, each
  `.csproj`, and `ArtiGen.sln`; then the `.sln` project path/name lines and each
  `.csproj` `ProjectReference` path are fixed. Project GUIDs are left unchanged.
- Hand-edited prose so the display name reads "The Registrar" (not "registrar")
  in: README H1 and intro, CLAUDE.md intro, the CLI RootCommand description,
  and the CLAUDE.md collision sentence.

Alternatives considered and rejected: (B) fully manual file-by-file editing --
safer-feeling but ~150 files invites inconsistency and typos; (C) IDE/Roslyn
rename -- not available in this headless environment.

## Order of operations

1. Config and schema strings (`$id`, style-rules `$comment`).
2. `.cs` namespaces and `using` directives (content replace).
3. `.csproj` contents (`AssemblyName`, `ProjectReference` paths) and `.sln`
   contents.
4. Folder and file renames via `git mv` (projects, tests, `.csproj`, `.sln`).
5. Launcher (`Arti-Gen.cmd` -> `Registrar.cmd`), `scripts/publish.ps1`,
   `scripts/normalize-hygiene.ps1`, `.github/workflows/ci.yml`.
6. Docs: living (README, CLAUDE.md, ROADMAP, tech-debt) and historical
   (`docs/superpowers/specs/*`, `docs/superpowers/plans/*`).
7. Collision reword (CLAUDE.md line 41 and any echo in README).

## Verification

Nothing is "done" until the build and test suite are green:

- `dotnet build Registrar.sln`
- `dotnet test Registrar.sln`

The existing suite is the safety net: `SourceHygieneTests` (source character
hygiene), the `ruleSource` assertions in `SectionSchemaLayerTests` /
`BlockTypeSupportLayerTests` (will fail if an `Arti-Generator` citation was
corrupted), `ReportSerializerTests` / `SchemaValidationLayerTests` (will fail if
a schema `$id` or namespace break slips through), and the full renderer/CLI
tests (will fail on any unresolved namespace). A green run across all of these
is the acceptance criterion. A final repo-wide grep confirms zero remaining
`ArtiGen`/`arti-gen` tokens except: the protected `Arti-Generator` sites, and
this design doc itself (which must name the old tool to describe the rename, the
same way an ADR references what it supersedes).

## Out of scope

- No behavior changes. This is identity-only; no logic, config semantics, or
  governance rules change.
- The repository directory is already named `The-Registrar`; no repo rename is
  part of this change.
- `EngineInfo.Version` and schema/report/payload version constants are not
  bumped by the rename itself; a version bump, if wanted, is a separate decision.
