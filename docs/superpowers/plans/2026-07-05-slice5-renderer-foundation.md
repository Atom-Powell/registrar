# Slice 5: Renderer Foundation and Universal Chrome Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `registrar build` validates an AGR and, on a pass, renders a real `.docx` containing the universal chrome (header table with the embedded ORG logo, Document Control Header, Configuration callout, footer) built from scratch with the Open XML SDK and driven by `style-rules.json`.

**Architecture:** A new `Registrar.Rendering` project (referencing `DocumentFormat.OpenXml`) holds an `ArtifactRenderer` that assembles a `WordprocessingDocument` from four focused component renderers, driven by the validated `AgrDocument` and a typed `StyleRules` loaded from config. `Registrar.Cli` gains a `build` verb that reuses `ValidationService` and renders on a pass. Body-section rendering is deferred to Slice 7 (Slice 6 is the AGR input-validation layer).

**Tech Stack:** .NET 8, C#, DocumentFormat.OpenXml, System.Text.Json, xUnit.

## Global Constraints

- **.NET 8** target (`net8.0`). No `net9.0`/`net10.0` features.
- **Laptop runtime gap:** this machine has only the .NET 10 runtime. Run tests with `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln` (or a `--filter` subset); prefix `dotnet run` the same way. Never add `<RollForward>` to any csproj.
- **Branch first:** do all work on a `slice5-renderer-foundation` branch off `main`.
- **Fail closed:** the DOCX is produced only when `overall == pass`, OR `overall == pass-with-warnings AND mode == draft`. A fail (or a promotion pass-with-warnings) produces no `.docx`.
- **Zero LLM calls at runtime.** Pure deterministic rendering.
- **Configuration over code:** fonts, colors, borders, page size, footer template, and the logo reference all come from `config/style-rules.json` (`visualIdentity`). Rendering code holds no style constants.
- **Output hygiene:** US English; no emojis, em/en dashes (U+2014/U+2013), ellipsis char (U+2026), or decorative unicode in any source, config, or rendered text run. The only approved non-ASCII glyph in rendered output is U+25CB (not used in this slice).
- **DocumentFormat.OpenXml** is the single sanctioned third-party dependency (CLAUDE.md stack). No other DOCX libraries, no seed `.docx`.
- **Determinism in tests:** core document properties come from an injected `IClock`; tests assert document CONTENT (reopened via Open XML), never bytes.
- **Value strings centralized** in `Registrar.Core` (`Vocabulary`, `EngineInfo`); the DOCX emission decision uses `OverallResult`/`Mode` constants.

## File map

- `src/Registrar.Rendering/Registrar.Rendering.csproj` - new project + OpenXml package (Task 1).
- `config/style-rules.json` - add `visualIdentity.wordmark` (Task 2).
- `src/Registrar.Core/Config/StyleRules.cs` - typed visual identity (Task 2).
- `src/Registrar.Core/Config/ConfigBundle.cs`, `ConfigLoader.cs`, `ConfigLocator.cs` - load style + `FindAsset` (Task 2).
- `src/Registrar.Core/Model/AgrDocument.cs` - extend `AgrContext` (Task 3).
- `src/Registrar.Rendering/OpenXmlHelpers.cs` - shared run/table/unit helpers (Task 4).
- `src/Registrar.Rendering/ArtifactRenderer.cs` - document assembly (Tasks 4-7).
- `src/Registrar.Rendering/HeaderTableRenderer.cs` (Task 5), `DchRenderer.cs` (Task 6), `ConfigCalloutRenderer.cs` + `FooterRenderer.cs` (Task 7).
- `src/Registrar.Cli/BuildCommand.cs`, `Program.cs` - the `build` verb (Task 8).
- `tests/Registrar.Tests/Rendering/*` - render tests reopening the `.docx` (Tasks 4-8).

---

### Task 1: Registrar.Rendering project, Open XML dependency, and smoke test

**Files:**
- Create: `src/Registrar.Rendering/Registrar.Rendering.csproj`
- Create: `tests/Registrar.Tests/Rendering/OpenXmlSmokeTest.cs`
- Modify: `Registrar.sln` (add the project), project references

**Interfaces:**
- Consumes: nothing.
- Produces: the `Registrar.Rendering` project (references `Registrar.Core`, package `DocumentFormat.OpenXml`); `Cli -> Rendering`; `Tests -> Rendering` (so tests get Open XML transitively).

- [ ] **Step 1: Create the project and wire references**

Run from repo root `C:\GitHub\Artifact-Generator`:

```bash
dotnet new classlib -n Registrar.Rendering -o src/Registrar.Rendering -f net8.0
rm src/Registrar.Rendering/Class1.cs
dotnet sln add src/Registrar.Rendering
dotnet add src/Registrar.Rendering reference src/Registrar.Core
dotnet add src/Registrar.Cli reference src/Registrar.Rendering
dotnet add tests/Registrar.Tests reference src/Registrar.Rendering
dotnet add src/Registrar.Rendering package DocumentFormat.OpenXml
```

Ensure `src/Registrar.Rendering/Registrar.Rendering.csproj` `<PropertyGroup>` has `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<LangVersion>latest</LangVersion>` (the net8.0 template sets these; add any missing). `DocumentFormat.OpenXml` resolves to its latest stable 3.x (net8.0-compatible); the exact version is not pinned here.

- [ ] **Step 2: Write the smoke test**

Create `tests/Registrar.Tests/Rendering/OpenXmlSmokeTest.cs`:

```csharp
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class OpenXmlSmokeTest
{
    [Fact]
    public void Can_create_and_reopen_a_wordprocessing_document()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("smoke")))));
        }

        stream.Position = 0;
        using var reopened = WordprocessingDocument.Open(stream, false);
        var text = reopened.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Equal("smoke", text);
    }
}
```

- [ ] **Step 3: Build and run the smoke test**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~OpenXmlSmokeTest"`
Expected: PASS - 1 test (proves the SDK loads and a doc round-trips).

