# Editor Screen (GUI-7) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A technician can create an artifact, author its sections, watch the engine validate as they type, and see exactly why Build is not yet legal, without hand-editing JSON.

**Architecture:** Six pure types in `Registrar.Core` decide everything (pointers, form shape, mutations, creation defaults, the rail). `Registrar.App` renders them and holds no rule of its own. Every input's DOM id is derived from its JSON Pointer, so the rail navigates by derivation rather than by a registry that would have to be kept in step.

**Tech Stack:** .NET 8, C#, Blazor components hosted in WPF via `Microsoft.AspNetCore.Components.WebView.Wpf`, xUnit, bUnit 1.40.

## Global Constraints

These bind every task. Copied from `CLAUDE.md` and `docs/superpowers/specs/2026-08-04-editor-screen-design.md`.

- **US English, no emojis, no em dashes or en dashes, no decorative unicode** in any source, config, comment, test name, markup, or commit message. Use a plain hyphen, straight quotes, and three periods. `SourceHygieneTests` enforces this on engine source: express any non-ASCII character in a test as an escape or a code point, never as a literal.
- **Zero LLM calls.** Nothing in this unit contacts a model.
- **Configuration over code.** No artifact constant, section title, clause text, colour, or radio minimum may be hard-coded in `src/`. The radio minimum is `config.RadioPattern.MinimumOptions`; the character policy is `CharacterPolicy.FromConfig(config.ContentLint)`; section titles are `type.SectionSchema.OrderedSections`.
- **Rule 6.** An artifact type whose `SectionSchema.Status` is not `"defined"` must never be offered for creation.
- **Rule 4.** Clause text is inserted by reference. The canonical text is displayed with no editable control bound to it.
- **`Registrar.App` enforces no rules of its own.** A condition in a `.razor` file is presentation only. Anything a technician could get wrong belongs in Core, where `tests/Registrar.Tests` (net8.0) can reach it.
- **No new validation rules, no changes to `agr.schema.json`, no changes to `ConfigBundle.Hashes`, no changes to `EngagementRow` or `AgrPicker`.**
- **No colour literals in `app.css`.** Every colour is a `var(--org-*)` custom property from `config/generated/org-tokens.css`, as GUI-5 established.
- Run the full suite with `dotnet test Registrar.sln` from the repo root. It must stay green and warning-free.
- End every commit message with the repo trailer: `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

---

## File Structure

**Create in `src/Registrar.Core/Authoring/`:**
- `FieldPath.cs` - the JSON Pointer dialect and `ElementId`. No other file builds a pointer by hand.
- `EditorForm.cs` - `EditorForm`, `FormSection`, `FormBlock`, `BlockEditorKind`. Turns a draft into the panels the editor renders.
- `DraftEdits.cs` - every `AgrDraft -> AgrDraft` mutation, including section normalization.
- `DraftCreation.cs` - a new draft from five inputs, and its file name.
- `EditorRail.cs` - `RailItem`, `RailSeverity`, `EditorRail`. Merges gaps or checks and computes the Build-blocked sentence.

**Create in `src/Registrar.App/`:**
- `AppState.cs` - the open draft, its path, the engagement folder. No rules.
- `Components/NewArtifact.razor` - `/new`.
- `Components/Editor.razor` - `/editor`.
- `Components/IdentityPane.razor`, `SectionPanel.razor`, `ProseBlock.razor`, `ClauseBlock.razor`, `RadioBlock.razor`, `ReadOnlyBlock.razor`, `ValidationRail.razor`, `EditorHeader.razor`.
- `wwwroot/editor.js` - one exported function that focuses an element by id.

**Create in tests:**
- `tests/Registrar.Tests/Authoring/FieldPathTests.cs`
- `tests/Registrar.Tests/Authoring/EditorFormTests.cs`
- `tests/Registrar.Tests/Authoring/DraftEditsTests.cs`
- `tests/Registrar.Tests/Authoring/DraftCreationTests.cs`
- `tests/Registrar.Tests/Authoring/EditorRailTests.cs`
- `tests/Registrar.App.Tests/BlockEditorComponentTests.cs`
- `tests/Registrar.App.Tests/ValidationRailComponentTests.cs`

**Modify:**
- `src/Registrar.App/MainWindow.xaml.cs` - register `EngineContext`, `IClock`, `AppState` as singletons.
- `src/Registrar.App/Components/Home.razor` - an open action per row; a New button.
- `src/Registrar.App/Components/Engagement.razor` - one column carrying the open callback.
- `src/Registrar.App/_Imports.razor` - the namespaces the new components need.
- `src/Registrar.App/wwwroot/app.css` - editor layout and rail styles, tokens only.
- `src/Registrar.App/wwwroot/index.html` - load `editor.js`.
- `docs/superpowers/specs/2026-08-01-operator-gui-design.md` - amendment recording that New artifact ships in GUI-7.
- `docs/ROADMAP.md` - GUI-7 status.
- `CLAUDE.md` - the draft-as-source-of-truth amendment.

---

### Task 1: FieldPath, the one place that spells a pointer

**Files:**
- Create: `src/Registrar.Core/Authoring/FieldPath.cs`
- Test: `tests/Registrar.Tests/Authoring/FieldPathTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `FieldPath.ArtifactType`, `.DocumentId`, `.ApprovalStatus`, `.Layer`, `.Owner`, `.ReviewDate`, `.Client`, `.Engagement`, `.PlaceholderMap`, `.SourceWorkflow` (all `const string`); `FieldPath.Section(int)`, `.SectionBlocks(int)`, `.Block(int, int)`, `.BlockField(int, int, string)`, `.ElementId(string)` (all `static string`).

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Authoring/FieldPathTests.cs`:

```csharp
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests.Authoring;

// The value of these tests is not that the strings look right. It is that the pointers
// FieldPath builds are the same pointers the validation layers emit for the same field.
// A layer that changes its spelling breaks a test here rather than quietly breaking rail
// navigation, which nothing else would notice.
public class FieldPathTests
{
    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static AgrDocument DocumentWithBlock(AgrBlock block) => new()
    {
        SchemaVersion = "2.0.0",
        Identity = new AgrIdentity
        {
            ArtifactType = "TCL", DocumentId = "ORG-OPS-TCL-001", Version = "0.1",
            Classification = "Client-Facing", ApprovalStatus = "Draft", Layer = "Delivery"
        },
        Context = new AgrContext
        {
            Owner = "Registry Owner", Audience = "Client", PreparedBy = "ORG",
            Client = null, Engagement = null, ReviewDate = null
        },
        SourceWorkflowContext = new AgrSourceWorkflowContext
        {
            WorkflowName = "test", Phase = "test", ExecutionId = "00000000-0000-0000-0000-000000000001"
        },
        DataInputs = new AgrDataInputs
        {
            PlaceholderMap = new Dictionary<string, string?>(),
            Sections = new[] { new AgrSection { Title = "Questions", DescriptorLine = null, Blocks = new[] { block } } }
        },
        ValidationPolicy = new AgrValidationPolicy { Mode = Mode.Draft },
        RenderingOptions = new AgrRenderingOptions
        {
            PackageName = "test", OutputFileName = "ORG - Technical Control List - v0.1.docx"
        }
    };

    [Fact]
    public void The_radio_layer_reports_at_the_pointer_FieldPath_builds()
    {
        var config = ConfigLoader.Load(RepoRoot());

        // One option short of the configured minimum, so QA-SF-10 reports on options.
        var options = config.RadioPattern.StandardOptionSet
            .Take(config.RadioPattern.MinimumOptions - 1).ToArray();

        var block = new AgrBlock
        {
            BlockType = "radioQuestion",
            Question = new AgrQuestion { Number = 1, Prompt = "Is it in place?", Required = true, Options = options }
        };

        var checks = new RadioQuestionLayer().Validate(
            new SemanticContext(DocumentWithBlock(block), config, Mode.Draft, null));

        var reported = checks.Single(c => c.Result == CheckResultValue.Fail);
        Assert.Equal(FieldPath.BlockField(0, 0, "question/options"), reported.JsonPath);
    }

    [Fact]
    public void The_block_type_layer_reports_at_the_pointer_FieldPath_builds()
    {
        var config = ConfigLoader.Load(RepoRoot());

        var block = new AgrBlock { BlockType = "aggregationTable", AggregationSource = "notASource" };

        var checks = new BlockTypeSupportLayer().Validate(
            new SemanticContext(DocumentWithBlock(block), config, Mode.Draft, null));

        var reported = checks.Single(c => c.Result == CheckResultValue.Fail);
        Assert.Equal(FieldPath.BlockField(0, 0, "aggregationSource"), reported.JsonPath);
    }

    // An id has to be usable in a CSS selector and as an anchor target, so the separator
    // cannot be a slash. Every pointer starts with one, so the prefix keeps the id from
    // beginning with the separator.
    [Fact]
    public void Element_ids_are_derived_from_the_pointer_and_are_selector_safe()
    {
        Assert.Equal("f-dataInputs-sections-2-blocks-0-text",
            FieldPath.ElementId(FieldPath.BlockField(2, 0, "text")));
        Assert.Equal("f-artifactIdentity-documentId", FieldPath.ElementId(FieldPath.DocumentId));
        Assert.DoesNotContain("/", FieldPath.ElementId(FieldPath.Block(1, 1)), StringComparison.Ordinal);
    }

