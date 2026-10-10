# warnCallout Block Rendering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the `warnCallout` block as a config-styled warn-red callout box, removing it from the Unsafe Block Exclusion Rule.

**Architecture:** A new `WarnCalloutBlock` renderer (mirroring `ConfigCalloutRenderer`) draws a bordered, warn-red-filled table from the block's `text` plus a config-driven label. `warnCallout` is added to `BlockRenderer`'s switch, `BlockTypeSupportLayer`'s supported set (QA-ENG-06), and `TokenResidueLayer`'s scanned fields (QA-ENG-07). Its promotion gate (QA-SF-09 / `WarnCalloutLayer`) already exists and is unchanged. Block types are not section-schema gated, so no `artifact-types.json` change.

**Tech Stack:** .NET 8, C#, DocumentFormat.OpenXml, System.Text.Json, xUnit.

## Global Constraints

- .NET 8 / C#; build/test with `dotnet build` / `dotnet test` from the repo root.
- US English; no emojis; no em/en dashes or decorative unicode in source, config, or generated strings (enforced on engine source by `SourceHygieneTests`; plain ASCII hyphens only).
- Configuration over code: the callout's label and colors come from `config/style-rules.json` (label from a new `warnCallout` object; colors from the existing `warnRedBorder` / `warnRedFill` keys). No artifact constants, clause text, or repository paths in core logic.
- Determinism: the renderer is a pure function of (AGR + config); no clock, no GUIDs. Rendered DOCX bytes are reproducible (goldens byte-compare artifactSha256).
- JSON config field names are camelCase; config records use `[JsonPropertyName]` or match the JSON via the loader's options.
- Reference design: `docs/superpowers/specs/2026-07-15-warn-callout-rendering-design.md`.
- `warnCallout` renders in draft (any artifact) and at promotion only for non-client-facing artifacts; QA-SF-09 fails it closed at client-facing promotion (already enforced, unchanged).

---

### Task 1: warnCallout style config and binding

**Files:**
- Create: `src/Registrar.Core/Config/WarnCalloutStyle.cs`
- Modify: `config/style-rules.json`
- Modify: `src/Registrar.Core/Config/ConfigBundle.cs`
- Modify: `src/Registrar.Core/Config/ConfigLoader.cs`
- Test: `tests/Registrar.Tests/Config/WarnCalloutConfigTests.cs`

**Interfaces:**
- Consumes: `ConfigLoader.Load` (existing), the `styleRoot` JSON node in `ConfigLoader` (the parsed `style-rules.json`), `SemanticTestSupport.Config()` (existing test helper returning a `ConfigBundle`).
- Produces:
  - `Registrar.Core.Config.WarnCalloutStyle` record: `Label` (string, required).
  - `ConfigBundle.WarnCallout` (`WarnCalloutStyle`, required).
  - `config/style-rules.json` `warnCallout` object `{ "label": "Warning" }`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Config/WarnCalloutConfigTests.cs`:

```csharp
using Registrar.Tests;

namespace Registrar.Tests.Config;