- [ ] **Step 4: Run the full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln`
Expected: PASS - all prior tests plus the smoke test.

- [ ] **Step 5: Commit**

```bash
git add Registrar.sln src/Registrar.Rendering src/Registrar.Cli/Registrar.Cli.csproj tests/Registrar.Tests/Registrar.Tests.csproj tests/Registrar.Tests/Rendering/OpenXmlSmokeTest.cs
git commit -m "chore(rendering): add Registrar.Rendering project with Open XML SDK"
```

---

### Task 2: StyleRules config, wordmark reference, and FindAsset

**Files:**
- Modify: `config/style-rules.json` (add `visualIdentity.wordmark`)
- Create: `src/Registrar.Core/Config/StyleRules.cs`
- Modify: `src/Registrar.Core/Config/ConfigBundle.cs`, `ConfigLoader.cs`, `ConfigLocator.cs`
- Modify: `tests/Registrar.Tests/Config/ConfigLoaderTests.cs`

**Interfaces:**
- Consumes: existing `ConfigLoader.Load`, `ConfigLocator`, `Hashing`.
- Produces:
  - `StyleRules` record: `FontSet Fonts`, `IReadOnlyDictionary<string,string> Colors`, `BorderSpec Borders`, `PageSpec Page`, `string FooterTemplate`, `Wordmark Wordmark`.
  - `ConfigBundle.Style` (`StyleRules`); a `style-rules.json` hash entry.
  - `ConfigLocator.FindAsset(baseDir, fileName)` -> absolute path to `assets/<fileName>`.

- [ ] **Step 1: Add the wordmark to style-rules.json**

In `config/style-rules.json`, inside `visualIdentity`, add a `wordmark` member (after `footerTemplate`):

```json
    "wordmark": { "asset": "org-logo.png", "heightIn": 0.4 }
```

(Add a comma after the `footerTemplate` line so the object stays valid JSON.)

- [ ] **Step 2: Write the failing tests**

Add to `tests/Registrar.Tests/Config/ConfigLoaderTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void Load_parses_style_rules()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Equal("#002060", bundle.Style.Colors["headingNavy"]);
        Assert.Equal("org-logo.png", bundle.Style.Wordmark.Asset);
        Assert.Equal(0.4, bundle.Style.Wordmark.HeightIn);
        Assert.Equal(8.5, bundle.Style.Page.WidthIn);
        Assert.Contains("[PACKAGE]", bundle.Style.FooterTemplate);
        Assert.True(bundle.Hashes.ContainsKey("style-rules.json"));
    }

    [Fact]
    public void FindAsset_resolves_logo()
    {
        var found = ConfigLocator.FindAsset(AppContext.BaseDirectory, "org-logo.png");
        Assert.True(File.Exists(found));
        Assert.EndsWith("org-logo.png", found.Replace('\\', '/'));
    }

    [Fact]
    public void Load_maps_artifact_type_display_fields()
    {
        var ds = ConfigLoader.Load(RepoRoot()).ArtifactTypesByCode["DS"];

        Assert.Equal("Deployment Summary", ds.Name);
        Assert.Equal("ORG-GOV-DS-NNN", ds.IdPattern);
        Assert.False(string.IsNullOrEmpty(ds.Description));
        Assert.False(string.IsNullOrEmpty(ds.AudienceDefault));
    }
```

Also update the existing hash-count assertion in the class: change `Assert.Equal(5, bundle.Hashes.Count);` to `Assert.Equal(6, bundle.Hashes.Count);`.

Also extend `ArtifactTypeConfig` in `src/Registrar.Core/Config/ConfigModels.cs` (these display fields exist in `artifact-types.json` but Slice 2 did not map them; the renderer needs them). Add to the `ArtifactTypeConfig` record:

```csharp
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? IdPattern { get; init; }
    public string? AudienceDefault { get; init; }
```

(All nullable; `ConfigLoader`'s case-insensitive deserialization maps `name`/`description`/`idPattern`/`audienceDefault` automatically, so no `ConfigLoader` change is needed for these.)

- [ ] **Step 3: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: FAIL - `Style`/`FindAsset` do not exist / hash count is 5.

- [ ] **Step 4: Create `StyleRules`**

Create `src/Registrar.Core/Config/StyleRules.cs`:

```csharp
namespace Registrar.Core.Config;

public sealed record StyleRules
{
    public required FontSet Fonts { get; init; }
    public required IReadOnlyDictionary<string, string> Colors { get; init; }
    public required BorderSpec Borders { get; init; }
    public required PageSpec Page { get; init; }
    public required string FooterTemplate { get; init; }
    public required Wordmark Wordmark { get; init; }
}

public sealed record FontSet
{
    public required FontSpec Heading { get; init; }
    public required FontSpec Body { get; init; }
    public required FontSpec DocumentId { get; init; }
}

public sealed record FontSpec
{
    public string? Family { get; init; }
    public string? Fallback { get; init; }
    public int? SizePt { get; init; }
    public bool? Bold { get; init; }
    public string? Color { get; init; }
}

public sealed record BorderSpec
{
    public int WidthHalfPoints { get; init; }
    public required string Color { get; init; }
}

public sealed record PageSpec
{
    public double WidthIn { get; init; }
    public double HeightIn { get; init; }
    public double MarginsIn { get; init; }
}

public sealed record Wordmark
{
    public required string Asset { get; init; }
    public double HeightIn { get; init; }
}
```

- [ ] **Step 5: Add `Style` to `ConfigBundle`**

In `src/Registrar.Core/Config/ConfigBundle.cs`, add inside the record:

```csharp
    public required StyleRules Style { get; init; }
```

- [ ] **Step 6: Add `FindAsset` to `ConfigLocator`**

In `src/Registrar.Core/ConfigLocator.cs`, add (next to `FindConfig`, reusing the existing private `Find`):

```csharp
    public static string FindAsset(string baseDir, string fileName)
        => Find(baseDir, "assets", fileName);
```

- [ ] **Step 7: Load and hash style-rules.json in `ConfigLoader`**

In `src/Registrar.Core/Config/ConfigLoader.cs`, ensure `using System.Text.Json;` and `using System.Text.Json.Nodes;` are present. In `Load(...)`, read the file near the other config reads:

```csharp
        var styleRulesText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "style-rules.json"));
        var style = JsonNode.Parse(styleRulesText)!["visualIdentity"]!.Deserialize<StyleRules>(Options)!;
```

Add to the returned `ConfigBundle` initializer:

```csharp
            Style = style,
```

And add to the `Hashes` dictionary initializer:

```csharp
                ["style-rules.json"] = Hashing.Sha256Hex(styleRulesText),
```

Note: `Options` is the existing private `JsonSerializerOptions { PropertyNameCaseInsensitive = true }` in `ConfigLoader`; it maps `visualIdentity`'s camelCase members onto the `StyleRules` records, and unmapped members (`colorUsage`) are ignored.

- [ ] **Step 8: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add config/style-rules.json src/Registrar.Core/Config/StyleRules.cs src/Registrar.Core/Config/ConfigBundle.cs src/Registrar.Core/Config/ConfigLoader.cs src/Registrar.Core/Config/ConfigModels.cs src/Registrar.Core/ConfigLocator.cs tests/Registrar.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat(core): load style rules, artifact display fields, and asset locator"
```