    [Fact]
    public void Distinct_fields_never_collide_on_one_id()
    {
        var ids = new[]
        {
            FieldPath.ElementId(FieldPath.Block(0, 1)),
            FieldPath.ElementId(FieldPath.Block(1, 0)),
            FieldPath.ElementId(FieldPath.BlockField(0, 1, "text")),
            FieldPath.ElementId(FieldPath.SectionBlocks(0)),
            FieldPath.ElementId(FieldPath.Owner)
        };

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~FieldPathTests`
Expected: build failure, `FieldPath` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.Core/Authoring/FieldPath.cs`:

```csharp
namespace Registrar.Core.Authoring;

// The single owner of the JSON Pointer dialect (RFC 6901, rooted at the AGR document)
// that AuthoringGap.JsonPath and Check.JsonPath both already speak. Nothing else in the
// engine or the app builds a pointer by hand.
//
// It exists so the rail can navigate by derivation rather than by a registry: an input's
// DOM id is ElementId(its pointer), so there is no map to populate during render and no
// map to drift. FieldPathTests couples these strings to the pointers the layers actually
// emit, which is the only thing that keeps the derivation honest.
public static class FieldPath
{
    public const string ArtifactType = "/artifactIdentity/artifactType";
    public const string DocumentId = "/artifactIdentity/documentId";
    public const string ApprovalStatus = "/artifactIdentity/approvalStatus";
    public const string Layer = "/artifactIdentity/layer";
    public const string Owner = "/context/owner";
    public const string Client = "/context/client";
    public const string Engagement = "/context/engagement";
    public const string ReviewDate = "/context/reviewDate";
    public const string PlaceholderMap = "/dataInputs/placeholderMap";
    public const string SourceWorkflow = "/sourceWorkflowContext";

    public static string Section(int sectionIndex)
        => $"/dataInputs/sections/{sectionIndex}";

    public static string SectionBlocks(int sectionIndex)
        => $"/dataInputs/sections/{sectionIndex}/blocks";

    public static string Block(int sectionIndex, int blockIndex)
        => $"/dataInputs/sections/{sectionIndex}/blocks/{blockIndex}";

    // Field may itself contain a slash, for example "question/options", because that is
    // how RadioQuestionLayer spells its deepest pointers.
    public static string BlockField(int sectionIndex, int blockIndex, string field)
        => $"{Block(sectionIndex, blockIndex)}/{field}";

    // A pointer always starts with "/", so the "f" prefix keeps the id from starting with
    // the separator, which would be a valid id but an awkward selector.
    public static string ElementId(string jsonPointer)
        => "f" + jsonPointer.Replace('/', '-');
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~FieldPathTests`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Authoring/FieldPath.cs tests/Registrar.Tests/Authoring/FieldPathTests.cs
git commit -m "feat: FieldPath owns the JSON Pointer dialect and derives element ids"
```

---

### Task 2: Section normalization, so one index means one thing

**Files:**
- Create: `src/Registrar.Core/Authoring/DraftEdits.cs`
- Test: `tests/Registrar.Tests/Authoring/DraftEditsTests.cs`

**Interfaces:**
- Consumes: `AgrDraft`, `ArtifactTypeConfig` from `Registrar.Core`.
- Produces: `DraftEdits.NormalizeSections(AgrDraft draft, ArtifactTypeConfig type) -> AgrDraft`.

**Why this task exists.** `ToDocument` emits section gaps at the config-title index and emits document sections at their position among populated sections. Those agree only when every section is populated, which is exactly when a document exists to validate. Rather than reason about that at every call site, normalize once on open: every config section is materialized, in config order, with strays kept at the end. After that the draft index, the title index, and the document index are the same number, and the editor never has to translate.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Authoring/DraftEditsTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Tests.Authoring;

public class DraftEditsTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static JsonNode Schema()
        => JsonNode.Parse(File.ReadAllText(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json")))!;

    private static AgrSection Section(string title, params AgrBlock[] blocks)
        => new() { Title = title, DescriptorLine = null, Blocks = blocks };

    private static AgrBlock Paragraph(string text)
        => new() { BlockType = "paragraph", Text = text };

    [Fact]
    public void Normalizing_materializes_every_config_section_in_config_order()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        // Only the last section has content, and it is the only one present.
        var draft = new AgrDraft { ArtifactType = "DS", Sections = new[] { Section(titles[^1], Paragraph("done")) } };

        var normalized = DraftEdits.NormalizeSections(draft, type);

        Assert.Equal(titles, normalized.Sections.Select(s => s.Title).ToList());
        Assert.Equal("done", normalized.Sections[^1].Blocks.Single().Text);
        Assert.All(normalized.Sections.Take(titles.Count - 1), s => Assert.Empty(s.Blocks));
    }

    [Fact]
    public void Normalizing_keeps_a_stray_section_rather_than_dropping_it()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[] { Section("Not A Real Section", Paragraph("keep me")), Section(titles[0], Paragraph("first")) }
        };

        var normalized = DraftEdits.NormalizeSections(draft, type);

        Assert.Equal(titles.Count + 1, normalized.Sections.Count);
        Assert.Equal(titles, normalized.Sections.Take(titles.Count).Select(s => s.Title).ToList());
        Assert.Equal("Not A Real Section", normalized.Sections[^1].Title);
        Assert.Equal("keep me", normalized.Sections[^1].Blocks.Single().Text);
    }

    [Fact]
    public void Normalizing_preserves_a_descriptor_line_a_section_already_carries()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                new AgrSection { Title = titles[0], DescriptorLine = "Intro line.", Blocks = new[] { Paragraph("a") } }
            }
        };

        var normalized = DraftEdits.NormalizeSections(draft, type);

        Assert.Equal("Intro line.", normalized.Sections[0].DescriptorLine);
    }

    // The whole point of normalizing: after it, the index a technician sees, the index the
    // pointer carries, and the index the converted document uses are the same number.
    [Fact]
    public void After_normalizing_a_complete_draft_indices_agree_with_the_converted_document()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = DraftEdits.NormalizeSections(
            new AgrDraft
            {
                ArtifactType = "DS",
                DocumentId = "ORG-GOV-DS-016",
                Owner = "Registry Owner",
                SourceWorkflow = new AgrSourceWorkflowContext
                {
                    WorkflowName = "gui", Phase = "authoring",
                    ExecutionId = "00000000-0000-0000-0000-000000000001"
                },
                Sections = titles.Select(t => Section(t, Paragraph("content for " + t))).ToList()
            },
            type);

        var result = AgrAuthoring.ToDocument(draft, config, Schema(), new FixedClock());

        Assert.Empty(result.Gaps);
        Assert.NotNull(result.Document);
        Assert.Equal(
            draft.Sections.Select(s => s.Title).ToList(),
            result.Document!.DataInputs.Sections.Select(s => s.Title).ToList());
    }

    // Normalizing must not change what ToDocument reports. An empty section that was
    // materialized reports the same gap as one that was simply absent.
    [Fact]
    public void Materializing_an_empty_section_does_not_change_the_gap_it_reports()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];

        var bare = new AgrDraft { ArtifactType = "DS" };
        var normalized = DraftEdits.NormalizeSections(bare, type);

        var before = AgrAuthoring.ToDocument(bare, config, Schema(), new FixedClock()).Gaps;
        var after = AgrAuthoring.ToDocument(normalized, config, Schema(), new FixedClock()).Gaps;

        Assert.Equal(
            before.Select(g => g.JsonPath + "|" + g.Message).ToList(),
            after.Select(g => g.JsonPath + "|" + g.Message).ToList());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~DraftEditsTests`
Expected: build failure, `DraftEdits` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.Core/Authoring/DraftEdits.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// Every change the editor can make to a draft, as pure AgrDraft -> AgrDraft functions.
// The app calls these and decides nothing, so every rule about what an edit does is
// testable in the fast suite with no UI.
public static class DraftEdits
{
    // Materializes every section the artifact type declares, in config order, keeping any
    // section the draft carries that the type does not declare at the end.
    //
    // This exists to collapse three indices into one. ToDocument reports a section gap at
    // the config-title index, but emits document sections at their position among the
    // populated ones; those agree only when nothing is missing. After normalizing, the
    // draft's own index is the config-title index, so the pointer an input carries is the
    // pointer a check will report at, with no translation anywhere.
    //
    // A materialized empty section is invisible to ToDocument, which tests the block count
    // rather than the section's presence, so this changes no gap and no check.
    public static AgrDraft NormalizeSections(AgrDraft draft, ArtifactTypeConfig type)
    {
        var titles = type.SectionSchema?.OrderedSections;
        if (titles is null) return draft;

        var byTitle = draft.Sections
            .GroupBy(s => s.Title, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var ordered = new List<AgrSection>(titles.Count);
        foreach (var title in titles)
        {
            ordered.Add(byTitle.TryGetValue(title, out var existing)
                ? existing
                : new AgrSection { Title = title, DescriptorLine = null, Blocks = Array.Empty<AgrBlock>() });
        }

        // Strays are kept, not dropped. ToDocument turns each into an explicit gap naming
        // the offending title, which is what the technician needs to see; discarding them
        // here would silently delete their content instead.
        var declared = new HashSet<string>(titles, StringComparer.Ordinal);
        ordered.AddRange(draft.Sections.Where(s => !declared.Contains(s.Title)));

        return draft with { Sections = ordered };
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~DraftEditsTests`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Authoring/DraftEdits.cs tests/Registrar.Tests/Authoring/DraftEditsTests.cs
git commit -m "feat: normalize draft sections to config order so one index means one thing"
```

---

### Task 3: EditorForm, the panels the editor renders

**Files:**
- Create: `src/Registrar.Core/Authoring/EditorForm.cs`
- Test: `tests/Registrar.Tests/Authoring/EditorFormTests.cs`

**Interfaces:**
- Consumes: `FieldPath` (Task 1), `DraftEdits.NormalizeSections` (Task 2).
- Produces: `enum BlockEditorKind { Prose, Clause, Radio, ReadOnly }`; `record FormBlock(int SectionIndex, int BlockIndex, string JsonPath, BlockEditorKind Kind, AgrBlock Block)`; `record FormSection(int Index, string Title, string? DescriptorLine, string BlocksJsonPath, IReadOnlyList<FormBlock> Blocks)`; `record EditorForm(IReadOnlyList<FormSection> Sections)`; `EditorForm.For(AgrDraft, ArtifactTypeConfig) -> EditorForm`; `EditorForm.KindOf(string blockType) -> BlockEditorKind`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Authoring/EditorFormTests.cs`:

```csharp
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Tests.Authoring;

public class EditorFormTests
{
    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static AgrBlock Block(string type) => new() { BlockType = type, Text = "x" };

    [Theory]
    [InlineData("paragraph", BlockEditorKind.Prose)]
    [InlineData("h2", BlockEditorKind.Prose)]
    [InlineData("h3", BlockEditorKind.Prose)]
    [InlineData("bulletList", BlockEditorKind.Prose)]
    [InlineData("clauseInsertion", BlockEditorKind.Clause)]
    [InlineData("radioQuestion", BlockEditorKind.Radio)]
    [InlineData("table", BlockEditorKind.ReadOnly)]
    [InlineData("warnCallout", BlockEditorKind.ReadOnly)]
    [InlineData("aggregationTable", BlockEditorKind.ReadOnly)]
    [InlineData("copyReadyMessage", BlockEditorKind.ReadOnly)]
    public void Every_block_type_maps_to_an_editor_kind(string blockType, BlockEditorKind expected)
        => Assert.Equal(expected, EditorForm.KindOf(blockType));

    // A type the engine does not know must not crash the screen, and must not be treated
    // as editable. Read-only is the fail-closed answer.
    [Fact]
    public void An_unknown_block_type_is_read_only_rather_than_an_exception()
        => Assert.Equal(BlockEditorKind.ReadOnly, EditorForm.KindOf("somethingNewInV3"));

    [Fact]
    public void Panels_match_the_artifact_types_ordered_sections()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["AUP"];

        var form = EditorForm.For(new AgrDraft { ArtifactType = "AUP" }, type);

        Assert.Equal(type.SectionSchema!.OrderedSections!, form.Sections.Select(s => s.Title).ToList());
        Assert.All(form.Sections, s => Assert.Empty(s.Blocks));
    }

    [Fact]
    public void A_block_carries_the_pointer_its_own_position_implies()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                new AgrSection { Title = titles[1], DescriptorLine = null, Blocks = new[] { Block("paragraph"), Block("table") } }
            }
        };

        var form = EditorForm.For(draft, type);

        var second = form.Sections[1];
        Assert.Equal(1, second.Index);
        Assert.Equal(FieldPath.SectionBlocks(1), second.BlocksJsonPath);
        Assert.Equal(FieldPath.Block(1, 0), second.Blocks[0].JsonPath);
        Assert.Equal(FieldPath.Block(1, 1), second.Blocks[1].JsonPath);
        Assert.Equal(BlockEditorKind.Prose, second.Blocks[0].Kind);
        Assert.Equal(BlockEditorKind.ReadOnly, second.Blocks[1].Kind);
    }

    // The form is built over the normalized draft, so a section the draft does not carry
    // still gets a panel. Without this the technician has nowhere to author it.
    [Fact]
    public void A_section_the_draft_does_not_carry_still_gets_a_panel()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                new AgrSection { Title = titles[^1], DescriptorLine = null, Blocks = new[] { Block("paragraph") } }
            }
        };

        var form = EditorForm.For(draft, type);

        Assert.Equal(titles.Count, form.Sections.Count);
        Assert.Empty(form.Sections[0].Blocks);
        Assert.Single(form.Sections[^1].Blocks);
    }

    // Rule 6: a type with no approved section structure cannot be authored, so it gets no
    // panels rather than an invented one.
    [Fact]
    public void A_type_with_no_defined_section_schema_produces_no_panels()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var undefined = config.ArtifactTypesByCode.Values
            .First(t => t.SectionSchema is null || t.SectionSchema.Status != "defined");

        var form = EditorForm.For(new AgrDraft { ArtifactType = undefined.Code }, undefined);

        Assert.Empty(form.Sections);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EditorFormTests`
Expected: build failure, `EditorForm` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.Core/Authoring/EditorForm.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// Which editor a block gets. Four kinds, three of them editable: the fourth is how an
// unsupported block stays visible and unlosable rather than being hidden or dropped.
public enum BlockEditorKind
{
    Prose,
    Clause,
    Radio,
    ReadOnly
}

public sealed record FormBlock(
    int SectionIndex,
    int BlockIndex,
    string JsonPath,
    BlockEditorKind Kind,
    AgrBlock Block);

public sealed record FormSection(
    int Index,
    string Title,
    string? DescriptorLine,
    string BlocksJsonPath,
    IReadOnlyList<FormBlock> Blocks);

// The shape of the editor screen, decided here so the app can render it without holding
// a rule. Titles come from config and are never editable: QA-ENG-02 rejects any deviation
// from the artifact type's ordered sections, so a title the technician could change is a
// build failure waiting to happen.
public sealed record EditorForm(IReadOnlyList<FormSection> Sections)
{
    private static readonly IReadOnlyDictionary<string, BlockEditorKind> Kinds =
        new Dictionary<string, BlockEditorKind>(StringComparer.Ordinal)
        {
            ["paragraph"] = BlockEditorKind.Prose,
            ["h2"] = BlockEditorKind.Prose,
            ["h3"] = BlockEditorKind.Prose,
            ["bulletList"] = BlockEditorKind.Prose,
            ["clauseInsertion"] = BlockEditorKind.Clause,
            ["radioQuestion"] = BlockEditorKind.Radio,
            ["table"] = BlockEditorKind.ReadOnly,
            ["warnCallout"] = BlockEditorKind.ReadOnly,
            ["aggregationTable"] = BlockEditorKind.ReadOnly,
            ["copyReadyMessage"] = BlockEditorKind.ReadOnly
        };

    // Unknown types are read-only rather than an exception. BlockRenderer's default branch
    // is already a guard for future types; this is the same guard on the authoring side.
    public static BlockEditorKind KindOf(string blockType)
        => Kinds.TryGetValue(blockType, out var kind) ? kind : BlockEditorKind.ReadOnly;

    public static EditorForm For(AgrDraft draft, ArtifactTypeConfig type)
    {
        if (type.SectionSchema?.OrderedSections is null)
            return new EditorForm(Array.Empty<FormSection>());

        var normalized = DraftEdits.NormalizeSections(draft, type);

        var sections = new List<FormSection>(normalized.Sections.Count);
        for (var s = 0; s < normalized.Sections.Count; s++)
        {
            var section = normalized.Sections[s];
            var blocks = new List<FormBlock>(section.Blocks.Count);
            for (var b = 0; b < section.Blocks.Count; b++)
            {
                var block = section.Blocks[b];
                blocks.Add(new FormBlock(s, b, FieldPath.Block(s, b), KindOf(block.BlockType), block));
            }

            sections.Add(new FormSection(
                s, section.Title, section.DescriptorLine, FieldPath.SectionBlocks(s), blocks));
        }

        return new EditorForm(sections);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EditorFormTests`
Expected: 15 passed (10 theory cases plus 5 facts).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Authoring/EditorForm.cs tests/Registrar.Tests/Authoring/EditorFormTests.cs
git commit -m "feat: EditorForm turns a draft into config-ordered panels carrying pointers"
```

---

### Task 4: The block mutations

**Files:**
- Modify: `src/Registrar.Core/Authoring/DraftEdits.cs`
- Modify: `tests/Registrar.Tests/Authoring/DraftEditsTests.cs`

**Interfaces:**
- Consumes: `AgrDraft`, `AgrSection`, `AgrBlock`, `AgrQuestion`.
- Produces, all `static AgrDraft` on `DraftEdits`: `SetProse(draft, s, b, string value)`, `SetClause(draft, s, b, string clauseId)`, `SetQuestionPrompt(draft, s, b, string prompt)`, `SetQuestionRequired(draft, s, b, bool required)`, `SetQuestionOptions(draft, s, b, IReadOnlyList<string> options)`, `SetInternalOnly(draft, s, b, bool internalOnly)`, `AddBlock(draft, s, AgrBlock block)`, `RemoveBlock(draft, s, b)`, `MoveBlock(draft, s, b, int delta)`. Also `DraftEdits.NewProse(string blockType)`, `NewClause(string clauseId)`, `NewQuestion(int number, IReadOnlyList<string> options)` returning `AgrBlock`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Registrar.Tests/Authoring/DraftEditsTests.cs` (inside the class):

