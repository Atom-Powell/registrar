# Slice 9: v2 Acceptance Closeout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Meet the adopted v2 Definition of Done by closing coverage and documentation gaps - promotion-render proof for DS and AUP, a failure-path coverage matrix, a LifecycleTransitionError fixture, and honest release-gate documentation - with no engine behavior change.

**Architecture:** All changes are fixtures, tests, one config `$comment`, and documentation. New fixtures exercise existing checks through existing layers; new tests use the existing `BuildCommandTests` / `ValidateCommandTests` / `PlaceholderLayerTests` harnesses. No production C# is modified.

**Tech Stack:** .NET 8, C#, xUnit, Open XML SDK (`OpenXmlValidator`), System.Text.Json. Spec: `docs/superpowers/specs/2026-07-07-slice9-v2-acceptance-closeout-design.md`.

## Global Constraints

- **No production engine code changes.** Only fixtures, tests, one config `$comment`, and docs. If a new test reveals a behavior gap, stop and report it as a finding rather than changing engine code.
- **Reserved error categories stay unwired.** `RenderingError`, `PromotionPolicyError`, `DependencyResolutionError`, `SharePointIntegrationError` are documented as reserved-unreachable; their `Vocabulary.cs` constants are not pruned.
- **The `reconciledAgainstPdxXlsx` flag stays unwired.** It is documented as provenance-only; the runtime gate remains `PlaceholderLayer` mode-gating.
- **Output hygiene (rule 8):** ASCII only in fixtures, tests, config, and docs; no em/en dashes, smart quotes, or emojis. Inject any non-ASCII test input as `\uXXXX` escapes.
- **The full suite must stay green.** Current baseline is 219 passing (plus known pre-existing CS8602 test-code warnings, tech-debt item 4).
- **Fixed clock:** tests use `FixedClock.UtcNow = 2026-07-03 12:00:00Z`; review dates must be non-stale against it (GOV artifacts use a 6-month interval).

## Running tests

From repo root `C:\GitRepos\The-Registrar` (PowerShell 7):
- Focused: `dotnet test --filter "FullyQualifiedName~BuildCommandTests"`
- Full suite: `dotnet test`

## File Structure

- `fixtures/agr-sample-aup-promotion.json` - new; AUP promotion AGR (Task 1).
- `fixtures/agr-sample-ds-illegal-transition.json` - new; DS with an illegal approval-status transition (Task 2).
- `fixtures/registry-sample.csv` - add one AUP row (Task 1).
- `tests/Registrar.Tests/BuildCommandTests.cs` - add DS + AUP promotion build tests (Task 1).
- `tests/Registrar.Tests/ValidateCommandTests.cs` - add the lifecycle end-to-end test (Task 2).
- `tests/Registrar.Tests/PlaceholderLayerTests.cs` - add the unknown-token test (Task 4).
- `docs/failure-path-coverage.md` - new; the coverage matrix (Task 3).
- `config/placeholder-dictionary.json`, `README.md`, `CLAUDE.md` - release-gate documentation (Task 4).
- `docs/ROADMAP.md`, `docs/tech-debt.md` - status update (Task 5).

---

### Task 1: Promotion render coverage (DS + AUP)

**Files:**
- Create: `fixtures/agr-sample-aup-promotion.json`
- Modify: `fixtures/registry-sample.csv`
- Modify: `tests/Registrar.Tests/BuildCommandTests.cs`

**Interfaces:**
- Consumes: `BuildCommand.Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? mode = null, string? registryPath = null)`; the `FixedClock`, `RepoRoot()`, `CopyToTemp(string)` helpers already in `BuildCommandTests`.
- Produces: nothing downstream (test-only).

- [ ] **Step 1: Write the failing tests**

Add `using System.Text.Json.Nodes;` to the top of `tests/Registrar.Tests/BuildCommandTests.cs`, then add these two methods inside the class:

```csharp
    [Fact]
    public void Ds_promotion_build_renders_docx_and_emits_payload()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            "promotion", registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        using (var doc = WordprocessingDocument.Open(docx, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
            Assert.Empty(errors);
        }

        var payload = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "promotion-payload.json")))!.AsObject();
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void Aup_promotion_build_renders_docx_and_emits_payload()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-aup-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            "promotion", registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docxPath = Path.Combine(dir, "ORG - Acceptable Use Policy - Talewind CPA - v1.0.docx");
        Assert.True(File.Exists(docxPath));
        using (var doc = WordprocessingDocument.Open(docxPath, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
            Assert.Empty(errors);
        }

        var payload = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "promotion-payload.json")))!.AsObject();
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~BuildCommandTests"`
Expected: `Ds_promotion_build...` PASSES (its fixture and registry row already exist), `Aup_promotion_build...` FAILS - the AUP promotion fixture does not exist yet (`CopyToTemp` throws / exit code non-zero), and even once created the AUP id is not in the registry. (A DS pass here is fine; it is a characterization test proving DS promotion renders.)

- [ ] **Step 3: Create the AUP promotion fixture**

Copy `fixtures/agr-sample-aup-draft.json` to `fixtures/agr-sample-aup-promotion.json`, then apply exactly these three edits to the new file (mirroring the DS draft-to-promotion delta):

- `"approvalStatus": "Draft"` becomes `"approvalStatus": "In Review"`
- `"reviewDate": null` becomes `"reviewDate": "2026-12-01"`
- `"mode": "draft"` becomes `"mode": "promotion"`

Leave every other field unchanged (documentId `ORG-GOV-AUP-004`, all 9 sections, all 10 placeholderMap entries, `outputFileName` `"ORG - Acceptable Use Policy - Talewind CPA - v1.0.docx"`).

- [ ] **Step 4: Add the AUP registry row**

Append one line to `fixtures/registry-sample.csv` (columns are `documentId,artifactType,classification,approvalStatus,version,client`):

```
ORG-GOV-AUP-004,AUP,Client-Facing,Draft,v1.0,"Talewind CPA, LLC"
```

The `Draft` status makes the `Draft` to `In Review` transition legal (QA-SF-04), and `AUP`/`Client-Facing` match the AGR so QA-SF-03 passes.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~BuildCommandTests"`
Expected: PASS (both new tests plus the pre-existing ones).

- [ ] **Step 6: Full suite + commit**

Run: `dotnet test` -> all pass.

```bash
git add fixtures/agr-sample-aup-promotion.json fixtures/registry-sample.csv tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "test: DS and AUP promotion build coverage (finish-line 3.1)"
```

---

### Task 2: LifecycleTransitionError fixture + end-to-end test

**Files:**
- Create: `fixtures/agr-sample-ds-illegal-transition.json`
- Modify: `tests/Registrar.Tests/ValidateCommandTests.cs`

**Interfaces:**
- Consumes: `ValidateCommand.Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? registryPath = null)`; the `FixedClock`, `RepoRoot()`, `CopyToTemp(string)` helpers already in `ValidateCommandTests`; `System.Text.Json.Nodes.JsonNode` (already imported there).

- [ ] **Step 1: Write the failing test**

Add this method inside `ValidateCommandTests`:

```csharp
    [Fact]
    public void Illegal_transition_with_registry_fails_with_qa_sf_04_lifecycle()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-illegal-transition.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(1, code);
        var report = JsonNode.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json")))!.AsObject();
        var check = Assert.Single(report["checks"]!.AsArray(), c => (string?)c!["checkId"] == "QA-SF-04");
        Assert.Equal("fail", (string?)check!["result"]);
        Assert.Equal("blocking", (string?)check!["severity"]);
        Assert.Equal("LifecycleTransitionError", (string?)check!["errorCategory"]);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~ValidateCommandTests.Illegal_transition_with_registry_fails_with_qa_sf_04_lifecycle"`
Expected: FAIL - the fixture `agr-sample-ds-illegal-transition.json` does not exist yet.

- [ ] **Step 3: Create the fixture**

Copy `fixtures/agr-sample-ds-draft.json` to `fixtures/agr-sample-ds-illegal-transition.json`, then change one field in the new file:

- `"approvalStatus": "Draft"` becomes `"approvalStatus": "Approved"`

Leave everything else (documentId `ORG-GOV-DS-016`, `"mode": "draft"`, sections, placeholderMap) unchanged. The registry record for `ORG-GOV-DS-016` is `Draft`, whose only legal next states are `In Review` and `Retired`; the AGR target `Approved` is therefore an illegal transition and QA-SF-04 fails with `LifecycleTransitionError`.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~ValidateCommandTests"`
Expected: PASS (the new test plus the pre-existing ones).

Note: the test asserts the QA-SF-04 check is present; other checks may also appear (for example a QA-SF-03 metadata-agreement check) and that is acceptable - do not add assertions requiring QA-SF-04 to be the only failure.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test` -> all pass.

```bash
git add fixtures/agr-sample-ds-illegal-transition.json tests/Registrar.Tests/ValidateCommandTests.cs
git commit -m "test: LifecycleTransitionError fixture and end-to-end coverage (QA-SF-04)"
```

---

### Task 3: Failure-path coverage matrix

**Files:**
- Create: `docs/failure-path-coverage.md`

**Interfaces:** none (documentation).

- [ ] **Step 1: Create the coverage document**

Write `docs/failure-path-coverage.md` with exactly this content:

```markdown
# Failure-Path Coverage Matrix

Maps every `ErrorCategory` (defined in `src/Registrar.Core/Vocabulary.cs`) to whether the
engine emits it, the emitting check(s), and the covering fixture or test. This satisfies
the v2 acceptance requirement that every reachable failure is covered and every error
category with no reachable path is documented as such
(`docs/v2-acceptance-roadmap.md`, Phase 4).

## Reachable categories (emitted by a check)

| ErrorCategory | Emitter (checkId / layer) | Covering fixture or test |
|---|---|---|
| SchemaValidationError | QA-SCHEMA-01 / SchemaValidationLayer | `fixtures/agr-sample-ds-invalid-schema.json` |
| ClauseResolutionError | QA-ENG-04 / ClauseReferenceLayer, ClauseResolver | `fixtures/agr-sample-ds-missing-clause.json` |
| RegistrySyncError | QA-SF-03 / RegistryLayer, ValidationService; QA-SPEC-18 / PromotionLayer | `fixtures/agr-sample-ds-unregistered.json` |
| LifecycleTransitionError | QA-SF-04 / LifecycleLayer | `fixtures/agr-sample-ds-illegal-transition.json`; `LifecycleLayerTests.QA_SF_04_fails_on_illegal_transition` |
| ConfigurationError | QA-ENG-06 / BlockTypeSupportLayer; QA-ENG-02 / SectionSchemaLayer | `fixtures/agr-sample-ds-unsupported-block.json`; `fixtures/agr-sample-ds-section-mismatch.json` |
| MetadataViolation | QA-ENG-05, QA-ENG-03, QA-SPEC-07, QA-SF-05, QA-SF-07, QA-SF-08, QA-SF-09, QA-ENG-07 | `fixtures/agr-sample-ds-prohibited-char.json`, `-id-code-mismatch.json`, `-placeholder-drift.json`, `-review-stale.json`, `-token-residue.json`, `-empty-h1.json`, `-heading-skip.json` |

## Reserved but not emitted in v2

These constants exist in `Vocabulary.cs` as a stable error taxonomy but are not emitted by
any code path in v2. They are intentionally left wired-off (not pruned) so the vocabulary
stays aligned with the spec's error model.