---

### Task 3: Extend AgrDocument with the metadata the chrome and build need

**Files:**
- Modify: `src/Registrar.Core/Model/AgrDocument.cs`
- Test: `tests/Registrar.Tests/Model/AgrContextTests.cs`

**Interfaces:**
- Consumes: existing `AgrParser`.
- Produces: `AgrContext` gains `Owner`, `Audience`, `PreparedBy` (required strings) and `Client`, `Engagement` (nullable strings); `AgrDocument` gains `RenderingOptions` (required `AgrRenderingOptions` with `PackageName` and `OutputFileName`).

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Model/AgrContextTests.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Tests.Model;

public class AgrContextTests
{
    [Fact]
    public void Parse_maps_context_and_rendering_metadata()
    {
        var agr = SemanticTestSupport.Agr("agr-sample-ds-draft.json");

        Assert.Equal("Registry Owner", agr.Context.Owner);
        Assert.Equal("Talewind CPA", agr.Context.Client);
        Assert.Equal("ORG Consulting, LLC", agr.Context.PreparedBy);
        Assert.False(string.IsNullOrEmpty(agr.Context.Audience));
        Assert.Equal("Governed AI Rollout", agr.RenderingOptions.PackageName);
        Assert.EndsWith(".docx", agr.RenderingOptions.OutputFileName);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~AgrContextTests"`
Expected: FAIL - the new members do not exist.

- [ ] **Step 3: Extend the model**

In `src/Registrar.Core/Model/AgrDocument.cs`, add these members to the `AgrContext` record (alongside `ReviewDate`/`ReviewDateOverride`):

```csharp
    [JsonPropertyName("owner")]      public required string Owner { get; init; }
    [JsonPropertyName("audience")]   public required string Audience { get; init; }
    [JsonPropertyName("preparedBy")] public required string PreparedBy { get; init; }
    [JsonPropertyName("client")]     public string? Client { get; init; }
    [JsonPropertyName("engagement")] public string? Engagement { get; init; }
```

Add this member to the `AgrDocument` record (alongside `Identity`/`Context`/`DataInputs`/`ValidationPolicy`):

```csharp
    [JsonPropertyName("renderingOptions")] public required AgrRenderingOptions RenderingOptions { get; init; }
```

And add the new record in the same file:

```csharp
public sealed record AgrRenderingOptions
{
    [JsonPropertyName("packageName")]    public required string PackageName { get; init; }
    [JsonPropertyName("outputFileName")] public required string OutputFileName { get; init; }
}
```

(`owner`/`audience`/`preparedBy` and `renderingOptions.packageName`/`outputFileName` are all required by `agr.schema.json`; `client`/`engagement` are nullable. Every fixture carries them, so parsing still succeeds.)

- [ ] **Step 4: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~AgrContextTests"`
Expected: PASS.

- [ ] **Step 5: Run the full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln`
Expected: PASS - the `required` additions do not break existing AGR parsing (fixtures carry the fields).

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Core/Model/AgrDocument.cs tests/Registrar.Tests/Model/AgrContextTests.cs
git commit -m "feat(core): extend AgrDocument with chrome and rendering metadata"
```

---

### Task 4: OpenXmlHelpers and the ArtifactRenderer skeleton

**Files:**
- Create: `src/Registrar.Rendering/OpenXmlHelpers.cs`
- Create: `src/Registrar.Rendering/ArtifactRenderer.cs`
- Test: `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`

**Interfaces:**
- Consumes: `AgrDocument`, `ConfigBundle` (`Style`), `IClock`.
- Produces:
  - `internal static class OpenXmlHelpers`: `int Twips(double inches)`, `long Emu(double inches)`, `string Hex(string color)`, `Run StyledRun(string text, string family, int sizePt, string colorHex, bool bold)`, `Paragraph Line(params Run[] runs)`.
  - `sealed class ArtifactRenderer(IClock clock)` with `byte[] Render(AgrDocument agr, ConfigBundle config, byte[] logoBytes)` producing a valid `.docx` (US-Letter page setup, core properties from the clock; universal chrome is appended by later tasks).

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Rendering;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class ArtifactRendererTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    internal static byte[] LogoBytes()
        => File.ReadAllBytes(ConfigLocator.FindAsset(AppContext.BaseDirectory, "org-logo.png"));

    internal static byte[] RenderGolden()
        => new ArtifactRenderer(new FixedClock()).Render(
            SemanticTestSupport.Agr("agr-sample-ds-draft.json"),
            SemanticTestSupport.Config(),
            LogoBytes());

    [Fact]
    public void Render_produces_valid_docx_with_letter_page_size()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);