```csharp
    private static AgrDraft OneSection(params AgrBlock[] blocks)
        => new() { ArtifactType = "DS", Sections = new[] { Section("Purpose and Scope", blocks) } };

    [Fact]
    public void Setting_prose_writes_text_for_a_paragraph()
    {
        var edited = DraftEdits.SetProse(OneSection(Paragraph("old")), 0, 0, "new");

        Assert.Equal("new", edited.Sections[0].Blocks[0].Text);
        Assert.Null(edited.Sections[0].Blocks[0].Items);
    }

    // A bulletList carries items, not text. One editor serves both shapes, so the split is
    // decided here rather than in markup: one line per item, blank lines dropped.
    [Fact]
    public void Setting_prose_writes_items_for_a_bullet_list()
    {
        var list = new AgrBlock { BlockType = "bulletList", Items = new[] { "old" } };

        var edited = DraftEdits.SetProse(OneSection(list), 0, 0, "first\n\nsecond\n");

        Assert.Equal(new[] { "first", "second" }, edited.Sections[0].Blocks[0].Items);
        Assert.Null(edited.Sections[0].Blocks[0].Text);
    }

    [Fact]
    public void Setting_a_clause_records_the_id_and_never_the_text()
    {
        var clause = new AgrBlock { BlockType = "clauseInsertion", ClauseId = "CL-001" };

        var edited = DraftEdits.SetClause(OneSection(clause), 0, 0, "CL-004");

        Assert.Equal("CL-004", edited.Sections[0].Blocks[0].ClauseId);
        Assert.Null(edited.Sections[0].Blocks[0].Text);
    }

    [Fact]
    public void Question_edits_change_one_field_and_leave_the_others()
    {
        var question = new AgrBlock
        {
            BlockType = "radioQuestion",
            Question = new AgrQuestion
            {
                Number = 3, Prompt = "Old prompt", Required = false,
                Options = new[] { "Yes", "No", "Partial", "Not applicable" }
            }
        };

        var draft = OneSection(question);
        var prompted = DraftEdits.SetQuestionPrompt(draft, 0, 0, "New prompt");
        var required = DraftEdits.SetQuestionRequired(prompted, 0, 0, true);
        var optioned = DraftEdits.SetQuestionOptions(required, 0, 0, new[] { "Yes", "No", "Partial", "Unknown" });

        var q = optioned.Sections[0].Blocks[0].Question!;
        Assert.Equal("New prompt", q.Prompt);
        Assert.True(q.Required);
        Assert.Equal(3, q.Number);
        Assert.Equal(new[] { "Yes", "No", "Partial", "Unknown" }, q.Options);
    }

    [Fact]
    public void Adding_removing_and_moving_blocks_changes_only_that_section()
    {
        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                Section("Purpose and Scope", Paragraph("a"), Paragraph("b")),
                Section("Second", Paragraph("untouched"))
            }
        };

        var added = DraftEdits.AddBlock(draft, 0, DraftEdits.NewProse("paragraph"));
        Assert.Equal(3, added.Sections[0].Blocks.Count);
        Assert.Equal("", added.Sections[0].Blocks[2].Text);

        var moved = DraftEdits.MoveBlock(added, 0, 0, 1);
        Assert.Equal(new[] { "b", "a", "" }, moved.Sections[0].Blocks.Select(x => x.Text).ToArray());

        var removed = DraftEdits.RemoveBlock(moved, 0, 2);
        Assert.Equal(2, removed.Sections[0].Blocks.Count);
        Assert.Equal("untouched", removed.Sections[1].Blocks.Single().Text);
    }

    // Moving off either end is a no-op rather than an exception: the buttons stay clickable
    // and the app does not have to hold the condition.
    [Fact]
    public void Moving_a_block_past_either_end_does_nothing()
    {
        var draft = OneSection(Paragraph("a"), Paragraph("b"));

        Assert.Equal("a", DraftEdits.MoveBlock(draft, 0, 0, -1).Sections[0].Blocks[0].Text);
        Assert.Equal("b", DraftEdits.MoveBlock(draft, 0, 1, 1).Sections[0].Blocks[1].Text);
    }

    // The read-only fallback is only honest if an unsupported block survives edits made
    // around it. This is the test that proves nothing is quietly dropped.
    [Fact]
    public void An_unsupported_block_survives_an_edit_to_its_neighbour()
    {
        var table = new AgrBlock
        {
            BlockType = "table",
            ColumnHeaders = new[] { "Control", "Status" },
            Rows = new[] { new[] { "MFA", "In place" } }
        };

        var edited = DraftEdits.SetProse(OneSection(Paragraph("before"), table), 0, 0, "after");

        var survivor = edited.Sections[0].Blocks[1];
        Assert.Equal("table", survivor.BlockType);
        Assert.Equal(new[] { "Control", "Status" }, survivor.ColumnHeaders);
        Assert.Equal("MFA", survivor.Rows!.Single()[0]);
    }

    [Fact]
    public void Setting_internal_only_leaves_the_rest_of_the_block_alone()
    {
        var edited = DraftEdits.SetInternalOnly(OneSection(Paragraph("note")), 0, 0, true);

        Assert.True(edited.Sections[0].Blocks[0].InternalOnly);
        Assert.Equal("note", edited.Sections[0].Blocks[0].Text);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~DraftEditsTests`
Expected: build failure, `SetProse` does not exist.

- [ ] **Step 3: Write the implementation**

Append to `src/Registrar.Core/Authoring/DraftEdits.cs` (inside the class):

```csharp
    private static readonly IReadOnlySet<string> ListBlockTypes =
        new HashSet<string>(StringComparer.Ordinal) { "bulletList" };

    public static AgrBlock NewProse(string blockType)
        => blockType is "bulletList"
            ? new AgrBlock { BlockType = blockType, Items = Array.Empty<string>() }
            : new AgrBlock { BlockType = blockType, Text = "" };

    public static AgrBlock NewClause(string clauseId)
        => new() { BlockType = "clauseInsertion", ClauseId = clauseId };

    public static AgrBlock NewQuestion(int number, IReadOnlyList<string> options)
        => new()
        {
            BlockType = "radioQuestion",
            Question = new AgrQuestion { Number = number, Prompt = "", Required = true, Options = options }
        };

    // One prose editor serves paragraph, h2, h3, and bulletList, so the difference between
    // a text block and a list block is decided here rather than in markup. A list is one
    // item per line; blank lines are dropped, because a blank bullet renders as an empty
    // bullet and no technician means that.
    public static AgrDraft SetProse(AgrDraft draft, int sectionIndex, int blockIndex, string value)
        => MapBlock(draft, sectionIndex, blockIndex, block => ListBlockTypes.Contains(block.BlockType)
            ? block with
            {
                Items = value.Split('\n')
                    .Select(line => line.Trim('\r'))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList(),
                Text = null
            }
            : block with { Text = value });

    // Records the id only. Rule 4: the engine inserts canonical text from the clause
    // library and proves it with SHA-256, so a clause block that carried prose would be
    // carrying something the renderer is required to ignore.
    public static AgrDraft SetClause(AgrDraft draft, int sectionIndex, int blockIndex, string clauseId)
        => MapBlock(draft, sectionIndex, blockIndex, block => block with { ClauseId = clauseId, Text = null });

    public static AgrDraft SetQuestionPrompt(AgrDraft draft, int sectionIndex, int blockIndex, string prompt)
        => MapQuestion(draft, sectionIndex, blockIndex, q => q with { Prompt = prompt });

    public static AgrDraft SetQuestionRequired(AgrDraft draft, int sectionIndex, int blockIndex, bool required)
        => MapQuestion(draft, sectionIndex, blockIndex, q => q with { Required = required });

    public static AgrDraft SetQuestionOptions(
        AgrDraft draft, int sectionIndex, int blockIndex, IReadOnlyList<string> options)
        => MapQuestion(draft, sectionIndex, blockIndex, q => q with { Options = options.ToList() });

    public static AgrDraft SetInternalOnly(AgrDraft draft, int sectionIndex, int blockIndex, bool internalOnly)
        => MapBlock(draft, sectionIndex, blockIndex, block => block with { InternalOnly = internalOnly });

    public static AgrDraft AddBlock(AgrDraft draft, int sectionIndex, AgrBlock block)
        => MapBlocks(draft, sectionIndex, blocks => blocks.Append(block).ToList());

    public static AgrDraft RemoveBlock(AgrDraft draft, int sectionIndex, int blockIndex)
        => MapBlocks(draft, sectionIndex, blocks => blocks.Where((_, i) => i != blockIndex).ToList());

    // Off either end is a no-op rather than a throw, so the app can render the buttons
    // unconditionally and hold no condition of its own.
    public static AgrDraft MoveBlock(AgrDraft draft, int sectionIndex, int blockIndex, int delta)
        => MapBlocks(draft, sectionIndex, blocks =>
        {
            var target = blockIndex + delta;
            if (blockIndex < 0 || blockIndex >= blocks.Count || target < 0 || target >= blocks.Count)
                return blocks;

            var moved = blocks.ToList();
            (moved[blockIndex], moved[target]) = (moved[target], moved[blockIndex]);
            return moved;
        });

    private static AgrDraft MapQuestion(
        AgrDraft draft, int sectionIndex, int blockIndex, Func<AgrQuestion, AgrQuestion> map)
        => MapBlock(draft, sectionIndex, blockIndex,
            block => block.Question is null ? block : block with { Question = map(block.Question) });

    private static AgrDraft MapBlock(
        AgrDraft draft, int sectionIndex, int blockIndex, Func<AgrBlock, AgrBlock> map)
        => MapBlocks(draft, sectionIndex, blocks => blockIndex < 0 || blockIndex >= blocks.Count
            ? blocks
            : blocks.Select((block, i) => i == blockIndex ? map(block) : block).ToList());

    private static AgrDraft MapBlocks(
        AgrDraft draft, int sectionIndex, Func<IReadOnlyList<AgrBlock>, IReadOnlyList<AgrBlock>> map)
    {
        if (sectionIndex < 0 || sectionIndex >= draft.Sections.Count) return draft;

        var sections = draft.Sections
            .Select((section, i) => i == sectionIndex ? section with { Blocks = map(section.Blocks) } : section)
            .ToList();

        return draft with { Sections = sections };
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~DraftEditsTests`
Expected: 13 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Authoring/DraftEdits.cs tests/Registrar.Tests/Authoring/DraftEditsTests.cs
git commit -m "feat: draft block mutations as pure functions, unsupported blocks preserved"
```

---

### Task 5: DraftCreation

**Files:**
- Create: `src/Registrar.Core/Authoring/DraftCreation.cs`
- Test: `tests/Registrar.Tests/Authoring/DraftCreationTests.cs`

**Interfaces:**
- Consumes: `AgrAuthoring.Defaults`, `DraftEdits.NormalizeSections`.
- Produces: `DraftCreation.New(ArtifactTypeConfig type, string documentId, string? client, string? engagement, string owner, ConfigBundle config, JsonNode agrSchema, IClock clock, string executionId) -> AgrDraft`; `DraftCreation.FileName(string documentId) -> string`; `DraftCreation.CreatableTypes(ConfigBundle config) -> IReadOnlyList<ArtifactTypeConfig>`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Authoring/DraftCreationTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Engagement;

namespace Registrar.Tests.Authoring;

public class DraftCreationTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static JsonNode Schema()
        => JsonNode.Parse(File.ReadAllText(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json")))!;

    private static AgrDraft Create(string code)
    {
        var config = ConfigLoader.Load(RepoRoot());
        return DraftCreation.New(
            config.ArtifactTypesByCode[code], "ORG-GOV-DS-016", "Talewind CPA", "Q3 governance review",
            "Registry Owner", config, Schema(), new FixedClock(),
            "00000000-0000-0000-0000-000000000001");
    }

    [Theory]
    [InlineData("DS", "2027-02-04")]
    [InlineData("TCL", "2027-08-04")]
    public void Review_date_is_derived_from_the_category_interval(string code, string expected)
        => Assert.Equal(expected, Create(code).ReviewDate);

    [Fact]
    public void The_five_human_inputs_are_carried_and_nothing_else_is_invented()
    {
        var draft = Create("DS");

        Assert.Equal("DS", draft.ArtifactType);
        Assert.Equal("ORG-GOV-DS-016", draft.DocumentId);
        Assert.Equal("Talewind CPA", draft.Client);
        Assert.Equal("Q3 governance review", draft.Engagement);
        Assert.Equal("Registry Owner", draft.Owner);
        Assert.Equal(Mode.Draft, draft.Mode);

        // Derived values are not frozen into the draft. They are computed on conversion,
        // so nothing here departs from config without an explicit override.
        Assert.Null(draft.Overrides);
    }

    [Fact]
    public void A_new_draft_carries_the_workflow_context_the_app_supplies()
    {
        var draft = Create("DS");

        Assert.NotNull(draft.SourceWorkflow);
        Assert.Equal("00000000-0000-0000-0000-000000000001", draft.SourceWorkflow!.ExecutionId);
    }

    [Fact]
    public void A_new_draft_already_has_a_panel_for_every_section()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var titles = config.ArtifactTypesByCode["DS"].SectionSchema!.OrderedSections!;

        Assert.Equal(titles, Create("DS").Sections.Select(s => s.Title).ToList());
    }

    // The only gaps a brand new draft reports are the sections that have no content yet.
    // Anything else would mean creation left a required field unfilled.
    [Fact]
    public void A_new_draft_reports_only_empty_section_gaps()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var gaps = AgrAuthoring.ToDocument(Create("DS"), config, Schema(), new FixedClock()).Gaps;

        Assert.NotEmpty(gaps);
        Assert.All(gaps, g => Assert.EndsWith("has no content yet.", g.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void The_file_name_is_the_document_id_and_the_draft_extension()
        => Assert.Equal("ORG-GOV-DS-016" + DraftFile.Extension, DraftCreation.FileName("ORG-GOV-DS-016"));

    // Rule 6: offering a type the engine will refuse would be a lie the engine then has to
    // tell. Creatable means the section schema is defined, nothing else.
    [Fact]
    public void Only_types_with_a_defined_section_schema_are_creatable()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var creatable = DraftCreation.CreatableTypes(config);

        Assert.NotEmpty(creatable);
        Assert.All(creatable, t => Assert.Equal("defined", t.SectionSchema!.Status));
        Assert.Contains(creatable, t => t.Code == "DS");
        Assert.Equal(creatable.Select(t => t.Code).OrderBy(c => c, StringComparer.Ordinal).ToList(),
            creatable.Select(t => t.Code).ToList());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~DraftCreationTests`
Expected: build failure, `DraftCreation` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.Core/Authoring/DraftCreation.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core.Config;
using Registrar.Core.Engagement;
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// A new artifact from the five things a technician actually knows: type, Document ID,
// client, engagement, owner. Everything else is derived, and derived values stay out of
// the draft so config remains the source of truth for them.
public static class DraftCreation
{
    // Rule 6. An artifact type whose section schema is not defined refuses generation with
    // a ConfigurationError, so offering it for creation would produce a draft that can
    // never build. Ordered by code so the picker is stable between runs.
    public static IReadOnlyList<ArtifactTypeConfig> CreatableTypes(ConfigBundle config)
        => config.ArtifactTypesByCode.Values
            .Where(t => t.SectionSchema?.Status == "defined" && t.SectionSchema.OrderedSections is not null)
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .ToList();

