# Rendering Polish (Direction B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the generated DS/AUP DOCX read as an authored consulting deliverable - vertical rhythm, a real heading hierarchy, left accent bars, lighter tables, a modernized Document Control Header, pagination controls, and a larger logo - entirely config-driven.

**Architecture:** Add typographic values as new `visualIdentity` keys in `config/style-rules.json`, bound onto the `StyleRules` record. Apply them in the existing Open XML renderers via a shared `ParagraphFormatting` helper (so no renderer hardcodes a measurement) and an extended `StyledRun`. No `styles.xml`, no seed `.docx`, no third-party engine, no runtime model call.

**Tech Stack:** .NET 8, C#, DocumentFormat.OpenXml, xUnit. Dev shell PowerShell 7.x on Windows.

## Global Constraints

- **Config over code (rule 3):** every spacing value, size, color, and flag lives in `config/style-rules.json`; renderers read them from `StyleRules`. No point sizes, colors, or measurements literal in renderer code.
- **Output hygiene (rule 8):** US English; no emojis; no em/en dashes or decorative unicode in generated documents or in config strings. New behavior adds NO characters to document text - spacing, borders, and caps are formatting only.
- **Fail closed / determinism (rules 1, 2):** no governance, validation, or QA-check semantics change; no runtime LLM call; no new checkId.
- **No invented structure (rule 6):** restyle only what already renders; add no sections, block types, or artifact types.
- **Open XML element order is schema-enforced.** `CT_PPr` (paragraph properties) child order is: `keepNext`, `widowControl`, `numPr`, `pBdr`, `spacing`, `ind`. `CT_RPr` (run properties) order is: `rFonts`, `b`, `caps`, `color`, `spacing`, `sz`. `CT_TblPr` order is: `tblW`, `tblBorders`, `tblCellMar`. Build children in these orders. The existing test `ArtifactRendererTests.Render_produces_schema_valid_docx` runs `OpenXmlValidator` and is the ordering safety net - keep it green.
- **Units:** points -> twips = `pt * 20`. Line spacing (auto rule) = `pct * 240 / 100`. Border/bar thickness `Size` is in eighths of a point; `OpenXmlHelpers.BorderSizeEighths(halfPoints)` converts (`halfPoints * 4`).
- **Config binding:** `ConfigLoader.Load` deserializes `styleRoot["visualIdentity"]` into `StyleRules` with `PropertyNameCaseInsensitive = true`. New `required` members MUST have matching JSON keys or deserialization throws. Tests read the real `config/style-rules.json` via `SemanticTestSupport.Config()`.

---

## File Structure

- `config/style-rules.json` - MODIFY: add `spacing`, `headingScale`, `sectionNumber`, `sectionAccent`, `tableCellMargin`, `tableBorders`, `pagination`, `dchLabel` under `visualIdentity`; change `wordmark.heightIn` 0.4 -> 0.55.
- `src/Registrar.Core/Config/StyleRules.cs` - MODIFY: add the eight new records and `required` members.
- `src/Registrar.Rendering/OpenXmlHelpers.cs` - MODIFY: extend `StyledRun` with optional `caps` and `characterSpacingTwips`.
- `src/Registrar.Rendering/ParagraphFormatting.cs` - CREATE: shared schema-ordered `ParagraphProperties` builder.
- `src/Registrar.Rendering/Blocks/ParagraphBlock.cs` - MODIFY: body spacing + widow control.
- `src/Registrar.Rendering/Blocks/BulletListBlock.cs` - MODIFY: bullet spacing + widow control (keep numbering).
- `src/Registrar.Rendering/Blocks/HeadingBlock.cs` - MODIFY: per-level sizes + spacing + keep-next.
- `src/Registrar.Rendering/SectionRenderer.cs` - MODIFY: blue number run + navy title + left accent bar + spacing + keep-next.
- `src/Registrar.Rendering/Blocks/TableBlock.cs` - MODIFY: cell margins + configurable border sides.
- `src/Registrar.Rendering/DchRenderer.cs` - MODIFY: borderless panel + micro-labels.
- `src/Registrar.Rendering/HeaderTableRenderer.cs` - NO code change (logo height already read from config); verified by an end-to-end assertion.
- Tests under `tests/Registrar.Tests/` mirror each change.

---

### Task 1: Config model and `style-rules.json` values

**Files:**
- Modify: `src/Registrar.Core/Config/StyleRules.cs`
- Modify: `config/style-rules.json`
- Test: `tests/Registrar.Tests/Config/RenderingPolishConfigTests.cs` (create)

**Interfaces:**
- Produces: new `StyleRules` members `Spacing Spacing`, `HeadingScale HeadingScale`, `SectionNumber SectionNumber`, `SectionAccent SectionAccent`, `TableCellMargin TableCellMargin`, `TableBorders TableBorders`, `Pagination Pagination`, `DchLabel DchLabel`; and the changed `Wordmark.HeightIn = 0.55`. Field names/types as defined below - later tasks depend on them exactly.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Config/RenderingPolishConfigTests.cs`:

```csharp
using Registrar.Tests;

namespace Registrar.Tests.Config;

public class RenderingPolishConfigTests
{
    private static Registrar.Core.Config.StyleRules Style() => SemanticTestSupport.Config().Style;

