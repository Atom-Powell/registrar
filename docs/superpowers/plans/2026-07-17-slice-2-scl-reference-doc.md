# Slice 2: SCL Reference Document Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate the Shared Clause Library (SCL) as a controlled reference DOCX whose body is a projection of `config/clause-library.json`, via a new config-sourced `aggregationTable` block.

**Architecture:** The `aggregationTable` block mirrors `clauseInsertion`: the AGR carries only a source reference (`aggregationSource`), never content, and the renderer pulls the canonical clause set from the loaded `ConfigBundle` at render time, so the document cannot diverge from config. Only `clauseLibrary` is a live source this slice; `placeholderDictionary` is a recognized schema value that fails closed until the PDX slice.

**Tech Stack:** .NET 8, C#, Open XML SDK (DocumentFormat.OpenXml), xUnit, JsonSchema.Net.

## Global Constraints

- Fail closed: any controlled-document violation blocks the deliverable; no partial output.
- Zero LLM calls at runtime. No model call may enter the generation path.
- Configuration over code: artifact structure, vocabularies, clauses, checks live in `/config` and `/schemas` as versioned data. Core logic contains no artifact constants, clause text, or repository paths.
- Clause insertion is by reference only; clause text is proven by SHA-256.
- The engine never mints IDs; the ID Registry is authoritative state.
- Output hygiene: US English; no emojis; no em/en dashes or decorative unicode in generated documents, config strings, or templates. Only printable Basic Latin plus the approved allowlist.
- Every QA check ID gets at least one passing and one failing test named after the checkId.
- TDD: write the failing test first; commit after each green step.
- Only `clauseLibrary` is a live aggregation source. `placeholderDictionary` fails closed. Only SCL flips `generationTarget`; PDX stays `false`.

---

## File Structure

**Created:**
- `src/Registrar.Rendering/Blocks/AggregationTableBlock.cs` - renders a config-sourced aggregation table. One responsibility: turn an ordered clause list into a `Table`.
- `fixtures/agr-sample-scl-draft.json` - golden SCL AGR (thin: metadata + four sections, one holding the aggregation block).
- `tests/Registrar.Tests/Rendering/AggregationTableBlockTests.cs`
- `tests/Registrar.Tests/AggregationSourceValidationTests.cs` - QA-ENG-06 source-validity + QA-ENG-07 exemption cases.
- `tests/Registrar.Tests/Rendering/SclEndToEndTests.cs`

**Modified:**
- `schemas/agr.schema.json` - add `aggregationSource` property to the block definition.
- `src/Registrar.Core/Model/AgrDocument.cs` - add `AggregationSource` to `AgrBlock`.
- `src/Registrar.Core/Config/ConfigBundle.cs` - add ordered `Clauses` list.
- `src/Registrar.Core/Config/ConfigLoader.cs` - populate `Clauses`.
- `src/Registrar.Rendering/RenderContext.cs` - add `ClauseLibrary` (ordered list).
- `src/Registrar.Rendering/ArtifactRenderer.cs:24` - pass `config.Clauses` into the context.
- `src/Registrar.Rendering/Blocks/BlockRenderer.cs` - dispatch `aggregationTable`.
- `src/Registrar.Validation/BlockTypeSupportLayer.cs` - QA-ENG-06 supported-set + source-validity.
- `src/Registrar.Validation/TokenResidueLayer.cs` - document the aggregationTable exemption (no behavior change).
- `config/artifact-types.json` - SCL `orderedSections` + `generationTarget: true`.
- `config/qa-checklist.json` - QA-ENG-06 rule text.
- `fixtures/registry-sample.csv` - add the SCL registry row.

---

## Task 1: Add `aggregationSource` to schema and model

**Files:**
- Modify: `schemas/agr.schema.json` (block `properties`)
- Modify: `src/Registrar.Core/Model/AgrDocument.cs` (`AgrBlock`)
- Test: `tests/Registrar.Tests/AggregationSourceValidationTests.cs`

**Interfaces:**
- Produces: `AgrBlock.AggregationSource` (`string?`, JSON name `aggregationSource`).

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/AggregationSourceValidationTests.cs`:

```csharp
using System.Text.Json;
using Registrar.Core.Model;
using Xunit;

namespace Registrar.Tests;

