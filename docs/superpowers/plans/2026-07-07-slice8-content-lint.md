# Slice 8: Content Lint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the deferred content-lint enforcement - heading structure (QA-SF-07), the client-facing warn-callout promotion gate (QA-SF-09), body-prose token residue (QA-ENG-07), and a QA-ENG-06 supported-set drift guard.

**Architecture:** Three new pre-render `ISemanticLayer` implementations reading the validated AGR model, one checkId each, registered in `ValidationService`'s layer list next to the other content layers. Body prose is rendered verbatim (only `clauseInsertion` substitutes placeholders), so the AGR source text equals the rendered output for every block these layers inspect - the checks run pre-render with real JSON pointers and no `ArtifactRenderer`/`BuildCommand` change. The fourth deliverable is a test-only drift guard tying `BlockTypeSupportLayer`'s supported set to `BlockRenderer`'s actual capability.

**Tech Stack:** .NET 8, C#, xUnit, System.Text.Json, System.Text.RegularExpressions. Design: `docs/superpowers/specs/2026-07-07-slice8-content-lint-design.md`.

## Global Constraints

- **Fail closed** (rule 1): a controlled-document violation never yields a deliverable; new blocking checks flow through the existing `OverallResultPolicy` -> `PostRenderMerge` -> `BuildCommand` gates unchanged.
- **Configuration over code** (rule 3): the token-residue pattern is config data in `config/style-rules.json`, never a hardcoded constant in a layer.
- **No magic strings**: all `Check` field values use the `Registrar.Core.Vocabulary` constants (`Layer`, `CheckResultValue`, `Severity`, `ErrorCategory`, `Mode`).
- **Never trust the Governor**: QA-SF-09 reads the artifact type's *configured* classification (`config/artifact-types.json`), not the AGR's self-declared `artifactIdentity.classification`.
- **Output hygiene** (rule 8): US English; no emojis, em/en dashes, or decorative unicode in config strings, source, or test data. Inject any non-ASCII test input as `\uXXXX` escapes (enforced by `SourceHygieneTests`).
- **One checkId per layer**, matching the existing convention (`InputHygieneLayer` = QA-ENG-05, `BlockTypeSupportLayer` = QA-ENG-06).

## Running tests on this machine

- Dev shell is PowerShell 7 on Windows 11. Run from repo root `C:\GitRepos\The-Registrar`.
- Full suite: `dotnet test`
- Focused: `dotnet test --filter "FullyQualifiedName~HeadingStructureLayerTests"`
- The current baseline is green (0 errors; six pre-existing CS8602 nullable warnings in test code, tech-debt item 4 - do not treat those as regressions).

## File Structure

- `src/Registrar.Core/Config/ConfigModels.cs` - add `Classification` to `ArtifactTypeConfig`.
- `src/Registrar.Core/Config/ContentLint.cs` - add nullable `TokenResidue` + a `TokenResidueConfig` record.
- `config/style-rules.json` - add `contentLint.tokenResidue.pattern`.
- `config/qa-checklist.json` - add the QA-ENG-07 catalog entry.
- `src/Registrar.Validation/HeadingStructureLayer.cs` - new, QA-SF-07.
- `src/Registrar.Validation/WarnCalloutLayer.cs` - new, QA-SF-09.
- `src/Registrar.Validation/TokenResidueLayer.cs` - new, QA-ENG-07.
- `src/Registrar.Validation/ValidationService.cs` - register the three layers.
- `src/Registrar.Validation/BlockTypeSupportLayer.cs` - expose `SupportedBlockTypes` publicly for the drift guard.
- `fixtures/agr-sample-ds-empty-h1.json`, `agr-sample-ds-heading-skip.json`, `agr-sample-ds-token-residue.json` - new single-defect fixtures.
- Tests: `tests/Registrar.Tests/Config/Slice8ContentLintConfigTests.cs`, `HeadingStructureLayerTests.cs`, `WarnCalloutLayerTests.cs`, `TokenResidueLayerTests.cs`; additions to `QaChecklistCatalogTests.cs` and `Rendering/BlockRendererTests.cs`.
- `docs/ROADMAP.md`, `docs/tech-debt.md`, `CLAUDE.md` - status updates (Task 6).

---

### Task 1: Config foundation (classification + token-residue pattern)

**Files:**
- Modify: `src/Registrar.Core/Config/ConfigModels.cs:8-19` (`ArtifactTypeConfig`)
- Modify: `src/Registrar.Core/Config/ContentLint.cs`
- Modify: `config/style-rules.json:61` (inside `contentLint`, after `approvedGlyphAllowlist`)
- Test: `tests/Registrar.Tests/Config/Slice8ContentLintConfigTests.cs` (create)

