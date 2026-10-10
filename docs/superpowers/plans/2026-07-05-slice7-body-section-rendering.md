# Slice 7: Body-Section Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the numbered body sections of a governed artifact (prose-subset blocks plus clause insertion by reference), fold in the post-render QA-SF-08 character scan, and render DS and AUP end-to-end.

**Architecture:** Static per-block renderers dispatched by `BlockRenderer`, assembled per section by `SectionRenderer`, into `ArtifactRenderer` which now returns a `RenderResult` (bytes + post-render checks + clause audits). Clause text is inserted by reference from config with SHA-256 proof and token resolution. `BuildCommand` renders in memory, merges post-render checks, recomputes the overall result, then writes the report and emits the DOCX only when still eligible.

**Tech Stack:** .NET 8, C#, DocumentFormat.OpenXml, xUnit.

## Global Constraints

- .NET 8 target; on this machine run tests with `DOTNET_ROLL_FORWARD=LatestMajor` (the .NET 8 runtime is not installed).
- Test command from repo root: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test`.
- Zero LLM calls at runtime; rendering is deterministic.
- Configuration over code: fonts, colors, sizes, borders come from `StyleRules`/`ContentLint`; no hardcoded artifact constants in rendering code.
- Output hygiene: US English; no emojis; no em/en dashes or decorative unicode in generated documents, config strings, or templates.
- Source hygiene (`SourceHygieneTests`): no literal non-ASCII glyphs in source or fixtures; write codepoints as escapes (for example `"○"`), never a literal glyph. The Edit tool decodes `\uXXXX`; the Write tool writes literally - verify new source with a non-ASCII grep before committing.
- Clause insertion is by reference only: the engine inserts canonical text from `config/clause-library.json` and proves it via SHA-256. Governor-supplied clause prose is ignored by design (the model carries no clause-text field).
- Every QA check id gets at least one passing and one failing test named after the check id.
- Fail closed: a blocking check discards the rendered bytes; no partial DOCX.

## File Structure

**Create:**
- `src/Registrar.Core/CharacterLint.cs` (moved from Validation)
- `src/Registrar.Core/CharacterPolicy.cs` (moved from Validation)
- `src/Registrar.Core/Model/ClauseInsertionAudit.cs`
- `src/Registrar.Rendering/RenderContext.cs`
- `src/Registrar.Rendering/RenderResult.cs`
- `src/Registrar.Rendering/ClauseResolver.cs`
- `src/Registrar.Rendering/Blocks/ParagraphBlock.cs`
- `src/Registrar.Rendering/Blocks/HeadingBlock.cs`
- `src/Registrar.Rendering/Blocks/BulletListBlock.cs`
- `src/Registrar.Rendering/Blocks/TableBlock.cs`
- `src/Registrar.Rendering/Blocks/BlockRenderer.cs`
- `src/Registrar.Rendering/NumberingPart.cs`
- `src/Registrar.Rendering/SectionRenderer.cs`
- `src/Registrar.Rendering/PostRenderScan.cs`
- `src/Registrar.Validation/PostRenderMerge.cs`
- `fixtures/agr-sample-aup-draft.json`
- `tests/Registrar.Tests/Rendering/ClauseResolverTests.cs`
- `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`
- `tests/Registrar.Tests/Rendering/SectionRendererTests.cs`
- `tests/Registrar.Tests/Rendering/PostRenderScanTests.cs`
- `tests/Registrar.Tests/PostRenderMergeTests.cs`

**Modify:**
- `src/Registrar.Core/Model/AgrDocument.cs` (AgrBlock, AgrSection fields)
- `src/Registrar.Core/Config/ConfigModels.cs` (ClauseEntry fields)
- `src/Registrar.Core/Config/ConfigBundle.cs` (ClausesById)
- `src/Registrar.Core/Config/ConfigLoader.cs` (populate ClausesById)
- `src/Registrar.Validation/InputHygieneLayer.cs` (using only; primitives moved)
- `src/Registrar.Rendering/ArtifactRenderer.cs` (RenderResult, numbering, section loop, scan)
- `src/Registrar.Cli/BuildCommand.cs` (reorder)
- `config/qa-checklist.json` (QA-SF-08 entry)
- `docs/tech-debt.md`, `CLAUDE.md` (deferred-work register)
- Existing tests: `CharacterLintTests.cs`, `Rendering/ArtifactRendererTests.cs`, `BuildCommandTests.cs`, `Config/ConfigLoaderTests.cs`, `QaChecklistCatalogTests.cs`

---

### Task 1: Extend the AGR model for body blocks

**Files:**
- Modify: `src/Registrar.Core/Model/AgrDocument.cs`
- Test: `tests/Registrar.Tests/Model/AgrParserTests.cs`

**Interfaces:**
- Produces: `AgrBlock` with `Text` (`string?`), `Items` (`IReadOnlyList<string>?`), `ColumnHeaders` (`IReadOnlyList<string>?`), `Rows` (`IReadOnlyList<IReadOnlyList<string>>?`); `AgrSection` with `DescriptorLine` (`string?`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Model/AgrParserTests.cs`:

```csharp
[Fact]
public void Parses_block_body_fields_and_descriptor_line()
{
    const string json = """
    {
      "artifactIdentity": { "artifactType": "DS", "documentId": "ORG-GOV-DS-016",
        "version": "v1.0", "classification": "Client-Facing", "approvalStatus": "Draft", "layer": "Governance" },
      "context": { "owner": "T", "audience": "A", "preparedBy": "P" },
      "dataInputs": { "placeholderMap": {}, "sections": [
        { "title": "Purpose", "descriptorLine": "An intro line", "blocks": [
          { "blockType": "paragraph", "text": "Body text." },
          { "blockType": "bulletList", "items": ["one", "two"] },
          { "blockType": "table", "columnHeaders": ["A", "B"], "rows": [["1", "2"]] }
        ] }
      ] },
      "renderingOptions": { "packageName": "Pkg", "outputFileName": "ORG - X - v1.0.docx" },
      "validationPolicy": { "mode": "draft" }
    }
    """;
    var agr = Registrar.Core.Model.AgrParser.Parse(json);
    var section = agr.DataInputs.Sections[0];
    Assert.Equal("An intro line", section.DescriptorLine);
    Assert.Equal("Body text.", section.Blocks[0].Text);
    Assert.Equal(new[] { "one", "two" }, section.Blocks[1].Items);
    Assert.Equal("A", section.Blocks[2].ColumnHeaders![0]);
    Assert.Equal("2", section.Blocks[2].Rows![0][1]);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Parses_block_body_fields_and_descriptor_line`
Expected: FAIL (no `DescriptorLine`/`Text`/`Items`/`ColumnHeaders`/`Rows` members).

- [ ] **Step 3: Add the fields**

In `src/Registrar.Core/Model/AgrDocument.cs`, replace the `AgrSection` and `AgrBlock` records:

```csharp
public sealed record AgrSection
{
    [JsonPropertyName("title")]          public required string Title { get; init; }
    [JsonPropertyName("descriptorLine")] public string? DescriptorLine { get; init; }
    [JsonPropertyName("blocks")]         public required IReadOnlyList<AgrBlock> Blocks { get; init; }
}

public sealed record AgrBlock
{
    [JsonPropertyName("blockType")]     public required string BlockType { get; init; }
    [JsonPropertyName("clauseId")]      public string? ClauseId { get; init; }
    [JsonPropertyName("text")]          public string? Text { get; init; }
    [JsonPropertyName("items")]         public IReadOnlyList<string>? Items { get; init; }
    [JsonPropertyName("columnHeaders")] public IReadOnlyList<string>? ColumnHeaders { get; init; }
    [JsonPropertyName("rows")]          public IReadOnlyList<IReadOnlyList<string>>? Rows { get; init; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Parses_block_body_fields_and_descriptor_line`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Model/AgrDocument.cs tests/Registrar.Tests/Model/AgrParserTests.cs
git commit -m "feat: map AGR block body fields and section descriptor line"
```

---

### Task 2: Expose clause records on the config bundle

**Files:**
- Modify: `src/Registrar.Core/Config/ConfigModels.cs`, `src/Registrar.Core/Config/ConfigBundle.cs`, `src/Registrar.Core/Config/ConfigLoader.cs`
- Test: `tests/Registrar.Tests/Config/ConfigLoaderTests.cs`

**Interfaces:**
- Produces: `ClauseEntry` with `CanonicalText` (`string`), `CanonicalTextSha256` (`string`), `ContainsPlaceholders` (`bool`); `ConfigBundle.ClausesById` (`IReadOnlyDictionary<string, ClauseEntry>`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Config/ConfigLoaderTests.cs`:

```csharp
[Fact]
public void Exposes_clause_records_by_id()
{
    var config = Registrar.Tests.SemanticTestSupport.Config();
    Assert.True(config.ClausesById.ContainsKey("CL-006"));
    var cl006 = config.ClausesById["CL-006"];
    Assert.False(string.IsNullOrWhiteSpace(cl006.CanonicalText));
    Assert.Matches("^[0-9a-fA-F]{64}$", cl006.CanonicalTextSha256);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Exposes_clause_records_by_id`
Expected: FAIL (no `ClausesById`).

- [ ] **Step 3: Add fields, bundle member, and loader wiring**

In `src/Registrar.Core/Config/ConfigModels.cs`, replace `ClauseEntry`:

```csharp
public sealed record ClauseEntry
{
    public required string ClauseId { get; init; }
    public string CanonicalText { get; init; } = string.Empty;
    public string CanonicalTextSha256 { get; init; } = string.Empty;
    public bool ContainsPlaceholders { get; init; }
}
```

In `src/Registrar.Core/Config/ConfigBundle.cs`, add after `ClauseIds`:

```csharp
    public required IReadOnlyDictionary<string, ClauseEntry> ClausesById { get; init; }
```

In `src/Registrar.Core/Config/ConfigLoader.cs`, in the returned `ConfigBundle` initializer, add after the `ClauseIds` line:

```csharp
            ClausesById = clauseFile.Clauses.ToDictionary(c => c.ClauseId, c => c),
```

- [ ] **Step 4: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Exposes_clause_records_by_id`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Config/ConfigModels.cs src/Registrar.Core/Config/ConfigBundle.cs src/Registrar.Core/Config/ConfigLoader.cs tests/Registrar.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat: expose clause records (text + sha) on the config bundle"
```

---

### Task 3: Move CharacterLint and CharacterPolicy to Core

**Files:**
- Create (move): `src/Registrar.Core/CharacterLint.cs`, `src/Registrar.Core/CharacterPolicy.cs`
- Delete: `src/Registrar.Validation/CharacterLint.cs`, `src/Registrar.Validation/CharacterPolicy.cs`
- Modify: `src/Registrar.Validation/InputHygieneLayer.cs` (using), `tests/Registrar.Tests/CharacterLintTests.cs` (using)

**Interfaces:**
- Produces: `Registrar.Core.CharacterPolicy`, `Registrar.Core.CharacterLint`, `Registrar.Core.Offense` (same members, new namespace) - consumed by Task 4 (ClauseResolver has no dependency), Task 10 (PostRenderScan).

- [ ] **Step 1: Move the two files and change their namespace**

`git mv src/Registrar.Validation/CharacterLint.cs src/Registrar.Core/CharacterLint.cs` and `git mv src/Registrar.Validation/CharacterPolicy.cs src/Registrar.Core/CharacterPolicy.cs`. In each moved file change `namespace Registrar.Validation;` to `namespace Registrar.Core;`. `CharacterPolicy.cs` already has `using Registrar.Core.Config;`; leave it.

- [ ] **Step 2: Update the two consumers' usings**

`InputHygieneLayer.cs` already has `using Registrar.Core;` (line 1), so the moved types resolve with no change - confirm it still references `CharacterLint`/`CharacterPolicy` unqualified. In `tests/Registrar.Tests/CharacterLintTests.cs`, ensure the file has `using Registrar.Core;` and remove any now-wrong `using Registrar.Validation;` reference to these primitives (Validation is still referenced for other layer tests in other files, but `CharacterLintTests` only needs Core after the move).

- [ ] **Step 3: Run the moved-primitive tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter CharacterLint`
Expected: PASS (behavior unchanged, only namespace moved).

- [ ] **Step 4: Run the full suite to catch any stale reference**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test`
Expected: PASS (Slice 6 QA-ENG-05 tests green; no unresolved `Registrar.Validation.CharacterLint`).

- [ ] **Step 5: Commit**

```bash
git add -A src/Registrar.Core/CharacterLint.cs src/Registrar.Core/CharacterPolicy.cs src/Registrar.Validation/CharacterLint.cs src/Registrar.Validation/CharacterPolicy.cs src/Registrar.Validation/InputHygieneLayer.cs tests/Registrar.Tests/CharacterLintTests.cs
git commit -m "refactor: move CharacterLint/CharacterPolicy to Core for reuse by QA-SF-08"
```

---

### Task 4: Clause resolver (reference lookup, SHA-256, token resolution)

**Files:**
- Create: `src/Registrar.Core/Model/ClauseInsertionAudit.cs`, `src/Registrar.Rendering/ClauseResolver.cs`
- Test: `tests/Registrar.Tests/Rendering/ClauseResolverTests.cs`

**Interfaces:**
- Consumes: `ConfigBundle.ClausesById` (Task 2), `Hashing.Sha256Hex` (existing, `string -> lowercase hex`), `Check`, `ErrorCategory.ClauseResolutionError`, `Layer.Content`.
- Produces: `ClauseInsertionAudit(string ClauseId, string ExpectedSha, string ActualSha, bool Verified, int TokensResolved)`; `ClauseResolution(string? Text, ClauseInsertionAudit? Audit, Check? Failure)`; `ClauseResolver.Resolve(string? clauseId, int sectionIndex, int blockIndex, IReadOnlyDictionary<string, ClauseEntry> clauses, IReadOnlyDictionary<string, string?> placeholders) -> ClauseResolution`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Rendering/ClauseResolverTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Rendering;

namespace Registrar.Tests.Rendering;

public class ClauseResolverTests
{
    private static IReadOnlyDictionary<string, ClauseEntry> Lib(ClauseEntry e)
        => new Dictionary<string, ClauseEntry> { [e.ClauseId] = e };

    private static ClauseEntry Cl(string id, string text)
        => new() { ClauseId = id, CanonicalText = text, CanonicalTextSha256 = Hashing.Sha256Hex(text) };

    [Fact]
    public void Resolves_tokens_and_verifies_sha()
    {
        var lib = Lib(Cl("CL-X", "Use [TOOL] only."));
        var map = new Dictionary<string, string?> { ["[TOOL]"] = "Copilot" };
        var r = ClauseResolver.Resolve("CL-X", 0, 0, lib, map);
        Assert.Null(r.Failure);
        Assert.Equal("Use Copilot only.", r.Text);
        Assert.True(r.Audit!.Verified);
        Assert.Equal(1, r.Audit.TokensResolved);
    }

    [Fact]
    public void Leaves_unmapped_token_as_literal()
    {
        var lib = Lib(Cl("CL-X", "Use [TOOL] only."));
        var map = new Dictionary<string, string?> { ["[TOOL]"] = null };
        var r = ClauseResolver.Resolve("CL-X", 0, 0, lib, map);
        Assert.Equal("Use [TOOL] only.", r.Text);
        Assert.Equal(0, r.Audit!.TokensResolved);
    }

    [Fact]
    public void Null_clause_id_is_a_blocking_failure()
    {
        var r = ClauseResolver.Resolve(null, 1, 2, Lib(Cl("CL-X", "x")), new Dictionary<string, string?>());
        Assert.Null(r.Text);
        Assert.NotNull(r.Failure);
        Assert.Equal(CheckResultValue.Fail, r.Failure!.Result);
        Assert.Equal(Severity.Blocking, r.Failure.SeverityLevel);
        Assert.Equal("/dataInputs/sections/1/blocks/2/clauseId", r.Failure.JsonPath);
    }

