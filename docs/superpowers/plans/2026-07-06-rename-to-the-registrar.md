# Rename arti-gen to The Registrar - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the tool from `arti-gen`/`ArtiGen` to **The Registrar** across product name, CLI/exe identity, C# namespaces, projects, solution, schema `$id` URIs, and all docs, without touching two protected terms.

**Architecture:** A scripted, case-sensitive token replace (guarded by a `(?!erator)` negative lookahead) does the mechanical work in path-scoped passes; a handful of prose spots are hand-edited to the proper display name "The Registrar". The existing build and test suite is the safety net after each code-affecting task.

**Tech Stack:** .NET 8 / C#, PowerShell 7.x (Windows 11), git. Spec: `docs/superpowers/specs/2026-07-06-rename-to-the-registrar-design.md`.

## Global Constraints

- **No behavior change.** Identity only. No logic, config semantics, or governance rules change.
- **Protected term 1 - `Arti-Generator`:** the governing spec document, cited in `ruleSource` const strings (asserted in tests, emitted in reports per engine rule 7) and schema descriptions. NEVER rewrite. All replaces use case-sensitive `-creplace` with a `(?!erator)` lookahead so `Arti-Gen`/`arti-gen` is rewritten only when NOT followed by `erator`.
- **Protected term 2 - `the Registrar` = Registry Owner:** the ID-minting entity. Reworded in exactly one CLAUDE.md sentence (Task 5); left as a distinct concept everywhere else.
- **Two forms of the new name:** prose display = `The Registrar`; code/path/CLI token = `Registrar` (PascalCase) / `registrar` (lowercase).
- **Output hygiene (engine rule 8 / QA-SF-08):** US English, no emojis, no em/en dashes or decorative unicode. Every hand-edited string uses plain ASCII (hyphen-minus `-` only).
- **Case-sensitive replaces only** (`-creplace`, not `-replace`) so `Registrar`/`registrar` casing is preserved.
- **Exclude the rename design doc** `docs/superpowers/specs/2026-07-06-rename-to-the-registrar-design.md` from all replaces and from the final grep audit: it intentionally names the old tool, like an ADR referencing what it supersedes.
- Platform: PowerShell 7.x on Windows. Use the Bash tool only where noted (grep audits); build/test/git via PowerShell.

## The three mechanical replacements (referenced by every content task)

Applied in this order, case-sensitive:

1. `ArtiGen` -> `Registrar`   (PascalCase, no hyphen; never matches `Arti-Generator`)
2. `Arti-Gen(?!erator)` -> `Registrar`   (Title-case hyphenated; skips `Arti-Generator`)
3. `arti-gen(?!erator)` -> `registrar`   (lowercase hyphenated; skips any `arti-generator`)

Reusable PowerShell scope function (each task calls it with its own file list):

```powershell
function Invoke-RegistrarRename {
  param([string[]]$Files)
  foreach ($f in $Files) {
    $c = Get-Content -LiteralPath $f -Raw
    $n = $c -creplace 'ArtiGen','Registrar' `
            -creplace 'Arti-Gen(?!erator)','Registrar' `
            -creplace 'arti-gen(?!erator)','registrar'
    if ($n -ne $c) {
      Set-Content -LiteralPath $f -Value $n -NoNewline
      Write-Host "renamed: $f"
    }
  }
}
```

---

## File Structure

Renames (folders/files, Task 3):
- `src/ArtiGen.Core/` -> `src/Registrar.Core/` (+ `ArtiGen.Core.csproj` -> `Registrar.Core.csproj`)
- `src/ArtiGen.Cli/` -> `src/Registrar.Cli/` (+ csproj)
- `src/ArtiGen.Validation/` -> `src/Registrar.Validation/` (+ csproj)
- `src/ArtiGen.Rendering/` -> `src/Registrar.Rendering/` (+ csproj)
- `tests/ArtiGen.Tests/` -> `tests/Registrar.Tests/` (+ csproj)
- `ArtiGen.sln` -> `Registrar.sln`
- `Arti-Gen.cmd` -> `Registrar.cmd`

