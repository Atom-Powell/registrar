# Core Authoring Foundation Implementation Plan (GUI-3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the engine everything a GUI needs to author and build an artifact from memory, with no user interface and no file access in the new code.

**Architecture:** A draft model that reuses the existing AGR tree, a converter that fills derived fields from config and reports what is still missing as jsonPath-addressed gaps, a serializer so an authored document can be schema-validated, and a build pipeline extracted out of the CLI into a new project that can host it. Every new type is a pure function of its inputs.

**Tech Stack:** .NET 8, C#, `System.Text.Json`, xUnit. No new package dependencies. One new project.

## Global Constraints

- **No file access, no console, no exit codes in any new Core, Validation, or Pipeline type.** The CLI keeps path resolution and writing. This is what makes the GUI able to reuse all of it.
- Configuration over code. No artifact type codes, layer names, classification values, or section titles as literals in `src/`. Everything derives from `ConfigBundle` or the AGR schema at runtime. Test files are exempt.
- Determinism: no clock except the injected `IClock`, no culture-sensitive formatting, no unsorted dictionary iteration where output order matters.
- US English, plain ASCII. No emojis, em dashes, en dashes, or smart quotes. A commit hook rejects them.
- `BuildCommandTests` must pass **unchanged** after the pipeline extraction. That is the guard that the extraction was faithful.
- Run `dotnet test` from the repo root. The suite is at **405 passing** before this plan starts.

---

## File Structure

**Create:**
- `src/Registrar.Core/Model/AgrDraft.cs` - the work-in-progress model plus `AgrDraftOverrides`. Records only.
- `src/Registrar.Core/Authoring/AgrAuthoring.cs` - draft to document and back, plus config defaulting.
- `src/Registrar.Core/Authoring/AuthoringResult.cs` - the result type and `AuthoringGap`.
- `src/Registrar.Core/AgrSerializer.cs` - `AgrDocument` to JSON. Lives beside `AgrParser`.
- `src/Registrar.Validation/EngineContext.cs` - config and schema, loaded once.
- `src/Registrar.Pipeline/Registrar.Pipeline.csproj` - new project.
- `src/Registrar.Pipeline/BuildPipeline.cs`
- `src/Registrar.Pipeline/BuildResult.cs`
- `tests/Registrar.Tests/Core/AgrSerializerTests.cs`
- `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs`
- `tests/Registrar.Tests/Pipeline/BuildPipelineTests.cs`

**Modify:**
- `src/Registrar.Validation/ValidationService.cs` - add the `EngineContext` overload; reimplement the path-based one in terms of it.
- `src/Registrar.Validation/IIdRegistry.cs` - add `All()`.
- `src/Registrar.Validation/CsvIdRegistry.cs` - implement it.
- `src/Registrar.Cli/BuildCommand.cs` - becomes a thin wrapper.
- `src/Registrar.Cli/Registrar.Cli.csproj` - reference `Registrar.Pipeline`.
- `Registrar.sln` - add the new project.
- `tests/Registrar.Tests/Registrar.Tests.csproj` - reference `Registrar.Pipeline`.

## One deviation from the spec, decided here

The spec says a technician supplies four things: artifact type, Document ID, client, and engagement. It is five. `context.owner` is required by the schema, is not derivable from config, and has no schema-declared default, unlike `preparedBy` which does. So `Owner` is a field on `AgrDraft` alongside the other four rather than an override, and a null owner produces a gap like any other missing required field. The alternative would be inventing a config key or hardcoding a business string into `src/`, and both are worse.

---

### Task 1: AgrSerializer

The GUI cannot validate anything it authors until an authored document can become JSON text. Schema validation operates on text, and nothing in the repo writes an AGR today.

**Files:**
- Create: `src/Registrar.Core/AgrSerializer.cs`
- Test: `tests/Registrar.Tests/Core/AgrSerializerTests.cs`

**Interfaces:**
- Consumes: `AgrDocument` and its nested records from `Registrar.Core.Model`.
- Produces: `AgrSerializer.ToJson(AgrDocument agr)` returning `string`, and `AgrSerializer.Options` as a `JsonSerializerOptions` for callers that need the same settings. Tasks 3, 4, and 6 use `ToJson`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Core/AgrSerializerTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests.Core;

public class AgrSerializerTests
{
    private static IEnumerable<string> FixturePaths() =>
        Directory.GetFiles(Path.Combine(SemanticTestSupport.RepoRoot(), "fixtures"), "agr-*.json")
            .OrderBy(p => p, StringComparer.Ordinal);

    // The strongest test available for free: every fixture, parsed and written back,
    // must still satisfy the schema. If the serializer emits anything the schema
    // rejects, the GUI can never build an artifact.
    [Fact]
    public void Round_tripped_fixtures_still_satisfy_the_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        var schema = IsolatedSchema.FromText(schemaText);
        var schemaRoot = JsonNode.Parse(schemaText)!;
        var layer = new SchemaValidationLayer();

        var checkedCount = 0;
        foreach (var path in FixturePaths())
        {
            var original = File.ReadAllText(path);

            // Fixtures that are deliberately schema-invalid cannot round trip; skip
            // them by checking they parse first.
            AgrDocument parsed;
            try { parsed = AgrParser.Parse(original); }
            catch { continue; }

            var rewritten = AgrSerializer.ToJson(parsed);
            var checks = layer.Validate(new SchemaValidationInput(rewritten, schema, schemaRoot));

            Assert.False(
                checks.Any(c => c.Result == CheckResultValue.Fail),
                $"{Path.GetFileName(path)} does not satisfy the schema after round trip: "
                    + string.Join("; ", checks.Where(c => c.Result == CheckResultValue.Fail).Select(c => c.Message)));
            checkedCount++;
        }