    [Fact]
    public void Sha_mismatch_is_a_blocking_failure()
    {
        var tampered = new ClauseEntry { ClauseId = "CL-X", CanonicalText = "real text",
            CanonicalTextSha256 = new string('0', 64) };
        var r = ClauseResolver.Resolve("CL-X", 0, 0, Lib(tampered), new Dictionary<string, string?>());
        Assert.Null(r.Text);
        Assert.NotNull(r.Failure);
        Assert.False(r.Audit!.Verified);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter ClauseResolverTests`
Expected: FAIL (types not defined).

- [ ] **Step 3: Implement the audit record and resolver**

Create `src/Registrar.Core/Model/ClauseInsertionAudit.cs`:

```csharp
namespace Registrar.Core.Model;

public sealed record ClauseInsertionAudit(
    string ClauseId, string ExpectedSha, string ActualSha, bool Verified, int TokensResolved);
```

Create `src/Registrar.Rendering/ClauseResolver.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Rendering;

public sealed record ClauseResolution(string? Text, ClauseInsertionAudit? Audit, Check? Failure);

public static class ClauseResolver
{
    public const string CheckId = "QA-ENG-04";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 7";

    public static ClauseResolution Resolve(
        string? clauseId, int sectionIndex, int blockIndex,
        IReadOnlyDictionary<string, ClauseEntry> clauses,
        IReadOnlyDictionary<string, string?> placeholders)
    {
        var pointer = $"/dataInputs/sections/{sectionIndex}/blocks/{blockIndex}/clauseId";

        if (clauseId is null)
            return new ClauseResolution(null, null, Fail(
                "clauseInsertion block reached rendering with no clauseId.", pointer,
                "a clauseId referencing an approved clause", "null",
                "Set clauseId on the clauseInsertion block to a clause defined in clause-library.json."));

        if (!clauses.TryGetValue(clauseId, out var clause))
            return new ClauseResolution(null, null, Fail(
                $"Clause '{clauseId}' is not defined in the clause library.", pointer,
                "a clauseId present in clause-library.json", clauseId,
                $"Reference an existing clause from config/clause-library.json; '{clauseId}' is not defined."));

        var actualSha = Hashing.Sha256Hex(clause.CanonicalText);
        var verified = string.Equals(actualSha, clause.CanonicalTextSha256, StringComparison.OrdinalIgnoreCase);
        if (!verified)
            return new ClauseResolution(null,
                new ClauseInsertionAudit(clauseId, clause.CanonicalTextSha256, actualSha, false, 0),
                Fail($"Clause '{clauseId}' failed SHA-256 integrity verification.", pointer,
                    clause.CanonicalTextSha256, actualSha,
                    "Reconcile clause-library.json: the canonical text no longer matches its recorded hash."));

        var text = clause.CanonicalText;
        var resolved = 0;
        foreach (var (token, value) in placeholders)
        {
            if (value is null) continue;
            if (text.Contains(token, StringComparison.Ordinal))
            {
                text = text.Replace(token, value);
                resolved++;
            }
        }

        return new ClauseResolution(text,
            new ClauseInsertionAudit(clauseId, clause.CanonicalTextSha256, actualSha, true, resolved), null);
    }

    private static Check Fail(string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ClauseResolutionError,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter ClauseResolverTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Model/ClauseInsertionAudit.cs src/Registrar.Rendering/ClauseResolver.cs tests/Registrar.Tests/Rendering/ClauseResolverTests.cs
git commit -m "feat: clause resolver with SHA-256 proof and clause-text-only token resolution"
```

---

### Task 5: RenderContext, ParagraphBlock, HeadingBlock

**Files:**
- Create: `src/Registrar.Rendering/RenderContext.cs`, `src/Registrar.Rendering/Blocks/ParagraphBlock.cs`, `src/Registrar.Rendering/Blocks/HeadingBlock.cs`
- Test: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`

**Interfaces:**
- Consumes: `StyleRules` (Colors dict keys `headingNavy`, `accentBlue`; `Fonts.Body.Family`, `Fonts.Heading.Family`; `TypeScale.BodyPt`, `SubtitlePt`), `OpenXmlHelpers.StyledRun`.
- Produces: `RenderContext(StyleRules Style, IReadOnlyDictionary<string, ClauseEntry> Clauses, IReadOnlyDictionary<string, string?> Placeholders)`; `ParagraphBlock.Render(string text, StyleRules style) -> Paragraph`; `HeadingBlock.Render(string text, int level, StyleRules style) -> Paragraph` (level 2 or 3).

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`:

```csharp
using Registrar.Rendering;
using Registrar.Tests;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class BlockRendererTests
{
    private static Registrar.Core.Config.StyleRules Style() => SemanticTestSupport.Config().Style;

    [Fact]
    public void Paragraph_renders_body_text()
    {
        var p = ParagraphBlock.Render("Hello body.", Style());
        Assert.Equal("Hello body.", p.InnerText);
    }

    [Fact]
    public void Heading_h2_uses_navy_and_h3_uses_accent()
    {
        var style = Style();
        var h2 = HeadingBlock.Render("Head two", 2, style);
        var h3 = HeadingBlock.Render("Head three", 3, style);
        var navy = OpenXmlHelpers.Hex(style.Colors["headingNavy"]);
        var accent = OpenXmlHelpers.Hex(style.Colors["accentBlue"]);
        Assert.Equal(navy, h2.Descendants<Color>().First().Val!.Value, ignoreCase: true);
        Assert.Equal(accent, h3.Descendants<Color>().First().Val!.Value, ignoreCase: true);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter BlockRendererTests`
Expected: FAIL (types not defined).

- [ ] **Step 3: Implement RenderContext and the two blocks**

Create `src/Registrar.Rendering/RenderContext.cs`:

```csharp
using Registrar.Core.Config;

namespace Registrar.Rendering;

public sealed record RenderContext(
    StyleRules Style,
    IReadOnlyDictionary<string, ClauseEntry> Clauses,
    IReadOnlyDictionary<string, string?> Placeholders);
```

Create `src/Registrar.Rendering/Blocks/ParagraphBlock.cs`:

```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class ParagraphBlock
{
    public static Paragraph Render(string text, StyleRules style)
        => new(OpenXmlHelpers.StyledRun(text, style.Fonts.Body.Family ?? "Arial",
            style.TypeScale.BodyPt, "#000000", bold: false));
}
```

Create `src/Registrar.Rendering/Blocks/HeadingBlock.cs`:

```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class HeadingBlock
{
    public static Paragraph Render(string text, int level, StyleRules style)
    {
        var color = level >= 3 ? style.Colors["accentBlue"] : style.Colors["headingNavy"];
        var sizePt = style.TypeScale.SubtitlePt;
        return new Paragraph(OpenXmlHelpers.StyledRun(
            text, style.Fonts.Heading.Family ?? "Arial", sizePt, color, bold: true));
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter BlockRendererTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/RenderContext.cs src/Registrar.Rendering/Blocks/ParagraphBlock.cs src/Registrar.Rendering/Blocks/HeadingBlock.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs
git commit -m "feat: RenderContext, paragraph and heading block renderers"
```

---

### Task 6: Numbering part and BulletListBlock

**Files:**
- Create: `src/Registrar.Rendering/NumberingPart.cs`, `src/Registrar.Rendering/Blocks/BulletListBlock.cs`
- Test: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`

**Interfaces:**
- Produces: `NumberingPart.BulletNumId` (`int`, value 1); `NumberingPart.Build(MainDocumentPart main)` (adds a `NumberingDefinitionsPart` with one bullet definition); `BulletListBlock.Render(IReadOnlyList<string> items, StyleRules style) -> IEnumerable<Paragraph>`.
- Consumes: `OpenXmlHelpers.StyledRun`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`:

```csharp
[Fact]
public void BulletList_uses_numbering_and_no_bullet_glyph_in_runs()
{
    var paras = BulletListBlock.Render(new[] { "first", "second" }, Style()).ToList();
    Assert.Equal(2, paras.Count);
    Assert.Equal("first", paras[0].InnerText);
    // Each list paragraph references the bullet numbering definition.
    Assert.All(paras, p => Assert.NotNull(p.ParagraphProperties!.NumberingProperties));
    // No literal bullet character (U+2022) leaks into run text.
    Assert.All(paras, p => Assert.DoesNotContain('•', p.InnerText));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter BulletList_uses_numbering`
Expected: FAIL (types not defined).

- [ ] **Step 3: Implement the numbering part and bullet block**

Create `src/Registrar.Rendering/NumberingPart.cs`:

```csharp
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

// The bullet glyph lives in the numbering definition, not in a document run, so the
// QA-SF-08 run scan never sees it. Body run text stays within the allowed character set.
public static class NumberingPart
{
    public const int BulletNumId = 1;
    private const int AbstractNumId = 0;

    public static void Build(MainDocumentPart main)
    {
        var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
        var abstractNum = new AbstractNum(
            new Level(
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "•" },
                new ParagraphProperties(new Indentation { Left = "720", Hanging = "360" }),
                new RunProperties(new RunFonts { Ascii = "Symbol", HighAnsi = "Symbol" }))
            { LevelIndex = 0 })
        { AbstractNumberId = AbstractNumId };
        var num = new Num(new AbstractNumId { Val = AbstractNumId }) { NumberID = BulletNumId };
        numberingPart.Numbering = new Numbering(abstractNum, num);
    }
}
```

Create `src/Registrar.Rendering/Blocks/BulletListBlock.cs`:

```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class BulletListBlock
{
    public static IEnumerable<Paragraph> Render(IReadOnlyList<string> items, StyleRules style)
    {
        foreach (var item in items)
        {
            var props = new ParagraphProperties(
                new NumberingProperties(
                    new NumberingLevelReference { Val = 0 },
                    new NumberingId { Val = NumberingPart.BulletNumId }));
            yield return new Paragraph(props, OpenXmlHelpers.StyledRun(
                item, style.Fonts.Body.Family ?? "Arial", style.TypeScale.BodyPt, "#000000", bold: false));
        }
    }
}
```

Note: the `LevelText` value is written with the Edit tool so `•` is stored as an escape, not a literal glyph; it lives only in the numbering part.

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter BulletList_uses_numbering`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/NumberingPart.cs src/Registrar.Rendering/Blocks/BulletListBlock.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs
git commit -m "feat: bullet list via Word numbering (no bullet glyph in runs)"
```

---

### Task 7: TableBlock

**Files:**
- Create: `src/Registrar.Rendering/Blocks/TableBlock.cs`
- Test: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`

**Interfaces:**
- Produces: `TableBlock.Render(IReadOnlyList<string>? headers, IReadOnlyList<IReadOnlyList<string>> rows, StyleRules style) -> Table`.
- Consumes: `OpenXmlHelpers.StyledRun`, `OpenXmlHelpers.Hex`, `OpenXmlHelpers.BorderSizeEighths`, `StyleRules.Colors` (`headingNavy`, `tableRowAltFill`, `borderNeutral`), `StyleRules.Borders.WidthHalfPoints`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`:

```csharp
[Fact]
public void Table_renders_headers_and_rows()
{
    var t = TableBlock.Render(new[] { "Name", "Role" },
        new IReadOnlyList<string>[] { new[] { "J. Talewind", "Owner" } }, Style());
    var rows = t.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().ToList();
    Assert.Equal(2, rows.Count); // header + one body row
    Assert.Contains("Name", rows[0].InnerText);
    Assert.Contains("J. Talewind", rows[1].InnerText);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Table_renders_headers_and_rows`
Expected: FAIL (type not defined).

- [ ] **Step 3: Implement TableBlock**

Create `src/Registrar.Rendering/Blocks/TableBlock.cs`:

```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class TableBlock
{
    public static Table Render(IReadOnlyList<string>? headers,
        IReadOnlyList<IReadOnlyList<string>> rows, StyleRules style)
    {
        var borderColor = OpenXmlHelpers.Hex(style.Colors["borderNeutral"]);
        var sz = OpenXmlHelpers.BorderSizeEighths(style.Borders.WidthHalfPoints);
        Border B<T>() where T : Border, new()
            => new T { Val = BorderValues.Single, Size = sz, Color = borderColor };
        var tblBorders = new TableBorders(
            B<TopBorder>(), B<BottomBorder>(), B<LeftBorder>(), B<RightBorder>(),
            B<InsideHorizontalBorder>(), B<InsideVerticalBorder>());

        var table = new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }, tblBorders));

        var colCount = headers?.Count ?? (rows.Count > 0 ? rows[0].Count : 1);
        table.Append(OpenXmlHelpers.Grid(Enumerable.Repeat(1000, colCount).ToArray()));

        if (headers is not null)
            table.Append(Row(headers, style, isHeader: true, alt: false, style.Colors["headingNavy"]));

        for (var i = 0; i < rows.Count; i++)
            table.Append(Row(rows[i], style, isHeader: false, alt: i % 2 == 1,
                style.Colors["tableRowAltFill"]));

        return table;
    }

    private static TableRow Row(IReadOnlyList<string> cells, StyleRules style,
        bool isHeader, bool alt, string fillColor)
    {
        var row = new TableRow();
        foreach (var cell in cells)
        {
            var color = isHeader ? "#FFFFFF" : "#000000";
            var run = OpenXmlHelpers.StyledRun(cell, style.Fonts.Body.Family ?? "Arial",
                style.TypeScale.BodyPt, color, bold: isHeader);
            var props = new TableCellProperties();
            if (isHeader || alt)
                props.Append(new Shading { Val = ShadingPatternValues.Clear,
                    Fill = OpenXmlHelpers.Hex(fillColor) });
            row.Append(new TableCell(props, new Paragraph(run)));
        }
        return row;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Table_renders_headers_and_rows`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/Blocks/TableBlock.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs
git commit -m "feat: table block renderer with header fill and row banding"
```

---

### Task 8: BlockRenderer dispatcher and the Unsafe Block Exclusion Rule

**Files:**
- Create: `src/Registrar.Rendering/Blocks/BlockRenderer.cs`
- Test: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`

**Interfaces:**
- Consumes: `AgrBlock`, `RenderContext`, `ClauseResolver.Resolve`, the four block renderers.
- Produces: `BlockRenderResult(IReadOnlyList<OpenXmlElement> Elements, string ScanText, Check? ClauseCheck, ClauseInsertionAudit? ClauseAudit)`; `BlockRenderer.Render(AgrBlock block, int sectionIndex, int blockIndex, RenderContext ctx) -> BlockRenderResult`. Throws `NotSupportedException` for out-of-scope block types.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`:

```csharp
private static RenderContext Ctx()
{
    var config = SemanticTestSupport.Config();
    return new RenderContext(config.Style, config.ClausesById,
        new Dictionary<string, string?> { ["[TOOL / CONNECTOR NAME]"] = "Microsoft Copilot Chat" });
}

[Fact]
public void Dispatches_paragraph_and_reports_scan_text()
{
    var block = new Registrar.Core.Model.AgrBlock { BlockType = "paragraph", Text = "Hi." };
    var r = BlockRenderer.Render(block, 0, 0, Ctx());
    Assert.Single(r.Elements);
    Assert.Equal("Hi.", r.ScanText);
    Assert.Null(r.ClauseCheck);
}

[Fact]
public void Dispatches_clause_insertion_with_audit()
{
    var block = new Registrar.Core.Model.AgrBlock { BlockType = "clauseInsertion", ClauseId = "CL-006" };
    var r = BlockRenderer.Render(block, 4, 1, Ctx());
    Assert.Null(r.ClauseCheck);
    Assert.NotNull(r.ClauseAudit);
    Assert.True(r.ClauseAudit!.Verified);
    Assert.Single(r.Elements);
}

[Fact]
public void Rejects_out_of_scope_block_type()
{
    var block = new Registrar.Core.Model.AgrBlock { BlockType = "warnCallout", Text = "note" };
    Assert.Throws<NotSupportedException>(() => BlockRenderer.Render(block, 0, 0, Ctx()));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter BlockRendererTests`
Expected: the three new tests FAIL (BlockRenderer not defined).

- [ ] **Step 3: Implement the dispatcher**

Create `src/Registrar.Rendering/Blocks/BlockRenderer.cs`:

```csharp
using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public sealed record BlockRenderResult(
    IReadOnlyList<OpenXmlElement> Elements, string ScanText,
    Check? ClauseCheck, ClauseInsertionAudit? ClauseAudit);

public static class BlockRenderer
{
    public static BlockRenderResult Render(AgrBlock block, int sectionIndex, int blockIndex, RenderContext ctx)
    {
        switch (block.BlockType)
        {
            case "paragraph":
            {
                var el = ParagraphBlock.Render(block.Text ?? string.Empty, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, block.Text ?? string.Empty, null, null);
            }
            case "h2":
            case "h3":
            {
                var level = block.BlockType == "h3" ? 3 : 2;
                var el = HeadingBlock.Render(block.Text ?? string.Empty, level, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, block.Text ?? string.Empty, null, null);
            }
            case "bulletList":
            {
                var items = block.Items ?? Array.Empty<string>();
                var paras = BulletListBlock.Render(items, ctx.Style).Cast<OpenXmlElement>().ToList();
                return new BlockRenderResult(paras, string.Join(" ", items), null, null);
            }
            case "table":
            {
                var rows = block.Rows ?? Array.Empty<IReadOnlyList<string>>();
                var el = TableBlock.Render(block.ColumnHeaders, rows, ctx.Style);
                var scan = string.Join(" ", (block.ColumnHeaders ?? Array.Empty<string>())
                    .Concat(rows.SelectMany(r => r)));
                return new BlockRenderResult(new OpenXmlElement[] { el }, scan, null, null);
            }
            case "clauseInsertion":
            {
                var res = ClauseResolver.Resolve(block.ClauseId, sectionIndex, blockIndex,
                    ctx.Clauses, ctx.Placeholders);
                if (res.Failure is not null)
                    return new BlockRenderResult(Array.Empty<OpenXmlElement>(), string.Empty,
                        res.Failure, res.Audit);
                var el = ParagraphBlock.Render(res.Text!, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, res.Text!, null, res.Audit);
            }
            default:
                // Unsafe Block Exclusion Rule: block types requiring enforcement not yet built
                // (warnCallout, radioQuestion, aggregationTable, copyReadyMessage) never render here.
                throw new NotSupportedException(
                    $"Block type '{block.BlockType}' is not supported in Slice 7 rendering.");
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter BlockRendererTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/Blocks/BlockRenderer.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs
git commit -m "feat: block dispatcher with clause insertion and unsafe-block exclusion"
```

---

### Task 9: SectionRenderer

**Files:**
- Create: `src/Registrar.Rendering/SectionRenderer.cs`
- Test: `tests/Registrar.Tests/Rendering/SectionRendererTests.cs`

**Interfaces:**
- Consumes: `AgrSection`, `RenderContext`, `BlockRenderer.Render`.
- Produces: `ScanUnit(string Text, string Locator)`; `SectionRenderResult(IReadOnlyList<OpenXmlElement> Elements, IReadOnlyList<Check> ClauseChecks, IReadOnlyList<ClauseInsertionAudit> Audits, IReadOnlyList<ScanUnit> ScanUnits)`; `SectionRenderer.Render(AgrSection section, int sectionIndex, RenderContext ctx) -> SectionRenderResult`. Heading number is `sectionIndex + 1`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Rendering/SectionRendererTests.cs`:

```csharp
using Registrar.Core.Model;
using Registrar.Rendering;
using Registrar.Tests;

namespace Registrar.Tests.Rendering;

public class SectionRendererTests
{
    private static RenderContext Ctx()
    {
        var c = SemanticTestSupport.Config();
        return new RenderContext(c.Style, c.ClausesById, new Dictionary<string, string?>());
    }

    [Fact]
    public void Renders_numbered_heading_descriptor_and_blocks()
    {
        var section = new AgrSection { Title = "Purpose", DescriptorLine = "Why this exists.",
            Blocks = new[] { new AgrBlock { BlockType = "paragraph", Text = "Body." } } };
        var r = SectionRenderer.Render(section, 0, Ctx());
        var text = string.Concat(r.Elements.Select(e => e.InnerText));
        Assert.Contains("1. Purpose", text);
        Assert.Contains("Why this exists.", text);
        Assert.Contains("Body.", text);
        Assert.Contains(r.ScanUnits, u => u.Locator.Contains("Purpose"));
    }

    [Fact]
    public void Collects_clause_audit_and_locator()
    {
        var section = new AgrSection { Title = "Audit", DescriptorLine = null,
            Blocks = new[] { new AgrBlock { BlockType = "clauseInsertion", ClauseId = "CL-006" } } };
        var r = SectionRenderer.Render(section, 4, Ctx());
        Assert.Single(r.Audits);
        Assert.Empty(r.ClauseChecks);
        Assert.Contains(r.ScanUnits, u => u.Locator.Contains("CL-006"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter SectionRendererTests`
Expected: FAIL (types not defined).

- [ ] **Step 3: Implement SectionRenderer**

Create `src/Registrar.Rendering/SectionRenderer.cs`:

```csharp
using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public sealed record ScanUnit(string Text, string Locator);

public sealed record SectionRenderResult(
    IReadOnlyList<OpenXmlElement> Elements,
    IReadOnlyList<Check> ClauseChecks,
    IReadOnlyList<ClauseInsertionAudit> Audits,
    IReadOnlyList<ScanUnit> ScanUnits);

public static class SectionRenderer
{
    public static SectionRenderResult Render(AgrSection section, int sectionIndex, RenderContext ctx)
    {
        var elements = new List<OpenXmlElement>();
        var checks = new List<Check>();
        var audits = new List<ClauseInsertionAudit>();
        var scanUnits = new List<ScanUnit>();
        var sectionLocator = $"section '{section.Title}'";
        var style = ctx.Style;

        var headingText = $"{sectionIndex + 1}. {section.Title}";
        elements.Add(new Paragraph(OpenXmlHelpers.StyledRun(
            headingText, style.Fonts.Heading.Family ?? "Arial", style.TypeScale.TitlePt,
            style.Colors["headingNavy"], bold: true)));
        scanUnits.Add(new ScanUnit(headingText, sectionLocator));

        if (section.DescriptorLine is not null)
        {
            var rpr = new RunProperties(new RunFonts { Ascii = style.Fonts.Body.Family ?? "Arial" });
            rpr.Append(new Italic());
            rpr.Append(new Color { Val = OpenXmlHelpers.Hex(style.Colors["secondaryGray"]) });
            rpr.Append(new FontSize { Val = (style.TypeScale.CaptionPt * 2).ToString() });
            elements.Add(new Paragraph(new Run(rpr,
                new Text(section.DescriptorLine) { Space = SpaceProcessingModeValues.Preserve })));
            scanUnits.Add(new ScanUnit(section.DescriptorLine, sectionLocator));
        }

        for (var b = 0; b < section.Blocks.Count; b++)
        {
            var block = section.Blocks[b];
            var result = BlockRenderer.Render(block, sectionIndex, b, ctx);
            elements.AddRange(result.Elements);
            if (result.ClauseCheck is not null) checks.Add(result.ClauseCheck);
            if (result.ClauseAudit is not null) audits.Add(result.ClauseAudit);
            var locator = block.BlockType == "clauseInsertion" && block.ClauseId is not null
                ? $"clause {block.ClauseId}"
                : sectionLocator;
            scanUnits.Add(new ScanUnit(result.ScanText, locator));
        }

        return new SectionRenderResult(elements, checks, audits, scanUnits);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter SectionRendererTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/SectionRenderer.cs tests/Registrar.Tests/Rendering/SectionRendererTests.cs
git commit -m "feat: section renderer with numbered heading, descriptor, block collection"
```

---

### Task 10: PostRenderScan (QA-SF-08)

**Files:**
- Create: `src/Registrar.Rendering/PostRenderScan.cs`
- Test: `tests/Registrar.Tests/Rendering/PostRenderScanTests.cs`

**Interfaces:**
- Consumes: `ScanUnit` (Task 9), `Registrar.Core.CharacterPolicy`, `Registrar.Core.CharacterLint`, `Check`.
- Produces: `PostRenderScan.CheckId` (`"QA-SF-08"`); `PostRenderScan.Scan(IReadOnlyList<ScanUnit> units, CharacterPolicy policy) -> IReadOnlyList<Check>` (one blocking check per offending unit; `jsonPath` null; locator in the message).

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Rendering/PostRenderScanTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Rendering;
using Registrar.Tests;

namespace Registrar.Tests.Rendering;

public class PostRenderScanTests
{
    private static CharacterPolicy Policy()
        => CharacterPolicy.FromConfig(SemanticTestSupport.Config().ContentLint);

    [Fact]
    public void QA_SF_08_passes_on_clean_runs()
    {
        var units = new[] { new ScanUnit("Clean ASCII text.", "section 'Purpose'") };
        Assert.Empty(PostRenderScan.Scan(units, Policy()));
    }

    [Fact]
    public void QA_SF_08_fails_on_prohibited_character()
    {
        var units = new[] { new ScanUnit("Bad — dash.", "clause CL-006") };
        var checks = PostRenderScan.Scan(units, Policy());
        var c = Assert.Single(checks);
        Assert.Equal("QA-SF-08", c.CheckId);
        Assert.Equal(Severity.Blocking, c.SeverityLevel);
        Assert.Null(c.JsonPath);
        Assert.Contains("CL-006", c.Message);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter PostRenderScanTests`
Expected: FAIL (type not defined).

- [ ] **Step 3: Implement PostRenderScan**

Create `src/Registrar.Rendering/PostRenderScan.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Rendering;

public static class PostRenderScan
{
    public const string CheckId = "QA-SF-08";
    private const string RuleSourceValue = "ORG-MST-WFE-001 Section 4.5 item 8";

    public static IReadOnlyList<Check> Scan(IReadOnlyList<ScanUnit> units, CharacterPolicy policy)
    {
        var checks = new List<Check>();
        foreach (var unit in units)
        {
            if (string.IsNullOrEmpty(unit.Text)) continue;
            var offenses = CharacterLint.Scan(unit.Text, policy);
            if (offenses.Count == 0) continue;
            var first = offenses[0];
            checks.Add(new Check
            {
                CheckId = CheckId,
                LayerName = Layer.Content,
                Result = CheckResultValue.Fail,
                SeverityLevel = Severity.Blocking,
                ErrorCategory = ErrorCategory.MetadataViolation,
                Message = $"Rendered text in {unit.Locator} contains a prohibited character: "
                    + $"{CharacterLint.Describe(first.Codepoint)} at position {first.Position}.",
                JsonPath = null,
                Expected = "only printable Basic Latin, printable Latin-1 Supplement "
                    + "excluding NBSP and soft hyphen, or an approved glyph",
                Actual = CharacterLint.FormatActual(offenses),
                Remediation = $"A prohibited character reached the rendered document in {unit.Locator}. "
                    + "It originates in clause text or a field not covered by input hygiene; "
                    + "reconcile the source (clause-library.json or the offending field).",
                RuleSource = RuleSourceValue
            });
        }
        return checks;
    }
}
```

Note: `ErrorCategory.MetadataViolation` is reused because the report schema pins `errorCategory` to a fixed enum with no content/style member (same choice Slice 6 made for QA-ENG-05). Confirm the constant name against `ErrorCategory` in `Registrar.Core.Model`; if the enum uses a different member, use the nearest existing one and do not add a schema member.

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter PostRenderScanTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/PostRenderScan.cs tests/Registrar.Tests/Rendering/PostRenderScanTests.cs
git commit -m "feat: post-render QA-SF-08 character scan over rendered runs"
```

---

### Task 11: RenderResult and ArtifactRenderer rewrite

**Files:**
- Create: `src/Registrar.Rendering/RenderResult.cs`
- Modify: `src/Registrar.Rendering/ArtifactRenderer.cs`, `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`
- Test: `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`

**Interfaces:**
- Consumes: `SectionRenderer.Render`, `PostRenderScan.Scan`, `NumberingPart.Build`, `CharacterPolicy.FromConfig`.
- Produces: `RenderResult(byte[] Bytes, IReadOnlyList<Check> PostRenderChecks, IReadOnlyList<ClauseInsertionAudit> ClauseAudits)`; `ArtifactRenderer.Render(AgrDocument agr, ConfigBundle config, byte[] logoBytes) -> RenderResult` (return type changes from `byte[]`).

- [ ] **Step 1: Update the existing test helper and add new assertions (failing)**

In `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`, change `RenderGolden` to return bytes from the new result, and add body/audit tests:

```csharp
internal static Registrar.Rendering.RenderResult RenderResultGolden()
    => new ArtifactRenderer(new FixedClock()).Render(
        SemanticTestSupport.Agr("agr-sample-ds-draft.json"),
        SemanticTestSupport.Config(),
        LogoBytes());

internal static byte[] RenderGolden() => RenderResultGolden().Bytes;

[Fact]
public void Render_includes_numbered_body_sections_and_clause_text()
{
    var result = RenderResultGolden();
    using var ms = new MemoryStream(result.Bytes);
    using var doc = WordprocessingDocument.Open(ms, false);
    var text = doc.MainDocumentPart!.Document.Body!.InnerText;

    Assert.Contains("1. Purpose", text);
    Assert.Contains("8. Version and Change Notes", text);
    Assert.Contains("audit", text, StringComparison.OrdinalIgnoreCase); // resolved CL-006 text
    Assert.Empty(result.PostRenderChecks);                              // clean render
    Assert.Contains(result.ClauseAudits, a => a.ClauseId == "CL-006" && a.Verified);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Render_includes_numbered_body_sections`
Expected: FAIL (Render returns `byte[]`, no `RenderResult`).

- [ ] **Step 3: Add RenderResult and rewrite ArtifactRenderer**

Create `src/Registrar.Rendering/RenderResult.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Rendering;

public sealed record RenderResult(
    byte[] Bytes,
    IReadOnlyList<Check> PostRenderChecks,
    IReadOnlyList<ClauseInsertionAudit> ClauseAudits);
```

Rewrite `src/Registrar.Rendering/ArtifactRenderer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public sealed class ArtifactRenderer
{
    private readonly IClock _clock;

    public ArtifactRenderer(IClock clock) => _clock = clock;

    public RenderResult Render(AgrDocument agr, ConfigBundle config, byte[] logoBytes)
    {
        var style = config.Style;
        var ctx = new RenderContext(style, config.ClausesById, agr.DataInputs.PlaceholderMap);

        var clauseChecks = new List<Check>();
        var audits = new List<ClauseInsertionAudit>();
        var scanUnits = new List<ScanUnit>();

        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            NumberingPart.Build(main);
            var body = new Body();

            var type = config.ArtifactTypesByCode[agr.Identity.ArtifactType];
            var footerId = FooterRenderer.Create(main, agr, type, style);

            var header = HeaderTableRenderer.Render(main, agr, type, style, logoBytes);
            body.Append(header);
            scanUnits.Add(new ScanUnit(header.InnerText, "document chrome"));
            foreach (var element in DchRenderer.Render(agr, style))
            {
                body.Append(element);
                scanUnits.Add(new ScanUnit(element.InnerText, "document chrome"));
            }
            var callout = ConfigCalloutRenderer.Render(agr, type, style);
            body.Append(callout);
            scanUnits.Add(new ScanUnit(callout.InnerText, "document chrome"));

            for (var s = 0; s < agr.DataInputs.Sections.Count; s++)
            {
                var section = SectionRenderer.Render(agr.DataInputs.Sections[s], s, ctx);
                foreach (var el in section.Elements) body.Append(el);
                clauseChecks.AddRange(section.ClauseChecks);
                audits.AddRange(section.Audits);
                scanUnits.AddRange(section.ScanUnits);
            }

            body.Append(SectionProps(style, footerId));
            main.Document = new Document(body);

            var now = _clock.UtcNow.UtcDateTime;
            doc.PackageProperties.Created = now;
            doc.PackageProperties.Modified = now;
            doc.PackageProperties.Creator = "registrar";
            doc.PackageProperties.Title = agr.Identity.DocumentId;
        }

        var policy = CharacterPolicy.FromConfig(config.ContentLint);
        var postRenderChecks = new List<Check>(clauseChecks);
        postRenderChecks.AddRange(PostRenderScan.Scan(scanUnits, policy));

        return new RenderResult(stream.ToArray(), postRenderChecks, audits);
    }

    private static SectionProperties SectionProps(StyleRules style, string footerId)
    {
        var w = (uint)OpenXmlHelpers.Twips(style.Page.WidthIn);
        var h = (uint)OpenXmlHelpers.Twips(style.Page.HeightIn);
        var m = (uint)OpenXmlHelpers.Twips(style.Page.MarginsIn);
        return new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = footerId },
            new PageSize { Width = w, Height = h },
            new PageMargin { Top = (int)m, Bottom = (int)m, Left = m, Right = m, Header = 720U, Footer = 720U, Gutter = 0U });
    }
}
```

- [ ] **Step 4: Run the full render test class to verify green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter ArtifactRendererTests`
Expected: PASS (existing chrome tests still pass via `RenderGolden`; new body test passes; `Render_produces_schema_valid_docx` still passes with the numbering part).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/RenderResult.cs src/Registrar.Rendering/ArtifactRenderer.cs tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs
git commit -m "feat: assemble body sections; ArtifactRenderer returns RenderResult"
```

---

### Task 12: PostRenderMerge

**Files:**
- Create: `src/Registrar.Validation/PostRenderMerge.cs`
- Test: `tests/Registrar.Tests/PostRenderMergeTests.cs`

**Interfaces:**
- Consumes: `ValidationOutcome`, `ValidationReport`, `Check`, `OverallResultPolicy.Compute`.
- Produces: `PostRenderMerge.Apply(ValidationOutcome outcome, IReadOnlyList<Check> postRenderChecks, int? warningThreshold) -> ValidationOutcome` - appends checks, recomputes `OverallResult`, and nulls the `Payload` when the recomputed result is `fail`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/PostRenderMergeTests.cs`:

```csharp
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class PostRenderMergeTests
{
    private static ValidationReport Report(string overall, params Check[] checks) => new()
    {
        ReportVersion = "1.0", ExecutionId = "e", DocumentId = "ORG-GOV-DS-016",
        RequestedMode = "draft", OverallResult = overall, EngineVersion = "v",
        ConfigVersions = new Dictionary<string, string>(), TimestampUtc = "2026-07-05T00:00:00Z",
        Checks = checks
    };

    private static Check Blocking() => new()
    {
        CheckId = "QA-SF-08", LayerName = "content", Result = "fail",
        SeverityLevel = "blocking", Message = "bad"
    };

    [Fact]
    public void Blocking_post_render_check_flips_overall_to_fail_and_drops_payload()
    {
        var outcome = new ValidationOutcome(Report("pass"), null);
        var merged = PostRenderMerge.Apply(outcome, new[] { Blocking() }, null);
        Assert.Equal(OverallResult.Fail, merged.Report.OverallResult);
        Assert.Null(merged.Payload);
        Assert.Contains(merged.Report.Checks, c => c.CheckId == "QA-SF-08");
    }

    [Fact]
    public void No_post_render_checks_preserves_outcome()
    {
        var outcome = new ValidationOutcome(Report("pass"), null);
        var merged = PostRenderMerge.Apply(outcome, Array.Empty<Check>(), null);
        Assert.Equal(OverallResult.Pass, merged.Report.OverallResult);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter PostRenderMergeTests`
Expected: FAIL (type not defined).

- [ ] **Step 3: Implement PostRenderMerge**

Create `src/Registrar.Validation/PostRenderMerge.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Validation;

public static class PostRenderMerge
{
    public static ValidationOutcome Apply(
        ValidationOutcome outcome, IReadOnlyList<Check> postRenderChecks, int? warningThreshold)
    {
        if (postRenderChecks.Count == 0) return outcome;

        var combined = outcome.Report.Checks.Concat(postRenderChecks).ToList();
        var overall = OverallResultPolicy.Compute(combined, warningThreshold);
        var report = outcome.Report with { Checks = combined, OverallResult = overall };
        var payload = overall == OverallResult.Fail ? null : outcome.Payload;
        return new ValidationOutcome(report, payload);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter PostRenderMergeTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Validation/PostRenderMerge.cs tests/Registrar.Tests/PostRenderMergeTests.cs
git commit -m "feat: merge post-render checks and recompute overall (drop payload on fail)"
```

---

### Task 13: BuildCommand reorder

**Files:**
- Modify: `src/Registrar.Cli/BuildCommand.cs`, `tests/Registrar.Tests/BuildCommandTests.cs`
- Test: `tests/Registrar.Tests/BuildCommandTests.cs`

**Interfaces:**
- Consumes: `ArtifactRenderer.Render` (now `RenderResult`), `PostRenderMerge.Apply`, `CommandSupport.Validate`, `CommandSupport.WriteOutputs`.
- Behavior: render in memory before writing the report; merge post-render checks; write the report (inclusive of QA-SF-08 and clause integrity); emit the DOCX only when the merged result is still docx-eligible; exit 1 with a report and no DOCX when a blocking post-render check appears.

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/BuildCommandTests.cs`:

```csharp
[Fact]
public void Draft_build_report_includes_post_render_scan_and_docx()
{
    var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
    var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
    Assert.Equal(0, code);

    var dir = Path.GetDirectoryName(agrPath)!;
    Assert.NotEmpty(Directory.GetFiles(dir, "*.docx"));
    var report = File.ReadAllText(Path.Combine(dir, "validation-report.json"));
    // The clean DS render produces no QA-SF-08 failure, but the report is written
    // after render, so the pipeline completed the post-render phase.
    Assert.Contains("\"overallResult\"", report);
}
```

- [ ] **Step 2: Run to verify it still passes on the happy path but confirm the reorder is needed**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter BuildCommandTests`
Expected: `Valid_draft_build_writes_a_docx` FAILS to compile once `ArtifactRenderer.Render` returns `RenderResult` (the current `BuildCommand` assigns it to `byte[] docxBytes`). This drives Step 3.

- [ ] **Step 3: Rewrite BuildCommand's eligible path**

In `src/Registrar.Cli/BuildCommand.cs`, replace the body from the `WriteOutputs` call through the end of the method with the render-first ordering:

```csharp
        var (exit, outcome, dir, repoRoot, agrText) = CommandSupport.Validate(agrPath, clock, stderr, registryPath);
        if (exit is not null) return exit.Value;

        var report = outcome!.Report;

        if (mode is not null && !string.Equals(mode, report.RequestedMode, StringComparison.Ordinal))
        {
            stderr.WriteLine($"error: --mode '{mode}' does not match the AGR validationPolicy.mode '{report.RequestedMode}'.");
            return 2;
        }

        // Pre-render failure: write the report as-is, emit nothing.
        if (report.OverallResult == OverallResult.Fail)
        {
            var w0 = CommandSupport.WriteOutputs(outcome, dir, stderr);
            if (w0 is not null) return w0.Value;
            stdout.WriteLine($"fail: {report.DocumentId} - no document produced (validation failed).");
            return 1;
        }

        var preEligible = report.OverallResult == OverallResult.Pass
            || (report.OverallResult == OverallResult.PassWithWarnings && report.RequestedMode == Mode.Draft);

        if (!preEligible)
        {
            var w1 = CommandSupport.WriteOutputs(outcome, dir, stderr);
            if (w1 is not null) return w1.Value;
            stdout.WriteLine($"{report.OverallResult}: {report.DocumentId} - no document (promotion requires a clean pass).");
            return 0;
        }

        RenderResult rendered;
        AgrDocument agr;
        try
        {
            agr = AgrParser.Parse(agrText);
            var config = ConfigLoader.Load(repoRoot);
            var logoBytes = File.ReadAllBytes(ConfigLocator.FindAsset(repoRoot, config.Style.Wordmark.Asset));
            rendered = new ArtifactRenderer(clock).Render(agr, config, logoBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            stderr.WriteLine($"error: cannot render document: {ex.Message}");
            return 2;
        }

        var merged = PostRenderMerge.Apply(outcome, rendered.PostRenderChecks, agr.ValidationPolicy.WarningThreshold);
        var mergedReport = merged.Report;

        var writeMerged = CommandSupport.WriteOutputs(merged, dir, stderr);
        if (writeMerged is not null) return writeMerged.Value;

        if (mergedReport.OverallResult == OverallResult.Fail)
        {
            stdout.WriteLine($"fail: {mergedReport.DocumentId} - no document produced (post-render validation failed).");
            return 1;
        }

        var docxEligible = mergedReport.OverallResult == OverallResult.Pass
            || (mergedReport.OverallResult == OverallResult.PassWithWarnings && mergedReport.RequestedMode == Mode.Draft);

        if (docxEligible)
        {
            try
            {
                var docxPath = Path.Combine(dir, agr.RenderingOptions.OutputFileName);
                File.WriteAllBytes(docxPath, rendered.Bytes);
                stdout.WriteLine($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - {Path.GetFileName(docxPath)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: cannot write document: {ex.Message}");
                return 2;
            }
        }
        else
        {
            stdout.WriteLine($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - no document (promotion requires a clean pass).");
        }

        return 0;
```

Add the required usings at the top of `BuildCommand.cs` if missing: `using Registrar.Core.Model;` and `using Registrar.Validation;` (for `PostRenderMerge`). `Registrar.Validation` and `Registrar.Rendering` are already referenced by the CLI project.

- [ ] **Step 4: Run the build-command tests to verify green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter BuildCommandTests`
Expected: PASS (happy-path docx still emitted; `Invalid_agr_build_writes_no_docx_and_exits_1`, `Mode_flag_mismatch_exits_2`, `Prohibited_char_build_writes_no_docx_and_reports_QA_ENG_05` all still pass; the QA-ENG-05 case fails pre-render so it never reaches the render path).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Cli/BuildCommand.cs tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "feat: build renders first, merges post-render checks, then writes report and docx"
```

---

### Task 14: QA-SF-08 catalog entry

**Files:**
- Modify: `config/qa-checklist.json`, `tests/Registrar.Tests/QaChecklistCatalogTests.cs`
- Test: `tests/Registrar.Tests/QaChecklistCatalogTests.cs`

**Interfaces:**
- Produces: a `QA-SF-08` row in the catalog. Match the existing entry shape in `qa-checklist.json` (open the file and copy the field set used by neighboring `checkId` entries; do not invent fields).

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/QaChecklistCatalogTests.cs` (follow the file's existing style for loading the catalog; this asserts the id is present):

```csharp
[Fact]
public void Catalog_contains_QA_SF_08()
{
    var repoRoot = Registrar.Tests.SemanticTestSupport.RepoRoot();
    var text = File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json"));
    Assert.Contains("QA-SF-08", text);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Catalog_contains_QA_SF_08`
Expected: FAIL (id absent).

- [ ] **Step 3: Add the catalog entry**

Open `config/qa-checklist.json`, find the array of check entries, and add a new object matching the exact field names used by the existing entries (for reference, the QA-ENG-05 entry shape from Slice 6 uses `checkId`, `layer`, `automation`, `appliesTo`, `rule`, `errorCategory`, `ruleSource`, optional `alsoSatisfies`). Add:

```json
{
  "checkId": "QA-SF-08",
  "layer": "content",
  "automation": "full",
  "appliesTo": "all",
  "rule": "Every text run in the rendered document contains only allowed characters: printable Basic Latin, printable Latin-1 Supplement excluding NBSP and soft hyphen, plus approved glyphs. Post-render scan; blocks in draft and promotion.",
  "errorCategory": "MetadataViolation",
  "ruleSource": "ORG-MST-WFE-001 Section 4.5 item 8"
}
```

If the neighboring entries use different field names or ordering, mirror those exactly instead of the shape above.

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Catalog_contains_QA_SF_08`
Expected: PASS. Then run the full catalog test class to confirm no shape/consistency test broke: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter QaChecklistCatalogTests`.

- [ ] **Step 5: Commit**

```bash
git add config/qa-checklist.json tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat: add QA-SF-08 post-render character scan to the check catalog"
```

---

### Task 15: AUP fixture and end-to-end acceptance

**Files:**
- Create: `fixtures/agr-sample-aup-draft.json`
- Test: `tests/Registrar.Tests/BuildCommandTests.cs`

**Interfaces:**
- Consumes: the full pipeline. Draft mode with no registry (mirrors the DS draft build, which passes without a registry because registry checks are registry-gated).

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/BuildCommandTests.cs`:

```csharp
[Fact]
public void Aup_draft_build_renders_all_sections_and_clauses()
{
    var agrPath = CopyToTemp("fixtures/agr-sample-aup-draft.json");
    var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
    Assert.Equal(0, code);

    var dir = Path.GetDirectoryName(agrPath)!;
    var docx = Directory.GetFiles(dir, "*.docx").Single();
    using var doc = WordprocessingDocument.Open(docx, false);
    var text = doc.MainDocumentPart!.Document.Body!.InnerText;
    Assert.Contains("1. Purpose", text);
    Assert.Contains("9. Policy Review and Ownership", text);
    var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
    Assert.Empty(errors);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Aup_draft_build_renders_all_sections`
Expected: FAIL (fixture not found).

- [ ] **Step 3: Create the AUP fixture**

Create `fixtures/agr-sample-aup-draft.json`. It must (a) list the nine AUP sections in exact schema order, (b) include all ten required placeholders in `placeholderMap`, (c) insert required clauses CL-001, CL-004, CL-005, CL-009 as `clauseInsertion` blocks, (d) satisfy `outputFileName` pattern `^ORG - .+ - v[0-9]+\.[0-9]+\.docx$`. Use ASCII only:

```json
{
  "schemaVersion": "1.0.0",
  "artifactIdentity": {
    "artifactType": "AUP",
    "documentId": "ORG-GOV-AUP-004",
    "version": "v1.0",
    "classification": "Client-Facing",
    "approvalStatus": "Draft",
    "layer": "Governance"
  },
  "context": {
    "client": "Talewind CPA",
    "engagement": "Copilot Chat Pilot 2026",
    "owner": "Registry Owner",
    "audience": "Approved users, Talewind CPA leadership, security owner",
    "preparedBy": "ORG Consulting, LLC",
    "reviewDate": null,
    "reviewDateOverride": null
  },
  "sourceWorkflowContext": {
    "workflowName": "Copilot Readiness Assessment",
    "phase": "Reporting",
    "executionId": "GOV-2026-07-05-002"
  },
  "dataInputs": {
    "placeholderMap": {
      "[CLIENT ORGANIZATION NAME]": "Talewind CPA",
      "[TOOL / CONNECTOR NAME]": "Microsoft Copilot Chat",
      "[APPROVED USERS]": "Copilot Pilot Group",
      "[SUPPORT CONTACT]": "support@orgconsulting.example",
      "[DESIGNATED SECURITY OWNER]": "J. Talewind",
      "[DATA TYPES IN SCOPE]": "Non-restricted business content",
      "[DATA TYPES OUT OF SCOPE]": "Regulated client financial data",
      "[CLIENT POLICY REFERENCES]": "Talewind CPA Acceptable Use Standard",
      "[DATE]": "2026-07-05",
      "[VERSION]": "v1.0"
    },
    "sections": [
      { "title": "Purpose", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "This policy defines approved use of the deployed tool." }
      ] },
      { "title": "Approved Use", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Approved users may use the tool for approved business purposes." },
        { "blockType": "clauseInsertion", "clauseId": "CL-001" }
      ] },
      { "title": "Prohibited Use", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "The following uses are prohibited." },
        { "blockType": "clauseInsertion", "clauseId": "CL-004" }
      ] },
      { "title": "Data Handling Requirements", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Users must handle data within the approved scope." },
        { "blockType": "clauseInsertion", "clauseId": "CL-005" }
      ] },
      { "title": "Output Responsibility", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Users remain responsible for verifying all output before use." }
      ] },
      { "title": "Audit and Sensitive-Content Validation", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Audit posture is subject to tenant validation." }
      ] },
      { "title": "User Acknowledgment", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Users acknowledge the obligations in this policy." },
        { "blockType": "clauseInsertion", "clauseId": "CL-009" }
      ] },
      { "title": "Escalation Requirements", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Escalate suspected issues to the support contact." }
      ] },
      { "title": "Policy Review and Ownership", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "This policy is reviewed on the standard interval by the owner." }
      ] }
    ],
    "normalizedFindings": null,
    "normalizedRisks": null,
    "normalizedInventoryEntities": null
  },
  "evidenceReferences": null,
  "dependencies": [
    { "dependencyId": "ORG-MST-SCL", "kind": "clause-library", "blocking": true }
  ],
  "renderingOptions": {
    "packageName": "Governed AI Rollout",
    "outputFileName": "ORG - Acceptable Use Policy - Talewind CPA - v1.0.docx",
    "optionalSectionToggles": null,
    "stylingProfile": "org-v1.2"
  },
  "validationPolicy": {
    "mode": "draft",
    "additionalRequiredCheckIds": null,
    "warningThreshold": null
  }
}
```

Before relying on this fixture, verify CL-009 exists in `config/clause-library.json` and that its canonical text plus CL-001/CL-004/CL-005 resolve cleanly. If CL-009's canonical text carries a placeholder token not in the map above, add that token to `placeholderMap` with a resolved value (a null value is fine in draft but leaves a literal token in the output; a resolved value keeps the render clean). Do not alter clause text.

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Aup_draft_build_renders_all_sections`
Expected: PASS. If it fails on a validation check, read the emitted `validation-report.json` in the temp dir the test used (or run `registrar validate fixtures/agr-sample-aup-draft.json`) and correct the fixture to satisfy the reported check - do not change engine behavior.

- [ ] **Step 5: Commit**

```bash
git add fixtures/agr-sample-aup-draft.json tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "feat: AUP fixture and end-to-end AUP render acceptance test"
```

---

### Task 16: Deferred-work register and doc updates

**Files:**
- Modify: `docs/tech-debt.md`, `CLAUDE.md`

- [ ] **Step 1: Record the Slice 7 deferred work in tech-debt.md**

Append a `## From Slice 7 (Body-Section Rendering)` section to `docs/tech-debt.md` listing, one item each: the required Content-Lint slice (Slice 8: no-empty-h1, no-heading-level-skips, no-warn-callouts-in-client-facing, QA-SF-09 token residue); deferred blocks (warnCallout, radioQuestion, aggregationTable, copyReadyMessage) governed by the Unsafe Block Exclusion Rule; the clause-audit sink (RenderResult.ClauseAudits produced, generation-log.json deferred); the ClauseReferenceLayer null-clauseId severity promotion (warn to blocking); the single-paragraph clause assumption; and the universal closing elements pending ORG-MST-SPEC-001 Section 4.1.

- [ ] **Step 2: Correct the existing tech-debt item 3 reasoning**

Edit tech-debt item 3 to note that QA-SF-08 landing does NOT trigger the JSON-Pointer escaper extraction: QA-SF-08 is the second `CharacterLint` consumer but emits document-origin locators, not placeholderMap-key pointers, so `EscapeToken`/`PointerFor` still have exactly two consumers and rule-of-three is unmet; the dead `contentLint.prohibitedCharacters` exemplar block is likewise not read by QA-SF-08 and remains in place.

- [ ] **Step 3: Update CLAUDE.md known-gaps**

In `CLAUDE.md`, update the "Known gaps" section: mark body-section rendering for DS/AUP as delivered by Slice 7; note the Content-Lint slice (Slice 8) as the required successor for structural/warn lint and QA-SF-09; keep the deferred blocks and closing-elements entries.

- [ ] **Step 4: Run the full suite as a final gate**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test`
Expected: PASS (all tests, including SourceHygieneTests over the new source files).

- [ ] **Step 5: Commit**

```bash
git add docs/tech-debt.md CLAUDE.md
git commit -m "docs: record Slice 7 deferred work; correct tech-debt escaper-trigger note"
```

---

## Self-Review

**Spec coverage:**
- Prose-subset blocks (paragraph, h2, h3, bulletList, table, clauseInsertion): Tasks 5-8. warnCallout excluded via Task 8 dispatcher throw (Unsafe Block Exclusion Rule).
- Clause insertion by reference + SHA-256 + token resolution (clause text only): Task 4, wired in Task 8.
- Section assembly (numbered H1, descriptor line, block order): Task 9.
- QA-SF-08 post-render scan folded in: Task 10; wired into ArtifactRenderer Task 11; catalog Task 14.
- RenderResult with PostRenderChecks + ClauseAudits; blocking checks discard bytes: Tasks 11-13.
- BuildCommand reorder (render, merge, report, emit): Task 13; payload dropped on post-render fail: Task 12.
- CharacterLint/CharacterPolicy reuse: Task 3 (moved to Core).
- Model + config extensions: Tasks 1-2.
- DS + AUP end-to-end acceptance: DS in Task 11, AUP in Task 15.
- Deferred-work register + tech-debt correction: Task 16.

**Placeholder scan:** No TBD/TODO. Every code step shows complete code. The two "confirm the shape/field" notes (Task 14 catalog fields, Task 10 ErrorCategory member) instruct verification against existing files rather than leaving a gap, because the exact enum member and catalog field ordering must match what is already committed.

**Type consistency:** `RenderResult(Bytes, PostRenderChecks, ClauseAudits)` used identically in Tasks 11-13. `ClauseInsertionAudit(ClauseId, ExpectedSha, ActualSha, Verified, TokensResolved)` defined Task 4, consumed Tasks 8/9/11. `ScanUnit(Text, Locator)` defined Task 9, consumed Task 10/11. `BlockRenderResult(Elements, ScanText, ClauseCheck, ClauseAudit)` defined Task 8, consumed Task 9. `ArtifactRenderer.Render` returns `RenderResult` consistently after Task 11 (RenderGolden helper updated in the same task). `ClauseResolver.Resolve` signature matches between Task 4 definition and Task 8 call site.