    [Fact]
    public void Heading_scale_gives_h2_and_h3_distinct_sizes()
    {
        var s = Style();
        Assert.Equal(16, s.HeadingScale.H1Pt);
        Assert.Equal(13, s.HeadingScale.H2Pt);
        Assert.Equal(11, s.HeadingScale.H3Pt);
    }

    [Fact]
    public void Spacing_puts_more_space_before_headings_than_after()
    {
        var s = Style();
        Assert.True(s.Spacing.SectionTitleBeforePt > s.Spacing.SectionTitleAfterPt);
        Assert.True(s.Spacing.H2BeforePt > s.Spacing.H2AfterPt);
        Assert.True(s.Spacing.H3BeforePt > s.Spacing.H3AfterPt);
        Assert.Equal(6, s.Spacing.BodyAfterPt);
        Assert.Equal(132, s.Spacing.LineSpacingPct);
    }

    [Fact]
    public void Accent_and_number_use_brand_blue_and_bars_are_horizontal_only()
    {
        var s = Style();
        Assert.Equal("#005CB9", s.SectionNumber.Color);
        Assert.Equal("#005CB9", s.SectionAccent.Color);
        Assert.False(s.TableBorders.Outer);
        Assert.True(s.TableBorders.InsideHorizontal);
        Assert.False(s.TableBorders.InsideVertical);
    }

    [Fact]
    public void Pagination_dch_label_and_logo_size_are_configured()
    {
        var s = Style();
        Assert.True(s.Pagination.KeepHeadingWithNext);
        Assert.True(s.Pagination.WidowControl);
        Assert.Equal("#5A6470", s.DchLabel.Color);
        Assert.True(s.DchLabel.Uppercase);
        Assert.Equal(0.55, s.Wordmark.HeightIn, 3);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RenderingPolishConfigTests"`
Expected: FAIL to compile (`StyleRules` has no `HeadingScale`, etc.).

- [ ] **Step 3: Add the records and members to `StyleRules.cs`**

Append these records to `src/Registrar.Core/Config/StyleRules.cs`:

```csharp
public sealed record Spacing
{
    public int BodyAfterPt { get; init; }
    public int LineSpacingPct { get; init; }
    public int SectionTitleBeforePt { get; init; }
    public int SectionTitleAfterPt { get; init; }
    public int H2BeforePt { get; init; }
    public int H2AfterPt { get; init; }
    public int H3BeforePt { get; init; }
    public int H3AfterPt { get; init; }
    public int BulletAfterPt { get; init; }
}

public sealed record HeadingScale
{
    public int H1Pt { get; init; }
    public int H2Pt { get; init; }
    public int H3Pt { get; init; }
}

public sealed record SectionNumber
{
    public required string Color { get; init; }
}

public sealed record SectionAccent
{
    public int WidthHalfPoints { get; init; }
    public required string Color { get; init; }
    public uint GapPoints { get; init; }
}

public sealed record TableCellMargin
{
    public int TopTwips { get; init; }
    public int BottomTwips { get; init; }
    public int LeftTwips { get; init; }
    public int RightTwips { get; init; }
}

public sealed record TableBorders
{
    public bool Outer { get; init; }
    public bool InsideHorizontal { get; init; }
    public bool InsideVertical { get; init; }
    public int WidthHalfPoints { get; init; }
    public required string Color { get; init; }
}

public sealed record Pagination
{
    public bool KeepHeadingWithNext { get; init; }
    public bool WidowControl { get; init; }
}

public sealed record DchLabel
{
    public required string Color { get; init; }
    public bool Uppercase { get; init; }
    public int CharacterSpacingTwips { get; init; }
}
```

Add these `required` members inside the existing `StyleRules` record (after `TypeScale TypeScale`):

```csharp
    public required Spacing Spacing { get; init; }
    public required HeadingScale HeadingScale { get; init; }
    public required SectionNumber SectionNumber { get; init; }
    public required SectionAccent SectionAccent { get; init; }
    public required TableCellMargin TableCellMargin { get; init; }
    public required TableBorders TableBorders { get; init; }
    public required Pagination Pagination { get; init; }
    public required DchLabel DchLabel { get; init; }
```

- [ ] **Step 4: Add the JSON keys to `config/style-rules.json`**

Inside the `visualIdentity` object, change `"wordmark"` to `{ "asset": "org-logo.png", "heightIn": 0.55 }`, and add these sibling keys (after `typeScale`):

```json
    "spacing": {
      "bodyAfterPt": 6,
      "lineSpacingPct": 132,
      "sectionTitleBeforePt": 16,
      "sectionTitleAfterPt": 6,
      "h2BeforePt": 10,
      "h2AfterPt": 4,
      "h3BeforePt": 8,
      "h3AfterPt": 3,
      "bulletAfterPt": 3
    },
    "headingScale": { "h1Pt": 16, "h2Pt": 13, "h3Pt": 11 },
    "sectionNumber": { "color": "#005CB9" },
    "sectionAccent": { "widthHalfPoints": 8, "color": "#005CB9", "gapPoints": 8 },
    "tableCellMargin": { "topTwips": 40, "bottomTwips": 40, "leftTwips": 90, "rightTwips": 90 },
    "tableBorders": { "outer": false, "insideHorizontal": true, "insideVertical": false, "widthHalfPoints": 4, "color": "#B8C1CC" },
    "pagination": { "keepHeadingWithNext": true, "widowControl": true },
    "dchLabel": { "color": "#5A6470", "uppercase": true, "characterSpacingTwips": 6 }
```

Note: `config/style-rules.json` is also copied to `dist/registrar/config/` for the packaged app; leave `dist/` alone here - it is regenerated on release.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~RenderingPolishConfigTests"`
Expected: PASS (4 tests). If deserialization throws about a missing required member, a JSON key name does not match a record member (case-insensitive) - reconcile spelling.

- [ ] **Step 6: Run the config loader tests to confirm nothing else broke**

Run: `dotnet test --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/Config/StyleRules.cs config/style-rules.json tests/Registrar.Tests/Config/RenderingPolishConfigTests.cs
git commit -m "feat: add Direction B typography config keys and bindings"
```

---

### Task 2: Shared formatting helpers

**Files:**
- Modify: `src/Registrar.Rendering/OpenXmlHelpers.cs`
- Create: `src/Registrar.Rendering/ParagraphFormatting.cs`
- Test: `tests/Registrar.Tests/Rendering/ParagraphFormattingTests.cs` (create)

**Interfaces:**
- Consumes: `OpenXmlHelpers.BorderSizeEighths`, `OpenXmlHelpers.Hex` (Task exists).
- Produces:
  - `OpenXmlHelpers.StyledRun(string text, string family, int sizePt, string colorHex, bool bold, bool caps = false, int characterSpacingTwips = 0)` - the two extra params are optional, so existing 5-arg callers are unaffected.
  - `record ParaFormat { bool KeepNext; bool WidowControl; NumberingProperties? Numbering; (int WidthEighths, string ColorHex, uint GapPoints)? LeftBar; int? SpaceBeforeTwips; int? SpaceAfterTwips; int? LineSpacing240; }`
  - `ParagraphFormatting.Build(ParaFormat f) : ParagraphProperties` - children appended in `CT_PPr` order.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Rendering/ParagraphFormattingTests.cs`:

```csharp
using Registrar.Rendering;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Validation;

namespace Registrar.Tests.Rendering;

public class ParagraphFormattingTests
{
    [Fact]
    public void Build_emits_children_in_schema_order_and_validates()
    {
        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            KeepNext = true,
            WidowControl = true,
            Numbering = new NumberingProperties(
                new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }),
            LeftBar = (32, "#005CB9", 8),
            SpaceBeforeTwips = 320,
            SpaceAfterTwips = 120,
            LineSpacing240 = 317
        });