public class WarnCalloutConfigTests
{
    [Fact]
    public void WarnCallout_label_binds_from_style_rules()
    {
        var config = SemanticTestSupport.Config();
        Assert.Equal("Warning", config.WarnCallout.Label);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~WarnCalloutConfigTests`
Expected: BUILD FAILS - `ConfigBundle.WarnCallout` and `WarnCalloutStyle` do not exist.

- [ ] **Step 3: Create the config record**

Create `src/Registrar.Core/Config/WarnCalloutStyle.cs`:

```csharp
namespace Registrar.Core.Config;

// The warn-callout presentation (config-driven so a v1.x template pack is a config
// release, not a rebuild). Only the label lives here; the box colors are the existing
// visualIdentity keys warnRedBorder / warnRedFill, referenced by the renderer.
public sealed record WarnCalloutStyle
{
    public required string Label { get; init; }
}
```

- [ ] **Step 4: Add the config to style-rules.json**

In `config/style-rules.json`, add a top-level `warnCallout` object next to the existing `radioOptionPattern` object (match the file's existing indentation and comma placement):

```json
  "warnCallout": {
    "label": "Warning"
  },
```

- [ ] **Step 5: Expose it on ConfigBundle**

In `src/Registrar.Core/Config/ConfigBundle.cs`, add the property next to `RadioPattern`:

```csharp
    public required WarnCalloutStyle WarnCallout { get; init; }
```

- [ ] **Step 6: Bind it in ConfigLoader**

In `src/Registrar.Core/Config/ConfigLoader.cs`, next to the `radioPattern` binding (around line 31), deserialize the new object:

```csharp
        var warnCallout = styleRoot["warnCallout"]!.Deserialize<WarnCalloutStyle>(Options)!;
```

and set it in the returned `ConfigBundle` initializer (next to `RadioPattern = radioPattern,`):

```csharp
            WarnCallout = warnCallout,
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~WarnCalloutConfigTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Core/Config/WarnCalloutStyle.cs config/style-rules.json src/Registrar.Core/Config/ConfigBundle.cs src/Registrar.Core/Config/ConfigLoader.cs tests/Registrar.Tests/Config/WarnCalloutConfigTests.cs
git commit -m "feat: warnCallout style config (label) bound into ConfigBundle"
```

---

### Task 2: WarnCalloutBlock renderer and BlockRenderer wiring

**Files:**
- Create: `src/Registrar.Rendering/Blocks/WarnCalloutBlock.cs`
- Modify: `src/Registrar.Rendering/RenderContext.cs`
- Modify: `src/Registrar.Rendering/ArtifactRenderer.cs:24`
- Modify: `src/Registrar.Rendering/Blocks/BlockRenderer.cs`
- Modify: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs` (the `Ctx()` helper at line ~70, and add a case test)
- Modify: `tests/Registrar.Tests/Rendering/SectionRendererTests.cs:12` (its `RenderContext` construction)
- Test: `tests/Registrar.Tests/Rendering/WarnCalloutBlockTests.cs`

**Interfaces:**
- Consumes: `WarnCalloutStyle` and `ConfigBundle.WarnCallout` (Task 1); `OpenXmlHelpers` (`StyledRun`, `Line`, `Grid`, `Hex`, `BorderSizeEighths`, `Twips`); `StyleRules` (`Colors`, `Fonts.Body`, `TypeScale`, `Borders.WidthHalfPoints`, `Page`).
- Produces:
  - `Registrar.Rendering.WarnCalloutBlock.Render(string text, WarnCalloutStyle warn, StyleRules style) -> Table`.
  - `RenderContext` gains a 5th positional member `WarnCalloutStyle Warn`.
  - `BlockRenderer.Render` handles `case "warnCallout"`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Rendering/WarnCalloutBlockTests.cs`:

```csharp
using System.Linq;
using Registrar.Rendering;
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class WarnCalloutBlockTests
{
    private static StyleRules Style() => SemanticTestSupport.Config().Style;
    private static WarnCalloutStyle Warn() => SemanticTestSupport.Config().WarnCallout;

    [Fact]
    public void Renders_a_bordered_box_with_the_label_and_text()
    {
        var table = WarnCalloutBlock.Render("Handle with care.", Warn(), Style());

        var text = table.InnerText;
        Assert.Contains("Warning", text);
        Assert.Contains("Handle with care.", text);

        // The box uses the warn-red border color from config, not a default.
        var expectedBorder = OpenXmlHelpers.Hex(Style().Colors["warnRedBorder"]);
        var top = table.Descendants<TopBorder>().Single();
        Assert.Equal(expectedBorder, top.Color!.Value);

        // The cell is shaded with the warn-red fill.
        var expectedFill = OpenXmlHelpers.Hex(Style().Colors["warnRedFill"]);
        var shading = table.Descendants<Shading>().Single();
        Assert.Equal(expectedFill, shading.Fill!.Value);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~WarnCalloutBlockTests`
Expected: BUILD FAILS - `WarnCalloutBlock` does not exist.

- [ ] **Step 3: Create the renderer**

Create `src/Registrar.Rendering/Blocks/WarnCalloutBlock.cs` (mirrors `ConfigCalloutRenderer`; the `OpenXmlTableBorders` alias avoids the `TableCell`/`Table` `TableBorders` ambiguity, same as `ConfigCalloutRenderer`):

```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;

namespace Registrar.Rendering;

// A warn callout: a full-width box, warn-red border and light-red fill, with a bold
// config-driven label line above the Governor-supplied text. Structure mirrors
// ConfigCalloutRenderer. Colors are the existing visualIdentity keys warnRedBorder /
// warnRedFill; the label comes from config (WarnCalloutStyle.Label). Pure: no clock,
// no ambient state, so the rendered bytes stay reproducible.
public static class WarnCalloutBlock
{
    public static Table Render(string text, WarnCalloutStyle warn, StyleRules style)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";
        var borderColor = OpenXmlHelpers.Hex(style.Colors["warnRedBorder"]);
        var borderSize = OpenXmlHelpers.BorderSizeEighths(style.Borders.WidthHalfPoints);
        var labelColor = style.Colors["warnRedBorder"];

        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = OpenXmlHelpers.Hex(style.Colors["warnRedFill"]) }),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun(warn.Label, family, style.TypeScale.SubtitlePt, labelColor, true)),
            new Paragraph(OpenXmlHelpers.StyledRun(text, family, style.TypeScale.BodyPt, "#000000", false)));

