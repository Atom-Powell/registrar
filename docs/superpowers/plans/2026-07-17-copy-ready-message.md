# copyReadyMessage Block Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render `copyReadyMessage` as a config-styled callout box and adopt it in the DCN delivery message, landing the last deferred block type.

**Architecture:** `CopyReadyMessageBlock` clones `WarnCalloutBlock` (a bordered, filled, config-labeled box) with neutral colors. It is wired through the same config-binding chain as `WarnCalloutStyle`. Unlike `aggregationTable`, it carries authored text, so QA-ENG-07 scans it. The DCN's "Suggested Delivery Message" section switches from a paragraph to this block.

**Tech Stack:** .NET 8, C#, Open XML SDK (DocumentFormat.OpenXml), xUnit.

## Global Constraints

- Fail closed: any controlled-document violation blocks the deliverable; no partial output.
- Zero LLM calls at runtime.
- Configuration over code: block styling lives in `/config` as versioned data; core logic holds no artifact constants.
- Output hygiene: US English; no emojis; no em/en dashes or decorative unicode in generated docs, config strings, or code.
- Every QA check ID gets at least one passing and one failing test named after the checkId.
- TDD: failing test first; commit after each green step. Full suite green before each commit.
- Reuse existing `visualIdentity` color keys; introduce NO new colors.
- `style-rules.json` AND `artifact-types.json` are in the validation-report `configVersions` hash set: editing either stales every fixture golden (a hash-only refresh, verified). `qa-checklist.json` is NOT in that set.

---

## File Structure

**Created:**
- `src/Registrar.Core/Config/CopyReadyMessageStyle.cs` - the config record (label). One responsibility.
- `src/Registrar.Rendering/Blocks/CopyReadyMessageBlock.cs` - renders the box. One responsibility.
- `tests/Registrar.Tests/Rendering/CopyReadyMessageBlockTests.cs`
- `tests/Registrar.Tests/CopyReadyMessageValidationTests.cs` - QA-ENG-07 scan pass/fail.

**Modified:**
- `config/style-rules.json` - add `copyReadyMessage` style object.
- `src/Registrar.Core/Config/ConfigLoader.cs` - deserialize + bind it.
- `src/Registrar.Core/Config/ConfigBundle.cs` - add `CopyReadyMessage` property.
- `src/Registrar.Rendering/RenderContext.cs` - add `CopyReadyMessage` parameter.
- `src/Registrar.Rendering/ArtifactRenderer.cs` - pass `config.CopyReadyMessage`.
- `src/Registrar.Rendering/Blocks/BlockRenderer.cs` - dispatch `copyReadyMessage`.
- `src/Registrar.Validation/BlockTypeSupportLayer.cs` - add to `SupportedBlockTypes`.
- `src/Registrar.Validation/TokenResidueLayer.cs` - add `copyReadyMessage` to the scanned `text` case.
- `config/qa-checklist.json` - QA-ENG-06 rule text: nothing deferred anymore.
- `config/artifact-types.json` - DCN description.
- `fixtures/agr-sample-dcn-draft.json`, `fixtures/agr-sample-dcn-promotion.json` - retarget delivery message.
- `tests/Registrar.Tests/Rendering/BlockRendererTests.cs` - retarget the two deferred-type references; add sample.
- `docs/governor-instructions.md`, `docs/governor-contract.md` - block-type reconciliation.
- `fixtures/expected/**` - golden refreshes.

---

## Task 1: copyReadyMessage config style and binding chain

**Files:**
- Create: `src/Registrar.Core/Config/CopyReadyMessageStyle.cs`
- Modify: `config/style-rules.json`, `src/Registrar.Core/Config/ConfigLoader.cs` (lines 32, 57 area), `src/Registrar.Core/Config/ConfigBundle.cs`, `src/Registrar.Rendering/RenderContext.cs`, `src/Registrar.Rendering/ArtifactRenderer.cs:24`
- Test: `tests/Registrar.Tests/Config/CopyReadyMessageConfigTests.cs`

**Interfaces:**
- Produces: `CopyReadyMessageStyle` (record with `string Label`); `ConfigBundle.CopyReadyMessage`; `RenderContext.CopyReadyMessage` (trailing parameter).

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Config/CopyReadyMessageConfigTests.cs`:

```csharp
using Registrar.Tests;
using Xunit;