        var names = ppr.ChildElements.Select(c => c.LocalName).ToList();
        Assert.Equal(new[] { "keepNext", "widowControl", "numPr", "pBdr", "spacing", "ind" }, names);

        // The paragraph must be schema-valid inside a real document.
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(ppr.CloneNode(true), new Run(new Text("x")))));
        }
        ms.Position = 0;
        using var check = WordprocessingDocument.Open(ms, false);
        Assert.Empty(new OpenXmlValidator().Validate(check));
    }

    [Fact]
    public void StyledRun_caps_and_spacing_order_is_rFonts_b_caps_color_spacing_sz()
    {
        var run = OpenXmlHelpers.StyledRun("Doc ID", "Montserrat", 8, "#5A6470",
            bold: false, caps: true, characterSpacingTwips: 6);
        var names = run.RunProperties!.ChildElements.Select(c => c.LocalName).ToList();
        Assert.Equal(new[] { "rFonts", "caps", "color", "spacing", "sz" }, names);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ParagraphFormattingTests"`
Expected: FAIL to compile (`ParaFormat`/`ParagraphFormatting` do not exist; `StyledRun` has no `caps` param).

- [ ] **Step 3: Extend `StyledRun` in `OpenXmlHelpers.cs`**

Replace the existing `StyledRun` method with:

```csharp
    // FontSize.Val is in half-points, so pass point size and it doubles.
    // CT_RPr is a strict schema-ordered sequence: rFonts, b, ..., caps, ..., color, spacing, ..., sz.
    public static Run StyledRun(string text, string family, int sizePt, string colorHex, bool bold,
        bool caps = false, int characterSpacingTwips = 0)
    {
        var rpr = new RunProperties(new RunFonts { Ascii = family, HighAnsi = family });
        if (bold) rpr.Append(new Bold());
        if (caps) rpr.Append(new Caps());
        rpr.Append(new Color { Val = Hex(colorHex) });
        if (characterSpacingTwips != 0) rpr.Append(new Spacing { Val = characterSpacingTwips });
        rpr.Append(new FontSize { Val = (sizePt * 2).ToString() });
        return new Run(rpr, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }
```

- [ ] **Step 4: Create `ParagraphFormatting.cs`**

Create `src/Registrar.Rendering/ParagraphFormatting.cs`:

```csharp
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public sealed record ParaFormat
{
    public bool KeepNext { get; init; }
    public bool WidowControl { get; init; }
    public NumberingProperties? Numbering { get; init; }
    public (int WidthEighths, string ColorHex, uint GapPoints)? LeftBar { get; init; }
    public int? SpaceBeforeTwips { get; init; }
    public int? SpaceAfterTwips { get; init; }
    public int? LineSpacing240 { get; init; }
}

// Builds a CT_PPr with children in the schema-required order:
// keepNext, widowControl, numPr, pBdr, spacing, ind.
public static class ParagraphFormatting
{
    public static ParagraphProperties Build(ParaFormat f)
    {
        var ppr = new ParagraphProperties();
        if (f.KeepNext) ppr.Append(new KeepNext());
        if (f.WidowControl) ppr.Append(new WidowControl());
        if (f.Numbering is not null) ppr.Append(f.Numbering);
        if (f.LeftBar is { } bar)
            ppr.Append(new ParagraphBorders(
                new LeftBorder
                {
                    Val = BorderValues.Single,
                    Size = (uint)bar.WidthEighths,
                    Color = OpenXmlHelpers.Hex(bar.ColorHex),
                    Space = bar.GapPoints
                }));

        var spacing = new SpacingBetweenLines();
        var hasSpacing = false;
        if (f.SpaceBeforeTwips is int sb) { spacing.Before = sb.ToString(); hasSpacing = true; }
        if (f.SpaceAfterTwips is int sa) { spacing.After = sa.ToString(); hasSpacing = true; }
        if (f.LineSpacing240 is int ln)
        {
            spacing.Line = ln.ToString();
            spacing.LineRule = LineSpacingRuleValues.Auto;
            hasSpacing = true;
        }
        if (hasSpacing) ppr.Append(spacing);

        return ppr;
    }
}
```

Note: `Indentation` (`ind`) is not needed here - the left accent bar's `Space` attribute offsets the text from the bar, so no separate indent is emitted. The test's expected child list includes `ind` only when an indent is set; remove `ind` from the expected array in Step 1 if you keep this design. (Design decision: the bar `Space` handles the gap; do NOT add `Indentation` - it would double the offset.)

- [ ] **Step 5: Fix the test's expected child list**

Since `Build` emits no `ind`, update the first assertion in `ParagraphFormattingTests` to:

```csharp
        Assert.Equal(new[] { "keepNext", "widowControl", "numPr", "pBdr", "spacing" }, names);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ParagraphFormattingTests"`
Expected: PASS (2 tests). If the validator reports an ordering error, check the append order matches `keepNext, widowControl, numPr, pBdr, spacing`.

- [ ] **Step 7: Confirm existing renderer tests still compile/pass**

Run: `dotnet test --filter "FullyQualifiedName~Rendering"`
Expected: PASS (StyledRun's new params are optional; existing callers unaffected).

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Rendering/OpenXmlHelpers.cs src/Registrar.Rendering/ParagraphFormatting.cs tests/Registrar.Tests/Rendering/ParagraphFormattingTests.cs
git commit -m "feat: shared paragraph/run formatting helpers for typography"
```

---

### Task 3: Body paragraph and bullet spacing

**Files:**
- Modify: `src/Registrar.Rendering/Blocks/ParagraphBlock.cs`
- Modify: `src/Registrar.Rendering/Blocks/BulletListBlock.cs`
- Test: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs:1` (add two facts)

**Interfaces:**
- Consumes: `ParagraphFormatting.Build`, `ParaFormat`, `StyleRules.Spacing`, `StyleRules.Pagination`, `NumberingPart.BulletNumId`.
- Produces: no signature changes (`ParagraphBlock.Render`, `BulletListBlock.Render` keep their current signatures).

- [ ] **Step 1: Write the failing tests**

Add to `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`:

```csharp
    [Fact]
    public void Paragraph_has_body_spacing_and_widow_control()
    {
        var p = ParagraphBlock.Render("Body.", Style());
        var ppr = p.ParagraphProperties!;
        Assert.NotNull(ppr.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.WidowControl>());
        var spacing = ppr.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.SpacingBetweenLines>()!;
        Assert.Equal("120", spacing.After!.Value);   // 6pt * 20
        Assert.Equal("317", spacing.Line!.Value);     // 132% * 240 / 100 (integer)
    }

    [Fact]
    public void Bullet_keeps_numbering_and_gains_spacing()
    {
        var paras = BulletListBlock.Render(new[] { "one" }, Style()).ToList();
        var ppr = paras[0].ParagraphProperties!;
        Assert.NotNull(ppr.NumberingProperties);
        var spacing = ppr.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.SpacingBetweenLines>()!;
        Assert.Equal("60", spacing.After!.Value);      // 3pt * 20
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~BlockRendererTests"`
Expected: FAIL - `ParagraphProperties` is null (no spacing yet).

- [ ] **Step 3: Update `ParagraphBlock.cs`**

Replace the body of `ParagraphBlock`:

```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class ParagraphBlock
{
    public static Paragraph Render(string text, StyleRules style)
    {
        var run = OpenXmlHelpers.StyledRun(text,
            style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial",
            style.TypeScale.BodyPt, "#000000", bold: false);
        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            WidowControl = style.Pagination.WidowControl,
            SpaceAfterTwips = style.Spacing.BodyAfterPt * 20,
            LineSpacing240 = style.Spacing.LineSpacingPct * 240 / 100
        });
        return new Paragraph(ppr, run);
    }
}
```

- [ ] **Step 4: Update `BulletListBlock.cs`**

Replace the body of `BulletListBlock`:

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
            var ppr = ParagraphFormatting.Build(new ParaFormat
            {
                WidowControl = style.Pagination.WidowControl,
                Numbering = new NumberingProperties(
                    new NumberingLevelReference { Val = 0 },
                    new NumberingId { Val = NumberingPart.BulletNumId }),
                SpaceAfterTwips = style.Spacing.BulletAfterPt * 20,
                LineSpacing240 = style.Spacing.LineSpacingPct * 240 / 100
            });
            yield return new Paragraph(ppr, OpenXmlHelpers.StyledRun(
                item, style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial",
                style.TypeScale.BodyPt, "#000000", bold: false));
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~BlockRendererTests"`
Expected: PASS (including the pre-existing `BulletList_uses_numbering_and_no_bullet_glyph_in_runs`).

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Rendering/Blocks/ParagraphBlock.cs src/Registrar.Rendering/Blocks/BulletListBlock.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs
git commit -m "feat: vertical rhythm on body paragraphs and bullets"
```

---

### Task 4: Heading hierarchy and keep-with-next

**Files:**
- Modify: `src/Registrar.Rendering/Blocks/HeadingBlock.cs`
- Test: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs` (add one fact; the existing `Heading_h2_uses_navy_and_h3_uses_accent` must still pass)

**Interfaces:**
- Consumes: `StyleRules.HeadingScale`, `StyleRules.Spacing`, `StyleRules.Pagination`, `ParagraphFormatting.Build`.
- Produces: `HeadingBlock.Render(string text, int level, StyleRules style)` unchanged signature.

- [ ] **Step 1: Write the failing test**

Add to `BlockRendererTests`:

```csharp
    [Fact]
    public void Heading_h2_and_h3_have_distinct_sizes_and_keep_next()
    {
        var style = Style();
        var h2 = HeadingBlock.Render("Two", 2, style);
        var h3 = HeadingBlock.Render("Three", 3, style);
        var h2Sz = h2.Descendants<DocumentFormat.OpenXml.Wordprocessing.FontSize>().First().Val!.Value;
        var h3Sz = h3.Descendants<DocumentFormat.OpenXml.Wordprocessing.FontSize>().First().Val!.Value;
        Assert.Equal("26", h2Sz);   // 13pt * 2 half-points
        Assert.Equal("22", h3Sz);   // 11pt * 2 half-points
        Assert.NotNull(h2.ParagraphProperties!.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.KeepNext>());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BlockRendererTests"`
Expected: FAIL - both headings currently render at `subtitlePt` (22) and have no `KeepNext`.

- [ ] **Step 3: Update `HeadingBlock.cs`**

```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class HeadingBlock
{
    public static Paragraph Render(string text, int level, StyleRules style)
    {
        var isH3 = level >= 3;
        var color = isH3 ? style.Colors["accentBlue"] : style.Colors["headingNavy"];
        var sizePt = isH3 ? style.HeadingScale.H3Pt : style.HeadingScale.H2Pt;
        var run = OpenXmlHelpers.StyledRun(
            text, style.Fonts.Heading.Family ?? style.Fonts.Heading.Fallback ?? "Arial",
            sizePt, color, bold: true);
        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            KeepNext = style.Pagination.KeepHeadingWithNext,
            SpaceBeforeTwips = (isH3 ? style.Spacing.H3BeforePt : style.Spacing.H2BeforePt) * 20,
            SpaceAfterTwips = (isH3 ? style.Spacing.H3AfterPt : style.Spacing.H2AfterPt) * 20
        });
        return new Paragraph(ppr, run);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~BlockRendererTests"`
Expected: PASS - both the new size/keep-next fact and the existing color fact.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/Blocks/HeadingBlock.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs
git commit -m "feat: distinct h2/h3 sizes with keep-with-next"
```

---

### Task 5: Section title - blue number, navy title, left accent bar

**Files:**
- Modify: `src/Registrar.Rendering/SectionRenderer.cs:26-30` (the heading paragraph)
- Test: `tests/Registrar.Tests/Rendering/SectionRendererTests.cs` (add one fact; the existing two must still pass)

**Interfaces:**
- Consumes: `StyleRules.HeadingScale.H1Pt`, `StyleRules.SectionNumber.Color`, `StyleRules.SectionAccent`, `StyleRules.Spacing`, `StyleRules.Pagination`, `OpenXmlHelpers.StyledRun`, `OpenXmlHelpers.BorderSizeEighths`, `ParagraphFormatting.Build`.
- Produces: the section-title paragraph now holds two runs (number + title) whose combined `InnerText` is still `"{n}. {Title}"`. `ScanUnit` text unchanged.

- [ ] **Step 1: Write the failing test**

Add to `SectionRendererTests`:

```csharp
    [Fact]
    public void Section_title_has_blue_number_navy_title_and_left_accent_bar()
    {
        var section = new AgrSection { Title = "Purpose", DescriptorLine = null,
            Blocks = new[] { new AgrBlock { BlockType = "paragraph", Text = "Body." } } };
        var r = SectionRenderer.Render(section, 0, Ctx());
        var titlePara = (DocumentFormat.OpenXml.Wordprocessing.Paragraph)r.Elements[0];

        // InnerText is unchanged so the QA-SF-08 scan unit and prior tests hold.
        Assert.Equal("1. Purpose", titlePara.InnerText);

        // Two runs: number in brand blue, title in navy.
        var colors = titlePara.Descendants<DocumentFormat.OpenXml.Wordprocessing.Color>()
            .Select(c => c.Val!.Value).ToList();
        Assert.Equal("005CB9", colors[0], ignoreCase: true);
        Assert.Equal("002060", colors[1], ignoreCase: true);

        // Left accent bar, no bottom/horizontal rule.
        var borders = titlePara.ParagraphProperties!
            .GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders>()!;
        Assert.NotNull(borders.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.LeftBorder>());
        Assert.Null(borders.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.BottomBorder>());
        Assert.NotNull(titlePara.ParagraphProperties!
            .GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.KeepNext>());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SectionRendererTests"`
Expected: FAIL - the title is currently a single navy run with no borders.

- [ ] **Step 3: Update the heading block in `SectionRenderer.cs`**

Replace lines 26-30 (`var headingText = ...` through the `scanUnits.Add(...)` for the heading) with:

```csharp
        var headingText = $"{sectionIndex + 1}. {section.Title}";
        var headingFamily = style.Fonts.Heading.Family ?? style.Fonts.Heading.Fallback ?? "Arial";
        var numberRun = OpenXmlHelpers.StyledRun(
            $"{sectionIndex + 1}. ", headingFamily, style.HeadingScale.H1Pt,
            style.SectionNumber.Color, bold: true);
        var titleRun = OpenXmlHelpers.StyledRun(
            section.Title, headingFamily, style.HeadingScale.H1Pt,
            style.Colors["headingNavy"], bold: true);
        var headingPpr = ParagraphFormatting.Build(new ParaFormat
        {
            KeepNext = style.Pagination.KeepHeadingWithNext,
            LeftBar = (
                (int)OpenXmlHelpers.BorderSizeEighths(style.SectionAccent.WidthHalfPoints),
                style.SectionAccent.Color,
                style.SectionAccent.GapPoints),
            SpaceBeforeTwips = style.Spacing.SectionTitleBeforePt * 20,
            SpaceAfterTwips = style.Spacing.SectionTitleAfterPt * 20
        });
        elements.Add(new Paragraph(headingPpr, numberRun, titleRun));
        scanUnits.Add(new ScanUnit(headingText, sectionLocator));
```

Note: `numberRun` text is `"1. "` (trailing space preserved) and `titleRun` is `"Purpose"`, so the paragraph `InnerText` is exactly `"1. Purpose"` - byte-identical to the old single-run heading. Do not add a tab (U+0009) between them; it is a C0 control character and would fail the QA-SF-08 scan.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SectionRendererTests"`
Expected: PASS - the new fact plus the pre-existing `Renders_numbered_heading_descriptor_and_blocks` (which asserts `Contains("1. Purpose")`).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/SectionRenderer.cs tests/Registrar.Tests/Rendering/SectionRendererTests.cs
git commit -m "feat: section title with blue number and left accent bar"
```

---

### Task 6: Lighter body tables

**Files:**
- Modify: `src/Registrar.Rendering/Blocks/TableBlock.cs:26-41` (the `TableProps` method)
- Test: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs` (add one fact)

**Interfaces:**
- Consumes: `StyleRules.TableBorders`, `StyleRules.TableCellMargin`.
- Produces: `TableBlock.Render` unchanged signature; the table's `TableProperties` now reflects config border sides and cell margins.

- [ ] **Step 1: Write the failing test**

Add to `BlockRendererTests`:

```csharp
    [Fact]
    public void Table_has_horizontal_rules_only_and_cell_margins()
    {
        var t = TableBlock.Render(new[] { "H" },
            new IReadOnlyList<string>[] { new[] { "c" } }, Style());
        var props = t.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableProperties>()!;
        var borders = props.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableBorders>()!;

        // Horizontal insides present; verticals and outer box off.
        Assert.Equal(DocumentFormat.OpenXml.Wordprocessing.BorderValues.Single,
            borders.InsideHorizontalBorder!.Val!.Value);
        Assert.Equal(DocumentFormat.OpenXml.Wordprocessing.BorderValues.None,
            borders.InsideVerticalBorder!.Val!.Value);
        Assert.Equal(DocumentFormat.OpenXml.Wordprocessing.BorderValues.None,
            borders.TopBorder!.Val!.Value);

        var margin = props.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableCellMarginDefault>();
        Assert.NotNull(margin);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BlockRendererTests"`
Expected: FAIL - all borders are currently `Single` and there is no `TableCellMarginDefault`.

- [ ] **Step 3: Rewrite `TableProps` in `TableBlock.cs`**

Replace the `TableProps` method with:

```csharp
    // CT_TblPr order: tblW, tblBorders, tblCellMar. CT_TblBorders is a strict
    // schema-ordered sequence of the six named border elements.
    private static TableProperties TableProps(StyleRules style)
    {
        var b = style.TableBorders;
        var color = OpenXmlHelpers.Hex(b.Color);
        var size = OpenXmlHelpers.BorderSizeEighths(b.WidthHalfPoints);
        BorderValues V(bool on) => on ? BorderValues.Single : BorderValues.None;
        var m = style.TableCellMargin;

        return new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(
                new TopBorder { Val = V(b.Outer), Size = size, Color = color },
                new LeftBorder { Val = V(b.Outer), Size = size, Color = color },
                new BottomBorder { Val = V(b.Outer), Size = size, Color = color },
                new RightBorder { Val = V(b.Outer), Size = size, Color = color },
                new InsideHorizontalBorder { Val = V(b.InsideHorizontal), Size = size, Color = color },
                new InsideVerticalBorder { Val = V(b.InsideVertical), Size = size, Color = color }),
            new TableCellMarginDefault(
                new TopMargin { Width = m.TopTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = (short)m.LeftTwips, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = m.BottomTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = (short)m.RightTwips, Type = TableWidthValues.Dxa }));
    }
```

Note the SDK quirk: `TopMargin`/`BottomMargin` take a string `Width` + `TableWidthUnitValues`, whereas `TableCellLeftMargin`/`TableCellRightMargin` take a numeric (`short`) `Width` + `TableWidthValues`. If the project's SDK version rejects `(short)`, use the exact `Width`/`Type` types its `TableCellLeftMargin` exposes - the `Render_produces_schema_valid_docx` test in Task 8 confirms correctness.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~BlockRendererTests"`
Expected: PASS, including the pre-existing `Table_renders_headers_and_rows`.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Rendering/Blocks/TableBlock.cs tests/Registrar.Tests/Rendering/BlockRendererTests.cs
git commit -m "feat: horizontal-rule tables with cell padding"
```

---

### Task 7: Modernized Document Control Header

**Files:**
- Modify: `src/Registrar.Rendering/DchRenderer.cs` (`TableProps` and `Cell`)
- Test: `tests/Registrar.Tests/Rendering/DchRendererTests.cs` (create)

**Interfaces:**
- Consumes: `StyleRules.DchLabel`, `StyleRules.TypeScale.LabelPt`, `OpenXmlHelpers.StyledRun` (with `caps`/`characterSpacingTwips`).
- Produces: `DchRenderer.Render` unchanged signature. DCH field-table borders all `None`; label runs use `DchLabel` styling. Banner text `"DOCUMENT CONTROL HEADER"` unchanged.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Rendering/DchRendererTests.cs`:

```csharp
using Registrar.Rendering;
using Registrar.Tests;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class DchRendererTests
{
    private static (DocumentFormat.OpenXml.OpenXmlElement[] els, Registrar.Core.Config.StyleRules style) Render()
    {
        var c = SemanticTestSupport.Config();
        var agr = SemanticTestSupport.Agr("agr-sample-ds-draft.json");
        return (DchRenderer.Render(agr, c.Style).ToArray(), c.Style);
    }

    [Fact]
    public void Dch_table_is_borderless()
    {
        var (els, _) = Render();
        var table = els.OfType<Table>().First();
        var borders = table.GetFirstChild<TableProperties>()!.GetFirstChild<TableBorders>()!;
        Assert.Equal(BorderValues.None, borders.TopBorder!.Val!.Value);
        Assert.Equal(BorderValues.None, borders.InsideHorizontalBorder!.Val!.Value);
        Assert.Equal(BorderValues.None, borders.InsideVerticalBorder!.Val!.Value);
    }

    [Fact]
    public void Dch_field_labels_use_gray_caps_and_character_spacing()
    {
        var (els, style) = Render();
        var table = els.OfType<Table>().First();
        // First cell's first paragraph is the "Document ID" label.
        var labelRun = table.Descendants<Run>().First();
        var rpr = labelRun.RunProperties!;
        Assert.NotNull(rpr.GetFirstChild<Caps>());
        Assert.Equal(OpenXmlHelpers.Hex(style.DchLabel.Color),
            rpr.GetFirstChild<Color>()!.Val!.Value, ignoreCase: true);
        Assert.NotNull(rpr.GetFirstChild<Spacing>());   // character spacing
    }

    [Fact]
    public void Dch_banner_caption_is_retained()
    {
        var (els, _) = Render();
        Assert.Contains(els, e => e.InnerText.Contains("DOCUMENT CONTROL HEADER"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DchRendererTests"`
Expected: FAIL - borders are `Single` and the label run has no `Caps`.

- [ ] **Step 3: Make the DCH table borderless**

In `DchRenderer.cs`, replace the `TableProps` method with:

```csharp
    private static TableProperties TableProps(StyleRules style) => new(
        new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
        new TableBorders(
            new TopBorder { Val = BorderValues.None },
            new LeftBorder { Val = BorderValues.None },
            new BottomBorder { Val = BorderValues.None },
            new RightBorder { Val = BorderValues.None },
            new InsideHorizontalBorder { Val = BorderValues.None },
            new InsideVerticalBorder { Val = BorderValues.None }));
```

- [ ] **Step 4: Restyle the field label in `Cell`**

In `DchRenderer.cs`, replace the `labelPara` assignment inside `Cell` with:

```csharp
        var labelPara = OpenXmlHelpers.Line(
            OpenXmlHelpers.StyledRun(label, family, style.TypeScale.LabelPt, style.DchLabel.Color,
                bold: false, caps: style.DchLabel.Uppercase,
                characterSpacingTwips: style.DchLabel.CharacterSpacingTwips));
```

(Leave the `valueRun` and the cell shading unchanged.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DchRendererTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Confirm the end-to-end DCH content test still passes**

Run: `dotnet test --filter "FullyQualifiedName~ArtifactRendererTests.Render_includes_dch_with_metadata"`
Expected: PASS - values and the uppercase banner are still present (`Caps` is a display property; underlying text is unchanged).

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Rendering/DchRenderer.cs tests/Registrar.Tests/Rendering/DchRendererTests.cs
git commit -m "feat: borderless Document Control Header with micro-labels"
```

---

### Task 8: End-to-end verification and value tuning

**Files:**
- Test: `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs` (add one fact)
- Verify only (no source change): `src/Registrar.Rendering/HeaderTableRenderer.cs`, `src/Registrar.Rendering/FooterRenderer.cs`

**Interfaces:**
- Consumes: everything above, end to end, via `ArtifactRenderer.Render`.

- [ ] **Step 1: Add the logo-size regression fact**

Add to `ArtifactRendererTests`:

```csharp
    [Fact]
    public void Render_uses_the_configured_larger_logo_height()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);
        var extent = doc.MainDocumentPart!.Document.Body!
            .Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().First();
        // 0.55in * 914400 EMU/in = 502920.
        Assert.Equal(502920L, extent.Cy!.Value);
    }
```

- [ ] **Step 2: Run the full rendering suite (ordering + scan + content guards)**

Run: `dotnet test --filter "FullyQualifiedName~Rendering"`
Expected: PASS. Critically, `Render_produces_schema_valid_docx` (OpenXmlValidator) must be green - it proves every new `pPr`/`rPr`/`tblPr` child is in schema order. `Render_includes_numbered_body_sections_and_clause_text` proves `PostRenderChecks` is still empty (QA-SF-08 clean) and headings still read `"1. Purpose"` / `"8. Version and Change Notes"`.

- [ ] **Step 3: Run the entire test suite**

Run: `dotnet test`
Expected: PASS (all projects). Any failure here is a regression to fix before proceeding.

- [ ] **Step 4: Render real DS and AUP DOCX for visual confirmation (the eyeball step)**

Build both fixtures in draft and promotion and open them in Word to confirm the spacing/size values look right (the spec flags these as first-pass, tunable config):

```bash
dotnet run --project src/Registrar.Cli -- build fixtures/agr-sample-ds-draft.json --mode draft
dotnet run --project src/Registrar.Cli -- build fixtures/agr-sample-aup-draft.json --mode draft
dotnet run --project src/Registrar.Cli -- build fixtures/agr-sample-ds-promotion.json --mode promotion
dotnet run --project src/Registrar.Cli -- build fixtures/agr-sample-aup-promotion.json --mode promotion
```

Open each generated `.docx` (written next to its AGR). Confirm: enlarged logo with no line under it, left accent bars on section titles, no grey masthead/section hairlines, h2 visibly larger than h3, breathing room in paragraphs and table cells, borderless DCH with gray uppercase micro-labels, and a borderless footer with working "Page X of Y".

- [ ] **Step 5: Tune values if needed (config only)**

If any measurement reads too tight/loose (line spacing, bar thickness `sectionAccent.widthHalfPoints`, `dchLabel.characterSpacingTwips`, cell margins), adjust the value in `config/style-rules.json` only and re-run Step 4. No code change. Re-run `dotnet test` after any config edit.

- [ ] **Step 6: Confirm the two no-change renderers**

Read `HeaderTableRenderer.cs` and confirm the header table is still fully borderless (no separator rule was added) and the logo height comes from `style.Wordmark.HeightIn`. Read `FooterRenderer.cs` and confirm the footer paragraph has no border. No edits expected.

- [ ] **Step 7: Commit**

```bash
git add tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs config/style-rules.json
git commit -m "test: end-to-end logo-size guard; finalize Direction B values"
```

---

## Self-Review

**Spec coverage** (checked against `docs/superpowers/specs/2026-07-08-rendering-polish-direction-b-design.md`):
- Config keys (spacing, headingScale, sectionNumber, sectionAccent, tableCellMargin, tableBorders, pagination, dchLabel, wordmark 0.55) -> Task 1.
- Shared spacing primitive + StyledRun caps/spacing -> Task 2.
- Body/bullet spacing -> Task 3. h2/h3 sizes + keep-next -> Task 4. Section title blue number + left accent bar + spacing + keep-next -> Task 5. Lighter tables + cell margins -> Task 6. Borderless DCH + micro-labels -> Task 7. Logo size + borderless header/footer verification + render-and-eyeball -> Task 8.
- Tab-character catch (number stays `"1. "`, InnerText unchanged) -> Task 5 Step 3 note + assertion.
- No new characters / QA-SF-08 clean -> guarded by `Render_includes_numbered_body_sections_and_clause_text` (Task 8) and `Caps` being a display property (Task 7).
- Schema ordering -> `ParagraphFormatting.Build` order + `Render_produces_schema_valid_docx` (Task 8).

**Placeholder scan:** no TBD/TODO; every code step shows complete code; every test shows real assertions.

**Type consistency:** `ParaFormat` field names (`KeepNext`, `WidowControl`, `Numbering`, `LeftBar`, `SpaceBeforeTwips`, `SpaceAfterTwips`, `LineSpacing240`) are used identically in Tasks 3-5. `StyleRules` member names (`HeadingScale.H1Pt/H2Pt/H3Pt`, `SectionAccent.WidthHalfPoints/Color/GapPoints`, `SectionNumber.Color`, `Spacing.*`, `TableBorders.*`, `TableCellMargin.*`, `Pagination.*`, `DchLabel.*`) match between Task 1's definitions and their consumers.

**Known risk flagged for the implementer:** the `tblCellMar` SDK width/type quirk (Task 6 Step 3 note) - the schema-valid test is the safety net.