**Interfaces:**
- Produces: `ArtifactTypeConfig.Classification` (`string?`); `ContentLint.TokenResidue` (`TokenResidueConfig?`) with `TokenResidueConfig.Pattern` (`string`, required). Consumed by Tasks 3 and 4.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Config/Slice8ContentLintConfigTests.cs`:

```csharp
using Registrar.Tests;

namespace Registrar.Tests.Config;

public class Slice8ContentLintConfigTests
{
    [Fact]
    public void DS_artifact_type_carries_its_configured_classification()
    {
        var config = SemanticTestSupport.Config();
        Assert.True(config.ArtifactTypesByCode.TryGetValue("DS", out var ds));
        Assert.Equal("Client-Facing", ds!.Classification);
    }

    [Fact]
    public void ContentLint_exposes_a_non_empty_token_residue_pattern()
    {
        var config = SemanticTestSupport.Config();
        Assert.NotNull(config.ContentLint.TokenResidue);
        Assert.False(string.IsNullOrWhiteSpace(config.ContentLint.TokenResidue!.Pattern));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~Slice8ContentLintConfigTests"`
Expected: FAIL to compile - `ArtifactTypeConfig` has no `Classification`, `ContentLint` has no `TokenResidue`.

- [ ] **Step 3: Add the config model members**

In `src/Registrar.Core/Config/ConfigModels.cs`, add one property to `ArtifactTypeConfig` (after `AudienceDefault`):

```csharp
    public string? Classification { get; init; }
```

Replace the whole of `src/Registrar.Core/Config/ContentLint.cs` with:

```csharp
namespace Registrar.Core.Config;

public sealed record ContentLint
{
    public required IReadOnlyList<string> ApprovedGlyphAllowlist { get; init; }

    // Nullable, not required: ContentLint is constructed directly in some unit tests
    // (CharacterLintTests) that only need the allowlist. TokenResidueLayer (QA-ENG-07)
    // fails closed with a ConfigurationError check when this is absent.
    public TokenResidueConfig? TokenResidue { get; init; }
}

public sealed record TokenResidueConfig
{
    public required string Pattern { get; init; }
}
```

- [ ] **Step 4: Add the config data**

In `config/style-rules.json`, insert one new line immediately after the existing `approvedGlyphAllowlist` line (line 61) - do NOT alter that line (it keeps its escaped `"○"` value). The inserted line is:

```json
    "tokenResidue": { "pattern": "\\[[A-Z0-9][A-Z0-9 ,./()&'-]*\\]", "$comment": "QA-ENG-07: matches the bracketed upper-case placeholder-token shape (for example [CLIENT TENANT NAME / TENANT ID]) so lowercase editorial brackets are not flagged." },
```

So the region reads:

```json
    "approvedGlyphAllowlist": ["○"],
    "tokenResidue": { "pattern": "\\[[A-Z0-9][A-Z0-9 ,./()&'-]*\\]", "$comment": "QA-ENG-07: matches the bracketed upper-case placeholder-token shape (for example [CLIENT TENANT NAME / TENANT ID]) so lowercase editorial brackets are not flagged." },
    "prohibitedPatterns": [
```

Note: the pattern is pure ASCII (rule 8). `System.Text.Json` ignores JSON members with no matching record property, so the `$comment` and the existing `prohibitedCharacters`/`prohibitedPatterns`/`punctuation` blocks continue to deserialize cleanly into `ContentLint`.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~Slice8ContentLintConfigTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Core/Config/ConfigModels.cs src/Registrar.Core/Config/ContentLint.cs config/style-rules.json tests/Registrar.Tests/Config/Slice8ContentLintConfigTests.cs
git commit -m "feat: config foundation for Slice 8 (classification + token-residue pattern)"
```

---

### Task 2: HeadingStructureLayer (QA-SF-07)

**Files:**
- Create: `src/Registrar.Validation/HeadingStructureLayer.cs`
- Modify: `src/Registrar.Validation/ValidationService.cs:70-71` (register the layer)
- Create: `fixtures/agr-sample-ds-empty-h1.json`, `fixtures/agr-sample-ds-heading-skip.json`
- Test: `tests/Registrar.Tests/HeadingStructureLayerTests.cs`
- Modify: `tests/Registrar.Tests/QaChecklistCatalogTests.cs` (add one fact)

**Interfaces:**
- Consumes: `ISemanticLayer.Validate(SemanticContext)`; `SemanticContext(AgrDocument Agr, ConfigBundle Config, string Mode, IIdRegistry? Registry)`; `AgrSection.Title`, `AgrSection.Blocks`, `AgrBlock.BlockType`.
- Produces: `HeadingStructureLayer.CheckId` (`"QA-SF-07"`).

- [ ] **Step 1: Create the two fixtures**

`fixtures/agr-sample-ds-empty-h1.json` - copy `fixtures/agr-sample-ds-draft.json` and replace the `Purpose` section's blocks so it has a heading but no content block:

```json
      { "title": "Purpose", "descriptorLine": null, "blocks": [
        { "blockType": "h2", "text": "Overview" }
      ] },
```

`fixtures/agr-sample-ds-heading-skip.json` - copy `fixtures/agr-sample-ds-draft.json` and replace the `Purpose` section's blocks with a content paragraph followed by an `h3` that has no preceding `h2`:

```json
      { "title": "Purpose", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "This summary documents the approved pilot deployment posture for Microsoft Copilot Chat." },
        { "blockType": "h3", "text": "Detailed Posture" }
      ] },