| ErrorCategory | Why it is not emitted in v2 |
|---|---|
| RenderingError | Unsupported block types are rejected pre-render as ConfigurationError (QA-ENG-06), so the renderer never reaches a state that would emit a RenderingError. |
| PromotionPolicyError | Promotion blocking is a BuildCommand output-eligibility decision (comparing OverallResult and RequestedMode), not a Check. Promotion registry failures use RegistrySyncError (PromotionLayer). |
| DependencyResolutionError | AGR `dependencies` are not cross-resolved at runtime in the file-system-only v2. |
| SharePointIntegrationError | SharePoint / Graph integration is post-v2. |
```

- [ ] **Step 2: Commit**

```bash
git add docs/failure-path-coverage.md
git commit -m "docs: failure-path coverage matrix (Phase 4 acceptance)"
```

---

### Task 4: Release-gate documentation + unknown-token test

**Files:**
- Modify: `config/placeholder-dictionary.json:5`
- Modify: `README.md` (known gaps)
- Modify: `CLAUDE.md` (known gaps)
- Modify: `tests/Registrar.Tests/PlaceholderLayerTests.cs`

**Interfaces:**
- Consumes: `PlaceholderLayer.Validate(SemanticContext)`; `SemanticTestSupport.FixtureText`/`.Config()`; `Registrar.Core.Model.AgrParser.Parse`; `SemanticContext(agr, config, mode, registry)`.

- [ ] **Step 1: Write the failing test**

Add this method inside `PlaceholderLayerTests`:

```csharp
    [Fact]
    public void QA_SPEC_07_warns_on_unknown_token_in_draft_and_fails_in_promotion()
    {
        // A key that is neither a canonical token nor a known drift alias.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[DATE]\": \"2026-07-03\"", "\"[DATE]\": \"2026-07-03\", \"[ZZ NOT A REAL TOKEN]\": \"x\"");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);
        var config = SemanticTestSupport.Config();

        var draft = new PlaceholderLayer().Validate(new SemanticContext(agr, config, Mode.Draft, null));
        var promo = new PlaceholderLayer().Validate(new SemanticContext(agr, config, Mode.Promotion, null));

        Assert.Equal(CheckResultValue.Warn,
            Assert.Single(draft, c => c.Actual == "[ZZ NOT A REAL TOKEN]").Result);
        Assert.Equal(CheckResultValue.Fail,
            Assert.Single(promo, c => c.Actual == "[ZZ NOT A REAL TOKEN]").Result);
    }
```

- [ ] **Step 2: Run to verify pass**

Run: `dotnet test --filter "FullyQualifiedName~PlaceholderLayerTests"`
Expected: PASS. This is a characterization test pinning existing behavior (unknown token warns in draft, fails on promotion); it should pass immediately. If it FAILS, stop and report - the behavior differs from the documented release gate, which is a finding, not a fix to make here.

- [ ] **Step 3: Document the flag as provenance-only in config**

In `config/placeholder-dictionary.json`, add one sibling key immediately after the `reconciledAgainstPdxXlsx` line (line 5) so it reads:

```json
  "reconciledAgainstPdxXlsx": false,
  "reconciledAgainstPdxXlsxNote": "PROVENANCE-ONLY: not read by the engine. Records reconciliation status, not runtime behavior. The active release gate is PlaceholderLayer (QA-SPEC-07) mode-gating: unknown or unresolved tokens warn in Draft and fail (block) on Promotion, independent of this flag. Wiring the flag is post-v2 PDX-reconciliation work.",
```

- [ ] **Step 4: Reconcile the known-gaps wording**

In `README.md`, replace the placeholder known-gap bullet:

Old:
```
- `config/placeholder-dictionary.json` is an extracted subset; the canonical PDX
  workbook has not been reconciled. Unknown tokens warn in draft and fail on
  promotion until `reconciledAgainstPdxXlsx` is set to true.
```
New:
```
- `config/placeholder-dictionary.json` is an extracted subset; the canonical PDX
  workbook has not been reconciled. Unknown tokens warn in draft and fail on
  promotion today via `PlaceholderLayer` mode-gating; the `reconciledAgainstPdxXlsx`
  flag is provenance-only and not yet read by the engine (wiring it is post-v2).
```

In `CLAUDE.md`, replace the corresponding bullet:

Old:
```
- config/placeholder-dictionary.json is an extracted subset; the canonical
  PDX xlsx has not been reconciled. Unknown tokens warn in draft, fail on
  promotion, until reconciledAgainstPdxXlsx is flipped to true.