public class AggregationSourceValidationTests
{
    [Fact]
    public void AgrBlock_deserializes_aggregationSource()
    {
        const string json = """
        { "blockType": "aggregationTable", "aggregationSource": "clauseLibrary" }
        """;
        var block = JsonSerializer.Deserialize<AgrBlock>(json);
        Assert.NotNull(block);
        Assert.Equal("aggregationTable", block!.BlockType);
        Assert.Equal("clauseLibrary", block.AggregationSource);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~AgrBlock_deserializes_aggregationSource"`
Expected: FAIL - `AgrBlock` has no `AggregationSource` property (compile error).

- [ ] **Step 3: Add the model property**

In `src/Registrar.Core/Model/AgrDocument.cs`, in the `AgrBlock` record, add after the `ClauseId` line:

```csharp
    [JsonPropertyName("aggregationSource")] public string? AggregationSource { get; init; }
```

- [ ] **Step 4: Add the schema property**

In `schemas/agr.schema.json`, in the block object's `properties` (the object that already contains `"blockType"` and `"clauseId"`), add:

```json
"aggregationSource": { "type": ["string", "null"], "enum": ["clauseLibrary", "placeholderDictionary", null], "description": "For an aggregationTable block: the config source to project. clauseLibrary is live; placeholderDictionary is reserved and fails closed until the PDX slice." }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~AgrBlock_deserializes_aggregationSource"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add schemas/agr.schema.json src/Registrar.Core/Model/AgrDocument.cs tests/Registrar.Tests/AggregationSourceValidationTests.cs
git commit -m "feat: add aggregationSource to AGR block schema and model"
```

---

## Task 2: Expose ordered clause list through config and render context

**Files:**
- Modify: `src/Registrar.Core/Config/ConfigBundle.cs`
- Modify: `src/Registrar.Core/Config/ConfigLoader.cs` (the `return new ConfigBundle { ... }` site)
- Modify: `src/Registrar.Rendering/RenderContext.cs`
- Modify: `src/Registrar.Rendering/ArtifactRenderer.cs:24`
- Test: `tests/Registrar.Tests/Config/ClauseOrderTests.cs`

**Interfaces:**
- Consumes: `ClauseEntry` (existing, `Registrar.Core.Config`).
- Produces: `ConfigBundle.Clauses` (`IReadOnlyList<ClauseEntry>`, library order); `RenderContext.ClauseLibrary` (`IReadOnlyList<ClauseEntry>`).

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Config/ClauseOrderTests.cs`:

```csharp
using Registrar.Core.Config;
using Xunit;

namespace Registrar.Tests.Config;

public class ClauseOrderTests
{
    [Fact]
    public void ConfigBundle_exposes_clauses_in_library_order()
    {
        var config = ConfigLoader.Load(TestPaths.ConfigDir);
        Assert.NotEmpty(config.Clauses);
        Assert.Equal("CL-001", config.Clauses[0].ClauseId);
        // Order matches the file, which is also strictly ascending by id here.
        var ids = config.Clauses.Select(c => c.ClauseId).ToList();
        Assert.Equal(ids.OrderBy(x => x, System.StringComparer.Ordinal).ToList(), ids);
    }
}
```

If `TestPaths.ConfigDir` does not exist in the test project, replace it with the pattern used by existing config tests (inspect `tests/Registrar.Tests/Config/RenderingPolishConfigTests.cs` for how it locates `/config`) and use that exact locator instead.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigBundle_exposes_clauses_in_library_order"`
Expected: FAIL - `ConfigBundle` has no `Clauses` property (compile error).

- [ ] **Step 3: Add the ConfigBundle property**

In `src/Registrar.Core/Config/ConfigBundle.cs`, add after the `ClausesById` line:

```csharp
    public required IReadOnlyList<ClauseEntry> Clauses { get; init; }
```

- [ ] **Step 4: Populate it in ConfigLoader**

In `src/Registrar.Core/Config/ConfigLoader.cs`, in the `return new ConfigBundle { ... }` initializer, add next to `ClausesById`:

```csharp
            Clauses = clauseFile.Clauses,
```

(`clauseFile.Clauses` is the ordered `IReadOnlyList<ClauseEntry>` already loaded from `clause-library.json`.)

- [ ] **Step 5: Thread it into RenderContext**

In `src/Registrar.Rendering/RenderContext.cs`, add a field to the record:

```csharp
public sealed record RenderContext(
    StyleRules Style,
    IReadOnlyDictionary<string, ClauseEntry> Clauses,
    IReadOnlyDictionary<string, string?> Placeholders,
    RadioOptionPattern Radio,
    WarnCalloutStyle Warn,
    IReadOnlyList<ClauseEntry> ClauseLibrary);
```

In `src/Registrar.Rendering/ArtifactRenderer.cs` line 24, update the construction:

```csharp
        var ctx = new RenderContext(style, config.ClausesById, agr.DataInputs.PlaceholderMap, config.RadioPattern, config.WarnCallout, config.Clauses);
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigBundle_exposes_clauses_in_library_order"`
Expected: PASS. Then build the solution to confirm no other `new RenderContext(...)` call sites broke:

Run: `dotnet build`
Expected: Build succeeded. If a test helper constructs `RenderContext` directly, add `config.Clauses` (or the test's clause list) as the trailing argument.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/Config/ConfigBundle.cs src/Registrar.Core/Config/ConfigLoader.cs src/Registrar.Rendering/RenderContext.cs src/Registrar.Rendering/ArtifactRenderer.cs tests/Registrar.Tests/Config/ClauseOrderTests.cs
git commit -m "feat: expose ordered clause library through config and render context"
```

---

## Task 3: Render the config-sourced aggregation table

**Files:**
- Create: `src/Registrar.Rendering/Blocks/AggregationTableBlock.cs`
- Modify: `src/Registrar.Rendering/Blocks/BlockRenderer.cs` (add `case "aggregationTable"` before `default`)
- Test: `tests/Registrar.Tests/Rendering/AggregationTableBlockTests.cs`

**Interfaces:**
- Consumes: `RenderContext.ClauseLibrary`, `TableBlock.Render(IReadOnlyList<string>? headers, IReadOnlyList<IReadOnlyList<string>> rows, StyleRules style)`, `BlockRenderResult(IReadOnlyList<OpenXmlElement> Elements, string ScanText, Check? ClauseCheck, ClauseInsertionAudit? ClauseAudit)`.
- Produces: `AggregationTableBlock.Render(string? aggregationSource, RenderContext ctx)` returning `BlockRenderResult`; column headers `["Clause ID", "Name", "Canonical Text", "SHA-256"]`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Rendering/AggregationTableBlockTests.cs`:

```csharp
using DocumentFormat.OpenXml.Wordprocessing;
using Registrar.Core.Config;
using Registrar.Rendering;
using Xunit;

namespace Registrar.Tests.Rendering;

public class AggregationTableBlockTests
{
    private static RenderContext Ctx()
    {
        var config = ConfigLoader.Load(TestPaths.ConfigDir);
        return new RenderContext(config.Style, config.ClausesById, new Dictionary<string, string?>(),
            config.RadioPattern, config.WarnCallout, config.Clauses);
    }

    [Fact]
    public void Render_projects_every_clause_in_order_with_recorded_sha()
    {
        var ctx = Ctx();
        var result = AggregationTableBlock.Render("clauseLibrary", ctx);
        var table = Assert.IsType<Table>(Assert.Single(result.Elements));
        var text = table.InnerText;

        foreach (var clause in ctx.ClauseLibrary)
        {
            Assert.Contains(clause.ClauseId, text);
            Assert.Contains(clause.CanonicalTextSha256, text);
        }
        // First data row is the first library clause: order is preserved.
        var firstDataRow = table.Elements<TableRow>().ElementAt(1).InnerText;
        Assert.Contains(ctx.ClauseLibrary[0].ClauseId, firstDataRow);
    }

    [Fact]
    public void Render_shows_canonical_text_verbatim_including_tokens()
    {
        var ctx = Ctx();
        var result = AggregationTableBlock.Render("clauseLibrary", ctx);
        // A clause library documents the token slots; canonical text is NOT substituted.
        Assert.Contains("[TOOL / CONNECTOR NAME]", result.ScanText);
    }

    [Fact]
    public void Render_throws_on_non_live_source()
    {
        var ctx = Ctx();
        Assert.Throws<InvalidOperationException>(() => AggregationTableBlock.Render("placeholderDictionary", ctx));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~AggregationTableBlockTests"`
Expected: FAIL - `AggregationTableBlock` does not exist (compile error).

- [ ] **Step 3: Implement the block**

Create `src/Registrar.Rendering/Blocks/AggregationTableBlock.cs`:

```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

// Config-sourced aggregation. The AGR block carries only aggregationSource; the whole
// body is projected from config here, so the document cannot diverge from config.
// Only clauseLibrary is live this slice. Canonical clause text is rendered verbatim,
// tokens included, because a clause library documents the token slots.
public static class AggregationTableBlock
{
    private static readonly IReadOnlyList<string> ClauseHeaders =
        new[] { "Clause ID", "Name", "Canonical Text", "SHA-256" };

    public static BlockRenderResult Render(string? aggregationSource, RenderContext ctx)
    {
        if (aggregationSource != "clauseLibrary")
            // BlockTypeSupportLayer (QA-ENG-06) fails closed on a non-live source before
            // rendering, so reaching here with one is an engine bug, not Governor input.
            throw new InvalidOperationException(
                $"aggregationTable reached the renderer with source '{aggregationSource ?? "null"}'; "
                + "QA-ENG-06 should have blocked it. Only 'clauseLibrary' is a live source.");

        var rows = ctx.ClauseLibrary
            .Select(c => (IReadOnlyList<string>)new[]
                { c.ClauseId, c.Name, c.CanonicalText, c.CanonicalTextSha256 })
            .ToList();

        var table = TableBlock.Render(ClauseHeaders, rows, ctx.Style);
        var scan = string.Join(" ", ClauseHeaders.Concat(rows.SelectMany(r => r)));
        return new BlockRenderResult(new OpenXmlElement[] { table }, scan, null, null);
    }
}
```

Confirm `ClauseEntry` has a `Name` property (it does: `ClauseId`, `Name`, `CanonicalText`, `CanonicalTextSha256`, `ContainsPlaceholders`).

- [ ] **Step 4: Dispatch it from BlockRenderer**

In `src/Registrar.Rendering/Blocks/BlockRenderer.cs`, add a case immediately before the `default:` branch:

```csharp
            case "aggregationTable":
            {
                return AggregationTableBlock.Render(block.AggregationSource, ctx);
            }
```

Update the `default:` comment so it no longer lists `aggregationTable` among the never-rendered types (leaving only `copyReadyMessage`).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~AggregationTableBlockTests"`
Expected: PASS (all three).

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Rendering/Blocks/AggregationTableBlock.cs src/Registrar.Rendering/Blocks/BlockRenderer.cs tests/Registrar.Tests/Rendering/AggregationTableBlockTests.cs
git commit -m "feat: render config-sourced aggregationTable for the clause library"
```

---

## Task 4: QA-ENG-06 supported-set and source-validity

**Files:**
- Modify: `src/Registrar.Validation/BlockTypeSupportLayer.cs`
- Test: `tests/Registrar.Tests/BlockTypeSupportLayerTests.cs` (add cases; keep existing)

**Interfaces:**
- Consumes: `AgrBlock.BlockType`, `AgrBlock.AggregationSource`, `SemanticContext`.
- Produces: QA-ENG-06 passes an `aggregationTable` with `aggregationSource == "clauseLibrary"`; fails closed on any other source (missing, unknown, or `placeholderDictionary`).

- [ ] **Step 1: Write the failing tests**

Add to `tests/Registrar.Tests/BlockTypeSupportLayerTests.cs` (follow the file's existing helpers for building a `SemanticContext`/AGR with one section and one block; mirror how the current passing/failing tests there construct input):

```csharp
    [Fact]
    public void QA_ENG_06_passes_aggregationTable_with_clauseLibrary_source()
    {
        var ctx = ContextWithBlock(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "clauseLibrary"
        });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-06");
    }

    [Fact]
    public void QA_ENG_06_fails_aggregationTable_with_placeholderDictionary_source()
    {
        var ctx = ContextWithBlock(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "placeholderDictionary"
        });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.Contains(checks, c => c.CheckId == "QA-ENG-06" && c.Result == CheckResultValue.Fail);
    }