        var pageSize = doc.MainDocumentPart!.Document.Body!
            .GetFirstChild<SectionProperties>()!.GetFirstChild<PageSize>()!;
        Assert.Equal(12240U, (uint)pageSize.Width!);   // 8.5in * 1440
        Assert.Equal(15840U, (uint)pageSize.Height!);  // 11in * 1440
        Assert.Equal("ORG-GOV-DS-016", doc.PackageProperties.Title);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ArtifactRendererTests"`
Expected: FAIL - `ArtifactRenderer`/`OpenXmlHelpers` do not exist.

- [ ] **Step 3: Create `OpenXmlHelpers`**

Create `src/Registrar.Rendering/OpenXmlHelpers.cs`:

```csharp
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

internal static class OpenXmlHelpers
{
    private const int TwipsPerInch = 1440;
    private const long EmuPerInch = 914400;

    public static int Twips(double inches) => (int)Math.Round(inches * TwipsPerInch);
    public static long Emu(double inches) => (long)Math.Round(inches * EmuPerInch);

    public static string Hex(string color) => color.TrimStart('#');

    // FontSize.Val is in half-points, so pass point size and it doubles.
    public static Run StyledRun(string text, string family, int sizePt, string colorHex, bool bold)
    {
        var rpr = new RunProperties(
            new RunFonts { Ascii = family, HighAnsi = family },
            new FontSize { Val = (sizePt * 2).ToString() },
            new Color { Val = Hex(colorHex) });
        if (bold) rpr.Append(new Bold());
        return new Run(rpr, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    public static Paragraph Line(params Run[] runs) => new(runs);
}
```

- [ ] **Step 4: Create the `ArtifactRenderer` skeleton**

Create `src/Registrar.Rendering/ArtifactRenderer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public sealed class ArtifactRenderer
{
    private readonly IClock _clock;

    public ArtifactRenderer(IClock clock) => _clock = clock;

    public byte[] Render(AgrDocument agr, ConfigBundle config, byte[] logoBytes)
    {
        var style = config.Style;

        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();

            // Universal chrome is appended here in later tasks (header, DCH, callout, footer).

            body.Append(SectionProps(style));
            main.Document = new Document(body);

            var now = _clock.UtcNow.UtcDateTime;
            doc.PackageProperties.Created = now;
            doc.PackageProperties.Modified = now;
            doc.PackageProperties.Creator = "registrar";
            doc.PackageProperties.Title = agr.Identity.DocumentId;
        }
        return stream.ToArray();
    }

    private static SectionProperties SectionProps(StyleRules style)
    {
        var w = (uint)OpenXmlHelpers.Twips(style.Page.WidthIn);
        var h = (uint)OpenXmlHelpers.Twips(style.Page.HeightIn);
        var m = (uint)OpenXmlHelpers.Twips(style.Page.MarginsIn);
        return new SectionProperties(
            new PageSize { Width = w, Height = h },
            new PageMargin { Top = (int)m, Bottom = (int)m, Left = m, Right = m, Header = 720U, Footer = 720U, Gutter = 0U });
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ArtifactRendererTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Rendering/OpenXmlHelpers.cs src/Registrar.Rendering/ArtifactRenderer.cs tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs
git commit -m "feat(rendering): add ArtifactRenderer skeleton with page setup"
```

---

### Task 5: HeaderTableRenderer with the embedded ORG logo

**Files:**
- Create: `src/Registrar.Rendering/HeaderTableRenderer.cs`
- Modify: `src/Registrar.Rendering/ArtifactRenderer.cs` (append the header table)
- Modify: `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`

**Interfaces:**
- Consumes: `MainDocumentPart`, `AgrDocument`, `ArtifactTypeConfig`, `StyleRules`, logo bytes; `OpenXmlHelpers`.
- Produces: `internal static class HeaderTableRenderer` with `Table Render(MainDocumentPart main, AgrDocument agr, ArtifactTypeConfig type, StyleRules style, byte[] logoBytes)` - a borderless two-cell table (embedded logo image left, three-line title stack right); it adds the PNG image part to `main`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void Render_embeds_logo_and_title_stack()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);

        Assert.NotEmpty(doc.MainDocumentPart!.ImageParts);           // logo embedded
        var text = doc.MainDocumentPart.Document.Body!.InnerText;
        Assert.Contains("Deployment Summary", text);                 // artifact name
        Assert.Contains("Talewind CPA", text);                      // client
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ArtifactRendererTests"`
Expected: FAIL - no image part / title text yet.

- [ ] **Step 3: Implement `HeaderTableRenderer`**

Create `src/Registrar.Rendering/HeaderTableRenderer.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Registrar.Rendering;

internal static class HeaderTableRenderer
{
    private const string PictureUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    public static Table Render(MainDocumentPart main, AgrDocument agr, ArtifactTypeConfig type, StyleRules style, byte[] logoBytes)
    {
        var logoCell = new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1500" }),
            LogoParagraph(main, style, logoBytes));

        var titleCell = new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "3500" }));
        foreach (var line in TitleStack(agr, type, style)) titleCell.Append(line);

        return new Table(Borderless(), new TableRow(logoCell, titleCell));
    }

    private static TableProperties Borderless() => new(
        new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
        new TableBorders(
            new TopBorder { Val = BorderValues.None },
            new BottomBorder { Val = BorderValues.None },
            new LeftBorder { Val = BorderValues.None },
            new RightBorder { Val = BorderValues.None },
            new InsideHorizontalBorder { Val = BorderValues.None },
            new InsideVerticalBorder { Val = BorderValues.None }));

    private static Paragraph LogoParagraph(MainDocumentPart main, StyleRules style, byte[] logoBytes)
    {
        var imagePart = main.AddImagePart(ImagePartType.Png);
        using (var s = new MemoryStream(logoBytes)) imagePart.FeedData(s);
        var relId = main.GetIdOfPart(imagePart);

        var (pw, ph) = PngSize(logoBytes);
        long cy = OpenXmlHelpers.Emu(style.Wordmark.HeightIn);
        long cx = cy * pw / ph;

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = 1U, Name = "ORG Logo" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = "org-logo.png" },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(
                            new A.Blip { Embed = relId },
                            new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(
                                new A.Offset { X = 0L, Y = 0L },
                                new A.Extents { Cx = cx, Cy = cy }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = PictureUri }))
            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });

        return new Paragraph(new Run(drawing));
    }

    private static Paragraph[] TitleStack(AgrDocument agr, ArtifactTypeConfig type, StyleRules style)
    {
        var navy = style.Colors["headingNavy"];
        var gray = style.Colors["secondaryGray"];
        var family = style.Fonts.Heading.Family ?? style.Fonts.Heading.Fallback ?? "Arial";
        var clientOrEngagement = agr.Context.Client ?? agr.Context.Engagement ?? string.Empty;

        return new[]
        {
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun(type.Name ?? type.Code, family, 16, navy, true)),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun(clientOrEngagement, family, 11, gray, false)),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun("ORG AI Governance Program", family, 9, gray, false))
        };
    }

    // PNG width/height live big-endian in the IHDR chunk at byte offsets 16 and 20.
    private static (int Width, int Height) PngSize(byte[] png)
    {
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }
}
```

- [ ] **Step 4: Append the header table in `ArtifactRenderer`**

In `src/Registrar.Rendering/ArtifactRenderer.cs`, replace the comment placeholder line inside `Render` (`// Universal chrome is appended here in later tasks (header, DCH, callout, footer).`) with:

```csharp
            var type = config.ArtifactTypesByCode[agr.Identity.ArtifactType];
            body.Append(HeaderTableRenderer.Render(main, agr, type, style, logoBytes));
            // DCH, callout, and footer are appended by later tasks.
```