        var contentWidth = OpenXmlHelpers.Twips(style.Page.WidthIn - (2 * style.Page.MarginsIn));

        return new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new OpenXmlTableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = borderSize, Color = borderColor },
                    new LeftBorder { Val = BorderValues.Single, Size = borderSize, Color = borderColor },
                    new BottomBorder { Val = BorderValues.Single, Size = borderSize, Color = borderColor },
                    new RightBorder { Val = BorderValues.Single, Size = borderSize, Color = borderColor },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None })),
            OpenXmlHelpers.Grid(contentWidth),
            new TableRow(cell));
    }
}
```

- [ ] **Step 4: Add Warn to RenderContext**

In `src/Registrar.Rendering/RenderContext.cs`, add the 5th positional member:

```csharp
public sealed record RenderContext(
    StyleRules Style,
    IReadOnlyDictionary<string, ClauseEntry> Clauses,
    IReadOnlyDictionary<string, string?> Placeholders,
    RadioOptionPattern Radio,
    WarnCalloutStyle Warn);
```

- [ ] **Step 5: Update the three RenderContext construction sites**

`src/Registrar.Rendering/ArtifactRenderer.cs:24` - add `config.WarnCallout`:

```csharp
        var ctx = new RenderContext(style, config.ClausesById, agr.DataInputs.PlaceholderMap, config.RadioPattern, config.WarnCallout);
```

`tests/Registrar.Tests/Rendering/SectionRendererTests.cs:12` - add `c.WarnCallout`:

```csharp
        return new RenderContext(c.Style, c.ClausesById, new Dictionary<string, string?>(), c.RadioPattern, c.WarnCallout);
```

`tests/Registrar.Tests/Rendering/BlockRendererTests.cs` (the `Ctx()` helper around line 70) - add `config.WarnCallout` as the fifth argument to its `new RenderContext(...)`. Read the helper and append the argument so it compiles.

- [ ] **Step 6: Add the BlockRenderer case**

In `src/Registrar.Rendering/Blocks/BlockRenderer.cs`, add a `case "warnCallout"` before the `default`:

```csharp
            case "warnCallout":
            {
                var el = WarnCalloutBlock.Render(block.Text ?? string.Empty, ctx.Warn, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, block.Text ?? string.Empty, null, null);
            }