    [Fact]
    public void QA_ENG_06_fails_aggregationTable_with_missing_source()
    {
        var ctx = ContextWithBlock(new AgrBlock { BlockType = "aggregationTable" });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.Contains(checks, c => c.CheckId == "QA-ENG-06" && c.Result == CheckResultValue.Fail);
    }
```

If no `ContextWithBlock` helper exists, add a small private one in this test class that wraps a single block in a one-section AGR and a `SemanticContext` the same way the existing tests do.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BlockTypeSupportLayerTests"`
Expected: the three new tests FAIL (`aggregationTable` is currently unsupported, so it fails as an unsupported *type*, and the passing-case test fails because a QA-ENG-06 check is present).

- [ ] **Step 3: Implement supported-set + source-validity**

In `src/Registrar.Validation/BlockTypeSupportLayer.cs`:

Add `"aggregationTable"` to `SupportedBlockTypes`:

```csharp
    public static readonly IReadOnlySet<string> SupportedBlockTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "paragraph", "h2", "h3", "bulletList", "table", "clauseInsertion", "radioQuestion", "warnCallout", "aggregationTable"
    };

    // Aggregation sources this engine version projects. placeholderDictionary is a
    // recognized schema value but not a live source until the PDX slice.
    private static readonly IReadOnlySet<string> LiveAggregationSources = new HashSet<string>(StringComparer.Ordinal)
    {
        "clauseLibrary"
    };
```

Replace the inner loop body in `Validate` so a supported `aggregationTable` still has its source checked:

```csharp
                var block = section.Blocks[b];
                var blockType = block.BlockType;
                if (!SupportedBlockTypes.Contains(blockType))
                {
                    checks.Add(Fail(section.Title, blockType, s, b));
                    continue;
                }
                if (blockType == "aggregationTable" && !LiveAggregationSources.Contains(block.AggregationSource ?? ""))
                    checks.Add(FailSource(section.Title, block.AggregationSource, s, b));
```

Add the `FailSource` helper next to `Fail`:

```csharp
    private static Check FailSource(string sectionTitle, string? source, int sectionIndex, int blockIndex) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ConfigurationError,
        Message = $"Section '{sectionTitle}' has an aggregationTable with an aggregationSource this engine "
            + $"version does not project: '{source ?? "null"}'.",
        JsonPath = $"/dataInputs/sections/{sectionIndex}/blocks/{blockIndex}/aggregationSource",
        Expected = "a live aggregation source: clauseLibrary",
        Actual = source ?? "null",
        Remediation = $"Set aggregationSource to 'clauseLibrary'. '{source ?? "null"}' is not projected by this "
            + "engine version (placeholderDictionary is reserved until the PDX slice).",
        RuleSource = RuleSourceValue
    };
```

Add `"aggregationTable"` to the `Expected` string in the existing `Fail` helper's supported-type list.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BlockTypeSupportLayerTests"`
Expected: PASS (new and pre-existing).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Validation/BlockTypeSupportLayer.cs tests/Registrar.Tests/BlockTypeSupportLayerTests.cs
git commit -m "feat: QA-ENG-06 admits config-sourced aggregationTable, fails closed on non-live source"
```

---

## Task 5: QA-ENG-07 token-residue exemption (documented + tested)

**Files:**
- Modify: `src/Registrar.Validation/TokenResidueLayer.cs` (comment only; no behavior change)
- Test: `tests/Registrar.Tests/AggregationSourceValidationTests.cs` (add cases)

**Interfaces:**
- Consumes: `TokenResidueLayer.Validate(SemanticContext)`.
- Produces: no QA-ENG-07 finding for an `aggregationTable` block; a QA-ENG-07 finding for a `[TOKEN]` left in an authored `paragraph`.

- [ ] **Step 1: Write the failing/guarding tests**

Add to `tests/Registrar.Tests/AggregationSourceValidationTests.cs` (reuse the same one-section-AGR helper approach as the QA-ENG-06 tests; a `TokenResidueLayer` needs a `SemanticContext` whose `Config.ContentLint.TokenResidue.Pattern` is loaded - build it from `ConfigLoader.Load(TestPaths.ConfigDir)` like other content-lint tests do):

```csharp
    [Fact]
    public void QA_ENG_07_does_not_flag_aggregationTable_block()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "clauseLibrary"
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-07");
    }

    [Fact]
    public void QA_ENG_07_still_flags_token_in_authored_paragraph()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "paragraph",
            Text = "This references [TOOL / CONNECTOR NAME] in authored prose."
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.Contains(checks, c => c.CheckId == "QA-ENG-07" && c.Result == CheckResultValue.Fail);
    }