```
New:
```
- config/placeholder-dictionary.json is an extracted subset; the canonical
  PDX xlsx has not been reconciled. Unknown tokens warn in draft and fail on
  promotion via PlaceholderLayer (QA-SPEC-07) mode-gating; reconciledAgainstPdxXlsx
  is provenance-only and not read by the engine today (wiring is post-v2).
```

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test` -> all pass.

```bash
git add config/placeholder-dictionary.json README.md CLAUDE.md tests/Registrar.Tests/PlaceholderLayerTests.cs
git commit -m "docs: document reconciledAgainstPdxXlsx as provenance-only; pin unknown-token gate"
```

---

### Task 5: Status update and final verification

**Files:**
- Modify: `docs/ROADMAP.md`
- Modify: `docs/tech-debt.md`

- [ ] **Step 1: Mark v2 closeout delivered in the roadmap**

In `docs/ROADMAP.md`, add a Slice 9 row to the Delivered table:

```markdown
| 9 | v2 acceptance closeout | 2026-07-07 | Merged | DS/AUP promotion build tests, failure-path coverage matrix (docs/failure-path-coverage.md), LifecycleTransitionError fixture, and provenance-only documentation of the reconciledAgainstPdxXlsx release gate. Meets the adopted v2 Definition of Done. |
```

Then replace the body of the `## Next: v2 acceptance closeout` section with a short statement that the closeout is complete and the bounded v2 finish line is met, and that further work is the post-v2 backlog below. Keep the `## Post-v2 backlog` section unchanged.

- [ ] **Step 2: Note completion in tech-debt**

In `docs/tech-debt.md`, add a short dated note under an appropriate heading recording that the v2 acceptance closeout (Slice 9) added the promotion build tests, the failure-path coverage matrix, the lifecycle fixture, and the release-gate documentation, and that the four reserved-unreachable error categories and the inert `reconciledAgainstPdxXlsx` flag are intentionally left unwired for v2.

- [ ] **Step 3: Final full-suite verification**

Run: `dotnet test`
Expected: PASS - the pre-existing suite plus the four new tests (2 build, 1 validate, 1 placeholder). No new warnings beyond the known pre-existing CS8602 test-code warnings.

- [ ] **Step 4: Commit**

```bash
git add docs/ROADMAP.md docs/tech-debt.md
git commit -m "docs: record Slice 9 v2 acceptance closeout as delivered"
```

---

## Self-Review

**1. Spec coverage:**
- Promotion render coverage (spec gap 1) -> Task 1 (AUP fixture + registry row + DS/AUP build tests). Covered.
- Failure-path coverage matrix (spec gap 2) -> Task 3. Reserved categories documented. Covered.
- LifecycleTransitionError fixture (spec gap 3) -> Task 2. Covered.
- Release-gate documentation + unknown-token test (spec gap 4) -> Task 4. Covered.
- No production engine change (scope decision 1) -> every task touches only fixtures/tests/config-comment/docs. Covered.
- Reserved categories and flag stay unwired (scope decisions 2, 3) -> Task 3 documents categories; Task 4 documents the flag; neither adds emitters or reads the flag. Covered.
- Acceptance milestone / status -> Task 5. Covered.

**2. Placeholder scan:** No TBD/TODO. Every code step shows complete code; every fixture step gives exact edits; the matrix and doc edits give exact content. Task 5 Steps 1-2 describe the doc updates with the exact table row to add and the specific wording change, which is complete for a status-doc edit.

**3. Type consistency:** `BuildCommand.Run(agrPath, clock, stdout, stderr, mode, registryPath)` and `ValidateCommand.Run(agrPath, clock, stdout, stderr, registryPath)` match their existing signatures. `JsonNode.Parse(...).AsObject()`, `report["checks"]!.AsArray()`, and `(string?)check!["errorCategory"]` match the patterns already used in `ValidateCommandTests`. `Assert.Single(collection, predicate)` returns the matched element, matching existing usage in `PlaceholderLayerTests`. Registry CSV columns (`documentId,artifactType,classification,approvalStatus,version,client`) match `fixtures/registry-sample.csv`. The AUP DOCX filename and the `In Review` / `QA-SPEC-18` payload assertions match the fixture and the DS promotion validate test. Consistent.