```

- [ ] **Step 7: Add a BlockRenderer case test**

Add to `tests/Registrar.Tests/Rendering/BlockRendererTests.cs` (reusing its `Ctx()` helper and the existing block-construction style in that file):

```csharp
    [Fact]
    public void Renders_a_warnCallout_block_and_returns_its_text_as_scan_text()
    {
        var block = new Registrar.Core.Model.AgrBlock { BlockType = "warnCallout", Text = "Do not deploy without sign-off." };

        var result = BlockRenderer.Render(block, 0, 0, Ctx());

        Assert.Single(result.Elements);
        Assert.Equal("Do not deploy without sign-off.", result.ScanText);
        Assert.Null(result.ClauseCheck);
    }
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~WarnCalloutBlockTests|FullyQualifiedName~BlockRendererTests"`
Expected: PASS (WarnCalloutBlock render + the new BlockRenderer case + all pre-existing BlockRenderer tests).

- [ ] **Step 9: Commit**

```bash
git add src/Registrar.Rendering/Blocks/WarnCalloutBlock.cs src/Registrar.Rendering/RenderContext.cs src/Registrar.Rendering/ArtifactRenderer.cs src/Registrar.Rendering/Blocks/BlockRenderer.cs tests/Registrar.Tests/Rendering/WarnCalloutBlockTests.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs tests/Registrar.Tests/Rendering/SectionRendererTests.cs
git commit -m "feat: render warnCallout blocks as a warn-red callout box"
```

---

### Task 3: Validation coverage (QA-ENG-06 supported set, QA-ENG-07 residue) and fixture retarget

**Files:**
- Modify: `src/Registrar.Validation/BlockTypeSupportLayer.cs`
- Modify: `src/Registrar.Validation/TokenResidueLayer.cs`
- Modify: `fixtures/agr-sample-ds-unsupported-block.json`
- Regenerate: `fixtures/expected/agr-sample-ds-unsupported-block/validation-report.json`
- Test: `tests/Registrar.Tests/BlockTypeSupportLayerTests.cs`, `tests/Registrar.Tests/TokenResidueLayerTests.cs`

**Interfaces:**
- Consumes: `BlockTypeSupportLayer.SupportedBlockTypes` (existing set), `TokenResidueLayer`'s per-block field switch (existing `case "paragraph"` etc.), the CLI `test --fixture <name> --update` golden regenerator.
- Produces: `warnCallout` in the supported set; `warnCallout.text` scanned by QA-ENG-07.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Registrar.Tests/BlockTypeSupportLayerTests.cs`:

```csharp
    [Fact]
    public void QA_ENG_06_accepts_warnCallout()
    {
        Assert.Contains("warnCallout", BlockTypeSupportLayer.SupportedBlockTypes);
    }
```

Add to `tests/Registrar.Tests/TokenResidueLayerTests.cs` (mirror an existing residue test in that file; it constructs an AGR via `SemanticTestSupport` and a section with a block carrying a `[TOKEN]`):

```csharp
    [Fact]
    public void QA_ENG_07_flags_token_residue_in_a_warnCallout()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("{ \"blockType\": \"clauseInsertion\", \"clauseId\": \"CL-006\" }",
                     "{ \"blockType\": \"warnCallout\", \"text\": \"Residual [CLIENT ORGANIZATION NAME] token.\" }");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var checks = new TokenResidueLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        Assert.Contains(checks, c => c.CheckId == "QA-ENG-07" && c.Result == CheckResultValue.Fail);
    }
```

(If the exact block string in `agr-sample-ds-draft.json` differs, adjust the `.Replace` search text to a clauseInsertion or paragraph block actually present so the fixture stays schema-valid; the point is a `warnCallout` whose `text` carries a bracketed token.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BlockTypeSupportLayerTests.QA_ENG_06_accepts_warnCallout|FullyQualifiedName~TokenResidueLayerTests.QA_ENG_07_flags_token_residue_in_a_warnCallout"`
Expected: FAIL - `warnCallout` is not yet in the supported set, and `TokenResidueLayer` does not scan `warnCallout`.

- [ ] **Step 3: Add warnCallout to the supported set**