- [ ] **Step 5: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ArtifactRendererTests"`
Expected: PASS - both render tests.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Rendering/HeaderTableRenderer.cs src/Registrar.Rendering/ArtifactRenderer.cs tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs
git commit -m "feat(rendering): render header table with embedded logo and title stack"
```

---

### Task 6: DchRenderer (Document Control Header)

**Files:**
- Create: `src/Registrar.Rendering/DchRenderer.cs`
- Modify: `src/Registrar.Rendering/ArtifactRenderer.cs`
- Modify: `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`

**Interfaces:**
- Consumes: `AgrDocument`, `StyleRules`, `OpenXmlHelpers`.
- Produces: `internal static class DchRenderer` with `IEnumerable<OpenXmlElement> Render(AgrDocument agr, StyleRules style)` - the banner label paragraph and a two-row, four-column table whose cells carry the DCH label/value pairs.

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`:

```csharp
    [Fact]
    public void Render_includes_dch_with_metadata()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;

        Assert.Contains("DOCUMENT CONTROL HEADER", text);
        Assert.Contains("ORG-GOV-DS-016", text);
        Assert.Contains("v1.0", text);
        Assert.Contains("Client-Facing", text);
        Assert.Contains("Draft", text);
        Assert.Contains("Registry Owner", text);
        Assert.Contains("To be set on Approval", text);   // golden reviewDate is null
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ArtifactRendererTests"`
Expected: FAIL - no DCH content yet.

- [ ] **Step 3: Implement `DchRenderer`**

Create `src/Registrar.Rendering/DchRenderer.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

internal static class DchRenderer
{
    public static IEnumerable<OpenXmlElement> Render(AgrDocument agr, StyleRules style)
    {
        yield return Banner(style);
        yield return Table(agr, style);
    }

    private static Paragraph Banner(StyleRules style)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";
        return OpenXmlHelpers.Line(
            OpenXmlHelpers.StyledRun("DOCUMENT CONTROL HEADER", family, 9, style.Colors["secondaryGray"], true));
    }

    private static Table Table(AgrDocument agr, StyleRules style)
    {
        var id = agr.Identity;
        var ctx = agr.Context;
        var reviewDate = string.IsNullOrEmpty(ctx.ReviewDate) ? "To be set on Approval" : ctx.ReviewDate!;

        var row1 = new TableRow(
            Cell("Document ID", id.DocumentId, style, monospace: true),
            Cell("Version", id.Version, style),
            Cell("Classification", id.Classification, style),
            Cell("Approval Status", id.ApprovalStatus, style));
        var row2 = new TableRow(
            Cell("Owner", ctx.Owner, style),
            Cell("Review Date", reviewDate, style),
            Cell("Audience", ctx.Audience, style),
            Cell("Prepared By", ctx.PreparedBy, style));

        return new Table(TableProps(style), row1, row2);
    }

    private static TableProperties TableProps(StyleRules style)
    {
        var color = OpenXmlHelpers.Hex(style.Colors["borderNeutral"]);
        var size = (uint)style.Borders.WidthHalfPoints;
        return new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = size, Color = color },
                new BottomBorder { Val = BorderValues.Single, Size = size, Color = color },
                new LeftBorder { Val = BorderValues.Single, Size = size, Color = color },
                new RightBorder { Val = BorderValues.Single, Size = size, Color = color },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = size, Color = color },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = size, Color = color }));
    }

    private static TableCell Cell(string label, string value, StyleRules style, bool monospace = false)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";
        var labelPara = OpenXmlHelpers.Line(
            OpenXmlHelpers.StyledRun(label, family, 8, style.Colors["headingNavy"], true));

        var valueRun = monospace
            ? OpenXmlHelpers.StyledRun(value, style.Fonts.DocumentId.Family ?? "Consolas",
                  style.Fonts.DocumentId.SizePt ?? 10, style.Fonts.DocumentId.Color ?? "#000000",
                  style.Fonts.DocumentId.Bold ?? true)
            : OpenXmlHelpers.StyledRun(value, family, 10, "#000000", false);

        return new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1250" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = OpenXmlHelpers.Hex(style.Colors["dchFill"]) }),
            labelPara,
            OpenXmlHelpers.Line(valueRun));
    }
}
```

- [ ] **Step 4: Append the DCH in `ArtifactRenderer`**

In `src/Registrar.Rendering/ArtifactRenderer.cs`, replace the line `// DCH, callout, and footer are appended by later tasks.` with:

```csharp
            foreach (var element in DchRenderer.Render(agr, style)) body.Append(element);
            // Callout and footer are appended by the next task.
```

- [ ] **Step 5: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ArtifactRendererTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Rendering/DchRenderer.cs src/Registrar.Rendering/ArtifactRenderer.cs tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs
git commit -m "feat(rendering): render the Document Control Header"
```

---

### Task 7: Configuration callout and footer

**Files:**
- Create: `src/Registrar.Rendering/ConfigCalloutRenderer.cs`
- Create: `src/Registrar.Rendering/FooterRenderer.cs`
- Modify: `src/Registrar.Rendering/ArtifactRenderer.cs`
- Modify: `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`

**Interfaces:**
- Consumes: `AgrDocument`, `ArtifactTypeConfig`, `StyleRules`, `MainDocumentPart`.
- Produces:
  - `internal static class ConfigCalloutRenderer` with `Table Render(AgrDocument agr, ArtifactTypeConfig type, StyleRules style)`.
  - `internal static class FooterRenderer` with `string Create(MainDocumentPart main, AgrDocument agr, ArtifactTypeConfig type, StyleRules style)` returning the footer part relationship id.
  - `ArtifactRenderer.SectionProps` now includes a `FooterReference` to the created footer.

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs`:

```csharp
    [Fact]
    public void Render_includes_callout_and_footer()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);
        var bodyText = doc.MainDocumentPart!.Document.Body!.InnerText;

        Assert.Contains("Configuration", bodyText);
        Assert.Contains("ORG-GOV-DS-NNN", bodyText);           // artifact id pattern in the callout

        Assert.NotEmpty(doc.MainDocumentPart.FooterParts);
        var footerText = doc.MainDocumentPart.FooterParts.First().Footer.InnerText;
        Assert.Contains("Governed AI Rollout", footerText);    // package name
        Assert.Contains("ORG Consulting, LLC", footerText);
        Assert.Contains("Page", footerText);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ArtifactRendererTests"`
Expected: FAIL - no callout / footer yet.

- [ ] **Step 3: Implement `ConfigCalloutRenderer`**