    public static string FileName(string documentId) => documentId + DraftFile.Extension;

    public static AgrDraft New(
        ArtifactTypeConfig type,
        string documentId,
        string? client,
        string? engagement,
        string owner,
        ConfigBundle config,
        JsonNode agrSchema,
        IClock clock,
        string executionId)
    {
        // ReviewDate is derived exactly once, here. AgrDraft is explicit that a converted
        // document must never have a review date invented for it on save, so this is the
        // only place the category interval is turned into a date.
        var defaults = AgrAuthoring.Defaults(type, config, agrSchema, clock);

        var draft = new AgrDraft
        {
            ArtifactType = type.Code,
            DocumentId = documentId,
            Client = string.IsNullOrWhiteSpace(client) ? null : client,
            Engagement = string.IsNullOrWhiteSpace(engagement) ? null : engagement,
            Owner = owner,
            ReviewDate = defaults.ReviewDate,
            Mode = Mode.Draft,
            SourceWorkflow = new AgrSourceWorkflowContext
            {
                WorkflowName = "operator-gui",
                Phase = "authoring",
                ExecutionId = executionId
            }
        };

        // Materialized up front so the editor opens with a panel per section and the
        // indices agree with the pointers from the first render.
        return DraftEdits.NormalizeSections(draft, type);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~DraftCreationTests`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Authoring/DraftCreation.cs tests/Registrar.Tests/Authoring/DraftCreationTests.cs
git commit -m "feat: DraftCreation builds a new draft from the five human inputs"
```

---

### Task 6: EditorRail

**Files:**
- Create: `src/Registrar.Core/Authoring/EditorRail.cs`
- Test: `tests/Registrar.Tests/Authoring/EditorRailTests.cs`

**Interfaces:**
- Consumes: `AuthoringGap`, `Check`, `FieldPath`.
- Produces: `enum RailSeverity { Gap, Warning, Failure, EngineError }`; `record RailItem(string? JsonPath, string? ElementId, RailSeverity Severity, string Headline, string? Detail)`; `record RailState(IReadOnlyList<RailItem> Items, bool BuildEnabled, string BuildBlockedReason)`; `EditorRail.FromGaps(IReadOnlyList<AuthoringGap>)`, `EditorRail.FromChecks(IReadOnlyList<Check>, string overallResult)`, `EditorRail.FromEngineError(string message)`, all returning `RailState`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Authoring/EditorRailTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Model;

namespace Registrar.Tests.Authoring;

public class EditorRailTests
{
    private static Check Check(string result, string severity, string message, string? path)
        => new()
        {
            CheckId = "QA-SF-08", LayerName = "content", Result = result, SeverityLevel = severity,
            Message = message, JsonPath = path, Remediation = "Fix it."
        };

    [Fact]
    public void Gaps_block_the_build_and_say_how_many_remain()
    {
        var state = EditorRail.FromGaps(new[]
        {
            new AuthoringGap(FieldPath.Owner, "Owner is required."),
            new AuthoringGap(FieldPath.SectionBlocks(2), "Controls has no content yet.")
        });

        Assert.False(state.BuildEnabled);
        Assert.Equal(2, state.Items.Count);
        Assert.All(state.Items, i => Assert.Equal(RailSeverity.Gap, i.Severity));
        Assert.Contains("2 fields", state.BuildBlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rail_item_carries_the_element_id_its_pointer_derives()
    {
        var state = EditorRail.FromGaps(new[] { new AuthoringGap(FieldPath.Owner, "Owner is required.") });

        Assert.Equal(FieldPath.ElementId(FieldPath.Owner), state.Items.Single().ElementId);
    }

    // Not every check names a field. Those items still appear; they simply are not
    // clickable, which is honest rather than scrolling nowhere.
    [Fact]
    public void A_check_with_no_pointer_produces_an_item_with_no_target()
    {
        var state = EditorRail.FromChecks(
            new[] { Check(CheckResultValue.Fail, Severity.Blocking, "Something global.", null) },
            OverallResult.Fail);

        Assert.Null(state.Items.Single().ElementId);
    }

    [Fact]
    public void Passing_checks_are_not_shown_and_the_build_is_enabled()
    {
        var state = EditorRail.FromChecks(
            new[]
            {
                Check(CheckResultValue.Pass, Severity.Blocking, "All good.", null),
                Check(CheckResultValue.Skipped, Severity.Warning, "No registry loaded.", null)
            },
            OverallResult.Pass);

        Assert.Empty(state.Items);
        Assert.True(state.BuildEnabled);
        Assert.Equal("", state.BuildBlockedReason);
    }

    [Fact]
    public void A_warning_is_shown_without_blocking_the_build()
    {
        var state = EditorRail.FromChecks(
            new[] { Check(CheckResultValue.Warn, Severity.Warning, "Unknown token.", FieldPath.PlaceholderMap) },
            OverallResult.PassWithWarnings);

        Assert.Equal(RailSeverity.Warning, state.Items.Single().Severity);
        Assert.True(state.BuildEnabled);
    }

    [Fact]
    public void A_failing_check_blocks_the_build_and_the_reason_names_the_check()
    {
        var state = EditorRail.FromChecks(
            new[] { Check(CheckResultValue.Fail, Severity.Blocking, "Prohibited character.", FieldPath.Owner) },
            OverallResult.Fail);

        Assert.False(state.BuildEnabled);
        Assert.Equal(RailSeverity.Failure, state.Items.Single().Severity);
        Assert.Contains("QA-SF-08", state.BuildBlockedReason, StringComparison.Ordinal);
    }

    // A blank rail must never be ambiguous between "nothing is wrong" and "we did not
    // check". This is the same failure GUI-6 fixed on the Engagement screen.
    [Fact]
    public void An_engine_error_fills_the_rail_rather_than_leaving_it_empty()
    {
        var state = EditorRail.FromEngineError("Config failed to reload: access denied.");

        Assert.Equal(RailSeverity.EngineError, state.Items.Single().Severity);
        Assert.False(state.BuildEnabled);
        Assert.Contains("could not be checked", state.BuildBlockedReason, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EditorRailTests`
Expected: build failure, `EditorRail` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.Core/Authoring/EditorRail.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

public enum RailSeverity
{
    // An unfinished field, not a governance violation. Distinct from Failure because a
    // blank box and a rejected value are different problems for the technician.
    Gap,
    Warning,
    Failure,
    EngineError
}

public sealed record RailItem(
    string? JsonPath,
    string? ElementId,
    RailSeverity Severity,
    string Headline,
    string? Detail);

public sealed record RailState(
    IReadOnlyList<RailItem> Items,
    bool BuildEnabled,
    string BuildBlockedReason);

// The right-hand rail, and the sentence under the Build button.
//
// A draft is in exactly one of three states, so the rail never mixes them: it will not
// convert (gaps), it converted and the engine reported (checks), or the engine could not
// run (error). Gaps and checks cannot both be present, because ToDocument returns no
// document when it returns gaps.
public static class EditorRail
{
    public static RailState FromGaps(IReadOnlyList<AuthoringGap> gaps)
    {
        var items = gaps
            .Select(g => new RailItem(g.JsonPath, FieldPath.ElementId(g.JsonPath), RailSeverity.Gap, g.Message, null))
            .ToList();

        var reason = items.Count == 1
            ? "1 field still needs filling in before this can be built."
            : $"{items.Count} fields still need filling in before this can be built.";

        return new RailState(items, false, reason);
    }

    public static RailState FromChecks(IReadOnlyList<Check> checks, string overallResult)
    {
        var items = checks
            .Where(c => c.Result is CheckResultValue.Fail or CheckResultValue.Warn)
            .Select(c => new RailItem(
                c.JsonPath,
                c.JsonPath is null ? null : FieldPath.ElementId(c.JsonPath),
                c.Result == CheckResultValue.Fail ? RailSeverity.Failure : RailSeverity.Warning,
                c.Message,
                c.Remediation))
            .ToList();

        var blocking = checks
            .Where(c => c.Result == CheckResultValue.Fail && c.SeverityLevel == Severity.Blocking)
            .Select(c => c.CheckId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var enabled = overallResult != OverallResult.Fail && blocking.Count == 0;

        var reason = enabled
            ? ""
            : blocking.Count > 0
                ? "Blocked by " + string.Join(", ", blocking) + "."
                : "The engine reported a failure.";

        return new RailState(items, enabled, reason);
    }

    // Never let a blank rail read as a clean one.
    public static RailState FromEngineError(string message)
        => new(
            new[] { new RailItem(null, null, RailSeverity.EngineError, "The engine could not check this draft.", message) },
            false,
            "This draft could not be checked, so Build is not available.");
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EditorRailTests`
Expected: 7 passed.

If `CheckResultValue`, `Severity`, or `OverallResult` member names differ from those used above, run `grep -rn "class CheckResultValue" -A 8 src/Registrar.Core/` and use the real names rather than adding new constants.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Authoring/EditorRail.cs tests/Registrar.Tests/Authoring/EditorRailTests.cs
git commit -m "feat: EditorRail merges gaps or checks and states why Build is blocked"
```

---

### Task 7: App wiring, so a screen can hold a draft

**Files:**
- Create: `src/Registrar.App/AppState.cs`
- Modify: `src/Registrar.App/MainWindow.xaml.cs`
- Modify: `src/Registrar.App/_Imports.razor`
- Modify: `src/Registrar.App/Components/Home.razor`
- Modify: `src/Registrar.App/Components/Engagement.razor`

**Interfaces:**
- Consumes: `EngineContext.Load`, `EngagementScanResult`, `DraftFile`, `AgrParser`, `AgrAuthoring.FromDocument`, `DraftEdits.NormalizeSections`.
- Produces: `AppState` with `EngineContext Engine`, `string? Folder`, `string? DraftPath`, `AgrDraft? Draft`, `string? RegistryCsvPath`, and `void Open(string path)`, `void SetDraft(AgrDraft draft)`. `Engagement.razor` gains `[Parameter] public EventCallback<EngagementRow> OnOpen`.

**Note on today's shell.** `Home.razor` currently rebuilds `ConfigBundle` and reparses the schema on every Rescan. Loading once per process is a parent-spec requirement and it is also what makes per-keystroke validation affordable, so this task moves that load into DI and `Home.razor` consumes it.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.App.Tests/EngagementOpenTests.cs`:

```csharp
using Bunit;
using Registrar.App.Components;
using Registrar.Core.Engagement;
using Xunit;

namespace Registrar.App.Tests;

public class EngagementOpenTests : TestContext
{
    private static EngagementRow Row(string file, ArtifactState state)
        => new(file, "DS", "ORG-GOV-DS-016", "Talewind CPA", state, "detail");

    [Fact]
    public void Opening_a_row_reports_that_row_to_the_caller()
    {
        EngagementRow? opened = null;

        var cut = RenderComponent<Engagement>(p => p
            .Add(c => c.Result, EngagementScanResult.Scanned(
                @"C:\Clients\Talewind", new[] { Row("a.agrdraft", ArtifactState.Draft) }))
            .Add(c => c.OnOpen, (EngagementRow row) => opened = row));

        cut.Find("tbody tr button.open").Click();

        Assert.NotNull(opened);
        Assert.Equal("a.agrdraft", opened!.FileName);
    }

    // A built document is not editable through the draft path, so it must not offer an
    // action that would misrepresent what happens next.
    [Fact]
    public void A_built_artifact_offers_no_open_action()
    {
        var cut = RenderComponent<Engagement>(p => p
            .Add(c => c.Result, EngagementScanResult.Scanned(
                @"C:\Clients\Talewind", new[] { Row("done.docx", ArtifactState.Built) }))
            .Add(c => c.OnOpen, (EngagementRow _) => { }));

        Assert.Empty(cut.FindAll("tbody tr button.open"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EngagementOpenTests`
Expected: build failure, `Engagement` has no `OnOpen` parameter.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.App/AppState.cs`:

```csharp
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.App;

// What the open window is currently working on. Deliberately a bag of values with no
// rules: every decision about a draft is made by Registrar.Core, and a condition that
// lived here would be one the fast suite cannot reach.
public sealed class AppState
{
    public AppState(EngineContext engine) => Engine = engine;

    public EngineContext Engine { get; }

    public string? Folder { get; set; }
    public string? DraftPath { get; set; }
    public AgrDraft? Draft { get; set; }

    // Set when the operator supplies a registry export. Null means no registry, which
    // RegistryLayer already reports as a Skipped check and the ID picker degrades openly
    // rather than silently.
    public string? RegistryCsvPath { get; set; }
}
```

Replace `src/Registrar.App/MainWindow.xaml.cs`:

```csharp
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Validation;

namespace Registrar.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();

        // Config and the compiled schema load once for the process lifetime. Per-keystroke
        // validation is only affordable because of this, and reloading config mid-session
        // would let two screens disagree about what the rules are.
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

        services.AddSingleton(EngineContext.Load(repoRoot));
        services.AddSingleton<IClock>(new SystemClock());
        services.AddSingleton<AppState>();

        Resources.Add("services", services.BuildServiceProvider());

        InitializeComponent();
    }
}
```

In `src/Registrar.App/_Imports.razor`, add:

```razor
@using Registrar.App
@using Registrar.Core
@using Registrar.Core.Authoring
@using Registrar.Core.Config
@using Registrar.Core.Model
@using Registrar.Validation
```

In `src/Registrar.App/Components/Engagement.razor`, add an Actions column. Add to the `<thead>` row:

```razor
                <th>Actions</th>
```

Add as the last cell of the `@foreach` row:

```razor
                    <td>
                        @if (row.State != ArtifactState.Built)
                        {
                            <button class="open" @onclick="() => OnOpen.InvokeAsync(row)">Open</button>
                        }
                    </td>
```

Add to the `@code` block:

```razor
    // Which rows are openable is a fact about artifact state, not a rule this component
    // invents: a Built artifact is a rendered document, and the editor edits drafts.
    [Parameter]
    public EventCallback<EngagementRow> OnOpen { get; set; }
```

Replace the `@code` block of `src/Registrar.App/Components/Home.razor`:

```razor
@page "/"
@using System.IO
@inject AppState State
@inject NavigationManager Nav

<div class="toolbar">
    <input @bind="folder" placeholder="Engagement folder" />
    <button @onclick="Rescan">Open</button>
    <button class="primary" @onclick="NewArtifact" disabled="@(string.IsNullOrWhiteSpace(State.Folder))">New artifact</button>
</div>

@* The registry is optional and its absence is stated rather than hidden, mirroring the
   Skipped check RegistryLayer already emits. Without it the ID picker degrades to a typed
   field; it never mints an ID either way, because rule 5 stands. *@
<div class="toolbar">
    <input @bind="State.RegistryCsvPath" placeholder="ID registry export (.csv), optional" />
    @if (string.IsNullOrWhiteSpace(State.RegistryCsvPath))
    {
        <span class="degraded">No registry loaded. Document IDs will be typed and the registry check skipped.</span>
    }
</div>

@if (error is not null)
{
    <p class="error">@error</p>
}

<Engagement Result="@result" OnOpen="OpenRow" />

@code {
    private string folder = string.Empty;
    private EngagementScanResult result = EngagementScanResult.NoFolderChosen;
    private string? error;

    private void Rescan()
    {
        error = null;
        try
        {
            result = EngagementScan.Run(folder, State.Engine.Config, State.Engine.SchemaRoot, new SystemClock());
            State.Folder = result.State == EngagementFolderState.NotFound ? null : result.Folder;
        }
        catch (Exception ex)
        {
            result = EngagementScanResult.NoFolderChosen;
            State.Folder = null;
            error = ex.Message;
        }
    }

    private void NewArtifact() => Nav.NavigateTo("/new");

    // A draft opens as itself. An AGR opens through FromDocument and is written beside it
    // as a draft: for a GUI-authored artifact the draft is the source of truth and the AGR
    // JSON is a build intermediate. Nothing is overwritten, because the draft takes the
    // draft extension.
    private void OpenRow(EngagementRow row)
    {
        error = null;
        var path = Path.Combine(result.Folder, row.FileName);

        try
        {
            if (path.EndsWith(DraftFile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                var draft = DraftFile.Read(path);
                if (draft is null)
                {
                    error = "That draft could not be read. It is listed as Unreadable above.";
                    return;
                }

                State.DraftPath = path;
                State.Draft = draft;
            }
            else
            {
                var agr = AgrParser.Parse(File.ReadAllText(path));
                var draftPath = Path.Combine(
                    result.Folder, DraftCreation.FileName(agr.Identity.DocumentId));

                var draft = AgrAuthoring.FromDocument(agr);
                DraftFile.Write(draftPath, draft);

                State.DraftPath = draftPath;
                State.Draft = draft;
            }

            Nav.NavigateTo("/editor");
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
    }
}
```

**One deliberate departure from the parent design's error table.** That table says a corrupt draft is moved aside with a timestamp and the editor starts clean. That is right for a draft the app opens on its own initiative; it is wrong here, where the technician explicitly asked for that file. Moving a file someone just pointed at, without asking, is a worse failure than refusing to open it. `EngagementScan` already lists it as `Unreadable`, so the condition is visible on the screen either way, and `OpenRow` reports it rather than touching the file. If a later unit opens drafts automatically, the move-aside behaviour belongs there.

Note: `EngagementScan.Run` takes the parsed schema node. `EngineContext.SchemaRoot` is that node, so no reparse is needed. If the signature does not match, run `grep -n "public static EngagementScanResult Run" -A 4 src/Registrar.Core/Engagement/EngagementScan.cs` and pass what it asks for.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EngagementOpenTests`
Expected: 2 passed.

Run: `dotnet test Registrar.sln`
Expected: all green, including the existing `EngagementComponentTests`.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.App tests/Registrar.App.Tests/EngagementOpenTests.cs
git commit -m "feat: load config once in DI and open an artifact from the engagement list"
```

---

### Task 8: The New artifact screen

**Files:**
- Create: `src/Registrar.App/Components/NewArtifact.razor`
- Test: `tests/Registrar.App.Tests/NewArtifactComponentTests.cs`

**Interfaces:**
- Consumes: `DraftCreation.CreatableTypes`, `DraftCreation.New`, `DraftCreation.FileName`, `DraftFile.Write`, `IIdRegistry.All`, `AppState`.
- Produces: the `/new` route. Navigates to `/editor` on create.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.App.Tests/NewArtifactComponentTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Registrar.App;
using Registrar.App.Components;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Validation;
using Xunit;

namespace Registrar.App.Tests;

public class NewArtifactComponentTests : TestContext
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private AppState Wire()
    {
        var state = new AppState(EngineContext.Load(RepoRoot()));
        Services.AddSingleton(state);
        Services.AddSingleton<IClock>(new FixedClock());
        return state;
    }

    // Rule 6 made visible: a type the engine refuses is never offered.
    [Fact]
    public void Only_creatable_types_appear_in_the_type_picker()
    {
        var state = Wire();
        var creatable = DraftCreation.CreatableTypes(state.Engine.Config).Select(t => t.Code).ToHashSet();
        var refused = state.Engine.Config.ArtifactTypesByCode.Values
            .First(t => t.SectionSchema?.Status != "defined").Code;

        var cut = RenderComponent<NewArtifact>();
        var offered = cut.FindAll("select.artifact-type option")
            .Select(o => o.GetAttribute("value")!)
            .Where(v => v.Length > 0)
            .ToList();

        Assert.Equal(creatable.OrderBy(c => c, StringComparer.Ordinal), offered);
        Assert.DoesNotContain(refused, offered);
    }

    // Degrade openly: with no registry the ID is typed, and the screen says the check is
    // being skipped rather than pretending it ran.
    [Fact]
    public void With_no_registry_the_id_is_typed_and_the_screen_says_so()
    {
        Wire();

        var cut = RenderComponent<NewArtifact>();

        Assert.Empty(cut.FindAll("select.document-id"));
        Assert.Single(cut.FindAll("input.document-id"));
        Assert.Contains("registry", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_is_disabled_until_the_required_fields_are_supplied()
    {
        Wire();

        var cut = RenderComponent<NewArtifact>();
        Assert.NotNull(cut.Find("button.create").GetAttribute("disabled"));

        cut.Find("select.artifact-type").Change("DS");
        cut.Find("input.document-id").Change("ORG-GOV-DS-016");
        cut.Find("input.owner").Change("Registry Owner");

        Assert.Null(cut.Find("button.create").GetAttribute("disabled"));
    }

    // The point of the review panel: the technician sees they supplied a few things and
    // the system supplied the rest.
    [Fact]
    public void The_review_panel_tags_each_derived_field_with_its_source()
    {
        Wire();

        var cut = RenderComponent<NewArtifact>();
        cut.Find("select.artifact-type").Change("DS");

        var markup = cut.Find(".review").InnerHtml;
        Assert.Contains("config", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("derived", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2027-02-04", markup, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~NewArtifactComponentTests`
Expected: build failure, `NewArtifact` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.App/Components/NewArtifact.razor`:

```razor
@page "/new"
@using System.IO
@inject AppState State
@inject IClock Clock
@inject NavigationManager Nav

@* Collects the five things a technician knows and shows what config supplies for the
   rest. Every value on the right is read from Core; nothing here derives anything. *@

<h1>New artifact</h1>

<div class="new-artifact">
    <div class="inputs">
        <label>
            Artifact type
            <select class="artifact-type" @onchange="TypeChanged">
                <option value=""></option>
                @foreach (var type in creatable)
                {
                    <option value="@type.Code">@type.Code - @type.Name</option>
                }
            </select>
        </label>

        <label>
            Document ID
            @if (registry is null)
            {
                <input class="document-id" @bind="documentId" placeholder="ORG-GOV-DS-016" />
            }
            else
            {
                <select class="document-id" @bind="documentId">
                    <option value=""></option>
                    @foreach (var record in IssuedIds())
                    {
                        <option value="@record.DocumentId">@record.DocumentId (@record.ApprovalStatus)</option>
                    }
                </select>
            }
        </label>

        @if (registry is null)
        {
            <p class="degraded">
                No ID registry is loaded, so the Document ID is typed and the registry check
                is skipped. Registry Owner issues IDs; this screen never mints one.
            </p>
        }

        <label>Client <input class="client" @bind="client" /></label>
        <label>Engagement <input class="engagement" @bind="engagement" /></label>
        <label>Owner <input class="owner" @bind="owner" /></label>

        <button class="create" disabled="@(!CanCreate)" @onclick="Create">Create</button>

        @if (error is not null)
        {
            <p class="error">@error</p>
        }
    </div>

    <div class="review">
        @if (selected is null)
        {
            <p>Choose an artifact type to see what the system will supply.</p>
        }
        else
        {
            <table>
                <thead><tr><th>Field</th><th>Value</th><th>Source</th></tr></thead>
                <tbody>
                    <tr><td>Classification</td><td>@defaults!.Classification</td><td>config</td></tr>
                    <tr><td>Layer</td><td>@defaults.Layer</td><td>config</td></tr>
                    <tr><td>Audience</td><td>@defaults.Audience</td><td>config</td></tr>
                    <tr><td>Version</td><td>@defaults.Version</td><td>config</td></tr>
                    <tr><td>Approval status</td><td>@defaults.ApprovalStatus</td><td>config</td></tr>
                    <tr><td>Prepared by</td><td>@defaults.PreparedBy</td><td>schema default</td></tr>
                    <tr><td>Review date</td><td>@defaults.ReviewDate</td><td>derived</td></tr>
                    <tr><td>Sections</td><td>@SectionCount</td><td>config</td></tr>
                </tbody>
            </table>
        }
    </div>
</div>

@code {
    private IReadOnlyList<ArtifactTypeConfig> creatable = Array.Empty<ArtifactTypeConfig>();
    private IIdRegistry? registry;
    private ArtifactTypeConfig? selected;
    private AuthoringDefaults? defaults;

    private string documentId = string.Empty;
    private string client = string.Empty;
    private string engagement = string.Empty;
    private string owner = string.Empty;
    private string? error;

    protected override void OnInitialized()
    {
        creatable = DraftCreation.CreatableTypes(State.Engine.Config);

        if (State.RegistryCsvPath is not null && File.Exists(State.RegistryCsvPath))
        {
            try { registry = CsvIdRegistry.FromCsvText(File.ReadAllText(State.RegistryCsvPath)); }
            catch (RegistryFormatException) { registry = null; }
        }
    }

    private bool CanCreate =>
        selected is not null
        && !string.IsNullOrWhiteSpace(documentId)
        && !string.IsNullOrWhiteSpace(owner);

    private int SectionCount => selected?.SectionSchema?.OrderedSections?.Count ?? 0;

    private IEnumerable<RegistryRecord> IssuedIds()
        => registry is null || selected is null
            ? Array.Empty<RegistryRecord>()
            : registry.All().Where(r => r.ArtifactType == selected.Code);

    private void TypeChanged(ChangeEventArgs args)
    {
        var code = args.Value?.ToString() ?? "";
        selected = creatable.FirstOrDefault(t => t.Code == code);
        defaults = selected is null
            ? null
            : AgrAuthoring.Defaults(selected, State.Engine.Config, State.Engine.SchemaRoot, Clock);
        documentId = "";
    }

    private void Create()
    {
        error = null;
        try
        {
            var draft = DraftCreation.New(
                selected!, documentId.Trim(), client, engagement, owner.Trim(),
                State.Engine.Config, State.Engine.SchemaRoot, Clock, Guid.NewGuid().ToString());

            var path = Path.Combine(State.Folder!, DraftCreation.FileName(documentId.Trim()));
            DraftFile.Write(path, draft);

            State.DraftPath = path;
            State.Draft = draft;
            Nav.NavigateTo("/editor");
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
    }
}
```

Add the layout to `src/Registrar.App/wwwroot/app.css`, colours by token only:

```css
.new-artifact { display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem; }
.new-artifact label { display: block; margin-bottom: 0.75rem; }
.new-artifact .review { border: 1px solid var(--org-border-neutral); padding: 1rem; }
.new-artifact .degraded { color: var(--org-secondary-gray); }
.new-artifact .error { color: var(--org-danger); }
```

Every custom property used in this plan is already in `config/generated/org-tokens.css`: `--org-danger`, `--org-border-neutral`, `--org-secondary-gray`, `--org-accent-blue`, `--org-table-row-alt-fill`, `--org-primary-blue`, `--org-heading-navy`, `--org-border`. Do not invent a colour and do not add one to config in this unit.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~NewArtifactComponentTests`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.App tests/Registrar.App.Tests/NewArtifactComponentTests.cs
git commit -m "feat: New artifact screen, five inputs and a review panel naming each source"
```

---

### Task 9: The editor shell, identity pane, and read-only fallback

**Files:**
- Create: `src/Registrar.App/Components/Editor.razor`, `IdentityPane.razor`, `SectionPanel.razor`, `ReadOnlyBlock.razor`
- Modify: `src/Registrar.App/wwwroot/app.css`
- Test: `tests/Registrar.App.Tests/EditorShellComponentTests.cs`

**Interfaces:**
- Consumes: `EditorForm.For`, `FieldPath.ElementId`, `AppState`.
- Produces: the `/editor` route. `IdentityPane` takes `[Parameter] AgrDraft Draft`, `[Parameter] AuthoringDefaults Defaults`, `[Parameter] EventCallback<AgrDraft> OnChanged`. `SectionPanel` takes `[Parameter] FormSection Section`, `[Parameter] EventCallback<AgrDraft> OnChanged`, `[Parameter] AgrDraft Draft`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.App.Tests/EditorShellComponentTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Registrar.App;
using Registrar.App.Components;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;
using Xunit;

namespace Registrar.App.Tests;

public class EditorShellComponentTests : TestContext
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static AgrBlock Table() => new()
    {
        BlockType = "table",
        ColumnHeaders = new[] { "Control", "Status" },
        Rows = new[] { new[] { "MFA", "In place" } }
    };

    private AppState Wire(AgrDraft draft)
    {
        var state = new AppState(EngineContext.Load(RepoRoot())) { Draft = draft, DraftPath = @"C:\x\y.agrdraft" };
        Services.AddSingleton(state);
        Services.AddSingleton<IClock>(new FixedClock());
        JSInterop.Mode = JSRuntimeMode.Loose;
        return state;
    }

    private static AgrDraft DraftWith(params AgrBlock[] blocks)
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        return DraftEdits.NormalizeSections(
            new AgrDraft
            {
                ArtifactType = "DS",
                DocumentId = "ORG-GOV-DS-016",
                Owner = "Registry Owner",
                Sections = new[] { new AgrSection { Title = titles[0], DescriptorLine = null, Blocks = blocks } }
            },
            type);
    }

    [Fact]
    public void One_panel_renders_per_configured_section_with_the_title_locked()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var titles = config.ArtifactTypesByCode["DS"].SectionSchema!.OrderedSections!;
        Wire(DraftWith());

        var cut = RenderComponent<Editor>();

        Assert.Equal(titles.Count, cut.FindAll("section.section-panel").Count);
        // A title is config's to decide (QA-ENG-02), so it is text and never an input.
        Assert.Empty(cut.FindAll("section.section-panel h2 input"));
        Assert.Contains(titles[0], cut.Markup, StringComparison.Ordinal);
    }

    // The read-only fallback exists so a block a technician cannot edit is still visible
    // and still saved. Hiding it would be the silent degradation the design forbids.
    [Fact]
    public void An_unsupported_block_is_shown_read_only_and_says_why()
    {
        Wire(DraftWith(Table()));

        var cut = RenderComponent<Editor>();
        var block = cut.Find(".block.read-only");

        Assert.Contains("MFA", block.InnerHtml, StringComparison.Ordinal);
        Assert.Contains("source AGR", block.InnerHtml, StringComparison.Ordinal);
        Assert.Empty(block.QuerySelectorAll("input, textarea, select"));
    }

    [Fact]
    public void Every_editable_field_carries_the_element_id_its_pointer_derives()
    {
        Wire(DraftWith(new AgrBlock { BlockType = "paragraph", Text = "hello" }));

        var cut = RenderComponent<Editor>();

        Assert.NotNull(cut.Find("#" + FieldPath.ElementId(FieldPath.BlockField(0, 0, "text"))));
        Assert.NotNull(cut.Find("#" + FieldPath.ElementId(FieldPath.Owner)));
    }

    // Derived values are config's answer, not a box to type in. Changing one is a
    // deliberate override, which is what AgrDraftOverrides exists to represent.
    [Fact]
    public void Derived_identity_values_render_as_text_rather_than_inputs()
    {
        Wire(DraftWith());

        var cut = RenderComponent<Editor>();
        var identity = cut.Find(".identity-pane");

        Assert.Contains("Client-Facing", identity.InnerHtml, StringComparison.Ordinal);
        Assert.Empty(identity.QuerySelectorAll("input.classification"));
        Assert.Single(identity.QuerySelectorAll("input.owner"));
    }

    [Fact]
    public void With_no_draft_open_the_editor_says_so_rather_than_rendering_an_empty_form()
    {
        Services.AddSingleton(new AppState(EngineContext.Load(RepoRoot())));
        Services.AddSingleton<IClock>(new FixedClock());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<Editor>();

        Assert.Empty(cut.FindAll("section.section-panel"));
        Assert.Contains("No artifact is open", cut.Markup, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EditorShellComponentTests`
Expected: build failure, `Editor` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.App/Components/ReadOnlyBlock.razor`:

```razor
@* A block type this unit does not author. Shown rather than hidden, and preserved
   verbatim on every save: a technician who cannot see their own content cannot know it
   is still there. *@

<div class="block read-only">
    <div class="block-kind">@Block.BlockType (read-only, edit this in the source AGR)</div>

    @if (Block.Text is not null)
    {
        <p>@Block.Text</p>
    }

    @if (Block.AggregationSource is not null)
    {
        <p>Projected from @Block.AggregationSource at render time.</p>
    }

    @if (Block.ColumnHeaders is not null && Block.Rows is not null)
    {
        <table>
            <thead>
                <tr>@foreach (var header in Block.ColumnHeaders) { <th>@header</th> }</tr>
            </thead>
            <tbody>
                @foreach (var row in Block.Rows)
                {
                    <tr>@foreach (var cell in row) { <td>@cell</td> }</tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    [Parameter]
    public AgrBlock Block { get; set; } = default!;
}
```

Create `src/Registrar.App/Components/SectionPanel.razor`:

```razor
@* Renders one section. The title is text, never an input: section titles are config's to
   decide and QA-ENG-02 rejects any deviation, so an editable title would be a build
   failure a technician could type. *@

<section class="section-panel" id="@FieldPath.ElementId(Section.BlocksJsonPath)">
    <h2>@Section.Title</h2>

    @if (Section.DescriptorLine is not null)
    {
        <p class="descriptor">@Section.DescriptorLine</p>
    }

    @if (Section.Blocks.Count == 0)
    {
        <p class="empty-section">Nothing here yet.</p>
    }

    @foreach (var block in Section.Blocks)
    {
        <div class="block-row">
            @switch (block.Kind)
            {
                case BlockEditorKind.Prose:
                    <ProseBlock Block="block" Draft="Draft" OnChanged="OnChanged" />
                    break;
                case BlockEditorKind.Clause:
                    <ClauseBlock Block="block" Draft="Draft" OnChanged="OnChanged" />
                    break;
                case BlockEditorKind.Radio:
                    <RadioBlock Block="block" Draft="Draft" OnChanged="OnChanged" />
                    break;
                default:
                    <ReadOnlyBlock Block="block.Block" />
                    break;
            }

            <div class="block-actions">
                <button @onclick="() => Move(block, -1)">Up</button>
                <button @onclick="() => Move(block, 1)">Down</button>
                <button class="danger" @onclick="() => Remove(block)">Remove</button>
            </div>
        </div>
    }

    <div class="add-block">
        <button @onclick='() => Add(DraftEdits.NewProse("paragraph"))'>Add paragraph</button>
        <button @onclick='() => Add(DraftEdits.NewProse("h3"))'>Add heading</button>
        <button @onclick='() => Add(DraftEdits.NewProse("bulletList"))'>Add list</button>
        <button @onclick="AddClause">Add clause</button>
        <button @onclick="AddQuestion">Add question</button>
    </div>
</section>

@code {
    [Parameter] public FormSection Section { get; set; } = default!;
    [Parameter] public AgrDraft Draft { get; set; } = default!;
    [Parameter] public IReadOnlyList<ClauseEntry> Clauses { get; set; } = Array.Empty<ClauseEntry>();
    [Parameter] public IReadOnlyList<string> StandardOptions { get; set; } = Array.Empty<string>();
    [Parameter] public EventCallback<AgrDraft> OnChanged { get; set; }

    private Task Add(AgrBlock block)
        => OnChanged.InvokeAsync(DraftEdits.AddBlock(Draft, Section.Index, block));

    private Task AddClause()
        => Clauses.Count == 0
            ? Task.CompletedTask
            : Add(DraftEdits.NewClause(Clauses[0].ClauseId));

    private Task AddQuestion()
        => Add(DraftEdits.NewQuestion(Section.Blocks.Count + 1, StandardOptions));

    private Task Move(FormBlock block, int delta)
        => OnChanged.InvokeAsync(DraftEdits.MoveBlock(Draft, block.SectionIndex, block.BlockIndex, delta));

    private Task Remove(FormBlock block)
        => OnChanged.InvokeAsync(DraftEdits.RemoveBlock(Draft, block.SectionIndex, block.BlockIndex));
}
```

Create `src/Registrar.App/Components/IdentityPane.razor`:

```razor
@* Human input on the left of the divide, config's answers on the right. A derived value
   is text rather than a box, because changing one is a deliberate override and
   AgrDraftOverrides exists so that departure is representable and visible rather than
   indistinguishable from typing. *@

<div class="identity-pane">
    <h2>Identity</h2>

    <dl class="derived">
        <dt>Artifact type</dt><dd>@Draft.ArtifactType</dd>
        <dt>Document ID</dt><dd id="@FieldPath.ElementId(FieldPath.DocumentId)">@Draft.DocumentId</dd>
        <dt>Classification</dt><dd>@Defaults.Classification</dd>
        <dt>Layer</dt><dd id="@FieldPath.ElementId(FieldPath.Layer)">@Defaults.Layer</dd>
        <dt>Audience</dt><dd>@Defaults.Audience</dd>
        <dt>Version</dt><dd>@Defaults.Version</dd>
        <dt>Approval status</dt><dd id="@FieldPath.ElementId(FieldPath.ApprovalStatus)">@Defaults.ApprovalStatus</dd>
        <dt>Prepared by</dt><dd>@Defaults.PreparedBy</dd>
    </dl>

    <label>
        Owner
        <input class="owner" id="@FieldPath.ElementId(FieldPath.Owner)"
               value="@Draft.Owner" @onchange="SetOwner" />
    </label>

    <label>
        Client
        <input class="client" id="@FieldPath.ElementId(FieldPath.Client)"
               value="@Draft.Client" @onchange="SetClient" />
    </label>

    <label>
        Engagement
        <input class="engagement" id="@FieldPath.ElementId(FieldPath.Engagement)"
               value="@Draft.Engagement" @onchange="SetEngagement" />
    </label>

    <label>
        Review date
        <input class="review-date" type="date" id="@FieldPath.ElementId(FieldPath.ReviewDate)"
               value="@Draft.ReviewDate" @onchange="SetReviewDate" />
    </label>
</div>

@code {
    [Parameter] public AgrDraft Draft { get; set; } = default!;
    [Parameter] public AuthoringDefaults Defaults { get; set; } = default!;
    [Parameter] public EventCallback<AgrDraft> OnChanged { get; set; }

    private static string? Text(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private Task SetOwner(ChangeEventArgs args) => OnChanged.InvokeAsync(Draft with { Owner = Text(args) });
    private Task SetClient(ChangeEventArgs args) => OnChanged.InvokeAsync(Draft with { Client = Text(args) });
    private Task SetEngagement(ChangeEventArgs args) => OnChanged.InvokeAsync(Draft with { Engagement = Text(args) });
    private Task SetReviewDate(ChangeEventArgs args) => OnChanged.InvokeAsync(Draft with { ReviewDate = Text(args) });
}
```

Create `src/Registrar.App/Components/Editor.razor` (the rail and header arrive in Task 11; this is the shell):

```razor
@page "/editor"
@inject AppState State
@inject IClock Clock

@if (State.Draft is null || type is null)
{
    <p class="prompt">No artifact is open. Choose one from the engagement list, or create one.</p>
}
else
{
    <div class="editor">
        <IdentityPane Draft="State.Draft" Defaults="defaults!" OnChanged="Changed" />

        <div class="sections">
            @foreach (var section in form.Sections)
            {
                <SectionPanel Section="section"
                              Draft="State.Draft"
                              Clauses="State.Engine.Config.Clauses"
                              StandardOptions="State.Engine.Config.RadioPattern.StandardOptionSet"
                              OnChanged="Changed" />
            }
        </div>
    </div>
}

@code {
    private ArtifactTypeConfig? type;
    private AuthoringDefaults? defaults;
    private EditorForm form = new(Array.Empty<FormSection>());

    protected override void OnInitialized() => Rebuild();

    private void Rebuild()
    {
        if (State.Draft?.ArtifactType is null) { type = null; return; }

        State.Engine.Config.ArtifactTypesByCode.TryGetValue(State.Draft.ArtifactType, out var found);
        type = found;
        if (type is null) return;

        defaults = AgrAuthoring.Defaults(type, State.Engine.Config, State.Engine.SchemaRoot, Clock);
        form = EditorForm.For(State.Draft, type);
    }

    // Task 11 attaches autosave and validation here. Until then a change updates state and
    // re-renders, which is enough for the shell to be testable on its own.
    private void Changed(AgrDraft draft)
    {
        State.Draft = draft;
        Rebuild();
    }
}
```

Add to `src/Registrar.App/wwwroot/app.css`:

```css
.editor { display: grid; grid-template-columns: 18rem 1fr; gap: 1.5rem; align-items: start; }
.identity-pane dl.derived dt { font-weight: 600; }
.identity-pane dl.derived dd { color: var(--org-secondary-gray); margin: 0 0 0.5rem 0; }
.section-panel { border: 1px solid var(--org-border-neutral); padding: 1rem; margin-bottom: 1rem; }
.section-panel .empty-section { color: var(--org-secondary-gray); }
.block-row { display: flex; gap: 0.75rem; align-items: flex-start; margin-bottom: 0.75rem; }
.block-row > :first-child { flex: 1; }
.block.read-only { background: var(--org-table-row-alt-fill); padding: 0.75rem; }
.block .block-kind { color: var(--org-secondary-gray); font-size: 0.85rem; }
button.danger { color: var(--org-danger); }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EditorShellComponentTests`
Expected: 5 passed. `ProseBlock`, `ClauseBlock`, and `RadioBlock` do not exist yet, so create empty stubs first if the build blocks: each is a `.razor` file with `[Parameter] public FormBlock Block`, `[Parameter] public AgrDraft Draft`, `[Parameter] public EventCallback<AgrDraft> OnChanged` and a single `<div>`. Task 10 fills them in.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.App tests/Registrar.App.Tests/EditorShellComponentTests.cs
git commit -m "feat: editor shell with config-generated panels and a read-only fallback"
```

---

### Task 10: The three block editors

**Files:**
- Create/replace: `src/Registrar.App/Components/ProseBlock.razor`, `ClauseBlock.razor`, `RadioBlock.razor`
- Test: `tests/Registrar.App.Tests/BlockEditorComponentTests.cs`

**Interfaces:**
- Consumes: `DraftEdits.SetProse/SetClause/SetQuestionPrompt/SetQuestionRequired/SetQuestionOptions`, `CharacterLint.Scan`, `CharacterPolicy.FromConfig`, `config.Clauses`, `config.RadioPattern`.
- Produces: three components, each taking `[Parameter] FormBlock Block`, `[Parameter] AgrDraft Draft`, `[Parameter] EventCallback<AgrDraft> OnChanged`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.App.Tests/BlockEditorComponentTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Registrar.App;
using Registrar.App.Components;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;
using Xunit;

namespace Registrar.App.Tests;

public class BlockEditorComponentTests : TestContext
{
    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private ConfigBundle Wire()
    {
        var engine = EngineContext.Load(RepoRoot());
        Services.AddSingleton(new AppState(engine));
        JSInterop.Mode = JSRuntimeMode.Loose;
        return engine.Config;
    }

    private static (AgrDraft Draft, FormBlock Block) One(AgrBlock block)
    {
        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[] { new AgrSection { Title = "S", DescriptorLine = null, Blocks = new[] { block } } }
        };

        return (draft, new FormBlock(0, 0, FieldPath.Block(0, 0), EditorForm.KindOf(block.BlockType), block));
    }

    // QA-ENG-05 fails a prohibited character at build. Surfacing it as it is typed means
    // the editor and the check cannot disagree, because both read contentLint from config.
    [Fact]
    public void QA_ENG_05_prose_editor_surfaces_a_prohibited_character_as_it_is_typed()
    {
        Wire();
        // U+2014 EM DASH, expressed as an escape: a literal would fail SourceHygieneTests.
        var (draft, block) = One(new AgrBlock { BlockType = "paragraph", Text = "before \u2014 after" });

        var cut = RenderComponent<ProseBlock>(p => p
            .Add(c => c.Block, block).Add(c => c.Draft, draft).Add(c => c.OnChanged, (AgrDraft _) => { }));

        var warning = cut.Find(".lint");
        Assert.Contains("U+2014", warning.TextContent, StringComparison.Ordinal);
        Assert.Contains("em dash", warning.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_prose_shows_no_lint_warning()
    {
        Wire();
        var (draft, block) = One(new AgrBlock { BlockType = "paragraph", Text = "Perfectly ordinary prose." });

        var cut = RenderComponent<ProseBlock>(p => p
            .Add(c => c.Block, block).Add(c => c.Draft, draft).Add(c => c.OnChanged, (AgrDraft _) => { }));

        Assert.Empty(cut.FindAll(".lint"));
    }

    [Fact]
    public void Editing_prose_reports_the_new_draft_to_the_caller()
    {
        Wire();
        AgrDraft? updated = null;
        var (draft, block) = One(new AgrBlock { BlockType = "paragraph", Text = "old" });

        var cut = RenderComponent<ProseBlock>(p => p
            .Add(c => c.Block, block).Add(c => c.Draft, draft).Add(c => c.OnChanged, (AgrDraft d) => updated = d));

        cut.Find("textarea").Change("new");

        Assert.Equal("new", updated!.Sections[0].Blocks[0].Text);
    }

    // Rule 4 made visible. The canonical text is on screen so the technician knows what
    // will be inserted, and there is no control bound to it, so they cannot paraphrase it.
    [Fact]
    public void Clause_text_is_displayed_and_has_no_editable_control()
    {
        var config = Wire();
        var clause = config.Clauses[0];
        var (draft, block) = One(new AgrBlock { BlockType = "clauseInsertion", ClauseId = clause.ClauseId });

        var cut = RenderComponent<ClauseBlock>(p => p
            .Add(c => c.Block, block).Add(c => c.Draft, draft).Add(c => c.OnChanged, (AgrDraft _) => { }));

        var text = cut.Find(".canonical-text");
        Assert.Contains(clause.CanonicalText, text.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".canonical-text textarea"));
        Assert.Empty(cut.FindAll(".canonical-text input"));
        // The only control is the selector for which clause, never for its wording.
        Assert.Single(cut.FindAll("select.clause-id"));
    }

    [Fact]
    public void Choosing_a_clause_records_the_id_only()
    {
        var config = Wire();
        AgrDraft? updated = null;
        var (draft, block) = One(new AgrBlock { BlockType = "clauseInsertion", ClauseId = config.Clauses[0].ClauseId });

        var cut = RenderComponent<ClauseBlock>(p => p
            .Add(c => c.Block, block).Add(c => c.Draft, draft).Add(c => c.OnChanged, (AgrDraft d) => updated = d));

        cut.Find("select.clause-id").Change(config.Clauses[1].ClauseId);

        Assert.Equal(config.Clauses[1].ClauseId, updated!.Sections[0].Blocks[0].ClauseId);
        Assert.Null(updated.Sections[0].Blocks[0].Text);
    }

    // The minimum is config, not a literal. This test reads it from config too, so it
    // still passes if the owner changes the pattern, and fails if the component hard-codes.
    [Fact]
    public void QA_SF_10_radio_editor_reports_too_few_options_against_the_configured_minimum()
    {
        var config = Wire();
        var minimum = config.RadioPattern.MinimumOptions;
        var options = config.RadioPattern.StandardOptionSet.Take(minimum - 1).ToArray();

        var (draft, block) = One(new AgrBlock
        {
            BlockType = "radioQuestion",
            Question = new AgrQuestion { Number = 1, Prompt = "In place?", Required = true, Options = options }
        });

        var cut = RenderComponent<RadioBlock>(p => p
            .Add(c => c.Block, block).Add(c => c.Draft, draft).Add(c => c.OnChanged, (AgrDraft _) => { }));

        var warning = cut.Find(".option-count");
        Assert.Contains(minimum.ToString(), warning.TextContent, StringComparison.Ordinal);
        Assert.Contains("below", warning.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_question_at_the_configured_minimum_is_not_flagged()
    {
        var config = Wire();
        var options = config.RadioPattern.StandardOptionSet.Take(config.RadioPattern.MinimumOptions).ToArray();

        var (draft, block) = One(new AgrBlock
        {
            BlockType = "radioQuestion",
            Question = new AgrQuestion { Number = 1, Prompt = "In place?", Required = true, Options = options }
        });

        var cut = RenderComponent<RadioBlock>(p => p
            .Add(c => c.Block, block).Add(c => c.Draft, draft).Add(c => c.OnChanged, (AgrDraft _) => { }));

        Assert.Empty(cut.FindAll(".option-count.below-minimum"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~BlockEditorComponentTests`
Expected: failures on every assertion, since the stubs render an empty div.

- [ ] **Step 3: Write the implementations**

Replace `src/Registrar.App/Components/ProseBlock.razor`:

```razor
@inject AppState State

@* Lints as the technician types, against the same contentLint policy InputHygieneLayer
   enforces for QA-ENG-05. Reading the policy from config rather than restating it is what
   keeps the editor from disagreeing with the check that will later fail the build. *@

<div class="block prose">
    <div class="block-kind">@Block.Block.BlockType</div>

    <textarea id="@FieldPath.ElementId(FieldPath.BlockField(Block.SectionIndex, Block.BlockIndex, Field))"
              rows="4"
              value="@Value"
              @onchange="Changed"></textarea>

    @if (offenses.Count > 0)
    {
        <p class="lint">Prohibited characters: @CharacterLint.FormatActual(offenses)</p>
    }
</div>

@code {
    [Parameter] public FormBlock Block { get; set; } = default!;
    [Parameter] public AgrDraft Draft { get; set; } = default!;
    [Parameter] public EventCallback<AgrDraft> OnChanged { get; set; }

    private IReadOnlyList<Offense> offenses = Array.Empty<Offense>();

    private bool IsList => Block.Block.BlockType == "bulletList";

    // A list edits items, a paragraph edits text, and the pointer has to name whichever it
    // is, because that is the field a check would report against.
    private string Field => IsList ? "items" : "text";

    private string Value => IsList
        ? string.Join("\n", Block.Block.Items ?? Array.Empty<string>())
        : Block.Block.Text ?? "";

    protected override void OnParametersSet() => Lint(Value);

    private void Lint(string text)
        => offenses = CharacterLint.Scan(text, CharacterPolicy.FromConfig(State.Engine.Config.ContentLint));

    private Task Changed(ChangeEventArgs args)
    {
        var text = args.Value?.ToString() ?? "";
        Lint(text);
        return OnChanged.InvokeAsync(
            DraftEdits.SetProse(Draft, Block.SectionIndex, Block.BlockIndex, text));
    }
}
```

Replace `src/Registrar.App/Components/ClauseBlock.razor`:

```razor
@inject AppState State

@* Rule 4: clause insertion is by reference. The block carries a clauseId and the engine
   inserts canonical text from the clause library, proving it with SHA-256. The canonical
   text is shown so the technician knows what lands in the document, and no control is
   bound to it, so paraphrase is not something they can express. *@

<div class="block clause">
    <div class="block-kind">Clause insertion</div>

    <select class="clause-id"
            id="@FieldPath.ElementId(FieldPath.BlockField(Block.SectionIndex, Block.BlockIndex, "clauseId"))"
            @onchange="Changed">
        @foreach (var clause in State.Engine.Config.Clauses)
        {
            <option value="@clause.ClauseId" selected="@(clause.ClauseId == Block.Block.ClauseId)">
                @clause.ClauseId - @clause.Name
            </option>
        }
    </select>

    @if (Selected is not null)
    {
        <blockquote class="canonical-text">@Selected.CanonicalText</blockquote>

        @if (Selected.ContainsPlaceholders)
        {
            <p class="note">This clause carries placeholder tokens, resolved from the placeholder map at build.</p>
        }
    }
    else
    {
        <p class="lint">@Block.Block.ClauseId is not in the clause library.</p>
    }
</div>

@code {
    [Parameter] public FormBlock Block { get; set; } = default!;
    [Parameter] public AgrDraft Draft { get; set; } = default!;
    [Parameter] public EventCallback<AgrDraft> OnChanged { get; set; }

    private ClauseEntry? Selected
        => Block.Block.ClauseId is not null
            && State.Engine.Config.ClausesById.TryGetValue(Block.Block.ClauseId, out var entry)
            ? entry
            : null;

    private Task Changed(ChangeEventArgs args)
        => OnChanged.InvokeAsync(DraftEdits.SetClause(
            Draft, Block.SectionIndex, Block.BlockIndex, args.Value?.ToString() ?? ""));
}
```

Replace `src/Registrar.App/Components/RadioBlock.razor`:

```razor
@inject AppState State

@* The option floor is config (radioOptionPattern.minimumOptions), read here exactly as
   RadioQuestionLayer reads it for QA-SF-10. A literal here would silently disagree with
   the check the moment the owner changed the pattern. *@

<div class="block radio">
    <div class="block-kind">Question @Number</div>

    <input class="prompt"
           id="@FieldPath.ElementId(FieldPath.BlockField(Block.SectionIndex, Block.BlockIndex, "question/prompt"))"
           value="@Prompt"
           @onchange="PromptChanged" />

    <label class="required">
        <input type="checkbox" checked="@Required" @onchange="RequiredChanged" />
        @(Required ? State.Engine.Config.RadioPattern.RequiredLabel : State.Engine.Config.RadioPattern.OptionalLabel)
    </label>

    <textarea class="options"
              id="@FieldPath.ElementId(FieldPath.BlockField(Block.SectionIndex, Block.BlockIndex, "question/options"))"
              rows="4"
              value="@OptionsText"
              @onchange="OptionsChanged"></textarea>

    <p class="option-count @(BelowMinimum ? "below-minimum" : "")">
        @Options.Count options, one per line.
        @if (BelowMinimum)
        {
            <span>This is below the configured minimum of @Minimum and will fail QA-SF-10.</span>
        }
    </p>
</div>

@code {
    [Parameter] public FormBlock Block { get; set; } = default!;
    [Parameter] public AgrDraft Draft { get; set; } = default!;
    [Parameter] public EventCallback<AgrDraft> OnChanged { get; set; }

    private AgrQuestion? Question => Block.Block.Question;
    private int Number => Question?.Number ?? 0;
    private string Prompt => Question?.Prompt ?? "";
    private bool Required => Question?.Required ?? true;
    private IReadOnlyList<string> Options => Question?.Options ?? Array.Empty<string>();
    private string OptionsText => string.Join("\n", Options);

    private int Minimum => State.Engine.Config.RadioPattern.MinimumOptions;
    private bool BelowMinimum => Options.Count < Minimum;

    private Task PromptChanged(ChangeEventArgs args)
        => OnChanged.InvokeAsync(DraftEdits.SetQuestionPrompt(
            Draft, Block.SectionIndex, Block.BlockIndex, args.Value?.ToString() ?? ""));

    private Task RequiredChanged(ChangeEventArgs args)
        => OnChanged.InvokeAsync(DraftEdits.SetQuestionRequired(
            Draft, Block.SectionIndex, Block.BlockIndex, args.Value is true));

    private Task OptionsChanged(ChangeEventArgs args)
    {
        var options = (args.Value?.ToString() ?? "")
            .Split('\n')
            .Select(line => line.Trim('\r').Trim())
            .Where(line => line.Length > 0)
            .ToList();

        return OnChanged.InvokeAsync(DraftEdits.SetQuestionOptions(
            Draft, Block.SectionIndex, Block.BlockIndex, options));
    }
}
```

Add to `src/Registrar.App/wwwroot/app.css`:

```css
.block .lint, .option-count.below-minimum { color: var(--org-danger); }
.block .note { color: var(--org-secondary-gray); }
.block.clause .canonical-text { border-left: 3px solid var(--org-accent-blue); padding-left: 0.75rem; }
.block textarea, .block input.prompt { width: 100%; }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~BlockEditorComponentTests`
Expected: 7 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.App tests/Registrar.App.Tests/BlockEditorComponentTests.cs
git commit -m "feat: prose, clause, and radio block editors driven by config"
```

---

### Task 11: The rail, the header, and autosave

**Files:**
- Create: `src/Registrar.App/Components/ValidationRail.razor`, `EditorHeader.razor`, `src/Registrar.App/wwwroot/editor.js`
- Modify: `src/Registrar.App/Components/Editor.razor`, `src/Registrar.App/wwwroot/index.html`, `app.css`
- Test: `tests/Registrar.App.Tests/ValidationRailComponentTests.cs`

**Interfaces:**
- Consumes: `EditorRail.FromGaps/FromChecks/FromEngineError`, `AgrAuthoring.ToDocument`, `AgrSerializer.ToJson`, `ValidationService.Validate`, `DraftFile.Write`.
- Produces: `ValidationRail` taking `[Parameter] RailState State`; `EditorHeader` taking `[Parameter] RailState Rail`, `[Parameter] string Mode`, `[Parameter] EventCallback<string> OnModeChanged`, `[Parameter] string? SaveError`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.App.Tests/ValidationRailComponentTests.cs`:

```csharp
using Bunit;
using Registrar.App.Components;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Model;
using Xunit;

namespace Registrar.App.Tests;

public class ValidationRailComponentTests : TestContext
{
    [Fact]
    public void A_rail_item_targets_the_element_id_its_pointer_derives()
    {
        var state = EditorRail.FromGaps(new[] { new AuthoringGap(FieldPath.Owner, "Owner is required.") });

        var cut = RenderComponent<ValidationRail>(p => p.Add(c => c.State, state));

        Assert.Equal(FieldPath.ElementId(FieldPath.Owner),
            cut.Find("li button.rail-item").GetAttribute("data-target"));
    }

    // An item with no field is still shown. Dropping it would hide a real problem, and
    // making it look clickable would send the technician nowhere.
    [Fact]
    public void An_item_with_no_field_is_shown_but_not_clickable()
    {
        var state = EditorRail.FromChecks(
            new[]
            {
                new Check
                {
                    CheckId = "QA-SCHEMA-01", LayerName = "input", Result = CheckResultValue.Fail,
                    SeverityLevel = Severity.Blocking, Message = "Something global.", JsonPath = null
                }
            },
            OverallResult.Fail);

        var cut = RenderComponent<ValidationRail>(p => p.Add(c => c.State, state));

        Assert.Empty(cut.FindAll("li button.rail-item"));
        Assert.Contains("Something global.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_rail_says_so_rather_than_rendering_nothing()
    {
        var state = EditorRail.FromChecks(Array.Empty<Check>(), OverallResult.Pass);

        var cut = RenderComponent<ValidationRail>(p => p.Add(c => c.State, state));

        Assert.Contains("Nothing blocking", cut.Markup, StringComparison.Ordinal);
    }

    // The reason is visible without hovering, which is the parent design's requirement.
    [Fact]
    public void The_header_disables_build_and_shows_the_reason_as_text()
    {
        var state = EditorRail.FromGaps(new[] { new AuthoringGap(FieldPath.Owner, "Owner is required.") });

        var cut = RenderComponent<EditorHeader>(p => p
            .Add(c => c.Rail, state).Add(c => c.CurrentMode, Mode.Draft)
            .Add(c => c.OnModeChanged, (string _) => { }));

        Assert.NotNull(cut.Find("button.build").GetAttribute("disabled"));
        Assert.Contains(state.BuildBlockedReason, cut.Find(".build-reason").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_save_is_reported_rather_than_swallowed()
    {
        var state = EditorRail.FromChecks(Array.Empty<Check>(), OverallResult.Pass);

        var cut = RenderComponent<EditorHeader>(p => p
            .Add(c => c.Rail, state).Add(c => c.CurrentMode, Mode.Draft)
            .Add(c => c.OnModeChanged, (string _) => { })
            .Add(c => c.SaveError, @"Access to C:\x\y.agrdraft was denied."));

        Assert.Contains(@"C:\x\y.agrdraft", cut.Find(".save-error").TextContent, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~ValidationRailComponentTests`
Expected: build failure, `ValidationRail` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.App/wwwroot/editor.js`:

```javascript
// Focus, not just scroll. A rail item that only scrolled would leave the keyboard where
// it was, which makes the rail useless without a mouse.
export function focusField(id) {
    const element = document.getElementById(id);
    if (!element) return false;
    element.scrollIntoView({ block: "center" });
    element.focus();
    return true;
}
```

In `src/Registrar.App/wwwroot/index.html`, add before the closing `</body>`, after the blazor script:

```html
    <script type="module" src="editor.js"></script>
```

Create `src/Registrar.App/Components/ValidationRail.razor`:

```razor
@inject IJSRuntime Js

@* Every item the engine or the authoring model reported, in the order it reported them.
   An item with a pointer is a button because it navigates; an item without one is text,
   because pretending it navigates would be worse than admitting it does not. *@

<aside class="rail">
    <h2>Validation</h2>

    @if (State.Items.Count == 0)
    {
        <p class="clean">Nothing blocking. This draft is ready to build.</p>
    }
    else
    {
        <ul>
            @foreach (var item in State.Items)
            {
                <li class="@item.Severity.ToString().ToLowerInvariant()">
                    @if (item.ElementId is not null)
                    {
                        <button class="rail-item" data-target="@item.ElementId"
                                @onclick="() => Focus(item.ElementId)">@item.Headline</button>
                    }
                    else
                    {
                        <span class="rail-item-static">@item.Headline</span>
                    }

                    @if (item.Detail is not null)
                    {
                        <p class="detail">@item.Detail</p>
                    }
                </li>
            }
        </ul>
    }
</aside>

@code {
    [Parameter] public RailState State { get; set; } = default!;

    private async Task Focus(string elementId)
    {
        var module = await Js.InvokeAsync<IJSObjectReference>("import", "./editor.js");
        await module.InvokeAsync<bool>("focusField", elementId);
    }
}
```

Create `src/Registrar.App/Components/EditorHeader.razor`:

```razor
@* Build's reason is text next to the button, not a tooltip. A technician should not have
   to hover to learn why the one action they came for is unavailable. *@

<header class="editor-header">
    <label class="mode">
        Mode
        <select @onchange="ModeChanged">
            <option value="@Mode.Draft" selected="@(CurrentMode == Mode.Draft)">Draft</option>
            <option value="@Mode.Promotion" selected="@(CurrentMode == Mode.Promotion)">Promotion</option>
        </select>
    </label>

    <button class="import" disabled>Import from Governor</button>
    <button class="build" disabled="@(!Rail.BuildEnabled)">Build</button>

    @if (!Rail.BuildEnabled)
    {
        <span class="build-reason">@Rail.BuildBlockedReason</span>
    }

    @if (SaveError is not null)
    {
        <p class="save-error">This draft could not be saved: @SaveError</p>
    }
</header>

@code {
    // Named CurrentMode because Mode is also the Registrar.Core static class this markup
    // reads its values from, and a parameter of that name would shadow it.
    [Parameter] public string CurrentMode { get; set; } = Registrar.Core.Mode.Draft;
    [Parameter] public RailState Rail { get; set; } = default!;
    [Parameter] public EventCallback<string> OnModeChanged { get; set; }
    [Parameter] public string? SaveError { get; set; }

    private Task ModeChanged(ChangeEventArgs args)
        => OnModeChanged.InvokeAsync(args.Value?.ToString() ?? Registrar.Core.Mode.Draft);
}
```

The parameter is `CurrentMode` rather than `Mode` because `Mode` is also the `Registrar.Core` static class this markup reads `Mode.Draft` and `Mode.Promotion` from, and a parameter of that name would shadow it.

Replace the `@code` block and markup of `src/Registrar.App/Components/Editor.razor`:

```razor
@page "/editor"
@using System.IO
@using System.Timers
@implements IDisposable
@inject AppState State
@inject IClock Clock

@if (State.Draft is null || type is null)
{
    <p class="prompt">No artifact is open. Choose one from the engagement list, or create one.</p>
}
else
{
    <EditorHeader Rail="rail" CurrentMode="State.Draft.Mode" OnModeChanged="ModeChanged" SaveError="saveError" />

    <div class="editor">
        <IdentityPane Draft="State.Draft" Defaults="defaults!" OnChanged="Changed" />

        <div class="sections">
            @foreach (var section in form.Sections)
            {
                <SectionPanel Section="section"
                              Draft="State.Draft"
                              Clauses="State.Engine.Config.Clauses"
                              StandardOptions="State.Engine.Config.RadioPattern.StandardOptionSet"
                              OnChanged="Changed" />
            }
        </div>

        <ValidationRail State="rail" />
    </div>
}

@code {
    // One debounce serves both jobs. Writing on the same tick that validates means the
    // file on disk and the rail on screen always describe the same draft.
    private const int DebounceMs = 400;

    private ArtifactTypeConfig? type;
    private AuthoringDefaults? defaults;
    private EditorForm form = new(Array.Empty<FormSection>());
    private RailState rail = EditorRail.FromGaps(Array.Empty<AuthoringGap>());
    private string? saveError;
    private Timer? debounce;

    protected override void OnInitialized()
    {
        Rebuild();
        Settle();
    }

    private void Rebuild()
    {
        if (State.Draft?.ArtifactType is null) { type = null; return; }

        State.Engine.Config.ArtifactTypesByCode.TryGetValue(State.Draft.ArtifactType, out var found);
        type = found;
        if (type is null) return;

        defaults = AgrAuthoring.Defaults(type, State.Engine.Config, State.Engine.SchemaRoot, Clock);
        form = EditorForm.For(State.Draft, type);
    }

    private void Changed(AgrDraft draft)
    {
        State.Draft = draft;
        Rebuild();

        debounce?.Dispose();
        debounce = new Timer(DebounceMs) { AutoReset = false };
        debounce.Elapsed += (_, _) => InvokeAsync(() => { Settle(); StateHasChanged(); });
        debounce.Start();
    }

    private void ModeChanged(string mode) => Changed(State.Draft! with { Mode = mode });

    // Write, then validate. Both are attempted every time; neither is allowed to fail
    // silently, because a rail that has stopped updating and a file that has stopped
    // saving both look exactly like everything being fine.
    private void Settle()
    {
        Save();
        Validate();
    }

    private void Save()
    {
        if (State.DraftPath is null || State.Draft is null) return;

        try
        {
            // Temp file then move, so an interrupted write cannot truncate the draft that
            // is already on disk.
            var temp = State.DraftPath + ".tmp";
            DraftFile.Write(temp, State.Draft);
            File.Move(temp, State.DraftPath, overwrite: true);
            saveError = null;
        }
        catch (Exception ex)
        {
            saveError = ex.Message;
        }
    }

    private void Validate()
    {
        if (State.Draft is null) return;

        try
        {
            var result = AgrAuthoring.ToDocument(
                State.Draft, State.Engine.Config, State.Engine.SchemaRoot, Clock);

            if (result.Document is null)
            {
                rail = EditorRail.FromGaps(result.Gaps);
                return;
            }

            var registryCsv = State.RegistryCsvPath is not null && File.Exists(State.RegistryCsvPath)
                ? File.ReadAllText(State.RegistryCsvPath)
                : null;

            var outcome = new ValidationService(Clock).Validate(
                AgrSerializer.ToJson(result.Document), State.Engine, registryCsv);

            rail = EditorRail.FromChecks(outcome.Report.Checks, outcome.Report.OverallResult);
        }
        catch (Exception ex)
        {
            rail = EditorRail.FromEngineError(ex.Message);
        }
    }

    public void Dispose() => debounce?.Dispose();
}
```

Add to `src/Registrar.App/wwwroot/app.css`:

```css
.editor { grid-template-columns: 18rem 1fr 20rem; }
.editor-header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1rem; }
.editor-header .build-reason { color: var(--org-secondary-gray); }
.editor-header .save-error { color: var(--org-danger); }
.rail ul { list-style: none; padding: 0; }
.rail li { border-left: 3px solid var(--org-border-neutral); padding-left: 0.5rem; margin-bottom: 0.75rem; }
.rail li.failure, .rail li.engineerror { border-left-color: var(--org-danger); }
.rail li .detail { color: var(--org-secondary-gray); font-size: 0.85rem; }
.rail button.rail-item { background: none; border: none; padding: 0; text-align: left; cursor: pointer; }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~ValidationRailComponentTests`
Expected: 5 passed.

Run: `dotnet test Registrar.sln`
Expected: all green, 0 warnings.

- [ ] **Step 5: Run the app and confirm the loop by hand**

Run: `dotnet run --project src/Registrar.App`

Confirm, in order: open an engagement folder; New artifact; pick DS, type a Document ID and an owner; Create; the editor opens with a panel per DS section and a rail full of "has no content yet"; add a paragraph and type into it; the rail shrinks; paste an em dash and the lint line appears immediately; fill every section and Build becomes enabled; switch the mode toggle to Promotion and the rail changes. Close the window, reopen, and confirm the draft is on disk with the content typed.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.App tests/Registrar.App.Tests/ValidationRailComponentTests.cs
git commit -m "feat: live validation rail, mode toggle, and debounced autosave"
```

---

### Task 12: The documentation this unit owes

**Files:**
- Modify: `docs/superpowers/specs/2026-08-01-operator-gui-design.md`, `docs/ROADMAP.md`, `CLAUDE.md`

- [ ] **Step 1: Record the amendment on the parent design**

Under `### Amendments`, replace `None yet.` with:

```markdown
- **2026-08-04, GUI-7.** The New artifact screen had no owning unit: the decomposition
  assigned Engagement to GUI-6, Editor to GUI-7, and Import to GUI-8, leaving creation
  unassigned, so the editor would have shipped able to open drafts that nothing could
  produce. Creation ships in GUI-7. The eight units remain the endpoint.
- **2026-08-04, GUI-7.** The `jsonPath` registry described under Data flows is not
  built. An input's DOM id is derived from its pointer (`FieldPath.ElementId`), so the
  lookup is the derivation and there is no map to populate or keep in step.
```

- [ ] **Step 2: Update the roadmap**

In `docs/ROADMAP.md`, change the GUI-7 row's status cell from `Not started` to `**Done** (PR #NN)`, substituting the PR number once it is opened. Then update the prose line beginning "**GUI-4 and GUI-5 are both delivered.**" to name GUI-7 as delivered and GUI-8 as the remaining GUI unit, since GUI-8 is the only one left and it depends on GUI-4 and GUI-7, both of which are now done.

- [ ] **Step 3: Amend CLAUDE.md for the draft inversion**

In the "Division of labor" section, after the operator paragraph, add:

```markdown
For a GUI-authored artifact the direction reverses: the `.agrdraft` file is the source
of truth and the AGR JSON is a build intermediate regenerated on every build. The
operator's transport role above still describes the Governor-to-CLI path, which is
unchanged. An AGR authored elsewhere can still be opened, which imports it into a draft.
```

- [ ] **Step 4: Verify hygiene and the full suite**

Run: `dotnet test Registrar.sln`
Expected: all green, 0 warnings, `SourceHygieneTests` passing.

Confirm no non-ASCII character entered any new file:

```bash
git diff --name-only main... | while read -r f; do
  [ -f "$f" ] && LC_ALL=C grep -n '[^ -~]' "$f" && echo "NON-ASCII in $f"
done
```

Expected: no output.

- [ ] **Step 5: Commit**

```bash
git add docs CLAUDE.md
git commit -m "docs: record GUI-7 amendments and the draft-as-source-of-truth inversion"
```

---

## Definition of done

- `dotnet test Registrar.sln` green, 0 warnings.
- A technician can create a DS from the New artifact screen, author every section, watch the rail empty as they type, and see Build become enabled, all without touching JSON.
- Closing and reopening the app recovers the draft from disk with no loss.
- An artifact type with no defined section schema is never offered for creation.
- A `table` block in an imported AGR is visible in the editor and byte-identical in the draft after unrelated edits.
- The radio minimum, the character policy, the clause library, and every section title come from config; no artifact constant appears in `src/`.
- The parent design carries both GUI-7 amendments, and `CLAUDE.md` carries the draft-as-source-of-truth paragraph.