In `src/Registrar.Validation/BlockTypeSupportLayer.cs`, add `"warnCallout"` to `SupportedBlockTypes`:

```csharp
    public static readonly IReadOnlySet<string> SupportedBlockTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "paragraph", "h2", "h3", "bulletList", "table", "clauseInsertion", "radioQuestion", "warnCallout"
    };
```

And add `warnCallout` to the two `Expected` strings in that file that list the supported set (the class-level list in `SupportedBlockTypes` comment is optional; the `Fail(...)` `Expected` string on line ~48 must be updated to include `warnCallout` so the remediation stays accurate).

- [ ] **Step 4: Scan warnCallout text in TokenResidueLayer**

In `src/Registrar.Validation/TokenResidueLayer.cs`, in the per-block `switch (block.BlockType)`, add a `warnCallout` case alongside `paragraph` (both yield `/text`):

```csharp
            case "paragraph":
            case "warnCallout":
                if (block.Text is not null)
                    yield return ("/text", block.Text);
                break;
```

(Merge with the existing `case "paragraph"` exactly as the file structures it - the existing paragraph case already yields `("/text", block.Text)`; add `case "warnCallout":` above it so both share that body.)

- [ ] **Step 5: Retarget the QA-ENG-06 fixture**

`fixtures/agr-sample-ds-unsupported-block.json` currently uses a `warnCallout` block to trigger QA-ENG-06, which now renders. Change that block's `blockType` from `warnCallout` to `aggregationTable` (still unsupported), keeping the rest of the block/fixture unchanged, so the fixture still fails QA-ENG-06:

Find the `{ "blockType": "warnCallout", ... }` block and change `"warnCallout"` to `"aggregationTable"`.

- [ ] **Step 6: Regenerate the fixture golden and run the residue/support tests**