Content-only edits: all `.cs`, `.csproj`, `.sln`, `schemas/*.json`, `config/*.json`, `scripts/*.ps1`, `.github/workflows/ci.yml`, `README.md`, `CLAUDE.md`, `docs/**`.

---

## Task 1: Schema and config string rename

Isolated, build-safe first step. The code still builds and tests run under the old solution name, so `dotnet test` here catches any `$id`/`$ref` breakage in isolation.

**Files:**
- Modify: `schemas/agr.schema.json` (`$id` line only; lines 5 and 135 are `Arti-Generator` - protected)
- Modify: `schemas/validation-report.schema.json` (`$id`)
- Modify: `schemas/promotion-payload.schema.json` (`$id`)
- Modify: `config/style-rules.json` (`$comment` type ref `ArtiGen.Validation.CharacterPolicy`)
- Untouched by guard: `config/vocabularies.json:50`, `config/qa-checklist.json` (both `Arti-Generator`)

**Interfaces:**
- Produces: `$id`s at `https://ironwright.dev/schemas/registrar/*.schema.json`; config comment referencing `Registrar.Validation.CharacterPolicy`.

- [ ] **Step 1: Apply the mechanical replace to schemas and config**

```powershell
$files = @(
  'schemas/agr.schema.json',
  'schemas/validation-report.schema.json',
  'schemas/promotion-payload.schema.json',
  'config/style-rules.json',
  'config/vocabularies.json',
  'config/qa-checklist.json'
)
Invoke-RegistrarRename -Files $files
```

Expected `renamed:` output for the 3 schema files and `config/style-rules.json`; the two `config` files with only `Arti-Generator` should NOT be reported (guard skipped them).

- [ ] **Step 2: Verify the protected term survived and the `$id`s changed**

Run (Bash tool):
```bash
rg -n 'Arti-Generator' schemas config          # expect: agr.schema.json:5, :135, vocabularies.json:50, qa-checklist.json (2)
rg -n 'schemas/registrar/' schemas              # expect: 3 $id lines
rg -niP 'arti-?gen(?!erator)' schemas config    # expect: NO output
```

- [ ] **Step 3: Verify JSON is still well-formed and tests pass**

```powershell
dotnet test ArtiGen.sln
```
Expected: PASS (all green). The schema-validation and report-serializer tests confirm the `$id` change did not break `$ref` resolution.

- [ ] **Step 4: Commit**