namespace Registrar.Tests.Config;

public class CopyReadyMessageConfigTests
{
    [Fact]
    public void Config_binds_copyReadyMessage_label()
    {
        var config = SemanticTestSupport.Config();
        Assert.False(string.IsNullOrWhiteSpace(config.CopyReadyMessage.Label));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~Config_binds_copyReadyMessage_label"`
Expected: FAIL - `ConfigBundle` has no `CopyReadyMessage` (compile error).

- [ ] **Step 3: Create the style record**

Create `src/Registrar.Core/Config/CopyReadyMessageStyle.cs`:

```csharp
namespace Registrar.Core.Config;

// The copy-ready-message presentation (config-driven so a template pack is a config
// release, not a rebuild). Only the label lives here; the box colors are existing
// visualIdentity keys (borderNeutral / dchFill / headingNavy), referenced by the renderer.
public sealed record CopyReadyMessageStyle
{
    public required string Label { get; init; }
}
```

- [ ] **Step 4: Add the config data**

In `config/style-rules.json`, add a sibling to `warnCallout`:

```json
"copyReadyMessage": { "label": "Copy-ready message" },
```

- [ ] **Step 5: Bind it in ConfigLoader**

In `src/Registrar.Core/Config/ConfigLoader.cs`, next to the `warnCallout` deserialize (line ~32):

```csharp
        var copyReadyMessage = styleRoot["copyReadyMessage"]!.Deserialize<CopyReadyMessageStyle>(Options)!;
```

And in the `return new ConfigBundle { ... }` initializer, next to `WarnCallout = warnCallout,` (line ~57):

```csharp
            CopyReadyMessage = copyReadyMessage,
```

- [ ] **Step 6: Add the ConfigBundle property**

In `src/Registrar.Core/Config/ConfigBundle.cs`, after `WarnCallout`:

```csharp
    public required CopyReadyMessageStyle CopyReadyMessage { get; init; }
```

- [ ] **Step 7: Thread it into RenderContext**

In `src/Registrar.Rendering/RenderContext.cs`, add the trailing parameter:

```csharp
public sealed record RenderContext(
    StyleRules Style,
    IReadOnlyDictionary<string, ClauseEntry> Clauses,
    IReadOnlyDictionary<string, string?> Placeholders,
    RadioOptionPattern Radio,
    WarnCalloutStyle Warn,
    IReadOnlyList<ClauseEntry> ClauseLibrary,
    CopyReadyMessageStyle CopyReadyMessage);
```

In `src/Registrar.Rendering/ArtifactRenderer.cs:24`, append the argument:

```csharp
        var ctx = new RenderContext(style, config.ClausesById, agr.DataInputs.PlaceholderMap, config.RadioPattern, config.WarnCallout, config.Clauses, config.CopyReadyMessage);
```

- [ ] **Step 8: Build and fix positional call sites**

Run: `dotnet build`
Expected: build succeeds. If any test helper constructs `RenderContext` positionally, append `config.CopyReadyMessage` (or a `new CopyReadyMessageStyle { Label = "Copy-ready message" }`) as the trailing argument. Grep `new RenderContext(` across `tests/` to find them.

- [ ] **Step 9: Run the config test**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~Config_binds_copyReadyMessage_label"`
Expected: PASS.

- [ ] **Step 10: Refresh goldens for the style-rules.json hash change (mandatory)**

Editing `style-rules.json` changes its SHA-256, staling every golden's `configVersions.style-rules.json` line.

Run: `dotnet run --project src/Registrar.Cli -- test --all --update`

Verify hash-only:
Run: `git diff --stat fixtures/expected/` (expect only `validation-report.json` files changed)
Run: `git diff fixtures/expected/ | grep -E "^[+-]" | grep -vE "^(\+\+\+|---)" | grep -viE "style-rules.json|[0-9a-f]{16}"` (expect NO output)

If any DOCX / generation-log / artifact-manifest / promotion-payload / non-hash line changed, STOP and report DONE_WITH_CONCERNS.

- [ ] **Step 11: Full suite green, then commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add src/Registrar.Core/Config/CopyReadyMessageStyle.cs config/style-rules.json src/Registrar.Core/Config/ConfigLoader.cs src/Registrar.Core/Config/ConfigBundle.cs src/Registrar.Rendering/RenderContext.cs src/Registrar.Rendering/ArtifactRenderer.cs tests/Registrar.Tests/Config/CopyReadyMessageConfigTests.cs fixtures/expected/
git commit -m "feat: copyReadyMessage config style and binding chain"
```

---

## Task 2: CopyReadyMessageBlock renderer and dispatch

**Files:**
- Create: `src/Registrar.Rendering/Blocks/CopyReadyMessageBlock.cs`
- Modify: `src/Registrar.Rendering/Blocks/BlockRenderer.cs`, `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`
- Test: `tests/Registrar.Tests/Rendering/CopyReadyMessageBlockTests.cs`

**Interfaces:**
- Consumes: `RenderContext.CopyReadyMessage`, `WarnCalloutBlock` (the pattern to mirror), `OpenXmlHelpers`, `BlockRenderResult`.
- Produces: `CopyReadyMessageBlock.Render(string text, CopyReadyMessageStyle style, StyleRules styleRules) -> Table`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Rendering/CopyReadyMessageBlockTests.cs`:

```csharp
using DocumentFormat.OpenXml.Wordprocessing;
using Registrar.Rendering;
using Xunit;

namespace Registrar.Tests.Rendering;

public class CopyReadyMessageBlockTests
{
    [Fact]
    public void Renders_a_box_with_label_and_message_text()
    {
        var config = SemanticTestSupport.Config();
        var block = new Registrar.Core.Model.AgrBlock
        {
            BlockType = "copyReadyMessage",
            Text = "Attached is your governed delivery package. Please review and confirm."
        };
        var result = BlockRenderer.Render(block, 0, 0,
            new RenderContext(config.Style, config.ClausesById, new Dictionary<string, string?>(),
                config.RadioPattern, config.WarnCallout, config.Clauses, config.CopyReadyMessage));

        var table = Assert.IsType<Table>(Assert.Single(result.Elements));
        Assert.Contains(config.CopyReadyMessage.Label, table.InnerText);
        Assert.Contains("Attached is your governed delivery package", table.InnerText);
        Assert.Equal(block.Text, result.ScanText);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~CopyReadyMessageBlockTests"`
Expected: FAIL - `copyReadyMessage` hits the `default` branch and throws `NotSupportedException`.

- [ ] **Step 3: Implement the renderer**

Create `src/Registrar.Rendering/Blocks/CopyReadyMessageBlock.cs`, mirroring `WarnCalloutBlock` but with neutral colors (border `borderNeutral`, fill `dchFill`, label color `headingNavy`):

```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;

namespace Registrar.Rendering;

// A copy-ready message: a full-width box with a neutral border and light fill, a bold
// config-driven label line above the operator-facing message text. Structure mirrors
// WarnCalloutBlock; colors are existing visualIdentity keys borderNeutral / dchFill and
// the headingNavy label. Pure: no clock, no ambient state.
public static class CopyReadyMessageBlock
{
    public static Table Render(string text, CopyReadyMessageStyle copyReady, StyleRules style)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";
        var borderColor = OpenXmlHelpers.Hex(style.Colors["borderNeutral"]);
        var borderSize = OpenXmlHelpers.BorderSizeEighths(style.Borders.WidthHalfPoints);
        var labelColor = style.Colors["headingNavy"];

        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = OpenXmlHelpers.Hex(style.Colors["dchFill"]) }),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun(copyReady.Label, family, style.TypeScale.SubtitlePt, labelColor, true)),
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

If any referenced member (`style.Colors[...]`, `TypeScale.SubtitlePt`, `OpenXmlHelpers.*`) differs from what `WarnCalloutBlock.cs` actually uses, match `WarnCalloutBlock.cs` exactly - it is the source of truth for these APIs.

- [ ] **Step 4: Dispatch it from BlockRenderer**

In `src/Registrar.Rendering/Blocks/BlockRenderer.cs`, add a case before `default:` (mirror the `warnCallout` case):

```csharp
            case "copyReadyMessage":
            {
                var el = CopyReadyMessageBlock.Render(block.Text ?? string.Empty, ctx.CopyReadyMessage, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, block.Text ?? string.Empty, null, null);
            }
```

Update the `default` branch comment: no schema block type remains deferred; the branch is now a guard for unknown/future types.

- [ ] **Step 5: Retarget the two tests that used copyReadyMessage as the deferred type**

In `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`:

Test `Rejects_out_of_scope_block_type` (around line 96) currently uses `copyReadyMessage`. copyReadyMessage now dispatches, so retarget it to a synthetic unknown type that genuinely falls to the `default` branch:

```csharp
    [Fact]
    public void Rejects_out_of_scope_block_type()
    {
        // No schema block type is deferred any more; the renderer's default branch still
        // guards against an unknown/future block type.
        var block = new Registrar.Core.Model.AgrBlock { BlockType = "notARenderableBlock", Text = "note" };
        Assert.Throws<NotSupportedException>(() => BlockRenderer.Render(block, 0, 0, Ctx()));
    }
```

In `QA_ENG_06_supported_set_matches_renderer_capability` (around line 179), change the `deferred` loop from `new[] { "copyReadyMessage" }` to a synthetic unknown type, and update the comment (do NOT add `copyReadyMessage` to `samples` yet - that happens in Task 3 with the support-set change):

```csharp
        // No schema block type is deferred. The renderer still rejects an unknown type.
        foreach (var deferred in new[] { "notARenderableBlock" })
        {
            Assert.DoesNotContain(deferred, BlockTypeSupportLayer.SupportedBlockTypes);
            Assert.Throws<NotSupportedException>(() =>
                BlockRenderer.Render(
                    new Registrar.Core.Model.AgrBlock { BlockType = deferred, Text = "x" }, 0, 0, Ctx()));
        }
```

Note: after this task, `QA_ENG_06_supported_set_matches_renderer_capability`'s `samples.Keys` still equals `SupportedBlockTypes` (copyReadyMessage is in neither yet), so the equality assertion still holds. Task 3 adds copyReadyMessage to both.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~CopyReadyMessageBlockTests"`
Then: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BlockRendererTests"`
Expected: PASS. No config file was touched, so no golden change; if the full suite shows golden failures, STOP and report.

- [ ] **Step 7: Full suite, then commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add src/Registrar.Rendering/Blocks/CopyReadyMessageBlock.cs src/Registrar.Rendering/Blocks/BlockRenderer.cs tests/Registrar.Tests/Rendering/CopyReadyMessageBlockTests.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs
git commit -m "feat: render copyReadyMessage as a config-styled box, dispatch from BlockRenderer"
```

---

## Task 3: QA-ENG-06 admits copyReadyMessage (nothing deferred)

**Files:**
- Modify: `src/Registrar.Validation/BlockTypeSupportLayer.cs`, `config/qa-checklist.json`, `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`, `tests/Registrar.Tests/BlockTypeSupportLayerTests.cs`

**Interfaces:**
- Produces: QA-ENG-06 accepts a `copyReadyMessage` block; the supported set now covers every schema block type.

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/BlockTypeSupportLayerTests.cs` (mirror the existing passing-case tests' helper):

```csharp
    [Fact]
    public void QA_ENG_06_passes_copyReadyMessage()
    {
        var ctx = ContextWithBlock(new AgrBlock { BlockType = "copyReadyMessage", Text = "Delivery message." });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-06");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~QA_ENG_06_passes_copyReadyMessage"`
Expected: FAIL - `copyReadyMessage` is not yet in `SupportedBlockTypes`, so QA-ENG-06 fails it.

- [ ] **Step 3: Add to the supported set**

In `src/Registrar.Validation/BlockTypeSupportLayer.cs`, add `"copyReadyMessage"` to `SupportedBlockTypes`, and add it to the `Expected` string(s) that enumerate supported types.

- [ ] **Step 4: Update the QA-ENG-06 catalog text**

In `config/qa-checklist.json`, update the `QA-ENG-06` `rule`: every schema block type now renders; nothing is deferred. Set `rule` to:

```
Every dataInputs.sections[].blocks[].blockType is one this engine version renders: paragraph, h2, h3, bulletList, table, clauseInsertion, radioQuestion, warnCallout, aggregationTable, copyReadyMessage. An aggregationTable is valid only with a live aggregationSource (clauseLibrary); placeholderDictionary is reserved and fails closed until the PDX slice. All ten block types in the AGR schema are now built. An unknown blockType fails closed with a reported check before rendering is attempted. Blocks in draft and promotion.
```

(qa-checklist.json is not in configVersions, so this does not stale goldens.)

- [ ] **Step 5: Add copyReadyMessage to the invariant test samples**

In `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`, in `QA_ENG_06_supported_set_matches_renderer_capability`, add to the `samples` dictionary:

```csharp
            ["copyReadyMessage"] = new() { BlockType = "copyReadyMessage", Text = "x" },
```

This keeps `samples.Keys == SupportedBlockTypes` true now that the layer includes copyReadyMessage. The synthetic-unknown `deferred` loop from Task 2 stays as-is.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BlockTypeSupportLayerTests"`
Then: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~QA_ENG_06_supported_set_matches_renderer_capability"`
Expected: PASS.

- [ ] **Step 7: Full suite, then commit**

Run: `dotnet test`
Expected: all pass. (No configVersions file changed; no golden refresh.)

```bash
git add src/Registrar.Validation/BlockTypeSupportLayer.cs config/qa-checklist.json tests/Registrar.Tests/Rendering/BlockRendererTests.cs tests/Registrar.Tests/BlockTypeSupportLayerTests.cs
git commit -m "feat: QA-ENG-06 admits copyReadyMessage; all ten block types now built"
```

---

## Task 4: QA-ENG-07 scans copyReadyMessage

**Files:**
- Modify: `src/Registrar.Validation/TokenResidueLayer.cs`
- Test: `tests/Registrar.Tests/CopyReadyMessageValidationTests.cs`

**Interfaces:**
- Produces: a `copyReadyMessage` whose text contains a bracketed token yields a QA-ENG-07 fail; a clean one does not.

- [ ] **Step 1: Write the failing/guarding tests**

Create `tests/Registrar.Tests/CopyReadyMessageValidationTests.cs` (reuse the config-backed SemanticContext helper pattern from `TokenResidueLayerTests.cs` / the QA-ENG-07 tests in `AggregationSourceValidationTests.cs`):

```csharp
using Registrar.Core.Model;
using Registrar.Validation;
using Xunit;

namespace Registrar.Tests;

public class CopyReadyMessageValidationTests
{
    [Fact]
    public void QA_ENG_07_flags_token_in_copyReadyMessage()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "copyReadyMessage",
            Text = "Dear [CLIENT ORGANIZATION NAME], your package is attached."
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.Contains(checks, c => c.CheckId == "QA-ENG-07" && c.Result == CheckResultValue.Fail);
    }

    [Fact]
    public void QA_ENG_07_passes_clean_copyReadyMessage()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "copyReadyMessage",
            Text = "Dear team, your governed package is attached. Please review and confirm."
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-07");
    }
}
```

`ContextWithBlockAndConfig` builds a `SemanticContext` with real loaded config (so the residue pattern is present) and a one-section AGR containing the block. Mirror the helper used by the existing QA-ENG-07 tests (`AggregationSourceValidationTests.cs` has this exact helper - reuse or copy its shape). Use a bracketed token that matches the configured pattern (`[CLIENT ORGANIZATION NAME]` does).

- [ ] **Step 2: Run tests to verify status**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~CopyReadyMessageValidationTests"`
Expected: `QA_ENG_07_flags_token_in_copyReadyMessage` FAILS (copyReadyMessage is not yet in `ScanFields`, so its text is not scanned and no residue check is raised). The clean-case test passes.

- [ ] **Step 3: Add copyReadyMessage to the scanned set**

In `src/Registrar.Validation/TokenResidueLayer.cs`, in `ScanFields`, add `copyReadyMessage` to the `case` that yields `("/text", block.Text)` (alongside `paragraph`, `warnCallout`, `h2`, `h3`):

```csharp
            case "paragraph":
            case "warnCallout":
            case "copyReadyMessage":
            case "h2":
            case "h3":
                yield return ("/text", block.Text);
                break;
```

Update the trailing comment so it stays accurate: `copyReadyMessage` IS scanned (it carries operator-facing authored text an unresolved token would corrupt), in contrast to `aggregationTable`/`clauseInsertion`, which are not.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~CopyReadyMessageValidationTests"`
Expected: PASS (both).

- [ ] **Step 5: Full suite, then commit**

Run: `dotnet test`
Expected: all pass. (No configVersions file changed; no golden refresh.)

```bash
git add src/Registrar.Validation/TokenResidueLayer.cs tests/Registrar.Tests/CopyReadyMessageValidationTests.cs
git commit -m "feat: QA-ENG-07 scans copyReadyMessage for token residue"
```

---

## Task 5: Adopt copyReadyMessage in the DCN

**Files:**
- Modify: `fixtures/agr-sample-dcn-draft.json`, `fixtures/agr-sample-dcn-promotion.json`, `config/artifact-types.json`
- Test: `tests/Registrar.Tests/Rendering/DcnCopyReadyMessageTests.cs`

**Interfaces:**
- Produces: the DCN "Suggested Delivery Message" section renders a copyReadyMessage box; DCN builds pass in draft and promotion.

- [ ] **Step 1: Write the failing end-to-end test**

Create `tests/Registrar.Tests/Rendering/DcnCopyReadyMessageTests.cs` (use the same full-build harness the SCL/DS end-to-end tests use - inspect `SclEndToEndTests.cs` and reuse its `BuildCommand.Run` pattern):

```csharp
using Registrar.Tests;
using Xunit;

namespace Registrar.Tests.Rendering;

public class DcnCopyReadyMessageTests
{
    [Theory]
    [InlineData("draft")]
    [InlineData("promotion")]
    public void Dcn_builds_with_copy_ready_message_box(string mode)
    {
        var config = SemanticTestSupport.Config();
        var fixture = mode == "draft" ? "agr-sample-dcn-draft" : "agr-sample-dcn-promotion";
        var result = TestBuildHarness.Run(fixture, mode); // match SclEndToEndTests' actual harness call
        Assert.True(result.Passed, result.Diagnostics);
        Assert.Contains(config.CopyReadyMessage.Label, result.DocxBodyText);
    }
}
```

Before writing, open `SclEndToEndTests.cs` and use its exact harness type/method names and its DOCX-body-text accessor; the snippet above is a template, not literal API.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~DcnCopyReadyMessageTests"`
Expected: FAIL - the DCN label is absent because the delivery message still renders as a plain paragraph.

- [ ] **Step 3: Retarget the DCN fixtures**

In `fixtures/agr-sample-dcn-draft.json` and `fixtures/agr-sample-dcn-promotion.json`, in the "Suggested Delivery Message" section, replace the single `{ "blockType": "paragraph", "text": "..." }` block with `{ "blockType": "copyReadyMessage", "text": "..." }`, keeping the exact same `text`. Change only `blockType`.

- [ ] **Step 4: Update the DCN description**

In `config/artifact-types.json`, in the DCN entry's `description`, replace the clause stating the delivery message renders as a body paragraph and the distinct copyReadyMessage block is not required, with wording that the DCN's suggested delivery message renders as a copyReadyMessage box. Keep it ASCII, no em/en dashes.

- [ ] **Step 5: Regenerate goldens (DCN real change + others hash-only)**

Run: `dotnet run --project src/Registrar.Cli -- test --all --update`

The DCN goldens (`fixtures/expected/agr-sample-dcn-draft/`, `agr-sample-dcn-promotion/`) change for real (the delivery message now renders differently). Every OTHER fixture golden changes only by the `artifact-types.json` hash line. Verify:

Run: `git status --short fixtures/expected/ | grep -v dcn` then inspect: for all NON-dcn goldens, confirm the only change is the hash:
Run: `git diff fixtures/expected/ -- ':!fixtures/expected/agr-sample-dcn-draft' ':!fixtures/expected/agr-sample-dcn-promotion' | grep -E "^[+-]" | grep -vE "^(\+\+\+|---)" | grep -viE "artifact-types.json|[0-9a-f]{16}"` (expect NO output)

Open a DCN golden's `validation-report.json` and confirm `overallResult` is still `pass`. The DCN DOCX byte change is expected (box vs paragraph). If a NON-dcn golden shows a non-hash change, STOP and report.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~DcnCopyReadyMessageTests"`
Then: `dotnet test`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add fixtures/agr-sample-dcn-draft.json fixtures/agr-sample-dcn-promotion.json config/artifact-types.json tests/Registrar.Tests/Rendering/DcnCopyReadyMessageTests.cs fixtures/expected/
git commit -m "feat: DCN suggested delivery message renders as a copyReadyMessage box"
```

---

## Task 6: Reconcile the Governor block-type docs

**Files:**
- Modify: `docs/governor-instructions.md`, `docs/governor-contract.md`

**Interfaces:**
- Produces: Governor docs state that all ten block types render and that the DCN delivery message uses copyReadyMessage; the instructions paste block is under 8000 chars.

- [ ] **Step 1: Rewrite the instructions block-type section**

In `docs/governor-instructions.md`, the block-type paragraph currently says only seven types render, lists `warnCallout`/`aggregationTable`/`copyReadyMessage` as not built, and says to express a warning as an ordinary paragraph, an aggregated table as an ordinary table, and a delivery message as an ordinary paragraph. This is stale (warnCallout and aggregationTable already render). Replace it with accurate guidance: all block types in the schema render. Keep the heading-level rule (no `h3` without a preceding `h2`; no heading without body). Add a short instruction that the DCN's suggested delivery message should be emitted as a `copyReadyMessage` block. Keep the aggregationTable-needs-clauseLibrary note if it exists elsewhere; do not duplicate it.

Keep every string ASCII, no em/en dashes.

- [ ] **Step 2: Verify the paste block is under the 8000-char cap**

The paste block is the text between the two `---` rules. Confirm it is under 8000 on the CRLF-pessimistic count:

Run (PowerShell):
```
$b = ((Get-Content docs\governor-instructions.md -Raw) -split '(?m)^---\s*$')[1]
$b.Trim().Length
```
Expected: a number under 8000 (the deletions should drop it well under; it was 8387 CRLF before). If it is NOT under 8000, trim additional restatement (not rules) until it is, following the precedent in the file's own preamble. Report the final count.

Also confirm no non-ASCII: the AI-tell lint hook must pass on save.

- [ ] **Step 3: Reconcile governor-contract.md**

In `docs/governor-contract.md`, update the block-type guidance: the "Forbidden" list that names `warnCallout`, `aggregationTable`, `copyReadyMessage` as unbuildable is stale. State that all block types render, and that the DCN delivery message uses `copyReadyMessage`. Keep the aggregationTable source constraint (clauseLibrary only) accurate. ASCII only.

- [ ] **Step 4: Commit**

No code or config changed, so no test run is required for correctness, but run the full suite once to be safe (docs-only changes cannot break it):

Run: `dotnet test`
Expected: all pass.

```bash
git add docs/governor-instructions.md docs/governor-contract.md
git commit -m "docs: reconcile Governor block-type guidance - all ten types render, DCN uses copyReadyMessage"
```

---

## Self-Review

**Spec coverage:**
- Renderer + config styling -> Tasks 1, 2.
- QA-ENG-06 admits it (all types built) -> Task 3.
- QA-ENG-07 scans it (the key distinction from aggregationTable) -> Task 4.
- DCN adoption + description + goldens -> Task 5.
- Governor-docs reconciliation + 8000-char verification (supersedes PR #43) -> Task 6.
- No new colors; reuse existing keys -> Task 2 uses borderNeutral / dchFill / headingNavy.
- style-rules.json and artifact-types.json golden staleness handled hash-only -> Tasks 1, 5.

**Placeholder scan:** No TBD/TODO. Tasks 2 and 5 say "match WarnCalloutBlock.cs / SclEndToEndTests.cs exactly" for APIs the plan cannot name without the file open; each names the exact file to read and the exact behavior required.

**Type consistency:** `CopyReadyMessageStyle` (record, `Label`), `ConfigBundle.CopyReadyMessage`, `RenderContext.CopyReadyMessage` (trailing param), `CopyReadyMessageBlock.Render(string, CopyReadyMessageStyle, StyleRules) -> Table` are used identically across tasks.

## Consequences and follow-ups

- After this slice, all ten AGR block types render; the Unsafe Block Exclusion Rule excludes nothing. tech-debt item 8 is fully closed.
- PR #43 (Governor instructions trim) is superseded: this slice's Task 6 reconciliation brings the paste block under 8000 by deletion. Close PR #43 when this merges.