        Assert.True(checkedCount > 20, $"only {checkedCount} fixtures round tripped; expected the bulk of them");
    }

    // placeholderMap values may legitimately be null, meaning "retained for client
    // completion". WhenWritingNull applies to properties, not dictionary entries, but
    // that is worth proving rather than assuming.
    [Fact]
    public void Null_placeholder_values_survive_serialization()
    {
        var agr = AgrParser.Parse(SemanticTestSupport.FixtureText("agr-sample-ds-draft.json"));
        var withNull = agr with
        {
            DataInputs = agr.DataInputs with
            {
                PlaceholderMap = new Dictionary<string, string?> { ["[DATE]"] = null }
            }
        };

        var json = AgrSerializer.ToJson(withNull);
        var reparsed = AgrParser.Parse(json);

        Assert.True(reparsed.DataInputs.PlaceholderMap.ContainsKey("[DATE]"));
        Assert.Null(reparsed.DataInputs.PlaceholderMap["[DATE]"]);
    }

    [Fact]
    public void Serialization_is_deterministic()
    {
        var agr = AgrParser.Parse(SemanticTestSupport.FixtureText("agr-sample-tcl-draft.json"));

        Assert.Equal(AgrSerializer.ToJson(agr), AgrSerializer.ToJson(agr));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AgrSerializerTests"`
Expected: FAIL, will not compile, `AgrSerializer` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Registrar.Core/AgrSerializer.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Registrar.Core.Model;

namespace Registrar.Core;

// Writes an AgrDocument back to JSON. AgrParser reads; nothing wrote one until the
// GUI needed to validate a document it authored in memory, because schema validation
// operates on text rather than on a parsed object.
//
// Options mirror ReportSerializer exactly so every JSON artifact this engine emits
// looks the same.
public static class AgrSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToJson(AgrDocument agr) => JsonSerializer.Serialize(agr, Options);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AgrSerializerTests"`
Expected: PASS, 3 tests.

If `Round_tripped_fixtures_still_satisfy_the_schema` fails, read the failure message: it names the fixture and the schema violation. The likely cause is a required property being omitted because its value is null. Do not weaken the test; fix the options or the model annotation so the output satisfies the schema.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/AgrSerializer.cs tests/Registrar.Tests/Core/AgrSerializerTests.cs
git commit -m "feat: serialize an AgrDocument back to JSON"
```

---

### Task 2: The draft model and config defaulting

**Files:**
- Create: `src/Registrar.Core/Model/AgrDraft.cs`
- Create: `src/Registrar.Core/Authoring/AgrAuthoring.cs`
- Test: `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs`

**Interfaces:**
- Consumes: `ConfigBundle`, `ArtifactTypeConfig`, `IClock`.
- Produces: `AgrDraft` and `AgrDraftOverrides` records; `AgrAuthoring.Defaults(ArtifactTypeConfig type, ConfigBundle config, IClock clock)` returning `AuthoringDefaults`. Tasks 3 and 4 use all of these.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Authoring;

namespace Registrar.Tests.Authoring;

public class AgrAuthoringTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    }

    [Theory]
    [InlineData("DS", "Client-Facing", "Governance", "2027-02-02")]
    [InlineData("AUP", "Client-Facing", "Governance", "2027-02-02")]
    [InlineData("TCL", "Client-Facing", "Delivery", "2027-08-02")]
    [InlineData("RAF", "Controlled Signoff", "Operational", "2027-02-02")]
    [InlineData("PIG", "Internal Working Document", "Administration", "2027-08-02")]
    public void Defaults_derive_from_config(string code, string classification, string layer, string reviewDate)
    {
        var config = SemanticTestSupport.Config();

        var d = AgrAuthoring.Defaults(config.ArtifactTypesByCode[code], config, new FixedClock());

        Assert.Equal(classification, d.Classification);
        Assert.Equal(layer, d.Layer);
        Assert.Equal(reviewDate, d.ReviewDate);
        Assert.Equal(config.ArtifactTypesByCode[code].AudienceDefault, d.Audience);
    }

    [Fact]
    public void Defaults_use_the_initial_approval_state_from_config()
    {
        var config = SemanticTestSupport.Config();

        var d = AgrAuthoring.Defaults(config.ArtifactTypesByCode["DS"], config, new FixedClock());

        Assert.Equal(config.Vocabulary.InitialState, d.ApprovalStatus);
    }
}
```

The review dates above come from the category intervals in `config/vocabularies.json`: GOV and RSK are 6 months, TRN, OPS, INT, and MST are 12, measured from the fixed clock date of 2026-08-02.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AgrAuthoringTests"`
Expected: FAIL, will not compile.

- [ ] **Step 3: Write the draft model**

Create `src/Registrar.Core/Model/AgrDraft.cs`:

```csharp
namespace Registrar.Core.Model;

// A work-in-progress artifact. Deliberately NOT an AgrDocument: a half-filled artifact
// cannot satisfy the AGR schema, and a model that pretends otherwise poisons everything
// downstream.
//
// It reuses AgrSection and AgrBlock rather than mirroring them, because the sections a
// technician edits are already the right shape and a parallel nullable tree would have
// to be kept in step with the AGR model forever.
//
// Everything derivable is absent by design and computed on conversion: classification,
// layer, audience, review date, package name, output filename, and section titles.
public sealed record AgrDraft
{
    public string? ArtifactType { get; init; }
    public string? DocumentId { get; init; }
    public string? Client { get; init; }
    public string? Engagement { get; init; }

    // Required by the schema, not derivable from config, and unlike preparedBy it has no
    // schema-declared default. So it is a human input like the four above rather than an
    // override.
    public string? Owner { get; init; }

    public string Mode { get; init; } = Registrar.Core.Mode.Draft;

    public IReadOnlyList<AgrSection> Sections { get; init; } = Array.Empty<AgrSection>();
    public IReadOnlyDictionary<string, string?> PlaceholderMap { get; init; }
        = new Dictionary<string, string?>();

    public AgrDraftOverrides? Overrides { get; init; }
}

// The escape hatch. Every field null, the normal case, means "use what config derives".
// It exists so a deliberate departure from a derived value is representable and visible,
// rather than making the derived fields editable in the main model.
public sealed record AgrDraftOverrides
{
    public string? Version { get; init; }
    public string? PreparedBy { get; init; }
    public string? ReviewDate { get; init; }
    public string? ApprovalStatus { get; init; }
    public string? PackageName { get; init; }
    public string? Classification { get; init; }
    public string? Layer { get; init; }
    public string? Audience { get; init; }
    public string? OutputFileName { get; init; }
}
```

- [ ] **Step 4: Write the defaults**

Create `src/Registrar.Core/Authoring/AgrAuthoring.cs`:

```csharp
using System.Globalization;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// Every value the engine can work out for itself, so a technician never has to.
public sealed record AuthoringDefaults(
    string Classification,
    string Layer,
    string Audience,
    string Version,
    string ApprovalStatus,
    string PreparedBy,
    string ReviewDate);

public static partial class AgrAuthoring
{
    private const string FirstVersion = "v1.0";

    public static AuthoringDefaults Defaults(ArtifactTypeConfig type, ConfigBundle config, IClock clock)
    {
        var months = config.ReviewIntervalMonthsByCategory.TryGetValue(type.Category, out var m) ? m : 12;
        var reviewDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)
            .AddMonths(months)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new AuthoringDefaults(
            Classification: type.Classification ?? "",
            Layer: type.Layer ?? "",
            Audience: type.AudienceDefault ?? "",
            Version: FirstVersion,
            ApprovalStatus: config.Vocabulary.InitialState,
            PreparedBy: DefaultPreparedBy,
            ReviewDate: reviewDate);
    }

    // agr.schema.json declares a default for context.preparedBy. Read it from the schema
    // rather than restating it, so the schema stays the single source.
    internal static string DefaultPreparedBy { get; set; } = "";
}
```

**Important:** the `DefaultPreparedBy` above is a placeholder that Step 5 replaces. Do not leave it as a mutable static.

- [ ] **Step 5: Read preparedBy's default from the schema instead**

The schema declares `"default": "ORG Consulting, LLC"` on `context.preparedBy`. Restating it in `src/` would be a business constant in code.

Change `Defaults` to take the schema, and delete the `DefaultPreparedBy` property:

```csharp
    public static AuthoringDefaults Defaults(
        ArtifactTypeConfig type, ConfigBundle config, JsonNode agrSchema, IClock clock)
    {
        var months = config.ReviewIntervalMonthsByCategory.TryGetValue(type.Category, out var m) ? m : 12;
        var reviewDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)
            .AddMonths(months)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var preparedBy = (string?)agrSchema["properties"]!["context"]!["properties"]!
            ["preparedBy"]!["default"] ?? "";

        return new AuthoringDefaults(
            Classification: type.Classification ?? "",
            Layer: type.Layer ?? "",
            Audience: type.AudienceDefault ?? "",
            Version: FirstVersion,
            ApprovalStatus: config.Vocabulary.InitialState,
            PreparedBy: preparedBy,
            ReviewDate: reviewDate);
    }
```

Add `using System.Text.Json.Nodes;` to the file.

Then update the tests to pass the schema. Add this helper to `AgrAuthoringTests` and pass `Schema()` as the third argument in both tests:

```csharp
    private static JsonNode Schema()
    {
        var path = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return JsonNode.Parse(File.ReadAllText(path))!;
    }
```

Add one more test asserting the schema default is actually picked up:

```csharp
    [Fact]
    public void Prepared_by_default_comes_from_the_schema()
    {
        var config = SemanticTestSupport.Config();

        var d = AgrAuthoring.Defaults(config.ArtifactTypesByCode["DS"], config, Schema(), new FixedClock());

        Assert.Equal("ORG Consulting, LLC", d.PreparedBy);
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AgrAuthoringTests"`
Expected: PASS, 7 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/Model/AgrDraft.cs src/Registrar.Core/Authoring tests/Registrar.Tests/Authoring
git commit -m "feat: draft model and config-derived authoring defaults"
```

---

### Task 3: Draft to document, with gaps

**Files:**
- Create: `src/Registrar.Core/Authoring/AuthoringResult.cs`
- Modify: `src/Registrar.Core/Authoring/AgrAuthoring.cs`
- Test: `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs`

**Interfaces:**
- Consumes: `AgrDraft`, `AgrDraftOverrides`, `AuthoringDefaults` from Task 2.
- Produces: `AuthoringResult(AgrDocument? Document, IReadOnlyList<AuthoringGap> Gaps)` and `AuthoringGap(string JsonPath, string Message)`; `AgrAuthoring.ToDocument(AgrDraft draft, ConfigBundle config, JsonNode agrSchema, IClock clock)` returning `AuthoringResult`. Task 4 and Task 6 use `ToDocument`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs`:

```csharp
    private static AgrDraft MinimalDraft() => new()
    {
        ArtifactType = "DS",
        DocumentId = "ORG-GOV-DS-016",
        Client = "Talewind CPA",
        Engagement = "M365 resident onboarding",
        Owner = "Registry Owner",
        Sections = SemanticTestSupport.Agr("agr-sample-ds-draft.json").DataInputs.Sections,
        PlaceholderMap = SemanticTestSupport.Agr("agr-sample-ds-draft.json").DataInputs.PlaceholderMap
    };

    [Fact]
    public void A_complete_draft_converts_and_reports_no_gaps()
    {
        var result = AgrAuthoring.ToDocument(MinimalDraft(), SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Empty(result.Gaps);
        Assert.NotNull(result.Document);
        Assert.Equal("DS", result.Document!.Identity.ArtifactType);
        Assert.Equal("Client-Facing", result.Document.Identity.Classification);
        Assert.Equal("Governance", result.Document.Identity.Layer);
    }

    [Theory]
    [InlineData(nameof(AgrDraft.ArtifactType), "/artifactIdentity/artifactType")]
    [InlineData(nameof(AgrDraft.DocumentId), "/artifactIdentity/documentId")]
    [InlineData(nameof(AgrDraft.Client), "/context/client")]
    [InlineData(nameof(AgrDraft.Engagement), "/context/engagement")]
    [InlineData(nameof(AgrDraft.Owner), "/context/owner")]
    public void A_missing_required_field_produces_a_gap_at_its_json_path(string field, string jsonPath)
    {
        var draft = MinimalDraft();
        draft = field switch
        {
            nameof(AgrDraft.ArtifactType) => draft with { ArtifactType = null },
            nameof(AgrDraft.DocumentId) => draft with { DocumentId = null },
            nameof(AgrDraft.Client) => draft with { Client = null },
            nameof(AgrDraft.Engagement) => draft with { Engagement = null },
            _ => draft with { Owner = null }
        };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Null(result.Document);
        Assert.Contains(result.Gaps, g => g.JsonPath == jsonPath);
        Assert.All(result.Gaps, g => Assert.False(string.IsNullOrWhiteSpace(g.Message)));
    }

    [Fact]
    public void An_unknown_artifact_type_is_a_gap_rather_than_a_crash()
    {
        var draft = MinimalDraft() with { ArtifactType = "NOPE" };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Null(result.Document);
        Assert.Contains(result.Gaps, g => g.JsonPath == "/artifactIdentity/artifactType");
    }

    [Fact]
    public void Overrides_win_over_derived_values()
    {
        var draft = MinimalDraft() with
        {
            Overrides = new AgrDraftOverrides { Version = "v2.1", ReviewDate = "2026-12-01" }
        };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        Assert.Equal("v2.1", result.Document!.Identity.Version);
        Assert.Equal("2026-12-01", result.Document.Context.ReviewDate);
    }

    [Fact]
    public void Section_titles_come_from_config_not_from_the_draft()
    {
        var config = SemanticTestSupport.Config();
        var wrongTitles = MinimalDraft().Sections
            .Select(s => s with { Title = "Whatever The Technician Typed" }).ToList();
        var draft = MinimalDraft() with { Sections = wrongTitles };

        var result = AgrAuthoring.ToDocument(draft, config, Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        Assert.Equal(
            config.ArtifactTypesByCode["DS"].SectionSchema!.OrderedSections!,
            result.Document!.DataInputs.Sections.Select(s => s.Title));
    }

    [Fact]
    public void The_output_file_name_matches_the_schema_pattern()
    {
        var result = AgrAuthoring.ToDocument(MinimalDraft(), SemanticTestSupport.Config(), Schema(), new FixedClock());

        var pattern = (string)Schema()["properties"]!["renderingOptions"]!["properties"]!
            ["outputFileName"]!["pattern"]!;
        Assert.Matches(pattern, result.Document!.RenderingOptions.OutputFileName);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AgrAuthoringTests"`
Expected: FAIL, `ToDocument` does not exist.

- [ ] **Step 3: Write the result types**

Create `src/Registrar.Core/Authoring/AuthoringResult.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// A field the technician has not filled in yet. Deliberately not a Check: that type
// carries checkId, ruleSource, and errorCategory, none of which apply to an unfinished
// form, and forcing it would blur a governance violation into a blank field.
//
// JsonPath uses the same vocabulary the validation report uses, so a GUI needs one
// click-to-field mechanism rather than two.
public sealed record AuthoringGap(string JsonPath, string Message);

public sealed record AuthoringResult(AgrDocument? Document, IReadOnlyList<AuthoringGap> Gaps);
```

- [ ] **Step 4: Write the conversion**

Add to `src/Registrar.Core/Authoring/AgrAuthoring.cs`:

```csharp
    public static AuthoringResult ToDocument(
        AgrDraft draft, ConfigBundle config, JsonNode agrSchema, IClock clock)
    {
        var gaps = new List<AuthoringGap>();

        void Require(string? value, string jsonPath, string message)
        {
            if (string.IsNullOrWhiteSpace(value)) gaps.Add(new AuthoringGap(jsonPath, message));
        }

        Require(draft.ArtifactType, "/artifactIdentity/artifactType", "Choose an artifact type.");
        Require(draft.DocumentId, "/artifactIdentity/documentId",
            "A Document ID must be issued by Registry Owner before this can be built.");
        Require(draft.Client, "/context/client", "Client legal name is required.");
        Require(draft.Engagement, "/context/engagement", "Engagement is required.");
        Require(draft.Owner, "/context/owner", "Owner is required, for example the accountable team.");

        ArtifactTypeConfig? type = null;
        if (!string.IsNullOrWhiteSpace(draft.ArtifactType)
            && !config.ArtifactTypesByCode.TryGetValue(draft.ArtifactType, out type))
            gaps.Add(new AuthoringGap("/artifactIdentity/artifactType",
                $"'{draft.ArtifactType}' is not an artifact type this engine knows."));

        if (type is not null && type.SectionSchema?.OrderedSections is null)
            gaps.Add(new AuthoringGap("/dataInputs/sections",
                $"'{draft.ArtifactType}' has no approved section structure, so it cannot be generated."));

        if (gaps.Count > 0 || type is null)
            return new AuthoringResult(null, gaps);

        var d = Defaults(type, config, agrSchema, clock);
        var o = draft.Overrides;

        var version = o?.Version ?? d.Version;
        var titles = type.SectionSchema!.OrderedSections!;

        // Section titles are config's to decide, never the draft's. The technician edits
        // the blocks inside a section; the title is fixed by the artifact type, and
        // QA-ENG-02 rejects any deviation. Positions beyond the schema are dropped and
        // missing ones become empty sections the technician still has to fill.
        var sections = titles.Select((title, i) => new AgrSection
        {
            Title = title,
            DescriptorLine = i < draft.Sections.Count ? draft.Sections[i].DescriptorLine : null,
            Blocks = i < draft.Sections.Count ? draft.Sections[i].Blocks : Array.Empty<AgrBlock>()
        }).ToList();

        var document = new AgrDocument
        {
            Identity = new AgrIdentity
            {
                ArtifactType = draft.ArtifactType!,
                DocumentId = draft.DocumentId!,
                Version = version,
                Classification = o?.Classification ?? d.Classification,
                ApprovalStatus = o?.ApprovalStatus ?? d.ApprovalStatus,
                Layer = o?.Layer ?? d.Layer
            },
            Context = new AgrContext
            {
                Owner = draft.Owner!,
                Audience = o?.Audience ?? d.Audience,
                PreparedBy = o?.PreparedBy ?? d.PreparedBy,
                Client = draft.Client,
                Engagement = draft.Engagement,
                ReviewDate = o?.ReviewDate ?? d.ReviewDate
            },
            DataInputs = new AgrDataInputs
            {
                PlaceholderMap = draft.PlaceholderMap,
                Sections = sections
            },
            ValidationPolicy = new AgrValidationPolicy { Mode = draft.Mode },
            RenderingOptions = new AgrRenderingOptions
            {
                PackageName = o?.PackageName ?? draft.Engagement!,
                OutputFileName = o?.OutputFileName
                    ?? $"ORG - {type.Name} - {draft.Client} - {version}.docx"
            }
        };

        return new AuthoringResult(document, Array.Empty<AuthoringGap>());
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~AgrAuthoringTests"`
Expected: PASS, 17 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Core/Authoring tests/Registrar.Tests/Authoring
git commit -m "feat: convert a draft to an AGR, reporting gaps by json path"
```

---

### Task 4: Document to draft, and the round trip

This is the task that proves the draft model does not silently lose data, which is the specific risk of choosing a thin draft over a full mirror.

**Files:**
- Modify: `src/Registrar.Core/Authoring/AgrAuthoring.cs`
- Test: `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2 and 3.
- Produces: `AgrAuthoring.FromDocument(AgrDocument agr)` returning `AgrDraft`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs`:

```csharp
    // The test that catches the draft model quietly dropping a field. Every fixture is
    // parsed, reduced to a draft, and rebuilt. Values that disagree with what config
    // derives must survive through Overrides; if a fixture cannot round trip, the draft
    // model is wrong and the fix is the model, not this test.
    [Fact]
    public void Every_parseable_fixture_round_trips_through_a_draft()
    {
        var config = SemanticTestSupport.Config();
        var schema = Schema();
        var clock = new FixedClock();
        var checkedCount = 0;

        foreach (var path in Directory
                     .GetFiles(Path.Combine(SemanticTestSupport.RepoRoot(), "fixtures"), "agr-*.json")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            AgrDocument original;
            try { original = AgrParser.Parse(File.ReadAllText(path)); }
            catch { continue; }

            // Types with no approved section structure cannot be authored at all, by
            // design (rule 6), so they are out of scope for a round trip.
            if (!config.ArtifactTypesByCode.TryGetValue(original.Identity.ArtifactType, out var type)
                || type.SectionSchema?.OrderedSections is null)
                continue;

            var draft = AgrAuthoring.FromDocument(original);
            var result = AgrAuthoring.ToDocument(draft, config, schema, clock);

            Assert.True(result.Document is not null,
                $"{Path.GetFileName(path)} produced gaps on round trip: "
                    + string.Join("; ", result.Gaps.Select(g => g.JsonPath)));

            var rebuilt = result.Document!;
            var name = Path.GetFileName(path);

            Assert.Equal(original.Identity, rebuilt.Identity);
            Assert.Equal(original.Context.Owner, rebuilt.Context.Owner);
            Assert.Equal(original.Context.Audience, rebuilt.Context.Audience);
            Assert.Equal(original.Context.PreparedBy, rebuilt.Context.PreparedBy);
            Assert.Equal(original.Context.Client, rebuilt.Context.Client);
            Assert.Equal(original.Context.Engagement, rebuilt.Context.Engagement);
            Assert.Equal(original.Context.ReviewDate, rebuilt.Context.ReviewDate);
            Assert.Equal(original.RenderingOptions, rebuilt.RenderingOptions);
            Assert.Equal(original.ValidationPolicy.Mode, rebuilt.ValidationPolicy.Mode);
            Assert.Equal(original.DataInputs.PlaceholderMap, rebuilt.DataInputs.PlaceholderMap);
            Assert.Equal(original.DataInputs.Sections.Count, rebuilt.DataInputs.Sections.Count);
            for (var i = 0; i < original.DataInputs.Sections.Count; i++)
                Assert.True(original.DataInputs.Sections[i] == rebuilt.DataInputs.Sections[i],
                    $"{name} section {i} changed on round trip");

            checkedCount++;
        }

        Assert.True(checkedCount > 20, $"only {checkedCount} fixtures round tripped; expected the bulk of them");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~Every_parseable_fixture_round_trips"`
Expected: FAIL, `FromDocument` does not exist.

- [ ] **Step 3: Write the implementation**

Add to `src/Registrar.Core/Authoring/AgrAuthoring.cs`:

```csharp
    // Reduces a finished AGR back to an editable draft. Every value that ToDocument would
    // otherwise derive is carried in Overrides rather than discarded, because an existing
    // document's stored values are facts about that document and must not be silently
    // recomputed when a technician opens it.
    public static AgrDraft FromDocument(AgrDocument agr) => new()
    {
        ArtifactType = agr.Identity.ArtifactType,
        DocumentId = agr.Identity.DocumentId,
        Client = agr.Context.Client,
        Engagement = agr.Context.Engagement,
        Owner = agr.Context.Owner,
        Mode = agr.ValidationPolicy.Mode,
        Sections = agr.DataInputs.Sections,
        PlaceholderMap = agr.DataInputs.PlaceholderMap,
        Overrides = new AgrDraftOverrides
        {
            Version = agr.Identity.Version,
            Classification = agr.Identity.Classification,
            ApprovalStatus = agr.Identity.ApprovalStatus,
            Layer = agr.Identity.Layer,
            Audience = agr.Context.Audience,
            PreparedBy = agr.Context.PreparedBy,
            ReviewDate = agr.Context.ReviewDate,
            PackageName = agr.RenderingOptions.PackageName,
            OutputFileName = agr.RenderingOptions.OutputFileName
        }
    };
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~AgrAuthoringTests"`
Expected: PASS, 18 tests.

If a fixture fails the round trip, read which assertion fired. A section mismatch means `ToDocument`'s section rebuilding drops something the draft carried. **Do not weaken the assertion.** Either the draft model needs the missing field or `ToDocument` needs to preserve it.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS, 426 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Core/Authoring tests/Registrar.Tests/Authoring
git commit -m "feat: reduce an AGR back to a draft, with a round-trip guard"
```

---

### Task 5: EngineContext, the cached-config overload, and registry enumeration

`ValidationService.Validate` re-reads the schema from disk and re-runs `ConfigLoader.Load` on every call. That is fine at one call per build and not fine at one call per keystroke, which is what the editor screen will do.

**Files:**
- Create: `src/Registrar.Validation/EngineContext.cs`
- Modify: `src/Registrar.Validation/ValidationService.cs`
- Modify: `src/Registrar.Validation/IIdRegistry.cs`
- Modify: `src/Registrar.Validation/CsvIdRegistry.cs`
- Test: `tests/Registrar.Tests/ValidationServiceTests.cs`, `tests/Registrar.Tests/CsvIdRegistryTests.cs`

**Interfaces:**
- Consumes: `ConfigBundle`, `IsolatedSchema`.
- Produces: `EngineContext.Load(string repoRoot)` and `EngineContext(ConfigBundle Config, JsonSchema Schema, JsonNode SchemaRoot, string SchemaHash)`; `ValidationService.Validate(string agrText, EngineContext engine, string? registryCsvText = null)`; `IIdRegistry.All()` returning `IReadOnlyCollection<RegistryRecord>`. Task 6 uses `EngineContext` and the overload.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Registrar.Tests/ValidationServiceTests.cs`:

```csharp
    // The overload exists so the editor can validate per keystroke without re-reading the
    // schema and re-parsing seven config files each time. It must produce exactly what
    // the path-based call produces, or the GUI and the CLI would disagree.
    [Fact]
    public void The_cached_overload_produces_the_same_report_as_the_path_based_call()
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var registryCsv = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "registry-sample.csv"));
        var engine = EngineContext.Load(repoRoot);
        var clock = new FixedClock();

        foreach (var path in Directory
                     .GetFiles(Path.Combine(repoRoot, "fixtures"), "agr-*.json")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(path);
            var viaPath = new ValidationService(clock).Validate(text, schemaPath, registryCsv);
            var viaContext = new ValidationService(clock).Validate(text, engine, registryCsv);

            Assert.Equal(
                ReportSerializer.ToJson(viaPath.Report),
                ReportSerializer.ToJson(viaContext.Report));
        }
    }
```

`ValidationServiceTests` already has a `FixedClock`; reuse it. If it does not, copy the one from `QaCatalogConsistencyTests`.

Append to `tests/Registrar.Tests/CsvIdRegistryTests.cs`:

```csharp
    [Fact]
    public void All_returns_every_row_in_the_export()
    {
        var csv = "documentId,artifactType,classification,approvalStatus,version\n"
            + "ORG-GOV-DS-016,DS,Client-Facing,Draft,v1.0\n"
            + "ORG-TRN-TCL-004,TCL,Client-Facing,Draft,v1.0\n";

        var all = CsvIdRegistry.FromCsvText(csv).All();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, r => r.DocumentId == "ORG-GOV-DS-016");
        Assert.Contains(all, r => r.DocumentId == "ORG-TRN-TCL-004");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ValidationServiceTests|FullyQualifiedName~CsvIdRegistryTests"`
Expected: FAIL, `EngineContext` and `All` do not exist.

- [ ] **Step 3: Create EngineContext**

Create `src/Registrar.Validation/EngineContext.cs`:

```csharp
using System.Text.Json.Nodes;
using Json.Schema;
using Registrar.Core;
using Registrar.Core.Config;

namespace Registrar.Validation;

// Config and schema, loaded once and reused. Lives in Validation rather than Pipeline
// because it holds the compiled schema, which is a Validation concern, and
// ValidationService consumes it.
public sealed record EngineContext(
    ConfigBundle Config,
    JsonSchema Schema,
    JsonNode SchemaRoot,
    string SchemaHash)
{
    public static EngineContext Load(string repoRoot)
    {
        var schemaPath = ConfigLocator.FindSchema(repoRoot, "agr.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        return new EngineContext(
            ConfigLoader.Load(repoRoot),
            IsolatedSchema.FromText(schemaText),
            JsonNode.Parse(schemaText)!,
            Hashing.Sha256Hex(schemaText));
    }
}
```

- [ ] **Step 4: Add the overload and reimplement the path-based call**

In `src/Registrar.Validation/ValidationService.cs`, replace the body of the existing `Validate(string agrText, string schemaPath, string? registryCsvText = null)` so it builds an `EngineContext` and delegates:

```csharp
    public ValidationOutcome Validate(string agrText, string schemaPath, string? registryCsvText = null)
    {
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        return Validate(agrText, EngineContext.Load(repoRoot), registryCsvText);
    }
```

Then add the real implementation as the new overload, moving everything that used to follow the schema loading into it:

```csharp
    public ValidationOutcome Validate(string agrText, EngineContext engine, string? registryCsvText = null)
    {
        var configVersions = new Dictionary<string, string>
        {
            ["agr.schema.json"] = engine.SchemaHash
        };
        foreach (var (name, hash) in engine.Config.Hashes) configVersions[name] = hash;

        var schemaChecks = _schemaLayer.Validate(
            new SchemaValidationInput(agrText, engine.Schema, engine.SchemaRoot));

        // ... the rest of the existing method body unchanged, using engine.Config
        // wherever it previously used the local `config` variable.
    }
```

Read the existing method and move its body carefully. The only substitutions are `config` becoming `engine.Config`, `schema` becoming `engine.Schema`, `schemaRoot` becoming `engine.SchemaRoot`, and the schema hash coming from `engine.SchemaHash`. Change no logic.

- [ ] **Step 5: Add registry enumeration**

In `src/Registrar.Validation/IIdRegistry.cs`, add to the interface:

```csharp
    // Read-only. Rule 5 stands: the engine never mints an ID. Enumerating what
    // Registry Owner already issued is not minting, and it is what lets an
    // operator pick an ID instead of typing one.
    IReadOnlyCollection<RegistryRecord> All();
```

In `src/Registrar.Validation/CsvIdRegistry.cs`, add beside `Lookup`:

```csharp
    public IReadOnlyCollection<RegistryRecord> All() =>
        _byId.Values.OrderBy(r => r.DocumentId, StringComparer.Ordinal).ToList();
```

If any test double implements `IIdRegistry`, it will now fail to compile. Add `All()` to it returning an empty list or its backing rows, whichever matches what the double is for.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: PASS, 428 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Validation tests/Registrar.Tests
git commit -m "feat: EngineContext, a cached-config validation overload, and registry enumeration"
```

---

### Task 6: Registrar.Pipeline and the BuildCommand extraction

**Files:**
- Create: `src/Registrar.Pipeline/Registrar.Pipeline.csproj`
- Create: `src/Registrar.Pipeline/BuildResult.cs`
- Create: `src/Registrar.Pipeline/BuildPipeline.cs`
- Modify: `Registrar.sln`
- Modify: `src/Registrar.Cli/Registrar.Cli.csproj`
- Modify: `src/Registrar.Cli/BuildCommand.cs`
- Modify: `tests/Registrar.Tests/Registrar.Tests.csproj`
- Test: `tests/Registrar.Tests/Pipeline/BuildPipelineTests.cs`

**Interfaces:**
- Consumes: `EngineContext` from Task 5, `AgrSerializer` from Task 1.
- Produces: `BuildPipeline.Run(...)` returning `BuildResult`.

- [ ] **Step 1: Create the project and wire references**

```bash
cd C:/tmp/registrar-gui3-wt
dotnet new classlib -o src/Registrar.Pipeline -f net8.0
rm src/Registrar.Pipeline/Class1.cs
dotnet add src/Registrar.Pipeline reference src/Registrar.Core src/Registrar.Validation src/Registrar.Rendering
dotnet add src/Registrar.Cli reference src/Registrar.Pipeline
dotnet add tests/Registrar.Tests reference src/Registrar.Pipeline
dotnet sln add src/Registrar.Pipeline
```

Then open `src/Registrar.Pipeline/Registrar.Pipeline.csproj` and match the property block used by `src/Registrar.Core/Registrar.Core.csproj`, in particular `Nullable` and `ImplicitUsings`, so the new project compiles under the same rules as the rest.

Run: `dotnet build`
Expected: succeeds, 0 warnings.

- [ ] **Step 2: Write the result type**

Create `src/Registrar.Pipeline/BuildResult.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Pipeline;

// Everything a build produces, in memory. The caller decides where any of it goes:
// the CLI writes files and maps to exit codes, a GUI keeps the bytes and shows the
// report. Nothing here touches the file system.
public sealed record BuildResult(
    ValidationReport Report,
    PromotionPayload? Payload,
    byte[]? DocxBytes,
    GenerationLog? GenerationLog,
    ArtifactManifest? Manifest,
    bool DocxEligible);
```

Check the actual type names for the generation log and manifest in `src/Registrar.Core/Model/` before writing this, and use whatever they really are.

- [ ] **Step 3: Write the failing test**

Create `tests/Registrar.Tests/Pipeline/BuildPipelineTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Pipeline;
using Registrar.Validation;

namespace Registrar.Tests.Pipeline;

public class BuildPipelineTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
    }

    private static (string Text, AgrDocument Agr, EngineContext Engine, byte[] Logo, string Registry) Inputs(
        string fixtureName)
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var engine = EngineContext.Load(repoRoot);
        var text = SemanticTestSupport.FixtureText(fixtureName);
        var logo = File.ReadAllBytes(
            ConfigLocator.FindAsset(repoRoot, engine.Config.Style.Wordmark.Asset));
        var registry = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "registry-sample.csv"));
        return (text, AgrParser.Parse(text), engine, logo, registry);
    }

    [Fact]
    public void A_passing_fixture_produces_a_document_and_a_manifest()
    {
        var (text, agr, engine, logo, registry) = Inputs("agr-sample-ds-draft.json");

        var result = BuildPipeline.Run(text, agr, engine, registry, logo, new FixedClock());

        Assert.True(result.DocxEligible);
        Assert.NotNull(result.DocxBytes);
        Assert.NotEmpty(result.DocxBytes!);
        Assert.NotNull(result.Manifest);
        Assert.NotNull(result.GenerationLog);
    }

    [Fact]
    public void A_failing_fixture_produces_a_report_and_no_document()
    {
        var (text, agr, engine, logo, registry) = Inputs("agr-sample-ds-layer-mismatch.json");

        var result = BuildPipeline.Run(text, agr, engine, registry, logo, new FixedClock());

        Assert.False(result.DocxEligible);
        Assert.Null(result.DocxBytes);
        Assert.Equal(OverallResult.Fail, result.Report.OverallResult);
    }

    // The pipeline must not touch disk. If it did, a GUI could not use it without a
    // temp directory and every test here would need one.
    [Fact]
    public void The_pipeline_writes_nothing_to_the_fixtures_directory()
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var dir = Path.Combine(repoRoot, "fixtures");
        var before = Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal).ToList();

        var (text, agr, engine, logo, registry) = Inputs("agr-sample-ds-draft.json");
        BuildPipeline.Run(text, agr, engine, registry, logo, new FixedClock());

        Assert.Equal(before, Directory.GetFiles(dir).OrderBy(f => f, StringComparer.Ordinal).ToList());
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BuildPipelineTests"`
Expected: FAIL, `BuildPipeline` does not exist.