Create `src/Registrar.Rendering/ConfigCalloutRenderer.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

internal static class ConfigCalloutRenderer
{
    public static Table Render(AgrDocument agr, ArtifactTypeConfig type, StyleRules style)
    {
        var navy = style.Colors["headingNavy"];
        var blue = OpenXmlHelpers.Hex(style.Colors["primaryBlue"]);
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";

        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = OpenXmlHelpers.Hex(style.Colors["headerRightCellFill"]) }),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun("Configuration", family, 11, navy, true)),
            LabelValue("Artifact", type.Name ?? type.Code, family, navy),
            LabelValue("Description", type.Description ?? string.Empty, family, navy),
            LabelValue("Audience", agr.Context.Audience, family, navy),
            LabelValue("ID Pattern", type.IdPattern ?? string.Empty, family, navy));

        return new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 12U, Color = blue },
                    new BottomBorder { Val = BorderValues.Single, Size = 12U, Color = blue },
                    new LeftBorder { Val = BorderValues.Single, Size = 12U, Color = blue },
                    new RightBorder { Val = BorderValues.Single, Size = 12U, Color = blue },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None })),
            new TableRow(cell));
    }

    private static Paragraph LabelValue(string label, string value, string family, string labelColor)
        => new(
            OpenXmlHelpers.StyledRun(label + ": ", family, 10, labelColor, true),
            OpenXmlHelpers.StyledRun(value, family, 10, "#000000", false));
}
```

- [ ] **Step 4: Implement `FooterRenderer`**

Create `src/Registrar.Rendering/FooterRenderer.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

internal static class FooterRenderer
{
    public static string Create(MainDocumentPart main, AgrDocument agr, ArtifactTypeConfig type, StyleRules style)
    {
        var footerPart = main.AddNewPart<FooterPart>();
        footerPart.Footer = new Footer(FooterParagraph(agr, type, style));
        return main.GetIdOfPart(footerPart);
    }

    private static Paragraph FooterParagraph(AgrDocument agr, ArtifactTypeConfig type, StyleRules style)
    {
        var gray = style.Colors["secondaryGray"];
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";

        var resolved = style.FooterTemplate
            .Replace("[PACKAGE]", agr.RenderingOptions.PackageName)
            .Replace("[ARTIFACT]", type.Name ?? type.Code)
            .Replace("[VERSION]", agr.Identity.Version.TrimStart('v'));

        // The template carries the literal "Page X of Y"; replace it with live fields.
        var parts = resolved.Split("Page X of Y");
        var before = parts[0] + "Page ";
        var after = parts.Length > 1 ? " " + parts[1].TrimStart() : string.Empty;

        return new Paragraph(
            OpenXmlHelpers.StyledRun(before, family, 8, gray, false),
            Field("PAGE", family, gray),
            OpenXmlHelpers.StyledRun(" of ", family, 8, gray, false),
            Field("NUMPAGES", family, gray),
            OpenXmlHelpers.StyledRun(after, family, 8, gray, false));
    }

    private static SimpleField Field(string instruction, string family, string colorHex)
        => new(OpenXmlHelpers.StyledRun("1", family, 8, colorHex, false)) { Instruction = instruction };
}
```

- [ ] **Step 5: Wire the callout and footer into `ArtifactRenderer`**

In `src/Registrar.Rendering/ArtifactRenderer.cs`:

First, change the `Render` body so the footer part is created and referenced. The section-assembly block (which currently resolves `type`, appends the header, appends the DCH, and ends with `// Callout and footer are appended by the next task.`) becomes:

```csharp
            var type = config.ArtifactTypesByCode[agr.Identity.ArtifactType];
            var footerId = FooterRenderer.Create(main, agr, type, style);

            body.Append(HeaderTableRenderer.Render(main, agr, type, style, logoBytes));
            foreach (var element in DchRenderer.Render(agr, style)) body.Append(element);
            body.Append(ConfigCalloutRenderer.Render(agr, type, style));

            body.Append(SectionProps(style, footerId));
            main.Document = new Document(body);
```

(Remove the earlier `body.Append(SectionProps(style));` line that had no footer id.)

Then change `SectionProps` to take the footer id and reference it:

```csharp
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
```

- [ ] **Step 6: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ArtifactRendererTests"`
Expected: PASS - all four render tests.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Rendering/ConfigCalloutRenderer.cs src/Registrar.Rendering/FooterRenderer.cs src/Registrar.Rendering/ArtifactRenderer.cs tests/Registrar.Tests/Rendering/ArtifactRendererTests.cs
git commit -m "feat(rendering): render the Configuration callout and footer"
```

---

### Task 8: Extract shared CommandSupport and refactor ValidateCommand

**Files:**
- Create: `src/Registrar.Cli/CommandSupport.cs`
- Modify: `src/Registrar.Cli/ValidateCommand.cs`

**Interfaces:**
- Consumes: `ValidationService`, `ReportSerializer`, `PromotionPayloadSerializer`, `ConfigLocator`, `ValidationOutcome`.
- Produces:
  - `internal static class CommandSupport` with:
    - `(int? Exit, ValidationOutcome? Outcome, string Dir, string RepoRoot, string AgrText) Validate(string agrPath, IClock clock, TextWriter stderr, string? registryPath)` - resolves paths, reads inputs, runs validation; `Exit` is a non-null usage/IO exit code (2) when resolution fails, otherwise the outcome + resolved dirs are returned.
    - `int? WriteOutputs(ValidationOutcome outcome, string dir, TextWriter stderr)` - writes `validation-report.json` and (when present) `promotion-payload.json`; returns 2 on an IO error, else null.
- This task is a behavior-preserving refactor: the existing `ValidateCommandTests` must still pass unchanged.

- [ ] **Step 1: Create `CommandSupport`**

Create `src/Registrar.Cli/CommandSupport.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Cli;