Run: `dotnet run --project src/Registrar.Cli -- test --fixture agr-sample-ds-unsupported-block --update`
Expected: `UPDATED agr-sample-ds-unsupported-block`. Then confirm the golden still reports a QA-ENG-06 failure for `aggregationTable`:

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BlockTypeSupportLayerTests|FullyQualifiedName~TokenResidueLayerTests"`
Expected: PASS.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS - all tests green, 0 warnings. (The retargeted fixture's golden now names `aggregationTable`; the `BuildCommandTests` case that asserts QA-ENG-06 on this fixture still passes because it still fails closed.)

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Validation/BlockTypeSupportLayer.cs src/Registrar.Validation/TokenResidueLayer.cs fixtures/agr-sample-ds-unsupported-block.json fixtures/expected/agr-sample-ds-unsupported-block/validation-report.json tests/Registrar.Tests/BlockTypeSupportLayerTests.cs tests/Registrar.Tests/TokenResidueLayerTests.cs
git commit -m "feat: accept warnCallout in QA-ENG-06 and scan its text in QA-ENG-07; retarget QA-ENG-06 fixture"
```

---

### Task 4: Integration fixtures and end-to-end rendering

**Files:**
- Create: `fixtures/agr-sample-ds-warn-callout-draft.json`
- Create: `fixtures/agr-sample-raf-warn-callout-promotion.json`
- Regenerate: `fixtures/expected/agr-sample-ds-warn-callout-draft/` and `fixtures/expected/agr-sample-raf-warn-callout-promotion/`
- Test: `tests/Registrar.Tests/BuildCommandTests.cs`

**Interfaces:**
- Consumes: `BuildCommand.Run` (existing), the fixture golden runner, the existing `agr-sample-ds-draft.json` and `agr-sample-raf-promotion.json` as bases, `fixtures/registry-sample.csv`.
- Produces: two new fixtures whose sections carry a `warnCallout` block, plus their goldens.

- [ ] **Step 1: Create the draft fixture**

Copy `fixtures/agr-sample-ds-draft.json` to `fixtures/agr-sample-ds-warn-callout-draft.json`. Into one existing section's `blocks` array (keep every section `title` unchanged so QA-ENG-02 still passes), add a `warnCallout` block, e.g. into the first section:

```json
        { "blockType": "warnCallout", "text": "This deployment summary is a draft and not yet approved for distribution." }
```

Ensure the block text contains no bracketed `[TOKEN]` (it would trip QA-ENG-07) and no prohibited characters. Leave `validationPolicy.mode` as `draft`.

- [ ] **Step 2: Create the promotion fixture**

Copy `fixtures/agr-sample-raf-promotion.json` to `fixtures/agr-sample-raf-warn-callout-promotion.json`. Add a `warnCallout` block into one existing section's `blocks` (titles unchanged). RAF is classification "Controlled Signoff" (not client-facing), so QA-SF-09 does not gate it and the callout renders at promotion. Keep `validationPolicy.mode` as `promotion`.

- [ ] **Step 3: Write the failing integration tests**

Add to the existing `BuildCommandTests` class:

```csharp
    [Fact]
    public void Draft_build_renders_a_warn_callout()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-warn-callout-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        using var doc = WordprocessingDocument.Open(docx, false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Warning", text);
        Assert.Contains("not yet approved for distribution", text);
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Promotion_build_of_a_non_client_facing_artifact_renders_a_warn_callout()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-raf-warn-callout-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            "promotion", registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        using var doc = WordprocessingDocument.Open(docx, false);
        Assert.Contains("Warning", doc.MainDocumentPart!.Document!.Body!.InnerText);
    }
```

- [ ] **Step 4: Run tests to verify they fail (or need registry rows)**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BuildCommandTests.Draft_build_renders_a_warn_callout|FullyQualifiedName~BuildCommandTests.Promotion_build_of_a_non_client_facing_artifact_renders_a_warn_callout"`
Expected: FAIL - the fixtures are not yet discovered/rendered as intended, or (for the promotion case) the RAF document id must exist in `fixtures/registry-sample.csv` in the expected status. If the promotion test fails on a registry lookup (unregistered id or wrong status), that is a fixture-data problem: confirm the RAF fixture's `documentId` and version match a row in `registry-sample.csv`; if the base `agr-sample-raf-promotion.json` already builds green against that registry, the copy will too. Do not weaken the assertion - fix the fixture data.

- [ ] **Step 5: Regenerate the goldens**

The code from Tasks 1-3 already renders `warnCallout`, so these fixtures build. Generate their goldens:

Run: `dotnet run --project src/Registrar.Cli -- test --fixture agr-sample-ds-warn-callout-draft --update`
Run: `dotnet run --project src/Registrar.Cli -- test --fixture agr-sample-raf-warn-callout-promotion --update`
Expected: `UPDATED ...` for each.

- [ ] **Step 6: Run the tests and the full suite**

Run: `dotnet test`
Expected: PASS - the two new integration tests, the regenerated fixture goldens (auto-discovered by the fixture runner), and all pre-existing tests, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add fixtures/agr-sample-ds-warn-callout-draft.json fixtures/agr-sample-raf-warn-callout-promotion.json fixtures/expected/agr-sample-ds-warn-callout-draft fixtures/expected/agr-sample-raf-warn-callout-promotion tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "test: end-to-end warnCallout rendering in draft and non-client-facing promotion"
```

---

### Task 5: Documentation reconciliation

**Files:**
- Modify: `CLAUDE.md` (Known gaps: deferred block types)
- Modify: `docs/tech-debt.md` (item 8)
- Modify: `docs/ROADMAP.md`, `docs/governor-briefing.md` (and annotate historical `docs/v2-*` if they enumerate the deferred blocks)

**Interfaces:**
- Consumes: nothing (documentation only). Produces: updated status text. No code, no tests.

- [ ] **Step 1: Update CLAUDE.md**

In `CLAUDE.md` "Known gaps", update the deferred-block-types note: `warnCallout` now renders (as a config-styled warn-red callout box, QA-ENG-06 accepts it, QA-ENG-07 scans its text, QA-SF-09 gates it at client-facing promotion). Three block types are no longer all deferred - only `aggregationTable` and `copyReadyMessage` remain deferred and fail closed. Match the existing wording style; plain ASCII hyphens only.

- [ ] **Step 2: Update tech-debt item 8**

In `docs/tech-debt.md`, extend item 8's "Resolved for ..." note (it already records `radioQuestion` resolved 2026-07-14) with a parallel line: `warnCallout` resolved 2026-07-15 - rendered via `WarnCalloutBlock`, accepted by QA-ENG-06, text scanned by QA-ENG-07, gated at client-facing promotion by QA-SF-09. Two block types remain deferred and still fail closed: `aggregationTable`, `copyReadyMessage`. Follow the file's existing resolved-item convention.

- [ ] **Step 3: Reconcile the other docs**

`docs/ROADMAP.md` and `docs/governor-briefing.md`: update the "Remaining block types" / deferred-block wording to list only `aggregationTable` and `copyReadyMessage` as deferred, noting `warnCallout` shipped 2026-07-15. If `docs/v2-acceptance-roadmap.md` or `docs/v2-coding-agent-directive.md` enumerate the deferred blocks as historical milestone records, annotate rather than rewrite (matching the established convention). Use the date 2026-07-15. If the pre-commit AI-tell lint hook rejects a commit, read its message and fix the prose (do NOT use `--no-verify`).

- [ ] **Step 4: Verify the tree is still green**

Run: `dotnet test`
Expected: PASS (documentation changes do not affect tests).

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md docs/tech-debt.md docs/ROADMAP.md docs/governor-briefing.md docs/v2-acceptance-roadmap.md docs/v2-coding-agent-directive.md
git commit -m "docs: warnCallout renders; two deferred block types remain"
```

---

## Self-Review

**Spec coverage:**
- Config-driven label + warn palette reuse -> Task 1 (config) + Task 2 (renderer). Covered.
- WarnCalloutBlock renders a warn-red box with label + text -> Task 2. Covered.
- BlockRenderer case + ScanText (QA-SF-08 coverage) -> Task 2 Steps 6-7. Covered.
- QA-ENG-06 supported set -> Task 3 Step 3. Covered.
- QA-ENG-07 warnCallout text residue -> Task 3 Step 4 + test. Covered.
- QA-SF-09 unchanged (already gates) -> no task; noted. Covered.
- Retarget the QA-ENG-06 fixture -> Task 3 Steps 5-6. Covered.
- Draft render + non-client-facing promotion render -> Task 4. Covered.
- No section-schema change (block types not schema-gated) -> honored; no artifact-types.json edit in any task. Covered.
- Determinism (golden byte-compare) -> Task 3/4 golden regen. Covered.
- Docs reconciliation (tech-debt item 8, CLAUDE.md, roadmap) -> Task 5. Covered.

**Placeholder scan:** No TBD/TODO/"add appropriate X". Steps that depend on exact fixture strings (Task 3 Step 1, Task 4 Steps 1-2) tell the implementer how to adapt if a search string differs, which is guidance, not a placeholder. Clean.

**Type consistency:** `WarnCalloutStyle` (`Label`), `ConfigBundle.WarnCallout`, `RenderContext(..., RadioOptionPattern Radio, WarnCalloutStyle Warn)`, `WarnCalloutBlock.Render(string, WarnCalloutStyle, StyleRules)`, and `ctx.Warn` are named identically across Tasks 1-2. The renderer consumes real `StyleRules` members used identically in `ConfigCalloutRenderer` (`Colors["warnRedBorder"]` / `["warnRedFill"]`, `Fonts.Body`, `TypeScale.SubtitlePt`/`BodyPt`, `Borders.WidthHalfPoints`, `Page.WidthIn`/`MarginsIn`) and `OpenXmlHelpers` (`StyledRun`, `Line`, `Grid`, `Hex`, `BorderSizeEighths`, `Twips`). The three `RenderContext` construction sites (ArtifactRenderer, SectionRendererTests, BlockRendererTests `Ctx()`) are all updated in Task 2 Step 5. Consistent.