```

`ContextWithBlockAndConfig` builds a `SemanticContext` with the real loaded config (for the token-residue pattern) and a one-section AGR containing the given block. Model it on the existing content-layer tests in the suite.

- [ ] **Step 2: Run tests to verify status**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~QA_ENG_07"`
Expected: `QA_ENG_07_does_not_flag_aggregationTable_block` PASSES already (the layer's `ScanFields` allowlist has no `aggregationTable` case, so it is not scanned); `QA_ENG_07_still_flags_token_in_authored_paragraph` PASSES already. Both green confirms the exemption is a property of the existing allowlist. If either fails, fix before proceeding.

- [ ] **Step 3: Document the exemption so it cannot be broken by a future edit**

In `src/Registrar.Validation/TokenResidueLayer.cs`, in `ScanFields`, extend the trailing comment:

```csharp
            // clauseInsertion and aggregationTable: not scanned here. Both are config-sourced
            // (clause text / the projected clause library), carry no AGR body text, and
            // legitimately contain [TOKEN] slots. Adding a case for either would fail closed
            // the very documents whose purpose is to display those slots.
```

- [ ] **Step 4: Run the full residue test class**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~QA_ENG_07"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Validation/TokenResidueLayer.cs tests/Registrar.Tests/AggregationSourceValidationTests.cs
git commit -m "test: pin QA-ENG-07 exemption for config-sourced aggregationTable"
```

---

## Task 6: SCL section schema, generationTarget, and QA-ENG-06 catalog text

**Files:**
- Modify: `config/artifact-types.json` (SCL entry)
- Modify: `config/qa-checklist.json` (QA-ENG-06 rule text)
- Test: `tests/Registrar.Tests/Config/SclTypeConfigTests.cs`

**Interfaces:**
- Produces: SCL is a generation target with an ordered section schema; `QA-ENG-06` catalog rule text names config-sourced aggregationTable.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Config/SclTypeConfigTests.cs`:

```csharp
using Registrar.Core.Config;
using Xunit;

namespace Registrar.Tests.Config;

public class SclTypeConfigTests
{
    [Fact]
    public void SCL_has_a_defined_section_schema()
    {
        var config = SemanticTestSupport.Config();
        var scl = config.ArtifactTypesByCode["SCL"];
        Assert.NotNull(scl.SectionSchema);
        Assert.Equal("defined", scl.SectionSchema!.Status);
        Assert.Equal(
            new[] { "Purpose", "How Clauses Are Inserted", "Clause Library", "Version and Change Notes" },
            scl.SectionSchema.OrderedSections);
    }
}
```

IMPORTANT: `ArtifactTypeConfig` (in `src/Registrar.Core/Config/ConfigModels.cs`) has NO `GenerationTarget` property. The engine does not read the JSON `generationTarget` field at all: generation is gated solely by `SectionSchemaLayer` (QA-ENG-02), which refuses generation unless `SectionSchema.Status == "defined"` with a non-null `OrderedSections`. So the test asserts the section schema, not a generation-target flag. `TestPaths.ConfigDir` does not exist; use `SemanticTestSupport.Config()` (the established pattern other config tests use). `SectionSchemaConfig` fields are `Status` (string) and `OrderedSections` (IReadOnlyList<string>?).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~SCL_has_a_defined_section_schema"`
Expected: FAIL - SCL currently has `sectionSchema.status: "not-defined-in-spec"` and `orderedSections: null`.

- [ ] **Step 3: Author the SCL schema**

In `config/artifact-types.json`, replace the SCL entry's `generationTarget` and `sectionSchema`:

```json
      "generationTarget": true,
      "sectionSchema": {
        "status": "defined",
        "orderedSections": [
          "Purpose",
          "How Clauses Are Inserted",
          "Clause Library",
          "Version and Change Notes"
        ]
      }
```

- [ ] **Step 4: Update the QA-ENG-06 catalog rule text**

In `config/qa-checklist.json`, find the `QA-ENG-06` entry and update its `rule` so it names the now-supported config-sourced block and the still-deferred one. Set `rule` to:

```
Every dataInputs.sections[].blocks[].blockType is one this engine version renders: paragraph, h2, h3, bulletList, table, clauseInsertion, radioQuestion, warnCallout, aggregationTable. An aggregationTable is valid only with a live aggregationSource (clauseLibrary); placeholderDictionary is reserved and fails closed until the PDX slice. Block types defined in the AGR schema but not yet built (copyReadyMessage) fail closed with a reported check before rendering is attempted. Blocks in draft and promotion.
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~SCL_is_a_generation_target_with_defined_sections"`
Expected: PASS.

- [ ] **Step 6: Refresh goldens for the artifact-types.json hash change (mandatory)**

Editing `config/artifact-types.json` changes its SHA-256, which every fixture golden records under `configVersions.artifact-types.json` in its `validation-report.json`. The full suite (`TestCommandTests`) will go red until the goldens are refreshed. Regenerate them:

Run: `dotnet run --project src/Registrar.Cli -- test --all --update`

Then verify the change is hash-only, exactly one line per golden and nothing else:

Run: `git diff --stat fixtures/expected/` (expect only `validation-report.json` files changed)
Run: `git diff fixtures/expected/ | grep -E "^[+-]" | grep -vE "^(\+\+\+|---)" | grep -viE "artifact-types.json|[0-9a-f]{16}"` (expect NO output; every changed line is the artifact-types.json hash)

If any DOCX, `generation-log.json`, `artifact-manifest.json`, `promotion-payload.json`, or a non-hash line changed, STOP and report DONE_WITH_CONCERNS: the config edit altered rendered output, which this task did not intend.

- [ ] **Step 7: Run the full suite to confirm green**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add config/artifact-types.json config/qa-checklist.json tests/Registrar.Tests/Config/SclTypeConfigTests.cs fixtures/expected/
git commit -m "feat: define SCL section schema and flip it to a generation target"
```

---

## Task 7: Golden SCL fixture and end-to-end build

**Files:**
- Create: `fixtures/agr-sample-scl-draft.json`
- Modify: `fixtures/registry-sample.csv` (add the SCL row)
- Test: `tests/Registrar.Tests/Rendering/SclEndToEndTests.cs`

**Interfaces:**
- Consumes: the full build path (validation + render) as exercised by existing end-to-end tests (model `SclEndToEndTests` on `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs` and the existing TCL/DS/AUP end-to-end fixtures).

- [ ] **Step 1: Inspect an existing draft fixture and the fixture registry**

Read `fixtures/agr-sample-tcl-draft.json` and `fixtures/registry-sample.csv` to copy the exact envelope shape (identity, validationPolicy, context, dataInputs) and the registry CSV columns. The SCL fixture must reuse that envelope exactly, changing only the identity, sections, and the one aggregation block.

- [ ] **Step 2: Write the failing end-to-end test**

Create `tests/Registrar.Tests/Rendering/SclEndToEndTests.cs`:

```csharp
using Registrar.Core.Config;
using Xunit;

namespace Registrar.Tests.Rendering;

public class SclEndToEndTests
{
    [Theory]
    [InlineData("draft")]
    [InlineData("promotion")]
    public void Scl_builds_and_every_clause_is_present(string mode)
    {
        var config = ConfigLoader.Load(TestPaths.ConfigDir);
        var result = TestBuild.Run("fixtures/agr-sample-scl-draft.json", mode, "fixtures/registry-sample.csv");

        Assert.Equal("pass", result.Report.OverallResult switch
        {
            "pass" => "pass",
            "pass-with-warnings" => "pass",
            _ => result.Report.OverallResult
        });
        Assert.NotNull(result.DocxBytes);
        var text = TestBuild.DocxBodyText(result.DocxBytes!);
        foreach (var clause in config.Clauses)
        {
            Assert.Contains(clause.ClauseId, text);
            Assert.Contains(clause.CanonicalTextSha256, text);
        }
    }

    [Fact]
    public void Scl_build_is_byte_deterministic()
    {
        var a = TestBuild.Run("fixtures/agr-sample-scl-draft.json", "draft", "fixtures/registry-sample.csv");
        var b = TestBuild.Run("fixtures/agr-sample-scl-draft.json", "draft", "fixtures/registry-sample.csv");
        Assert.Equal(a.DocxBytes, b.DocxBytes);
    }
}
```

`TestBuild.Run` / `TestBuild.DocxBodyText` stand in for the suite's existing end-to-end harness. Before writing this, inspect `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs` and `DeterministicRenderTests.cs` and use whatever helper they already use to run a full build and read DOCX text. Match that API exactly rather than introducing a new one.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~SclEndToEndTests"`
Expected: FAIL - the fixture and registry row do not exist yet.

- [ ] **Step 4: Add the registry row**

In `fixtures/registry-sample.csv`, add a row for `ORG-MST-SCL-001` matching the existing column order, with status `Draft` (so draft keeps it and promotion advances to `In Review`), category `MST`, and the fields the other rows populate. Copy an existing MST-style row's shape; only the ID, title, and status differ.

- [ ] **Step 5: Author the golden SCL AGR**

Create `fixtures/agr-sample-scl-draft.json` using the envelope copied in Step 1. Requirements:
- `identity.artifactType`: `SCL`; `identity.documentId`: `ORG-MST-SCL-001`; version `v1.0`; classification `Internal Working Document`.
- `validationPolicy.mode`: `draft`.
- Four sections whose titles exactly match the schema, in order:
  1. `Purpose` - one `paragraph` block, short authored prose, ASCII only, no bracketed tokens.
  2. `How Clauses Are Inserted` - one `paragraph` block, authored prose explaining clauses are inserted by reference and proven by SHA-256. No bracketed tokens.
  3. `Clause Library` - one block: `{ "blockType": "aggregationTable", "aggregationSource": "clauseLibrary" }`.
  4. `Version and Change Notes` - one `paragraph` block, authored prose. No bracketed tokens.
- `dataInputs.placeholderMap`: whatever the DCH/callout requires for an Internal Working Document (copy the minimal set an existing internal fixture uses; the aggregation body needs none).

Keep every string ASCII, no em/en dashes, no smart quotes.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~SclEndToEndTests"`
Expected: PASS (draft, promotion, determinism). If promotion fails on the registry transition, confirm the registry row status is `Draft` so the legal next state is `In Review`.

- [ ] **Step 7: Generate and commit the new fixture's golden (mandatory)**

`TestCommandTests.All_committed_fixtures_match_their_goldens` discovers every fixture under `fixtures/`, so the new `agr-sample-scl-draft` fixture needs a committed golden under `fixtures/expected/agr-sample-scl-draft/` or that test fails. Generate it:

Run: `dotnet run --project src/Registrar.Cli -- test --fixture agr-sample-scl-draft --update`

Then inspect the generated golden as a correctness check, not a rubber stamp:
- `fixtures/expected/agr-sample-scl-draft/` should contain the SCL DOCX and `validation-report.json` (and any other package files the other fixtures' expected dirs contain).
- Open the generated `validation-report.json` and confirm `overallResult` is `pass` (or `pass-with-warnings`).
- This is the first time the SCL fixture's output is frozen as the golden, so eyeball that the DOCX was produced and the report is a pass before committing it.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test`
Expected: all green, build clean, zero warnings. If `All_committed_fixtures_match_their_goldens` fails, the SCL golden was not generated or committed. Fix any test that constructed `RenderContext` or `ConfigBundle` positionally and broke on the new trailing field.

- [ ] **Step 9: Commit**

```bash
git add fixtures/agr-sample-scl-draft.json fixtures/registry-sample.csv fixtures/expected/agr-sample-scl-draft/ tests/Registrar.Tests/Rendering/SclEndToEndTests.cs
git commit -m "test: end-to-end SCL clause-library reference document build"
```

---

## Self-Review

**Spec coverage:**
- Config-sourced `aggregationTable` mechanism -> Tasks 1, 2, 3.
- SCL section schema + `generationTarget` flip (rule 6 owner decision, recorded in config) -> Task 6.
- QA-ENG-06 supported-set + source-validity; `placeholderDictionary` not live -> Task 4 + Task 6 catalog text.
- QA-ENG-07 scoped exemption (aggregation exempt, prose still scanned) -> Task 5.
- Config fidelity (rows + SHA match config) -> Task 3 + Task 7.
- End-to-end draft + promotion + byte-determinism -> Task 7.
- SCL-only; PDX held; `copyReadyMessage` still deferred -> enforced by Task 4 (only `clauseLibrary` live) and the Task 3 `default` comment.
- ID Registry dependency: exercised against the fixture registry (Task 7); production `ORG-MST-SCL-NNN` remains a Registry Owner action, not a code deliverable - noted below.

**Placeholder scan:** No TBD/TODO. Two tasks (2, 7) intentionally say "match the existing helper" for test-harness APIs the plan cannot name without inspecting the suite; each gives the exact file to read and the exact behavior required. This is a codebase-convention lookup, not an unspecified requirement.

**Type consistency:** `AggregationSource` (`string?`), `RenderContext.ClauseLibrary` (`IReadOnlyList<ClauseEntry>`), `ConfigBundle.Clauses` (`IReadOnlyList<ClauseEntry>`), `AggregationTableBlock.Render(string?, RenderContext) -> BlockRenderResult`, headers `["Clause ID","Name","Canonical Text","SHA-256"]` are used identically across Tasks 1-7.

## Consequences and follow-ups (not in this slice)

- The aggregation displays each clause's recorded SHA-256. Re-verifying recorded-vs-actual across the whole library at render time (as `ClauseResolver` does at insertion) is a cheap future hardening, deferred to keep the block minimal; clause integrity is already enforced on the insertion path by QA-SF-11.
- `placeholderDictionary` as a live source, the PDX section schema, and flipping PDX `generationTarget` are the PDX slice, unblocked once `placeholder-dictionary.json` is reconciled against the canonical xlsx.
- Producing the real standing SCL document depends on `ORG-MST-SCL-001` (or the assigned number) existing in the production ID Registry; the engine never mints it.