internal static class CommandSupport
{
    public static (int? Exit, ValidationOutcome? Outcome, string Dir, string RepoRoot, string AgrText) Validate(
        string agrPath, IClock clock, TextWriter stderr, string? registryPath)
    {
        if (!File.Exists(agrPath))
        {
            stderr.WriteLine($"error: AGR file not found: {agrPath}");
            return (2, null, string.Empty, string.Empty, string.Empty);
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(agrPath))!;

        string schemaPath;
        try
        {
            schemaPath = ConfigLocator.FindSchema(dir, "agr.schema.json");
        }
        catch (FileNotFoundException)
        {
            try
            {
                schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
            }
            catch (FileNotFoundException ex)
            {
                stderr.WriteLine($"error: {ex.Message}");
                return (2, null, string.Empty, string.Empty, string.Empty);
            }
        }
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;

        string agrText;
        try
        {
            agrText = File.ReadAllText(agrPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"error: cannot read AGR: {ex.Message}");
            return (2, null, string.Empty, string.Empty, string.Empty);
        }

        string? registryCsvText = null;
        if (registryPath is not null)
        {
            if (!File.Exists(registryPath))
            {
                stderr.WriteLine($"error: registry file not found: {registryPath}");
                return (2, null, string.Empty, string.Empty, string.Empty);
            }
            try
            {
                registryCsvText = File.ReadAllText(registryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: cannot read registry: {ex.Message}");
                return (2, null, string.Empty, string.Empty, string.Empty);
            }
        }

        var outcome = new ValidationService(clock).Validate(agrText, schemaPath, registryCsvText);
        return (null, outcome, dir, repoRoot, agrText);
    }

    public static int? WriteOutputs(ValidationOutcome outcome, string dir, TextWriter stderr)
    {
        var reportPath = Path.Combine(dir, "validation-report.json");
        try
        {
            File.WriteAllText(reportPath, ReportSerializer.ToJson(outcome.Report));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"error: cannot write validation report: {ex.Message}");
            return 2;
        }

        if (outcome.Payload is not null)
        {
            var payloadPath = Path.Combine(dir, "promotion-payload.json");
            try
            {
                File.WriteAllText(payloadPath, PromotionPayloadSerializer.ToJson(outcome.Payload));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: cannot write promotion payload: {ex.Message}");
                return 2;
            }
        }
        return null;
    }
}
```

- [ ] **Step 2: Refactor `ValidateCommand` onto `CommandSupport`**

Replace the body of `Run` in `src/Registrar.Cli/ValidateCommand.cs` (keep the method signature `Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? registryPath = null)`):

```csharp
    public static int Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? registryPath = null)
    {
        var (exit, outcome, dir, _, _) = CommandSupport.Validate(agrPath, clock, stderr, registryPath);
        if (exit is not null) return exit.Value;

        var write = CommandSupport.WriteOutputs(outcome!, dir, stderr);
        if (write is not null) return write.Value;

        var report = outcome!.Report;
        var failCount = report.Checks.Count(c => c.Result == CheckResultValue.Fail);
        var warnCount = report.Checks.Count(c => c.Result == CheckResultValue.Warn);
        stdout.WriteLine(
            $"{report.OverallResult}: {report.DocumentId} ({failCount} failing, {warnCount} warning check(s)). Report: {Path.Combine(dir, "validation-report.json")}");

        return report.OverallResult == OverallResult.Fail ? 1 : 0;
    }
```

Ensure the file's usings still cover what `Run` uses (`Registrar.Core` for `CheckResultValue`/`OverallResult`/`IClock`); remove any now-unused usings the compiler flags.

- [ ] **Step 3: Run the validate tests to verify no behavior change**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidateCommandTests"`
Expected: PASS - every existing validate test still passes (exit codes and output files unchanged).

- [ ] **Step 4: Run the full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Cli/CommandSupport.cs src/Registrar.Cli/ValidateCommand.cs
git commit -m "refactor(cli): extract shared CommandSupport from ValidateCommand"
```

---

### Task 9: BuildCommand, the `build` verb, and end-to-end

**Files:**
- Create: `src/Registrar.Cli/BuildCommand.cs`
- Modify: `src/Registrar.Cli/Program.cs`
- Test: `tests/Registrar.Tests/BuildCommandTests.cs`

**Interfaces:**
- Consumes: `CommandSupport`, `AgrParser`, `ConfigLoader`, `ConfigLocator`, `ArtifactRenderer`, `OverallResult`/`Mode`.
- Produces: `static class BuildCommand { int Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? mode = null, string? registryPath = null); }` - validates, writes the report/payload, and on a DOCX-eligible pass renders and writes the `.docx` under `renderingOptions.outputFileName`. The `build` verb wires it in `Program.cs`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/BuildCommandTests.cs`:

```csharp
using Registrar.Cli;
using Registrar.Core;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests;

public class BuildCommandTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    private static string CopyToTemp(string repoRelative)
    {
        var src = Path.Combine(RepoRoot(), repoRelative);
        var dir = Directory.CreateTempSubdirectory().FullName;
        var dest = Path.Combine(dir, Path.GetFileName(src));
        File.Copy(src, dest);
        return dest;
    }

    [Fact]
    public void Valid_draft_build_writes_a_docx()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, code);
        var docxPath = Path.Combine(Path.GetDirectoryName(agrPath)!,
            "ORG - Deployment Summary - Talewind CPA - v1.0.docx");
        Assert.True(File.Exists(docxPath));

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var text = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("ORG-GOV-DS-016", text);
        Assert.NotEmpty(doc.MainDocumentPart.ImageParts);
    }

    [Fact]
    public void Invalid_agr_build_writes_no_docx_and_exits_1()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-invalid-schema.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));
    }

    [Fact]
    public void Mode_flag_mismatch_exits_2()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json"); // mode is draft
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, "promotion");

        Assert.Equal(2, code);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BuildCommandTests"`
Expected: FAIL - `BuildCommand` does not exist.

- [ ] **Step 3: Implement `BuildCommand`**

Create `src/Registrar.Cli/BuildCommand.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Rendering;

namespace Registrar.Cli;

public static class BuildCommand
{
    public static int Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr,
        string? mode = null, string? registryPath = null)
    {
        var (exit, outcome, dir, repoRoot, agrText) = CommandSupport.Validate(agrPath, clock, stderr, registryPath);
        if (exit is not null) return exit.Value;

        var write = CommandSupport.WriteOutputs(outcome!, dir, stderr);
        if (write is not null) return write.Value;

        var report = outcome!.Report;

        // --mode, when supplied, must agree with the AGR's declared mode.
        if (mode is not null && !string.Equals(mode, report.RequestedMode, StringComparison.Ordinal))
        {
            stderr.WriteLine($"error: --mode '{mode}' does not match the AGR validationPolicy.mode '{report.RequestedMode}'.");
            return 2;
        }

        if (report.OverallResult == OverallResult.Fail)
        {
            stdout.WriteLine($"fail: {report.DocumentId} - no document produced (validation failed).");
            return 1;
        }

        var docxEligible = report.OverallResult == OverallResult.Pass
            || (report.OverallResult == OverallResult.PassWithWarnings && report.RequestedMode == Mode.Draft);

        if (docxEligible)
        {
            var agr = AgrParser.Parse(agrText);
            var config = ConfigLoader.Load(repoRoot);
            var logoBytes = File.ReadAllBytes(ConfigLocator.FindAsset(repoRoot, config.Style.Wordmark.Asset));
            var docxBytes = new ArtifactRenderer(clock).Render(agr, config, logoBytes);

            var docxPath = Path.Combine(dir, agr.RenderingOptions.OutputFileName);
            try
            {
                File.WriteAllBytes(docxPath, docxBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: cannot write document: {ex.Message}");
                return 2;
            }

            stdout.WriteLine($"{report.OverallResult}: {report.DocumentId} - {Path.GetFileName(docxPath)}");
        }
        else
        {
            stdout.WriteLine($"{report.OverallResult}: {report.DocumentId} - no document (promotion requires a clean pass).");
        }

        return 0;
    }
}
```