```

Leave every other section unchanged in both files (they each keep a `paragraph`, so only the `Purpose` section carries the defect).

- [ ] **Step 2: Write the failing test**

Create `tests/Registrar.Tests/HeadingStructureLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class HeadingStructureLayerTests
{
    [Fact]
    public void QA_SF_07_passes_for_well_formed_body()
    {
        var checks = new HeadingStructureLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));
        Assert.Empty(checks);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_07_fails_on_empty_h1(string mode)
    {
        var checks = new HeadingStructureLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-empty-h1.json", mode));
        var check = Assert.Single(checks);
        Assert.Equal("QA-SF-07", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0", check.JsonPath);
    }

    [Fact]
    public void QA_SF_07_fails_on_heading_level_skip()
    {
        var checks = new HeadingStructureLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-heading-skip.json", Mode.Draft));
        var check = Assert.Single(checks);
        Assert.Equal("QA-SF-07", check.CheckId);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/1", check.JsonPath);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~HeadingStructureLayerTests"`
Expected: FAIL to compile - `HeadingStructureLayer` does not exist.

- [ ] **Step 4: Create the layer**

Create `src/Registrar.Validation/HeadingStructureLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

// QA-SF-07: heading structure of section bodies. A section is an implicit H1;
// h2/h3 blocks carry explicit sub-levels. Two rules: every H1 has body content
// (no-empty-h1) and no H1 -> H3 level skip (no-heading-level-skips). The
// section-schema-driven "subsection or table where required" clause of QA-SF-07
// is owned by QA-ENG-02 (SectionSchemaLayer) and is out of scope here.
public sealed class HeadingStructureLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-07";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 7";

    private static readonly HashSet<string> HeadingTypes = new(StringComparer.Ordinal) { "h2", "h3" };

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var sections = context.Agr.DataInputs.Sections;
        for (var s = 0; s < sections.Count; s++)
        {
            var section = sections[s];

            var hasContent = section.Blocks.Any(b => !HeadingTypes.Contains(b.BlockType));
            if (!hasContent) checks.Add(EmptyH1(section.Title, s));

            var sawH2 = false;
            for (var b = 0; b < section.Blocks.Count; b++)
            {
                var type = section.Blocks[b].BlockType;
                if (type == "h2") sawH2 = true;
                else if (type == "h3" && !sawH2) checks.Add(HeadingSkip(section.Title, s, b));
            }
        }
        return checks;
    }

    private static Check EmptyH1(string title, int s) => Build(
        $"Section '{title}' (an H1) has no body content.",
        $"/dataInputs/sections/{s}",
        "at least one content block (paragraph, bulletList, table, or clauseInsertion)",
        "heading-only or empty section",
        $"Add body content to section '{title}', or remove the section.");

    private static Check HeadingSkip(string title, int s, int b) => Build(
        $"Section '{title}' has an h3 with no preceding h2 (an H1 to H3 level skip).",
        $"/dataInputs/sections/{s}/blocks/{b}",
        "an h2 heading before this h3",
        "h3 without a preceding h2",
        $"Insert an h2 before the h3 in section '{title}', or promote the h3 to h2.");

    private static Check Build(string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
```

- [ ] **Step 5: Register the layer**

In `src/Registrar.Validation/ValidationService.cs`, change the mode-independent layer list (currently ending `new ClauseReferenceLayer()` at line 70) so it reads:

```csharp
                new ClauseReferenceLayer(),
                new HeadingStructureLayer()
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~HeadingStructureLayerTests"`
Expected: PASS (4 test cases).

- [ ] **Step 7: Add the catalog test**

In `tests/Registrar.Tests/QaChecklistCatalogTests.cs`, add one fact mirroring the existing per-layer facts:

```csharp
    [Fact]
    public void HeadingStructureLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;
        var ids = checklist["checks"]!.AsArray().Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(HeadingStructureLayer.CheckId, ids);
    }
```

(QA-SF-07 already exists in `config/qa-checklist.json:12`; no catalog edit is needed.)

- [ ] **Step 8: Run the catalog test and commit**

Run: `dotnet test --filter "FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS.

```bash
git add src/Registrar.Validation/HeadingStructureLayer.cs src/Registrar.Validation/ValidationService.cs fixtures/agr-sample-ds-empty-h1.json fixtures/agr-sample-ds-heading-skip.json tests/Registrar.Tests/HeadingStructureLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat: HeadingStructureLayer enforcing QA-SF-07 (no-empty-h1, no-heading-level-skips)"
```

---

### Task 3: WarnCalloutLayer (QA-SF-09)

**Files:**
- Create: `src/Registrar.Validation/WarnCalloutLayer.cs`
- Modify: `src/Registrar.Validation/ValidationService.cs` (register after `HeadingStructureLayer`)
- Test: `tests/Registrar.Tests/WarnCalloutLayerTests.cs`
- Modify: `tests/Registrar.Tests/QaChecklistCatalogTests.cs` (add one fact)
- Reuses fixture: `fixtures/agr-sample-ds-unsupported-block.json` (already contains a `warnCallout` block in a Client-Facing DS).

**Interfaces:**
- Consumes: `ArtifactTypeConfig.Classification` (Task 1); `context.Config.ArtifactTypesByCode`; `context.Mode`; `Mode.Promotion`.
- Produces: `WarnCalloutLayer.CheckId` (`"QA-SF-09"`).

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/WarnCalloutLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class WarnCalloutLayerTests
{
    [Fact]
    public void QA_SF_09_fails_on_warn_callout_in_client_facing_promotion()
    {
        var checks = new WarnCalloutLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-unsupported-block.json", Mode.Promotion));
        var check = Assert.Single(checks);
        Assert.Equal("QA-SF-09", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/1", check.JsonPath);
    }

    [Fact]
    public void QA_SF_09_passes_in_draft_even_with_warn_callout()
    {
        var checks = new WarnCalloutLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-unsupported-block.json", Mode.Draft));
        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_09_passes_when_no_warn_callout_at_promotion()
    {
        var checks = new WarnCalloutLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Promotion));
        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_09_passes_for_non_client_facing_type_at_promotion()
    {
        // PIG (Package Inventory and Executive Guide) is classified Internal Working Document.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-unsupported-block.json")
            .Replace("\"artifactType\": \"DS\"", "\"artifactType\": \"PIG\"");
        var context = new SemanticContext(
            AgrParser.Parse(text), SemanticTestSupport.Config(), Mode.Promotion, Registry: null);

        Assert.Empty(new WarnCalloutLayer().Validate(context));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~WarnCalloutLayerTests"`
Expected: FAIL to compile - `WarnCalloutLayer` does not exist.

- [ ] **Step 3: Create the layer**

Create `src/Registrar.Validation/WarnCalloutLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

// QA-SF-09: zero warn (red) callouts in client-facing output at promotion.
// Client-facing status is read from the artifact type's CONFIGURED classification
// (config/artifact-types.json), never the Governor-supplied artifactIdentity.classification.
// The "zero internal-only notes" half of the catalogued rule has no AGR data model
// and is deferred (see docs/tech-debt.md). This is the gate that remains once a
// future slice enables warnCallout rendering; today QA-ENG-06 also rejects the block.
public sealed class WarnCalloutLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-09";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 9";
    private const string ClientFacing = "Client-Facing";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        if (context.Mode != Mode.Promotion) return Array.Empty<Check>();

        var artifactType = context.Agr.Identity.ArtifactType;
        if (!context.Config.ArtifactTypesByCode.TryGetValue(artifactType, out var type)
            || type.Classification != ClientFacing)
            return Array.Empty<Check>();

        var checks = new List<Check>();
        var sections = context.Agr.DataInputs.Sections;
        for (var s = 0; s < sections.Count; s++)
        {
            var blocks = sections[s].Blocks;
            for (var b = 0; b < blocks.Count; b++)
                if (blocks[b].BlockType == "warnCallout")
                    checks.Add(Fail(sections[s].Title, s, b));
        }
        return checks;
    }

    private static Check Fail(string title, int s, int b) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = $"Section '{title}' contains a warn callout, which is prohibited in client-facing output at promotion.",
        JsonPath = $"/dataInputs/sections/{s}/blocks/{b}",
        Expected = "no warnCallout blocks in client-facing output at promotion",
        Actual = "warnCallout",
        Remediation = $"Remove the warn callout from section '{title}' before promotion, or move the note into an internal-only artifact.",
        RuleSource = RuleSourceValue
    };
}
```

- [ ] **Step 4: Register the layer**

In `src/Registrar.Validation/ValidationService.cs`, extend the list end:

```csharp
                new ClauseReferenceLayer(),
                new HeadingStructureLayer(),
                new WarnCalloutLayer()
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~WarnCalloutLayerTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Add the catalog test**

In `tests/Registrar.Tests/QaChecklistCatalogTests.cs`, add:

```csharp
    [Fact]
    public void WarnCalloutLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;
        var ids = checklist["checks"]!.AsArray().Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(WarnCalloutLayer.CheckId, ids);
    }
```

(QA-SF-09 already exists in `config/qa-checklist.json:14`.)

- [ ] **Step 7: Run the catalog test and commit**

Run: `dotnet test --filter "FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS.

```bash
git add src/Registrar.Validation/WarnCalloutLayer.cs src/Registrar.Validation/ValidationService.cs tests/Registrar.Tests/WarnCalloutLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat: WarnCalloutLayer enforcing QA-SF-09 (client-facing warn-callout promotion gate)"
```

---

### Task 4: TokenResidueLayer (QA-ENG-07)

**Files:**
- Modify: `config/qa-checklist.json:33` (add the QA-ENG-07 entry after QA-ENG-06)
- Create: `src/Registrar.Validation/TokenResidueLayer.cs`
- Modify: `src/Registrar.Validation/ValidationService.cs` (register after `WarnCalloutLayer`)
- Create: `fixtures/agr-sample-ds-token-residue.json`
- Test: `tests/Registrar.Tests/TokenResidueLayerTests.cs`
- Modify: `tests/Registrar.Tests/QaChecklistCatalogTests.cs` (add one fact + a raw-string presence fact)

**Interfaces:**
- Consumes: `ContentLint.TokenResidue.Pattern` (Task 1); `AgrBlock.BlockType/Text/Items/ColumnHeaders/Rows`.
- Produces: `TokenResidueLayer.CheckId` (`"QA-ENG-07"`).

- [ ] **Step 1: Add the catalog entry**

In `config/qa-checklist.json`, add a comma after the QA-ENG-06 object (line 33) and append the QA-ENG-07 entry as the new last element of the `checks` array:

```json
    { "checkId": "QA-ENG-07", "layer": "content", "automation": "full", "appliesTo": "all", "rule": "Body-prose blocks (paragraph, h2, h3, bulletList, table) contain no unresolved bracketed placeholder tokens. Body prose is rendered verbatim (only clauseInsertion resolves placeholders), so a residual bracketed token would ship literally. QA-SPEC-07 inspects only placeholderMap and QA-SF-08 only prohibited characters, so neither catches this. Blocks in draft and promotion.", "errorCategory": "MetadataViolation", "ruleSource": "ORG-MST-SPEC-001 Section 9 item 7" }
```

- [ ] **Step 2: Create the fixture**

`fixtures/agr-sample-ds-token-residue.json` - copy `fixtures/agr-sample-ds-draft.json` and replace the `Purpose` section's paragraph text so it carries an unresolved token in body prose:

```json
      { "title": "Purpose", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "This summary documents the approved pilot deployment posture for [CLIENT ORGANIZATION NAME]." }
      ] },
```

Leave every other section unchanged (no other body prose contains brackets, so exactly one finding results).

- [ ] **Step 3: Write the failing test**

Create `tests/Registrar.Tests/TokenResidueLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class TokenResidueLayerTests
{
    [Fact]
    public void QA_ENG_07_passes_on_clean_body_prose()
    {
        var checks = new TokenResidueLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));
        Assert.Empty(checks);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_ENG_07_fails_on_token_in_paragraph(string mode)
    {
        var checks = new TokenResidueLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-token-residue.json", mode));
        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-07", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/0/text", check.JsonPath);
        Assert.Equal("[CLIENT ORGANIZATION NAME]", check.Actual);
    }

    [Fact]
    public void QA_ENG_07_does_not_false_positive_on_clause_blocks()
    {
        // The Audit section (index 4) is paragraph + clauseInsertion. clauseInsertion
        // carries no AGR-level text (clause text is resolved from the library at render
        // and governed by ClauseResolver / QA-SF-11), and ScanFields omits it, so a
        // document containing clause blocks yields no QA-ENG-07 finding.
        var checks = new TokenResidueLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Promotion));
        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_07_fails_closed_when_pattern_is_not_configured()
    {
        var config = SemanticTestSupport.Config() with
        {
            ContentLint = new ContentLint
            {
                ApprovedGlyphAllowlist = SemanticTestSupport.Config().ContentLint.ApprovedGlyphAllowlist,
                TokenResidue = null
            }
        };
        var context = new SemanticContext(
            SemanticTestSupport.Agr("agr-sample-ds-draft.json"), config, Mode.Draft, Registry: null);

        var check = Assert.Single(new TokenResidueLayer().Validate(context));
        Assert.Equal("QA-ENG-07", check.CheckId);
        Assert.Equal(ErrorCategory.ConfigurationError, check.ErrorCategory);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~TokenResidueLayerTests"`
Expected: FAIL to compile - `TokenResidueLayer` does not exist.

- [ ] **Step 5: Create the layer**

Create `src/Registrar.Validation/TokenResidueLayer.cs`:

```csharp
using System.Text.RegularExpressions;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

// QA-ENG-07: unresolved bracketed placeholder tokens left in body prose. Body prose
// is rendered verbatim (only clauseInsertion substitutes placeholders), so a residual
// [TOKEN] ships literally. Scans non-clause blocks only; clauseInsertion is governed by
// ClauseResolver / QA-SF-11 and may legitimately retain placeholders. The token pattern
// is config data (config/style-rules.json contentLint.tokenResidue.pattern), not code.
public sealed class TokenResidueLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-07";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 9 item 7";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var pattern = context.Config.ContentLint.TokenResidue?.Pattern;
        if (string.IsNullOrEmpty(pattern)) return new[] { MissingPatternConfig() };

        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        var checks = new List<Check>();
        var sections = context.Agr.DataInputs.Sections;
        for (var s = 0; s < sections.Count; s++)
        {
            var blocks = sections[s].Blocks;
            for (var b = 0; b < blocks.Count; b++)
            {
                foreach (var (suffix, text) in ScanFields(blocks[b]))
                {
                    if (text is null) continue;
                    var match = regex.Match(text);
                    if (match.Success)
                        checks.Add(Fail(sections[s].Title, match.Value,
                            $"/dataInputs/sections/{s}/blocks/{b}{suffix}"));
                }
            }
        }
        return checks;
    }

    private static IEnumerable<(string Suffix, string? Text)> ScanFields(AgrBlock block)
    {
        switch (block.BlockType)
        {
            case "paragraph":
            case "h2":
            case "h3":
                yield return ("/text", block.Text);
                break;
            case "bulletList":
                if (block.Items is not null)
                    yield return ("/items", string.Join(" ", block.Items));
                break;
            case "table":
                if (block.ColumnHeaders is not null)
                    yield return ("/columnHeaders", string.Join(" ", block.ColumnHeaders));
                if (block.Rows is not null)
                    yield return ("/rows", string.Join(" ", block.Rows.SelectMany(r => r)));
                break;
            // clauseInsertion and any unsupported type: not scanned here.
        }
    }

    private static Check Fail(string title, string token, string jsonPath) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = $"Section '{title}' body prose contains an unresolved placeholder token '{token}'.",
        JsonPath = jsonPath,
        Expected = "no unresolved bracketed placeholder tokens in body prose",
        Actual = token,
        Remediation = $"Resolve '{token}' to its literal value in the body text of section '{title}'. "
            + "Body prose is not placeholder-substituted; only clause text resolves tokens at render time.",
        RuleSource = RuleSourceValue
    };

    private static Check MissingPatternConfig() => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ConfigurationError,
        Message = "The token-residue pattern is missing from config (contentLint.tokenResidue.pattern).",
        Expected = "contentLint.tokenResidue.pattern present in config/style-rules.json",
        Actual = "absent",
        Remediation = "Add contentLint.tokenResidue.pattern to config/style-rules.json.",
        RuleSource = RuleSourceValue
    };
}
```

- [ ] **Step 6: Register the layer**

In `src/Registrar.Validation/ValidationService.cs`, extend the list end:

```csharp
                new ClauseReferenceLayer(),
                new HeadingStructureLayer(),
                new WarnCalloutLayer(),
                new TokenResidueLayer()
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~TokenResidueLayerTests"`
Expected: PASS (5 test cases).

- [ ] **Step 8: Add the catalog tests**

In `tests/Registrar.Tests/QaChecklistCatalogTests.cs`, add:

```csharp
    [Fact]
    public void TokenResidueLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;
        var ids = checklist["checks"]!.AsArray().Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(TokenResidueLayer.CheckId, ids);
    }

    [Fact]
    public void Catalog_contains_QA_ENG_07()
    {
        var repoRoot = Registrar.Tests.SemanticTestSupport.RepoRoot();
        var text = File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json"));
        Assert.Contains("QA-ENG-07", text);
    }
```

- [ ] **Step 9: Run the catalog tests and commit**

Run: `dotnet test --filter "FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS.

```bash
git add config/qa-checklist.json src/Registrar.Validation/TokenResidueLayer.cs src/Registrar.Validation/ValidationService.cs fixtures/agr-sample-ds-token-residue.json tests/Registrar.Tests/TokenResidueLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat: TokenResidueLayer enforcing QA-ENG-07 (body-prose token residue)"
```

---

### Task 5: QA-ENG-06 supported-set self-verify (drift guard)

**Files:**
- Modify: `src/Registrar.Validation/BlockTypeSupportLayer.cs:16-19` (expose the set)
- Modify: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs` (add one test)

**Interfaces:**
- Consumes: `BlockTypeSupportLayer.SupportedBlockTypes` (now public); `BlockRenderer.Render(AgrBlock, int, int, RenderContext)`; the existing `Ctx()` helper in `BlockRendererTests`.
- Produces: nothing (test-only).

- [ ] **Step 1: Expose the supported set**

In `src/Registrar.Validation/BlockTypeSupportLayer.cs`, change the field declaration (line 16) from `private static readonly HashSet<string>` to a public read-only set (keep the initializer and the retained comment):

```csharp
    public static readonly IReadOnlySet<string> SupportedBlockTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "paragraph", "h2", "h3", "bulletList", "table", "clauseInsertion"
    };
```

`Validate` already calls `SupportedBlockTypes.Contains(...)`, which `IReadOnlySet<string>` provides - no other change in that file.

- [ ] **Step 2: Write the failing test**

In `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`, add `using Registrar.Validation;` to the usings and add this test method inside the class:

```csharp
    [Fact]
    public void QA_ENG_06_supported_set_matches_renderer_capability()
    {
        var samples = new Dictionary<string, Registrar.Core.Model.AgrBlock>(StringComparer.Ordinal)
        {
            ["paragraph"] = new() { BlockType = "paragraph", Text = "x" },
            ["h2"] = new() { BlockType = "h2", Text = "x" },
            ["h3"] = new() { BlockType = "h3", Text = "x" },
            ["bulletList"] = new() { BlockType = "bulletList", Items = new[] { "x" } },
            ["table"] = new() { BlockType = "table", ColumnHeaders = new[] { "H" },
                Rows = new IReadOnlyList<string>[] { new[] { "c" } } },
            ["clauseInsertion"] = new() { BlockType = "clauseInsertion", ClauseId = "CL-006" },
        };

        // The layer's declared set is exactly the set the test covers.
        Assert.Equal(
            BlockTypeSupportLayer.SupportedBlockTypes.OrderBy(x => x, StringComparer.Ordinal),
            samples.Keys.OrderBy(x => x, StringComparer.Ordinal));

        // Every declared-supported type renders without a NotSupportedException.
        foreach (var (type, block) in samples)
        {
            var ex = Record.Exception(() => BlockRenderer.Render(block, 4, 1, Ctx()));
            Assert.False(ex is NotSupportedException,
                $"'{type}' is declared supported but the renderer rejected it.");
        }

        // Every deferred type is excluded from the set and rejected by the renderer.
        foreach (var deferred in new[] { "warnCallout", "radioQuestion", "aggregationTable", "copyReadyMessage" })
        {
            Assert.DoesNotContain(deferred, BlockTypeSupportLayer.SupportedBlockTypes);
            Assert.Throws<NotSupportedException>(() =>
                BlockRenderer.Render(
                    new Registrar.Core.Model.AgrBlock { BlockType = deferred, Text = "x" }, 0, 0, Ctx()));
        }
    }
```

- [ ] **Step 3: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~BlockRendererTests"`
Expected: PASS (existing tests plus the new one). If it fails to compile before Step 1, that confirms the visibility change is required.

- [ ] **Step 4: Commit**

```bash
git add src/Registrar.Validation/BlockTypeSupportLayer.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs
git commit -m "test: QA-ENG-06 supported-set drift guard against BlockRenderer (tech-debt item 16)"
```

---

### Task 6: Documentation and full-suite verification

**Files:**
- Modify: `docs/ROADMAP.md` (move Slice 8 from Next to Delivered)
- Modify: `docs/tech-debt.md` (resolve items 6, 7, 16; record the internal-only-notes deferral)
- Modify: `CLAUDE.md` (update the Slice 8 known-gaps entry)

- [ ] **Step 1: Update the roadmap**

In `docs/ROADMAP.md`, add a row to the Delivered table:

```markdown
| 8 | Content lint | 2026-07-07 | Merged | HeadingStructureLayer (QA-SF-07 no-empty-h1 / no-heading-level-skips), WarnCalloutLayer (QA-SF-09 client-facing warn-callout promotion gate), TokenResidueLayer (QA-ENG-07 body-prose token residue), and a QA-ENG-06 supported-set drift guard. |
```

Replace the `## Next` section body: the deferred block types and heading-bearing bodies are now unblocked. Set it to the next backlog pull (remaining block types / generation targets), and update `Last updated` to `2026-07-07`.

- [ ] **Step 2: Update tech-debt**

In `docs/tech-debt.md`: mark items 6 (content-lint slice), 7 (QA-ENG-07), and 16 (QA-ENG-06 self-verify) as delivered by Slice 8. Add a short note under QA-SF-09 that internal-only-note detection remains deferred (no AGR data model), and that the `no-warn-callouts-in-client-facing` / `no-empty-h1` / `no-heading-level-skips` entries in `style-rules.json contentLint.prohibitedPatterns` are now enforced by the new layers (documentary duplicates of the catalogued checks).

- [ ] **Step 3: Update CLAUDE.md**

In `CLAUDE.md`, update the "Known gaps" entry that lists the deferred Slice 8 set (QA-SF-09, no-empty-h1, no-heading-level-skips, QA-ENG-07) to record it as delivered, noting the remaining deferral (internal-only notes; rendering the deferred block types).

- [ ] **Step 4: Run the full suite**

Run: `dotnet test`
Expected: PASS - all pre-existing tests plus the Slice 8 additions (Task 1: 2, Task 2: 4, Task 3: 4, Task 4: 5, Task 5: 1 new + existing BlockRenderer tests, plus 3 new catalog facts). No new build warnings beyond the six pre-existing CS8602 (tech-debt item 4).

- [ ] **Step 5: Commit**

```bash
git add docs/ROADMAP.md docs/tech-debt.md CLAUDE.md
git commit -m "docs: record Slice 8 content-lint as delivered"
```

---

## Self-Review

**1. Spec coverage:**
- Three pre-render semantic layers, one checkId each -> Tasks 2/3/4. Covered.
- Pre-render placement (body prose verbatim) -> layers read the AGR model; no renderer change. Covered.
- Reuse QA-SF-07; QA-ENG-07 new id -> Task 2 binds QA-SF-07 (no catalog edit); Task 4 adds the QA-ENG-07 entry. Covered.
- QA-ENG-07 scans non-clause blocks only -> `ScanFields` omits `clauseInsertion`; test `QA_ENG_07_ignores_tokens_inside_clause_blocks`. Covered.
- QA-SF-07 and QA-ENG-07 blocking both modes; QA-SF-09 promotion-only -> `[Theory]` Draft/Promotion tests; `WarnCalloutLayer` returns empty off promotion; `QA_SF_09_passes_in_draft...`. Covered.
- QA-SF-09 keys off configured classification -> `ArtifactTypeConfig.Classification` (Task 1) read via `ArtifactTypesByCode`; `QA_SF_09_passes_for_non_client_facing_type...`. Covered.
- Config-driven token pattern (rule 3) -> Task 1 adds `contentLint.tokenResidue.pattern`; layer reads it and fails closed when absent (`QA_ENG_07_fails_closed_when_pattern_is_not_configured`). Covered.
- QA-ENG-06 self-verify (tech-debt item 16) -> Task 5. Covered.
- Non-goals recorded -> Task 6 tech-debt/CLAUDE updates (internal-only notes; deferred block rendering). Covered.

**2. Placeholder scan:** No TBD/TODO. Every code and test step shows complete code; every fixture step shows the exact JSON fragment. Task 6 doc edits describe concrete content to write.

**3. Type consistency:** `ISemanticLayer.Validate(SemanticContext)` returns `IReadOnlyList<Check>` (matches `InputHygieneLayer`/`BlockTypeSupportLayer`). `SemanticContext` positional record `(AgrDocument, ConfigBundle, string Mode, IIdRegistry? Registry)` - constructed in tests exactly as in `SemanticTestSupport`. `Check` uses the same field set as existing layers. `ArtifactTypeConfig.Classification` is `string?` and compared with `!=` to a `string` (safe). `ContentLint.TokenResidue` is `TokenResidueConfig?`, `.Pattern` is `string`. `BlockTypeSupportLayer.SupportedBlockTypes` becomes `IReadOnlySet<string>` and is still used via `.Contains` in `Validate`. `Mode.Draft`/`Mode.Promotion`, `Layer.Content`, `Severity.Blocking`, `ErrorCategory.MetadataViolation`/`ConfigurationError` all exist in `Vocabulary.cs`. Consistent.