- [ ] **Step 5: Write the pipeline by moving BuildCommand's logic**

Create `src/Registrar.Pipeline/BuildPipeline.cs`. Move the body of `BuildCommand.Run` into it, dropping everything that is CLI concern and keeping every ordering decision.

**Drop:** file existence checks, path resolution, `File.ReadAllText`, `File.WriteAllText`, `File.WriteAllBytes`, `CommandSupport.WriteOutputs`, every `stdout`/`stderr` write, and every `return` of an exit code.

**Keep, unchanged:** the pre-render failure short circuit; the `preEligible` calculation; rendering; `PostRenderMerge.Apply`; the `docxEligible` calculation and the fact that it is computed **before** the report is finalised, so `artifactSha256` never attests to a document that was not produced; stamping the hash only when eligible; building the generation log for every run that reached rendering; building the manifest only when a document is produced.

The signature is:

```csharp
public static BuildResult Run(
    string agrText,
    AgrDocument agr,
    EngineContext engine,
    string? registryCsvText,
    byte[] logoBytes,
    IClock clock)
```

Let rendering exceptions propagate. The pipeline does not own error presentation; the CLI catches and maps them to exit code 2 exactly as it does today.

- [ ] **Step 6: Rewire BuildCommand**

Rewrite `src/Registrar.Cli/BuildCommand.cs` so it keeps every one of its current responsibilities and delegates the middle:

1. `CommandSupport.Validate` for path resolution and reading, unchanged.
2. The `--mode` disagreement check, unchanged, still exit code 2.
3. Load the logo, catching the same exception types as today and returning 2 on failure.
4. Call `BuildPipeline.Run`.
5. Write the report and payload with `CommandSupport.WriteOutputs`; write the generation log if present; write the DOCX and manifest if `DocxEligible`.
6. Emit the same stdout messages and return the same exit codes as today.

**`BuildCommandTests` must pass unchanged.** That is the whole guard on this extraction. If a test needs editing, the extraction changed behavior and is wrong.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS, 431 tests, 0 warnings.

- [ ] **Step 8: Verify the extraction was faithful**

Run: `git diff --stat HEAD -- tests/Registrar.Tests/BuildCommandTests.cs`
Expected: no output. If that file changed, the extraction altered observable behavior.

- [ ] **Step 9: Commit**

```bash
git add Registrar.sln src/Registrar.Pipeline src/Registrar.Cli tests/Registrar.Tests
git commit -m "refactor: extract BuildPipeline into its own project"
```

---

## Self-Review

**Spec coverage.** Decision 1, the draft reusing the AGR tree, is Task 2. Decision 2, jsonPath gaps, is Task 3. Decision 3, a pure pipeline, is Task 6. Decision 4, the new project, is Task 6 Step 1. Decision 5, reimplementing the path-based call in terms of the overload, is Task 5 Step 4. The serializer the spec identified as missing is Task 1. `IIdRegistry.All` is Task 5 Step 5. Both named high-value tests are present: schema-valid round trip in Task 1, lossless round trip in Task 4.