- [ ] **Step 4: Add the `build` verb in `Program.cs`**

In `src/Registrar.Cli/Program.cs`, register a `build` command mirroring how the existing `validate` verb is registered (the resolved System.CommandLine v3 surface this repo already uses - argument via `Command.Arguments.Add`, options via `Command.Options.Add`, handler via `Command.SetAction`, subcommand via the root's `Subcommands.Add`/`Add`). It takes the AGR path argument, a `--mode` option, and a `--registry` option, forwarding to `BuildCommand.Run`. For example (adapt names/API to match the existing `validate` registration exactly):

```csharp
var buildAgr = new Argument<string>("agr.json") { Description = "Path to the AGR JSON file to build." };
var buildMode = new Option<string?>("--mode") { Description = "draft or promotion; must match the AGR mode." };
var buildRegistry = new Option<string?>("--registry") { Description = "Path to a CSV export of the ID Registry." };

var build = new Command("build", "Validate an AGR and, on a pass, render the DOCX.");
build.Arguments.Add(buildAgr);
build.Options.Add(buildMode);
build.Options.Add(buildRegistry);
build.SetAction(parseResult => BuildCommand.Run(
    parseResult.GetValue(buildAgr)!, new SystemClock(), Console.Out, Console.Error,
    parseResult.GetValue(buildMode), parseResult.GetValue(buildRegistry)));

root.Subcommands.Add(build);
```

Keep the contract if the resolved API differs: a `build` subcommand taking the AGR argument plus `--mode` and `--registry`, forwarding to `BuildCommand.Run`. All build behavior is tested through `BuildCommand.Run`, so `Program.cs` stays a thin adapter.

- [ ] **Step 5: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BuildCommandTests"`
Expected: PASS - 3 tests.

- [ ] **Step 6: Run the full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln`
Expected: PASS - all tests.

- [ ] **Step 7: Verify the CLI end-to-end by hand**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project src/Registrar.Cli -- build fixtures/agr-sample-ds-draft.json`
Expected: prints `pass: ORG-GOV-DS-016 - ORG - Deployment Summary - Talewind CPA - v1.0.docx`, exit 0, and writes that `.docx` in `fixtures/`. Open it in Word to eyeball the header (logo + title), DCH, callout, and footer.

Clean up the hand-run outputs so they are not committed:

```bash
rm -f "fixtures/validation-report.json" "fixtures/ORG - Deployment Summary - Talewind CPA - v1.0.docx"
```

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Cli/BuildCommand.cs src/Registrar.Cli/Program.cs tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "feat(cli): add build verb rendering the DOCX on a passing validation"
```

---

## Self-Review

**Spec coverage** (each spec section maps to a task):
- `Registrar.Rendering` project + Open XML dependency -> Task 1.
- `StyleRules` from `style-rules.json`, `ConfigBundle.Style`, wordmark reference, `FindAsset`, artifact-type display fields -> Task 2.
- `AgrContext`/`AgrRenderingOptions` extensions -> Task 3.
- `OpenXmlHelpers` + `ArtifactRenderer` page setup + clock core properties -> Task 4.
- Header table with embedded logo + title stack -> Task 5.
- Document Control Header -> Task 6.
- Configuration callout + footer (with page fields) -> Task 7.
- `build` = validate + render-on-pass; shared CommandSupport -> Tasks 8-9.
- DOCX emission rule (pass, or draft pass-with-warnings; fail closed) -> Task 9 (`docxEligible`).
- Determinism via `IClock`, content-based tests -> Tasks 4-9.
- Deferred (body sections, manifest, generation-log) -> not implemented.

**Placeholder scan:** No TBD/TODO. Every code step shows complete code; every command shows expected output and the `DOTNET_ROLL_FORWARD` prefix.

**Type consistency:** `ArtifactRenderer(IClock).Render(AgrDocument, ConfigBundle, byte[])` defined in Task 4, extended (not resignatured) in Tasks 5-7, called in Task 9. Component signatures - `HeaderTableRenderer.Render(MainDocumentPart, AgrDocument, ArtifactTypeConfig, StyleRules, byte[])`, `DchRenderer.Render(AgrDocument, StyleRules)`, `ConfigCalloutRenderer.Render(AgrDocument, ArtifactTypeConfig, StyleRules)`, `FooterRenderer.Create(MainDocumentPart, AgrDocument, ArtifactTypeConfig, StyleRules) -> string` - are defined and consumed consistently. `StyleRules`/`FontSet`/`FontSpec`/`BorderSpec`/`PageSpec`/`Wordmark` (Task 2) and `ConfigBundle.Style` used throughout Tasks 4-9. `ArtifactTypeConfig.Name`/`Description`/`IdPattern` (Task 2) used in Tasks 5/7. `AgrContext.Owner`/`Audience`/`PreparedBy`/`Client` and `AgrRenderingOptions.PackageName`/`OutputFileName` (Task 3) used in Tasks 6/7/9. `CommandSupport.Validate`/`WriteOutputs` (Task 8) consumed by `ValidateCommand` (Task 8) and `BuildCommand` (Task 9). `OpenXmlHelpers.StyledRun`/`Line`/`Twips`/`Emu`/`Hex` (Task 4) used by every renderer.

**Known risks:** (1) Open XML is verbose and version-sensitive; the smoke test (Task 1) proves the SDK, and every renderer task reopens the `.docx` and asserts content, so a malformed element surfaces immediately. (2) `System.CommandLine` is prerelease; the `build` registration mirrors the existing `validate` verb and all behavior is tested through `BuildCommand.Run`, so a differing beta API surfaces at Task 9 Step 5. (3) The `ArtifactRenderer.Render` signature stays fixed from Task 4; Tasks 5-7 only add body content and one `SectionProps` parameter (Task 7), so earlier render tests continue to pass as chrome accretes.