```powershell
git add schemas config
git commit -m @'
refactor: rename arti-gen to registrar in schema $id and config strings

Renames the three schema $id URIs to .../schemas/registrar/... and the
Registrar.Validation.CharacterPolicy type reference in style-rules. The
Arti-Generator spec-document citations are left intact.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 2: C# code identity rename (namespaces, projects, solution, files)

The atomic core: content replace across `src/`, `tests/`, and the solution, then the folder/file renames, then one prose hand-edit, gated by a green build and test run. The build is intentionally broken mid-task (between the content pass and the file renames); the gate is at the end.

**Files:**
- Modify (content): every `.cs` and `.csproj` under `src/` and `tests/`; `ArtiGen.sln`
- Modify (hand): `src/Registrar.Cli/Program.cs` RootCommand description (post-rename path, after Step 2)
- Rename: the five project folders, their `.csproj` files, and `ArtiGen.sln` (see File Structure)

**Interfaces:**
- Consumes: schema `$id`s from Task 1 (unchanged dependency).
- Produces: namespaces `Registrar.Core` / `Registrar.Cli` / `Registrar.Validation` / `Registrar.Rendering` / test namespace `Registrar.Tests`; `Registrar.sln`; assembly name `registrar` (the `AssemblyName arti-gen` token in the CLI csproj is rewritten to `registrar` by pass 3 of Step 1's content replace - no separate edit needed).

- [ ] **Step 1: Content replace across src, tests, and the solution**

```powershell
$codeFiles = @()
$codeFiles += Get-ChildItem -Path src,tests -Recurse -File -Include *.cs,*.csproj | ForEach-Object FullName
$codeFiles += (Resolve-Path 'ArtiGen.sln').Path
Invoke-RegistrarRename -Files $codeFiles
```

This rewrites `namespace`/`using` declarations, `AssemblyName arti-gen` -> `registrar` in the CLI csproj, `ProjectReference` paths, and the `.sln` project names/paths. (Files on disk are still named `ArtiGen.*` at this point - build is temporarily broken. Expected.)

- [ ] **Step 2: Rename folders and project files with git mv**

```powershell
git mv src/ArtiGen.Core        src/Registrar.Core
git mv src/Registrar.Core/ArtiGen.Core.csproj             src/Registrar.Core/Registrar.Core.csproj
git mv src/ArtiGen.Cli         src/Registrar.Cli
git mv src/Registrar.Cli/ArtiGen.Cli.csproj               src/Registrar.Cli/Registrar.Cli.csproj
git mv src/ArtiGen.Validation  src/Registrar.Validation
git mv src/Registrar.Validation/ArtiGen.Validation.csproj src/Registrar.Validation/Registrar.Validation.csproj
git mv src/ArtiGen.Rendering   src/Registrar.Rendering
git mv src/Registrar.Rendering/ArtiGen.Rendering.csproj   src/Registrar.Rendering/Registrar.Rendering.csproj
git mv tests/ArtiGen.Tests     tests/Registrar.Tests
git mv tests/Registrar.Tests/ArtiGen.Tests.csproj         tests/Registrar.Tests/Registrar.Tests.csproj
git mv ArtiGen.sln             Registrar.sln
```

- [ ] **Step 3: Hand-edit the CLI RootCommand description to the display name**

In `src/Registrar.Cli/Program.cs`, the mechanical pass turned the RootCommand text into `"registrar - governed artifact compiler"`. Change it to the proper display name:

```csharp
var root = new RootCommand("The Registrar - governed artifact compiler") { validate, build, menu };
```

(Leave the command *names* `validate`/`build`/`menu` and the exe token as-is; only this human-facing help string uses the display name.)

- [ ] **Step 4: Build**

```powershell
dotnet build Registrar.sln
```
Expected: succeeds, 0 errors. (Any unresolved namespace or stale path fails here.)

- [ ] **Step 5: Test**

```powershell
dotnet test Registrar.sln
```
Expected: PASS (all green), including `SourceHygieneTests` and the `ruleSource` assertions in `SectionSchemaLayerTests` / `BlockTypeSupportLayerTests` (proving no `Arti-Generator` citation was corrupted).

- [ ] **Step 6: Verify no tool tokens remain under src/tests (Bash tool)**

```bash
rg -niP 'arti-?gen(?!erator)' src tests   # expect: NO output
rg -n 'ArtiGen' src tests                 # expect: NO output
rg -n 'Arti-Generator' src tests          # expect: SectionSchemaLayer.cs, BlockTypeSupportLayer.cs (protected)
```

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m @'
refactor: rename ArtiGen.* namespaces, projects, and solution to Registrar

Renames all C# namespaces, project folders and files, and the solution
from ArtiGen.* to Registrar.*, and the CLI assembly from arti-gen to
registrar. RootCommand help text uses the display name "The Registrar".
Arti-Generator ruleSource citations are preserved.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 3: Launcher, publish/scripts, and CI

Renames the operator-facing launcher and the build/release plumbing so the exe, zip, dist path, and CI solution reference all read `registrar` / `Registrar.sln`.

**Files:**
- Rename: `Arti-Gen.cmd` -> `Registrar.cmd`
- Modify: `scripts/publish.ps1`, `scripts/normalize-hygiene.ps1`, `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `Registrar.sln`, `src/Registrar.Cli/Registrar.Cli.csproj`, exe `registrar.exe` (from Tasks 1-2).
- Produces: launcher `Registrar.cmd` running `registrar.exe menu`; `dist/registrar/` + `dist/registrar.zip`; CI building `Registrar.sln`.

- [ ] **Step 1: Rename the launcher**

```powershell
git mv Arti-Gen.cmd Registrar.cmd
```

- [ ] **Step 2: Content replace across scripts, the launcher, and CI**

```powershell
$plumbing = @(
  'Registrar.cmd',
  'scripts/publish.ps1',
  'scripts/normalize-hygiene.ps1',
  '.github/workflows/ci.yml'
)
Invoke-RegistrarRename -Files $plumbing
```