**One deviation, stated up front rather than buried.** The spec says four human-supplied fields; it is five, because `context.owner` is schema-required, is not derivable, and has no schema default. The alternative was inventing a config key or hardcoding a business string in `src/`.

**Two things deliberately left approximate, and why that is honest.** Task 5 Step 4 and Task 6 Step 5 both say to move an existing method body rather than quoting the result. `ValidationService.Validate` is roughly sixty lines and `BuildCommand.Run` is a hundred and forty, and reproducing either here would create a second copy that silently diverges from the real one the moment anything else changes. Both steps name the exact substitutions to make, name what must not change, and are guarded by tests: the overload by a report-equality check across every fixture, the extraction by `BuildCommandTests` passing untouched.

**Placeholder scan.** No TBD or TODO. Task 2 Step 4 contains a deliberate placeholder that Step 5 removes, and it is labelled as such in the step itself.

**Type consistency.** `AgrDraft`, `AgrDraftOverrides`, `AuthoringDefaults`, `AuthoringResult`, `AuthoringGap` are defined in Tasks 2 and 3 and used under those names in Tasks 3, 4, and 6. `Defaults` gains its `JsonNode agrSchema` parameter in Task 2 Step 5 and every later call passes it. `EngineContext` is defined in Task 5 and consumed in Task 6. The AGR block record is `AgrBlock`, not `AgrContentBlock` as the parent spec's prose calls it.

**Test count.** 405 before. Task 1 adds 3, Task 2 adds 7, Task 3 adds 10, Task 4 adds 1, Task 5 adds 2, Task 6 adds 3, reaching 431. Adjust if the suite has moved.