This turns `arti-gen.exe` -> `registrar.exe`, `dist/arti-gen` -> `dist/registrar`, `arti-gen.zip` -> `registrar.zip`, the `Arti-Gen.cmd` reference in `publish.ps1` -> `Registrar.cmd`, and any `ArtiGen.sln` -> `Registrar.sln` in CI.

- [ ] **Step 3: Verify the plumbing references are consistent (Bash tool)**

```bash
rg -niP 'arti-?gen(?!erator)' scripts .github Registrar.cmd   # expect: NO output
rg -n 'Registrar.cmd|registrar.exe|dist/registrar|Registrar.sln' scripts .github Registrar.cmd
```
The second command should show the launcher copy line in `publish.ps1`, the exe/dist/zip references, and the CI solution reference.

- [ ] **Step 4: Sanity-check the publish script parses**

```powershell
$null = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path scripts/publish.ps1), [ref]$null, [ref]$null); if ($?) { 'publish.ps1 parses' }
```
Expected: `publish.ps1 parses`. (Full `dotnet publish` is not run here; it is exercised at release time.)

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m @'
refactor: rename launcher, publish/scripts, and CI to registrar

Arti-Gen.cmd -> Registrar.cmd; publish emits dist/registrar and
registrar.zip and launches registrar.exe; CI builds Registrar.sln.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 4: Documentation rename (living and historical)

Renames all prose to the display name "The Registrar" for tool references and to token forms where a path/command/type is quoted, across README, ROADMAP, tech-debt, and every dated spec/plan - excluding this rename's own design doc.

**Files:**
- Modify: `README.md`, `docs/ROADMAP.md`, `docs/tech-debt.md`, `docs/smoke-test-quickstart.md`, all `docs/superpowers/specs/*.md` and `docs/superpowers/plans/*.md`
- Exclude: `docs/superpowers/specs/2026-07-06-rename-to-the-registrar-design.md` and `docs/superpowers/plans/2026-07-06-rename-to-the-registrar.md` (this plan)
- (CLAUDE.md is handled in Task 5 together with the collision reword.)

**Interfaces:**
- Produces: docs referring to the tool as "The Registrar" (prose) and `registrar`/`Registrar` (commands, paths, namespaces in code fences).

- [ ] **Step 1: Content replace across docs, excluding the two rename docs**

```powershell
$docs = @()
$docs += 'README.md','docs/ROADMAP.md','docs/tech-debt.md','docs/smoke-test-quickstart.md'
$docs += Get-ChildItem docs/superpowers/specs, docs/superpowers/plans -File -Filter *.md |
  Where-Object { $_.Name -ne '2026-07-06-rename-to-the-registrar-design.md' -and $_.Name -ne '2026-07-06-rename-to-the-registrar.md' } |
  ForEach-Object FullName
Invoke-RegistrarRename -Files $docs
```

- [ ] **Step 2: Hand-edit the display name in the living prose**

The mechanical pass produced lowercase `registrar` where prose should read "The Registrar". Fix the headline/intro references (leave code fences, commands like `registrar validate`, paths, and `Registrar.*` namespaces as the token form):

In `README.md`, line 1 heading and the intro sentences:
```markdown
# The Registrar

A governed artifact compiler for the ORG AI Governance Program. The Registrar
consumes **Artifact Generation Requests (AGRs)** produced by the Governor and
...
```
And later: `` `arti-gen` owns rendering `` became `` `registrar` owns rendering ``; change that sentence to `The Registrar owns rendering and enforcement.` (plain prose, no backticks). Scan the rest of README for any remaining `` `registrar` `` used as a *noun for the product* (not a command) and change those to "The Registrar"; keep `registrar validate`/`registrar build`/`registrar.exe` in command examples as tokens.

Do the same headline pass in `docs/ROADMAP.md` and `docs/smoke-test-quickstart.md` if they open with "Generation Engine v2 (registrar)" or narrate the product by the bare token.

- [ ] **Step 3: Verify no unprotected tool tokens remain in docs (Bash tool)**

```bash
rg -niP 'arti-?gen(?!erator)' docs README.md \
  -g '!docs/superpowers/specs/2026-07-06-rename-to-the-registrar-design.md' \
  -g '!docs/superpowers/plans/2026-07-06-rename-to-the-registrar.md'
# expect: NO output
rg -n 'Arti-Generator' docs README.md   # expect: only genuine spec-doc citations (e.g. Arti-Generator.docx in README:187)
```

- [ ] **Step 4: Commit**

```powershell
git add -A
git commit -m @'
docs: rename arti-gen to The Registrar across README and docs

Living and historical docs now name the tool "The Registrar" in prose and
registrar/Registrar in commands, paths, and namespaces. The rename design
doc and plan are left referencing the old name by design. Arti-Generator
spec citations are preserved.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

## Task 5: CLAUDE.md rename plus the collision reword, and final audit

CLAUDE.md gets the mechanical rename AND the one judgment edit that resolves the "Registrar" collision, followed by a whole-repo acceptance audit.

**Files:**
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: everything from Tasks 1-4.
- Produces: a repo with zero unprotected `arti-gen`/`ArtiGen` tokens; the ID-minting entity named unambiguously.

- [ ] **Step 1: Mechanical replace on CLAUDE.md**

```powershell
Invoke-RegistrarRename -Files @('CLAUDE.md')
```
This renames the title `# Generation Engine v2 (Arti-Gen)` to `# Generation Engine v2 (Registrar)` and other tool references; the next step fixes prose and the collision.

- [ ] **Step 2: Hand-edit the title and the collision sentence**

Change the top title to the display name:
```markdown
# The Registrar (Generation Engine v2)
```

Then find the rule 5 sentence (was line ~41): `The engine never mints IDs; Registry Owner is the Registrar.` Reword to name the actor by function so "Registrar" is not overloaded:
```markdown
   the ID Registry in the expected status before rendering and refuses
   promotion on any metadata/registry disagreement. The engine never mints
   IDs; Registry Owner owns the ID Registry.
```
Scan the rest of CLAUDE.md for any other place the bare word "Registrar" now ambiguously means the entity vs. the tool; where it means the entity, use "Registry Owner" or "the ID Registry". (The tool is referred to as "The Registrar" or "the engine".)

- [ ] **Step 3: Final whole-repo acceptance audit (Bash tool)**

```bash
# No unprotected tool tokens anywhere except the two rename docs:
rg -niP 'arti-?gen(?!erator)' \
  -g '!docs/superpowers/specs/2026-07-06-rename-to-the-registrar-design.md' \
  -g '!docs/superpowers/plans/2026-07-06-rename-to-the-registrar.md'
# expect: NO output

# ArtiGen (no hyphen) fully gone:
rg -n 'ArtiGen' -g '!docs/superpowers/**/2026-07-06-rename-to-the-registrar*'
# expect: NO output

# Protected citations still present:
rg -c 'Arti-Generator'
# expect: matches in README.md, config/vocabularies.json, config/qa-checklist.json,
#         schemas/agr.schema.json, src/Registrar.Validation/SectionSchemaLayer.cs,
#         src/Registrar.Validation/BlockTypeSupportLayer.cs (and the dated plan)
```

- [ ] **Step 4: Final build and test**

```powershell
dotnet build Registrar.sln
dotnet test Registrar.sln
```
Expected: build succeeds, all tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m @'
docs: rename CLAUDE.md to The Registrar and resolve the Registrar collision

Renames tool references in the architectural constitution and rewords the
ID-minting sentence so "Registrar" no longer names both the engine and the
Registry Owner role (which now reads "owns the ID Registry").

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
'@
```

---

## Verification summary (acceptance criteria)

- `dotnet build Registrar.sln` succeeds; `dotnet test Registrar.sln` all green (final run in Task 5).
- `rg -niP 'arti-?gen(?!erator)'` returns nothing outside the two rename docs.
- `rg -n 'ArtiGen'` returns nothing outside the two rename docs.
- `Arti-Generator` citations remain intact in the six+ known sites.
- The exe is `registrar.exe`, launched by `Registrar.cmd`; solution is `Registrar.sln`.
- CLAUDE.md names the ID-minting entity without overloading "Registrar".
