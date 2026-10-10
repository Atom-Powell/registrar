# Slice 2: AGR Semantic Validation Layers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After schema validation passes, run five config-driven semantic layers over the AGR and fold their checks into `validation-report.json`, gated so semantic layers never run on a schema-invalid AGR.

**Architecture:** The existing `SchemaValidationLayer` becomes the gate. On a clean schema pass, `ValidationService` deserializes a typed `AgrDocument`, loads a typed `ConfigBundle`, builds a `SemanticContext`, and runs `SectionSchemaLayer`, `PlaceholderLayer`, `ReviewDateLayer`, `MetadataLayer`, and `ClauseReferenceLayer`. `OverallResultPolicy` computes pass/pass-with-warnings/fail. No renderer, no external registry.

**Tech Stack:** .NET 8, C#, System.Text.Json, JsonSchema.Net (schema layer only), xUnit.

## Global Constraints

- **.NET 8** target (`net8.0`). No `net9.0`/`net10.0` features.
- **Laptop runtime gap:** this machine has only the .NET 10 runtime. Run tests with `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln` (env var only; never add `<RollForward>` to a csproj).
- **Branch first:** do all work on a `slice2-semantic-validation` branch off `main`.
- **Fail closed:** any blocking check failure yields overall `fail` and CLI exit 1.
- **Schema gates semantics:** if schema validation produces any blocking failure, semantic layers do not run and the report carries schema checks only.
- **Zero LLM calls at runtime.** Pure deterministic validation.
- **Configuration over code:** new check IDs are catalog entries in `config/qa-checklist.json`; layers hold only their own `checkId` literal and rule-source string.
- **Report contract:** the emitted report MUST validate against `schemas/validation-report.schema.json`. `errorCategory` values must come from its closed enum; placeholder failures use `MetadataViolation` (there is no `PlaceholderViolation` in the set).
- **Determinism in tests:** time comes from an injected `IClock`; use a fixed instant.
- **Output hygiene:** US English; no emojis, em/en dashes, or decorative unicode in any source, config, or emitted string.
- **Value strings centralized** in `Registrar.Core` `Vocabulary.cs` constant classes (`Layer`, `CheckResultValue`, `Severity`, `ErrorCategory`, `OverallResult`, `Mode`). Never inline these magic strings.

## File map

- `src/Registrar.Core/ConfigLocator.cs` - add `FindConfig` (Task 1).
- `src/Registrar.Core/Config/ConfigModels.cs` - config record types (Task 1).
- `src/Registrar.Core/Config/ConfigBundle.cs` - assembled typed config (Task 1).
- `src/Registrar.Core/Config/ConfigLoader.cs` - loads + hashes config (Task 1).
- `src/Registrar.Core/Model/AgrDocument.cs` - typed AGR DTO (Task 2).
- `src/Registrar.Core/Model/AgrParser.cs` - deserialize AGR text (Task 2).
- `src/Registrar.Validation/IIdRegistry.cs`, `ISemanticLayer.cs` - seam (Task 3).
- `src/Registrar.Validation/SectionSchemaLayer.cs` (Task 3), `PlaceholderLayer.cs` (Task 4), `ReviewDateLayer.cs` (Task 5), `MetadataLayer.cs` (Task 6), `ClauseReferenceLayer.cs` (Task 7).
- `src/Registrar.Validation/OverallResultPolicy.cs` (Task 8).
- `src/Registrar.Validation/ValidationService.cs` - orchestration (Task 9).
- `src/Registrar.Cli/ValidateCommand.cs` - warn count (Task 9).
- `config/qa-checklist.json` - QA-ENG-03 (Task 6), QA-ENG-04 (Task 7).
- `fixtures/` - upgraded golden + five invalid fixtures (Tasks 3-7).
- `tests/Registrar.Tests/SemanticTestSupport.cs` - shared harness (Task 3).

---

### Task 1: Config bundle loader

**Files:**
- Modify: `src/Registrar.Core/ConfigLocator.cs`
- Create: `src/Registrar.Core/Config/ConfigModels.cs`
- Create: `src/Registrar.Core/Config/ConfigBundle.cs`
- Create: `src/Registrar.Core/Config/ConfigLoader.cs`
- Test: `tests/Registrar.Tests/Config/ConfigLoaderTests.cs`
- Modify: `tests/Registrar.Tests/Core/ConfigLocatorTests.cs`

**Interfaces:**
- Consumes: `Hashing`, `ConfigLocator` (existing).
- Produces:
  - `ConfigLocator.FindConfig(string baseDir, string fileName)` -> absolute path to `config/<fileName>`.
  - `ConfigBundle` with `IReadOnlyDictionary<string,ArtifactTypeConfig> ArtifactTypesByCode`, `IReadOnlyDictionary<string,int> ReviewIntervalMonthsByCategory`, `IReadOnlySet<string> ClauseIds`, `IReadOnlyList<CanonicalToken> CanonicalTokens`, `IReadOnlyDictionary<string,string> Hashes`.
  - `ArtifactTypeConfig { string Code; string Category; SectionSchemaConfig? SectionSchema; IReadOnlyList<string>? RequiredPlaceholders; IReadOnlyList<string>? RequiredClauses; }`
  - `SectionSchemaConfig { string Status; IReadOnlyList<string>? OrderedSections; }`
  - `CanonicalToken { string Token; IReadOnlyList<string> KnownDriftAliases; }`
  - `ConfigLoader.Load(string searchBaseDir) -> ConfigBundle`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Config/ConfigLoaderTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;

namespace Registrar.Tests.Config;

public class ConfigLoaderTests
{
    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    [Fact]
    public void Load_indexes_artifact_types_by_code()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.True(bundle.ArtifactTypesByCode.ContainsKey("DS"));
        var ds = bundle.ArtifactTypesByCode["DS"];
        Assert.Equal("GOV", ds.Category);
        Assert.Equal("defined", ds.SectionSchema!.Status);
        Assert.Equal("Purpose", ds.SectionSchema.OrderedSections![0]);
        Assert.Contains("CL-006", ds.RequiredClauses!);
    }

    [Fact]
    public void Load_parses_review_intervals_skipping_comment_keys()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Equal(6, bundle.ReviewIntervalMonthsByCategory["GOV"]);
        Assert.Equal(12, bundle.ReviewIntervalMonthsByCategory["TRN"]);
        Assert.DoesNotContain("$comment", bundle.ReviewIntervalMonthsByCategory.Keys);
    }

    [Fact]
    public void Load_collects_clause_ids_and_canonical_tokens_and_hashes()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Contains("CL-006", bundle.ClauseIds);
        Assert.Contains(bundle.CanonicalTokens, t => t.Token == "[CLIENT ORGANIZATION NAME]");
        Assert.Contains(bundle.CanonicalTokens,
            t => t.Token == "[CLIENT ORGANIZATION NAME]" && t.KnownDriftAliases.Contains("[CLIENT]"));
        Assert.True(bundle.Hashes.ContainsKey("artifact-types.json"));
        Assert.Equal(4, bundle.Hashes.Count);
    }
}
```

Add to `tests/Registrar.Tests/Core/ConfigLocatorTests.cs` two facts (inside the existing class):

```csharp
    [Fact]
    public void FindConfig_walks_up_to_directory_containing_schemas_and_config()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(root, "schemas"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "vocabularies.json"), "{}");
        var nested = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;

        var found = ConfigLocator.FindConfig(nested, "vocabularies.json");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "config", "vocabularies.json")),
            Path.GetFullPath(found));
    }

    [Fact]
    public void FindConfig_throws_when_no_repo_root_found()
    {
        var isolated = Directory.CreateTempSubdirectory().FullName;
        Assert.Throws<FileNotFoundException>(
            () => ConfigLocator.FindConfig(isolated, "vocabularies.json"));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigLoaderTests|FullyQualifiedName~ConfigLocatorTests"`
Expected: FAIL - `ConfigLoader`/`ConfigBundle`/`FindConfig` do not exist.

- [ ] **Step 3: Add `FindConfig` and refactor the shared walk in `ConfigLocator.cs`**

Replace the body of `src/Registrar.Core/ConfigLocator.cs` with:

```csharp
namespace Registrar.Core;

public static class ConfigLocator
{
    // Walk up from baseDir until a directory holds both schemas/ and config/,
    // then return the absolute path to <subDir>/<fileName>.
    private static string Find(string baseDir, string subDir, string fileName)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(baseDir));
        while (dir is not null)
        {
            var schemas = Path.Combine(dir.FullName, "schemas");
            var config = Path.Combine(dir.FullName, "config");
            if (Directory.Exists(schemas) && Directory.Exists(config))
            {
                var candidate = Path.Combine(dir.FullName, subDir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate {subDir}/{fileName} by walking up from {baseDir}.");
    }

    public static string FindSchema(string baseDir, string fileName)
        => Find(baseDir, "schemas", fileName);

    public static string FindConfig(string baseDir, string fileName)
        => Find(baseDir, "config", fileName);
}
```

- [ ] **Step 4: Create the config models**

Create `src/Registrar.Core/Config/ConfigModels.cs`:

```csharp
namespace Registrar.Core.Config;

public sealed record ArtifactTypesFile
{
    public required IReadOnlyList<ArtifactTypeConfig> ArtifactTypes { get; init; }
}

public sealed record ArtifactTypeConfig
{
    public required string Code { get; init; }
    public required string Category { get; init; }
    public SectionSchemaConfig? SectionSchema { get; init; }
    public IReadOnlyList<string>? RequiredPlaceholders { get; init; }
    public IReadOnlyList<string>? RequiredClauses { get; init; }
}

public sealed record SectionSchemaConfig
{
    public required string Status { get; init; }
    public IReadOnlyList<string>? OrderedSections { get; init; }
}

public sealed record ClauseLibraryFile
{
    public required IReadOnlyList<ClauseEntry> Clauses { get; init; }
}

public sealed record ClauseEntry
{
    public required string ClauseId { get; init; }
}

public sealed record PlaceholderDictionaryFile
{
    public required IReadOnlyList<CanonicalToken> CanonicalTokens { get; init; }
}

public sealed record CanonicalToken
{
    public required string Token { get; init; }
    public IReadOnlyList<string> KnownDriftAliases { get; init; } = Array.Empty<string>();
}
```

- [ ] **Step 5: Create `ConfigBundle`**

Create `src/Registrar.Core/Config/ConfigBundle.cs`:

```csharp
namespace Registrar.Core.Config;

public sealed record ConfigBundle
{
    public required IReadOnlyDictionary<string, ArtifactTypeConfig> ArtifactTypesByCode { get; init; }
    public required IReadOnlyDictionary<string, int> ReviewIntervalMonthsByCategory { get; init; }
    public required IReadOnlySet<string> ClauseIds { get; init; }
    public required IReadOnlyList<CanonicalToken> CanonicalTokens { get; init; }
    public required IReadOnlyDictionary<string, string> Hashes { get; init; }
}
```

- [ ] **Step 6: Create `ConfigLoader`**

Create `src/Registrar.Core/Config/ConfigLoader.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Registrar.Core.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ConfigBundle Load(string searchBaseDir)
    {
        var artifactTypesText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "artifact-types.json"));
        var vocabulariesText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "vocabularies.json"));
        var clauseLibraryText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "clause-library.json"));
        var placeholderText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "placeholder-dictionary.json"));

        var artifactTypesFile = JsonSerializer.Deserialize<ArtifactTypesFile>(artifactTypesText, Options)!;
        var clauseFile = JsonSerializer.Deserialize<ClauseLibraryFile>(clauseLibraryText, Options)!;
        var placeholderFile = JsonSerializer.Deserialize<PlaceholderDictionaryFile>(placeholderText, Options)!;

        return new ConfigBundle
        {
            ArtifactTypesByCode = artifactTypesFile.ArtifactTypes.ToDictionary(t => t.Code, t => t),
            ReviewIntervalMonthsByCategory = ParseReviewIntervals(vocabulariesText),
            ClauseIds = clauseFile.Clauses.Select(c => c.ClauseId).ToHashSet(),
            CanonicalTokens = placeholderFile.CanonicalTokens,
            Hashes = new Dictionary<string, string>
            {
                ["artifact-types.json"] = Hashing.Sha256Hex(artifactTypesText),
                ["vocabularies.json"] = Hashing.Sha256Hex(vocabulariesText),
                ["clause-library.json"] = Hashing.Sha256Hex(clauseLibraryText),
                ["placeholder-dictionary.json"] = Hashing.Sha256Hex(placeholderText)
            }
        };
    }

    // reviewIntervalMonths mixes a "$comment" string with integer category keys,
    // so a Dictionary<string,int> deserialization would throw. Walk it and keep
    // only the integer entries.
    private static IReadOnlyDictionary<string, int> ParseReviewIntervals(string vocabulariesText)
    {
        var node = JsonNode.Parse(vocabulariesText)!["category"]!["reviewIntervalMonths"]!.AsObject();
        var result = new Dictionary<string, int>();
        foreach (var (key, value) in node)
        {
            if (key.StartsWith('$')) continue;
            if (value is JsonValue v && v.TryGetValue<int>(out var months))
                result[key] = months;
        }
        return result;
    }
}
```

Note: `Hashing` and `ConfigLocator` are in the `Registrar.Core` namespace; `ConfigLoader` is in the nested `Registrar.Core.Config` namespace, which sees `Registrar.Core` via the file's position - if the compiler cannot resolve them, add `using Registrar.Core;`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigLoaderTests|FullyQualifiedName~ConfigLocatorTests"`
Expected: PASS - 5 tests.

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Core/ConfigLocator.cs src/Registrar.Core/Config tests/Registrar.Tests/Config tests/Registrar.Tests/Core/ConfigLocatorTests.cs
git commit -m "feat(core): add config bundle loader and FindConfig locator"
```

---

### Task 2: Typed AGR document and parser

**Files:**
- Create: `src/Registrar.Core/Model/AgrDocument.cs`
- Create: `src/Registrar.Core/Model/AgrParser.cs`
- Test: `tests/Registrar.Tests/Model/AgrParserTests.cs`

**Interfaces:**
- Consumes: nothing beyond System.Text.Json.
- Produces:
  - `AgrDocument { AgrIdentity Identity; AgrContext Context; AgrDataInputs DataInputs; AgrValidationPolicy ValidationPolicy; }`
  - `AgrIdentity { string ArtifactType; string DocumentId; string Version; string Classification; string ApprovalStatus; string Layer; }`
  - `AgrContext { string? ReviewDate; AgrReviewOverride? ReviewDateOverride; }`
  - `AgrReviewOverride { string ApprovedBy; string ApprovalDate; string Justification; }`
  - `AgrDataInputs { IReadOnlyDictionary<string,string?> PlaceholderMap; IReadOnlyList<AgrSection> Sections; }`
  - `AgrSection { string Title; IReadOnlyList<AgrBlock> Blocks; }`
  - `AgrBlock { string BlockType; string? ClauseId; }`
  - `AgrValidationPolicy { string Mode; int? WarningThreshold; }`
  - `AgrParser.Parse(string agrText) -> AgrDocument`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Model/AgrParserTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests.Model;

public class AgrParserTests
{
    private static string GoldenText()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        return File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));
    }

    [Fact]
    public void Parse_maps_identity_and_policy()
    {
        var agr = AgrParser.Parse(GoldenText());

        Assert.Equal("DS", agr.Identity.ArtifactType);
        Assert.Equal("ORG-GOV-DS-016", agr.Identity.DocumentId);
        Assert.Equal("draft", agr.ValidationPolicy.Mode);
    }

    [Fact]
    public void Parse_maps_placeholder_map_and_sections()
    {
        var agr = AgrParser.Parse(GoldenText());

        Assert.True(agr.DataInputs.PlaceholderMap.ContainsKey("[TOOL / CONNECTOR NAME]"));
        Assert.NotEmpty(agr.DataInputs.Sections);
        Assert.All(agr.DataInputs.Sections, s => Assert.NotEmpty(s.Blocks));
    }
}
```

Note: this test reads the current (Slice 1) minimal `agr-sample-ds-draft.json`, which already carries every schema-required field, so parsing succeeds before the Task 3 fixture upgrade.

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~AgrParserTests"`
Expected: FAIL - `AgrDocument`/`AgrParser` do not exist.

- [ ] **Step 3: Create `AgrDocument`**

Create `src/Registrar.Core/Model/AgrDocument.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record AgrDocument
{
    [JsonPropertyName("artifactIdentity")] public required AgrIdentity Identity { get; init; }
    [JsonPropertyName("context")]          public required AgrContext Context { get; init; }
    [JsonPropertyName("dataInputs")]       public required AgrDataInputs DataInputs { get; init; }
    [JsonPropertyName("validationPolicy")] public required AgrValidationPolicy ValidationPolicy { get; init; }
}

public sealed record AgrIdentity
{
    [JsonPropertyName("artifactType")]   public required string ArtifactType { get; init; }
    [JsonPropertyName("documentId")]     public required string DocumentId { get; init; }
    [JsonPropertyName("version")]        public required string Version { get; init; }
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("approvalStatus")] public required string ApprovalStatus { get; init; }
    [JsonPropertyName("layer")]          public required string Layer { get; init; }
}

public sealed record AgrContext
{
    [JsonPropertyName("reviewDate")]         public string? ReviewDate { get; init; }
    [JsonPropertyName("reviewDateOverride")] public AgrReviewOverride? ReviewDateOverride { get; init; }
}

public sealed record AgrReviewOverride
{
    [JsonPropertyName("approvedBy")]   public required string ApprovedBy { get; init; }
    [JsonPropertyName("approvalDate")] public required string ApprovalDate { get; init; }
    [JsonPropertyName("justification")] public required string Justification { get; init; }
}

public sealed record AgrDataInputs
{
    [JsonPropertyName("placeholderMap")] public required IReadOnlyDictionary<string, string?> PlaceholderMap { get; init; }
    [JsonPropertyName("sections")]       public required IReadOnlyList<AgrSection> Sections { get; init; }
}

public sealed record AgrSection
{
    [JsonPropertyName("title")]  public required string Title { get; init; }
    [JsonPropertyName("blocks")] public required IReadOnlyList<AgrBlock> Blocks { get; init; }
}

public sealed record AgrBlock
{
    [JsonPropertyName("blockType")] public required string BlockType { get; init; }
    [JsonPropertyName("clauseId")]  public string? ClauseId { get; init; }
}

public sealed record AgrValidationPolicy
{
    [JsonPropertyName("mode")]             public required string Mode { get; init; }
    [JsonPropertyName("warningThreshold")] public int? WarningThreshold { get; init; }
}
```

- [ ] **Step 4: Create `AgrParser`**

Create `src/Registrar.Core/Model/AgrParser.cs`:

```csharp
using System.Text.Json;

namespace Registrar.Core.Model;

public static class AgrParser
{
    // Call only after schema validation has passed: the schema guarantees every
    // required member is present, so deserialization of required members cannot
    // throw for missing fields. Unmapped AGR fields are ignored by design.
    public static AgrDocument Parse(string agrText)
        => JsonSerializer.Deserialize<AgrDocument>(agrText)!;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~AgrParserTests"`
Expected: PASS - 2 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Core/Model/AgrDocument.cs src/Registrar.Core/Model/AgrParser.cs tests/Registrar.Tests/Model
git commit -m "feat(core): add typed AgrDocument and parser"
```

---

### Task 3: Semantic seam, golden fixture upgrade, and SectionSchemaLayer

**Files:**
- Create: `src/Registrar.Validation/IIdRegistry.cs`
- Create: `src/Registrar.Validation/ISemanticLayer.cs`
- Create: `src/Registrar.Validation/SectionSchemaLayer.cs`
- Modify: `fixtures/agr-sample-ds-draft.json` (upgrade to fully generation-valid)
- Create: `fixtures/agr-sample-ds-section-mismatch.json`
- Create: `tests/Registrar.Tests/SemanticTestSupport.cs`
- Test: `tests/Registrar.Tests/SectionSchemaLayerTests.cs`

**Interfaces:**
- Consumes: `AgrDocument` (Task 2), `ConfigBundle` (Task 1), `Check`, `Vocabulary` constants.
- Produces:
  - `interface IIdRegistry {}` (empty Slice 2 marker).
  - `record SemanticContext(AgrDocument Agr, ConfigBundle Config, string Mode, IIdRegistry? Registry)`.
  - `interface ISemanticLayer { IReadOnlyList<Check> Validate(SemanticContext context); }`
  - `sealed class SectionSchemaLayer : ISemanticLayer` with `const string CheckId = "QA-ENG-02"`.
  - `SemanticTestSupport` helper (`RepoRoot()`, `Config()`, `FixtureText(name)`, `Agr(name)`, `Context(name, mode)`).

- [ ] **Step 1: Upgrade the golden fixture**

Replace the entire contents of `fixtures/agr-sample-ds-draft.json` with a fully generation-valid DS (all eight ordered sections, all ten required placeholders resolved, clause CL-006 inserted):

```json
{
  "schemaVersion": "1.0.0",
  "artifactIdentity": {
    "artifactType": "DS",
    "documentId": "ORG-GOV-DS-016",
    "version": "v1.0",
    "classification": "Client-Facing",
    "approvalStatus": "Draft",
    "layer": "Governance"
  },
  "context": {
    "client": "Talewind CPA",
    "engagement": "Copilot Chat Pilot 2026",
    "owner": "Registry Owner",
    "audience": "Talewind CPA leadership, tenant admin, ORG delivery lead",
    "preparedBy": "ORG Consulting, LLC",
    "reviewDate": null,
    "reviewDateOverride": null
  },
  "sourceWorkflowContext": {
    "workflowName": "Copilot Readiness Assessment",
    "phase": "Reporting",
    "executionId": "GOV-2026-07-03-001"
  },
  "dataInputs": {
    "placeholderMap": {
      "[CLIENT ORGANIZATION NAME]": "Talewind CPA",
      "[CLIENT TENANT NAME / TENANT ID]": "talewindcpa.onmicrosoft.com",
      "[APPROVED USER GROUP OR PILOT GROUP]": "Copilot Pilot Group",
      "[TOOL / CONNECTOR NAME]": "Microsoft Copilot Chat",
      "[APPROVED PURPOSE]": "Drafting and summarization of non-restricted business content",
      "[OUT OF SCOPE USES]": "Processing regulated client financial data",
      "[DESIGNATED SECURITY OWNER]": "J. Talewind",
      "[ORG SUPPORT CONTACT]": "support@orgconsulting.example",
      "[DATE]": "2026-07-03",
      "[VERSION]": "v1.0"
    },
    "sections": [
      { "title": "Purpose", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "This summary documents the approved pilot deployment posture for Microsoft Copilot Chat." }
      ] },
      { "title": "Deployment Scope", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Scope covers the approved pilot group only." }
      ] },
      { "title": "Access Model", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Access is limited to approved users through approved accounts." }
      ] },
      { "title": "Control Framework", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Controls follow the approved governance baseline." }
      ] },
      { "title": "Audit, Logging, and Discoverability Validation", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Audit posture is subject to tenant validation." },
        { "blockType": "clauseInsertion", "clauseId": "CL-006" }
      ] },
      { "title": "Roles and Responsibilities", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Roles are assigned to the security owner and delivery lead." }
      ] },
      { "title": "Client Acknowledgment", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "The client acknowledges the deployment posture described above." }
      ] },
      { "title": "Version and Change Notes", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "v1.0 initial issue." }
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
    "outputFileName": "ORG - Deployment Summary - Talewind CPA - v1.0.docx",
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

- [ ] **Step 2: Confirm the upgrade keeps Slice 1 green**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln`
Expected: PASS - all existing tests still pass (the upgraded fixture remains schema-valid).

- [ ] **Step 3: Create the section-mismatch fixture**

Create `fixtures/agr-sample-ds-section-mismatch.json` as a copy of the upgraded golden fixture with the first section title changed from `"Purpose"` to `"Overview"` (schema-valid, semantically wrong). Copy the whole file and change only that one title.

- [ ] **Step 4: Write the shared test harness**

Create `tests/Registrar.Tests/SemanticTestSupport.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

internal static class SemanticTestSupport
{
    public static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    public static ConfigBundle Config() => ConfigLoader.Load(RepoRoot());

    public static string FixtureText(string name)
        => File.ReadAllText(Path.Combine(RepoRoot(), "fixtures", name));

    public static AgrDocument Agr(string fixtureName) => AgrParser.Parse(FixtureText(fixtureName));

    public static SemanticContext Context(string fixtureName, string mode)
        => new(Agr(fixtureName), Config(), mode, Registry: null);
}
```

- [ ] **Step 5: Write the failing tests**

Create `tests/Registrar.Tests/SectionSchemaLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class SectionSchemaLayerTests
{
    [Fact]
    public void QA_ENG_02_passes_for_matching_sections()
    {
        var checks = new SectionSchemaLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_02_fails_for_mismatched_sections()
    {
        var checks = new SectionSchemaLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-section-mismatch.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-02", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.ConfigurationError, check.ErrorCategory);
        Assert.Contains("Purpose", check.Expected);
        Assert.Contains("Overview", check.Actual);
    }
}
```

- [ ] **Step 6: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~SectionSchemaLayerTests"`
Expected: FAIL - the seam and layer types do not exist.

- [ ] **Step 7: Create the seam**

Create `src/Registrar.Validation/IIdRegistry.cs`:

```csharp
namespace Registrar.Validation;

// Marker for the Slice 3 ID Registry adapter. Slice 2 has no registry-backed
// checks; SemanticContext carries an IIdRegistry? so those layers slot in later
// without changing the seam. Intentionally empty until Slice 3.
public interface IIdRegistry
{
}
```

Create `src/Registrar.Validation/ISemanticLayer.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed record SemanticContext(
    AgrDocument Agr,
    ConfigBundle Config,
    string Mode,
    IIdRegistry? Registry);

public interface ISemanticLayer
{
    IReadOnlyList<Check> Validate(SemanticContext context);
}
```

- [ ] **Step 8: Implement `SectionSchemaLayer`**

Create `src/Registrar.Validation/SectionSchemaLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class SectionSchemaLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-02";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 6; Arti-Generator Section 14.2";
    private const string DefinedStatus = "defined";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var artifactType = context.Agr.Identity.ArtifactType;

        if (!context.Config.ArtifactTypesByCode.TryGetValue(artifactType, out var type))
            return new[] { Fail(
                $"Artifact type '{artifactType}' is not defined in artifact-types.json.",
                "/artifactIdentity/artifactType",
                "a code present in artifact-types.json", artifactType,
                "Add the artifact type to config/artifact-types.json or correct artifactIdentity.artifactType.") };

        var schema = type.SectionSchema;
        if (schema is null || schema.Status != DefinedStatus || schema.OrderedSections is null)
            return new[] { Fail(
                $"Artifact type '{artifactType}' has no defined section schema; generation is refused.",
                "/dataInputs/sections",
                "an artifact type whose sectionSchema.status is 'defined'",
                $"sectionSchema.status = {schema?.Status ?? "absent"}",
                "Author and approve a section schema for this artifact type before generating it.") };

        var expected = schema.OrderedSections;
        var actual = context.Agr.DataInputs.Sections.Select(s => s.Title).ToList();
        if (expected.SequenceEqual(actual))
            return Array.Empty<Check>();

        return new[] { Fail(
            "Section titles do not match the artifact type's section schema exactly and in order.",
            "/dataInputs/sections",
            string.Join(" | ", expected), string.Join(" | ", actual),
            $"Emit exactly these sections, in order, with no additions, omissions, or reordering: {string.Join(" | ", expected)}.") };
    }

    private static Check Fail(string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ConfigurationError,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~SectionSchemaLayerTests"`
Expected: PASS - 2 tests.

- [ ] **Step 10: Commit**

```bash
git add src/Registrar.Validation/IIdRegistry.cs src/Registrar.Validation/ISemanticLayer.cs src/Registrar.Validation/SectionSchemaLayer.cs fixtures/agr-sample-ds-draft.json fixtures/agr-sample-ds-section-mismatch.json tests/Registrar.Tests/SemanticTestSupport.cs tests/Registrar.Tests/SectionSchemaLayerTests.cs
git commit -m "feat(validation): add semantic seam and section-schema layer (QA-ENG-02)"
```

---

### Task 4: PlaceholderLayer (QA-SPEC-07)

**Files:**
- Create: `src/Registrar.Validation/PlaceholderLayer.cs`
- Create: `fixtures/agr-sample-ds-placeholder-drift.json`
- Test: `tests/Registrar.Tests/PlaceholderLayerTests.cs`

**Interfaces:**
- Consumes: `SemanticContext`, `ConfigBundle.CanonicalTokens`, `ArtifactTypeConfig.RequiredPlaceholders`.
- Produces: `sealed class PlaceholderLayer : ISemanticLayer` with `const string CheckId = "QA-SPEC-07"`. Emits: drift-alias key -> blocking fail (both modes); unknown token key -> warn in draft / fail in promotion; null value on a canonical key -> warn in draft / fail in promotion; missing required placeholder key -> blocking fail (both modes). All `MetadataViolation`, layer `content`.

- [ ] **Step 1: Create the placeholder-drift fixture**

Create `fixtures/agr-sample-ds-placeholder-drift.json` as a copy of `agr-sample-ds-draft.json` with one extra placeholderMap entry added (a known drift alias): add `"[CLIENT]": "Talewind CPA",` as the first entry inside `placeholderMap`. Keep every other entry.

- [ ] **Step 2: Write the failing tests**

Create `tests/Registrar.Tests/PlaceholderLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class PlaceholderLayerTests
{
    [Fact]
    public void QA_SPEC_07_passes_for_fully_resolved_canonical_placeholders()
    {
        var checks = new PlaceholderLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SPEC_07_fails_on_known_drift_alias_in_both_modes()
    {
        var draft = new PlaceholderLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-placeholder-drift.json", Mode.Draft));

        var check = Assert.Single(draft, c => c.Actual == "[CLIENT]");
        Assert.Equal("QA-SPEC-07", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("[CLIENT ORGANIZATION NAME]", check.Expected);
    }

    [Fact]
    public void QA_SPEC_07_warns_on_missing_value_in_draft_and_fails_in_promotion()
    {
        // A canonical token present with a null value.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[DATE]\": \"2026-07-03\"", "\"[DATE]\": null");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);
        var config = SemanticTestSupport.Config();

        var draft = new PlaceholderLayer().Validate(new SemanticContext(agr, config, Mode.Draft, null));
        var promo = new PlaceholderLayer().Validate(new SemanticContext(agr, config, Mode.Promotion, null));

        Assert.Equal(CheckResultValue.Warn, Assert.Single(draft, c => c.Actual == "null").Result);
        Assert.Equal(CheckResultValue.Fail, Assert.Single(promo, c => c.Actual == "null").Result);
    }

    [Fact]
    public void QA_SPEC_07_fails_when_required_placeholder_is_absent()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[VERSION]\": \"v1.0\"", "\"[NONREQUIRED TOKEN]\": \"x\"");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var checks = new PlaceholderLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        Assert.Contains(checks, c => c.Result == CheckResultValue.Fail
            && c.Message.Contains("[VERSION]") && c.Message.Contains("missing"));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~PlaceholderLayerTests"`
Expected: FAIL - `PlaceholderLayer` does not exist.

- [ ] **Step 4: Implement `PlaceholderLayer`**

Create `src/Registrar.Validation/PlaceholderLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class PlaceholderLayer : ISemanticLayer
{
    public const string CheckId = "QA-SPEC-07";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 9 item 7";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var canonical = context.Config.CanonicalTokens.Select(t => t.Token).ToHashSet();
        var aliasToCanonical = new Dictionary<string, string>();
        foreach (var t in context.Config.CanonicalTokens)
            foreach (var alias in t.KnownDriftAliases)
                aliasToCanonical[alias] = t.Token;

        foreach (var (key, value) in context.Agr.DataInputs.PlaceholderMap)
        {
            if (aliasToCanonical.TryGetValue(key, out var canonicalForm))
            {
                checks.Add(Fail(
                    $"Placeholder key '{key}' is a known drift alias.",
                    PointerFor(key), canonicalForm, key,
                    $"Replace the placeholder key '{key}' with the canonical token '{canonicalForm}' in dataInputs.placeholderMap and all section blocks."));
                continue;
            }

            if (!canonical.Contains(key))
            {
                checks.Add(ModeGated(context.Mode,
                    $"Placeholder key '{key}' is not a known canonical token.",
                    PointerFor(key), "a canonical token from placeholder-dictionary.json", key,
                    $"Confirm '{key}' against the canonical Placeholder Dictionary; if it is canonical, add it to config/placeholder-dictionary.json."));
                continue;
            }

            if (value is null)
                checks.Add(ModeGated(context.Mode,
                    $"Placeholder '{key}' is unresolved (null).",
                    PointerFor(key), "a resolved value at promotion", "null",
                    $"Provide a value for '{key}' before promotion, or keep it null only while the artifact remains in Draft."));
        }

        if (context.Config.ArtifactTypesByCode.TryGetValue(context.Agr.Identity.ArtifactType, out var type)
            && type.RequiredPlaceholders is not null)
        {
            foreach (var token in type.RequiredPlaceholders)
                if (!context.Agr.DataInputs.PlaceholderMap.ContainsKey(token))
                    checks.Add(Fail(
                        $"Required placeholder '{token}' is missing from placeholderMap.",
                        "/dataInputs/placeholderMap", $"a placeholderMap entry for '{token}'", "absent",
                        $"Add '{token}' to dataInputs.placeholderMap with a resolved value."));
        }

        return checks;
    }

    // Placeholder tokens contain '/', which must be escaped in a JSON Pointer.
    private static string PointerFor(string token)
        => "/dataInputs/placeholderMap/" + token.Replace("~", "~0").Replace("/", "~1");

    private static Check Fail(string message, string jsonPath, string expected, string actual, string remediation)
        => Build(CheckResultValue.Fail, Severity.Blocking, message, jsonPath, expected, actual, remediation);

    private static Check ModeGated(string mode, string message, string jsonPath, string expected, string actual, string remediation)
    {
        var promotion = mode == Mode.Promotion;
        return Build(
            promotion ? CheckResultValue.Fail : CheckResultValue.Warn,
            promotion ? Severity.Blocking : Severity.Warning,
            message, jsonPath, expected, actual, remediation);
    }

    private static Check Build(string result, string severity, string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = result,
        SeverityLevel = severity,
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

- [ ] **Step 5: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~PlaceholderLayerTests"`
Expected: PASS - 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Validation/PlaceholderLayer.cs fixtures/agr-sample-ds-placeholder-drift.json tests/Registrar.Tests/PlaceholderLayerTests.cs
git commit -m "feat(validation): add placeholder resolution/drift layer (QA-SPEC-07)"
```

---

### Task 5: ReviewDateLayer (QA-SF-05)

**Files:**
- Create: `src/Registrar.Validation/ReviewDateLayer.cs`
- Create: `fixtures/agr-sample-ds-review-stale.json`
- Test: `tests/Registrar.Tests/ReviewDateLayerTests.cs`

**Interfaces:**
- Consumes: `SemanticContext`, `IClock`, `ConfigBundle.ReviewIntervalMonthsByCategory`.
- Produces: `sealed class ReviewDateLayer(IClock clock) : ISemanticLayer` with `const string CheckId = "QA-SF-05"`. Null reviewDate: pass in draft, blocking fail in promotion. Non-ISO date: blocking fail. reviewDate beyond `today + categoryInterval` without an override: blocking fail. Override present: pass. All `MetadataViolation`, layer `input`.

- [ ] **Step 1: Create the stale-review fixture**

Create `fixtures/agr-sample-ds-review-stale.json` as a copy of `agr-sample-ds-draft.json` with `"reviewDate": null` changed to `"reviewDate": "2030-01-01"` (well beyond the six-month GOV interval).

- [ ] **Step 2: Write the failing tests**

Create `tests/Registrar.Tests/ReviewDateLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class ReviewDateLayerTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void QA_SF_05_passes_when_review_date_null_in_draft()
    {
        var checks = new ReviewDateLayer(new FixedClock())
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_05_fails_when_review_date_null_in_promotion()
    {
        var checks = new ReviewDateLayer(new FixedClock())
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Promotion));

        var check = Assert.Single(checks);
        Assert.Equal("QA-SF-05", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
    }

    [Fact]
    public void QA_SF_05_fails_when_review_date_exceeds_category_interval()
    {
        var checks = new ReviewDateLayer(new FixedClock())
            .Validate(SemanticTestSupport.Context("agr-sample-ds-review-stale.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal("2030-01-01", check.Actual);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ReviewDateLayerTests"`
Expected: FAIL - `ReviewDateLayer` does not exist.

- [ ] **Step 4: Implement `ReviewDateLayer`**

Create `src/Registrar.Validation/ReviewDateLayer.cs`:

```csharp
using System.Globalization;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class ReviewDateLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-05";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 5";
    private readonly IClock _clock;

    public ReviewDateLayer(IClock clock) => _clock = clock;

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var reviewDate = context.Agr.Context.ReviewDate;

        if (reviewDate is null)
        {
            if (context.Mode == Mode.Promotion)
                return new[] { Fail("Review Date is required at promotion.",
                    "an ISO 8601 date", "null",
                    "Set context.reviewDate to an ISO 8601 date within the category review interval before promotion.") };
            return Array.Empty<Check>();
        }

        if (!DateOnly.TryParseExact(reviewDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return new[] { Fail("Review Date is not a valid ISO 8601 (yyyy-MM-dd) date.",
                "yyyy-MM-dd", reviewDate,
                "Correct context.reviewDate to an ISO 8601 date, for example 2026-12-01.") };

        if (context.Agr.Context.ReviewDateOverride is not null)
            return Array.Empty<Check>();

        var category = CategoryOf(context.Agr.Identity.DocumentId);
        if (category is null ||
            !context.Config.ReviewIntervalMonthsByCategory.TryGetValue(category, out var months))
            return Array.Empty<Check>();

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var maxDate = today.AddMonths(months);
        if (parsed > maxDate)
        {
            var max = maxDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new[] { Fail(
                $"Review Date {reviewDate} exceeds the {category} review interval of {months} months.",
                $"a date on or before {max}", reviewDate,
                $"Set context.reviewDate on or before {max}, or attach a reviewDateOverride approved by the documentation owner.") };
        }

        return Array.Empty<Check>();
    }

    private static string? CategoryOf(string documentId)
    {
        var parts = documentId.Split('-');
        return parts.Length >= 2 ? parts[1] : null;
    }

    private static Check Fail(string message, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = message,
        JsonPath = "/context/reviewDate",
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ReviewDateLayerTests"`
Expected: PASS - 3 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Validation/ReviewDateLayer.cs fixtures/agr-sample-ds-review-stale.json tests/Registrar.Tests/ReviewDateLayerTests.cs
git commit -m "feat(validation): add review-date interval layer (QA-SF-05)"
```

---

### Task 6: MetadataLayer and QA-ENG-03 catalog entry

**Files:**
- Create: `src/Registrar.Validation/MetadataLayer.cs`
- Modify: `config/qa-checklist.json`
- Create: `fixtures/agr-sample-ds-id-code-mismatch.json`
- Test: `tests/Registrar.Tests/MetadataLayerTests.cs`
- Modify: `tests/Registrar.Tests/QaChecklistCatalogTests.cs`

**Interfaces:**
- Consumes: `SemanticContext`, `ArtifactTypeConfig.Category`.
- Produces: `sealed class MetadataLayer : ISemanticLayer` with `const string CheckId = "QA-ENG-03"`. Fails (blocking, `MetadataViolation`, layer `input`) when the documentId code segment differs from `artifactType`, or its category segment differs from the type's configured category.

- [ ] **Step 1: Add the QA-ENG-03 catalog entry**

In `config/qa-checklist.json`, add this object to the `checks` array immediately after the `QA-ENG-02` entry:

```json
    { "checkId": "QA-ENG-03", "layer": "input", "automation": "full", "appliesTo": "all", "rule": "Document ID embedded category and code segments match artifactType and the type's configured category.", "errorCategory": "MetadataViolation", "ruleSource": "ORG-MST-SPEC-001 5.2" },
```

- [ ] **Step 2: Create the id-code-mismatch fixture**

Create `fixtures/agr-sample-ds-id-code-mismatch.json` as a copy of `agr-sample-ds-draft.json` with `"documentId": "ORG-GOV-DS-016"` changed to `"documentId": "ORG-GOV-AUP-016"` (schema-valid pairing, but the code no longer matches `artifactType: "DS"`).

- [ ] **Step 3: Write the failing tests**

Create `tests/Registrar.Tests/MetadataLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class MetadataLayerTests
{
    [Fact]
    public void QA_ENG_03_passes_when_document_id_matches_type()
    {
        var checks = new MetadataLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_03_fails_when_document_id_code_mismatches_artifact_type()
    {
        var checks = new MetadataLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-id-code-mismatch.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-03", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("AUP", check.Actual);
    }
}
```

Add to `tests/Registrar.Tests/QaChecklistCatalogTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void MetadataLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;
        var ids = checklist["checks"]!.AsArray().Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(MetadataLayer.CheckId, ids);
    }
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~MetadataLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: FAIL - `MetadataLayer` does not exist.

- [ ] **Step 5: Implement `MetadataLayer`**

Create `src/Registrar.Validation/MetadataLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class MetadataLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-03";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 5.2";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var id = context.Agr.Identity.DocumentId;
        var artifactType = context.Agr.Identity.ArtifactType;

        // The schema pattern guarantees the ORG-CATEGORY-CODE-NNN shape.
        var parts = id.Split('-');
        if (parts.Length < 4) return checks;
        var idCategory = parts[1];
        var idCode = parts[2];

        if (idCode != artifactType)
            checks.Add(Fail(
                $"Document ID code '{idCode}' does not match artifactType '{artifactType}'.",
                $"a document ID whose code segment is '{artifactType}'", idCode,
                $"Reconcile artifactIdentity.documentId and artifactIdentity.artifactType so the code segment equals '{artifactType}'."));

        if (context.Config.ArtifactTypesByCode.TryGetValue(artifactType, out var type)
            && idCategory != type.Category)
            checks.Add(Fail(
                $"Document ID category '{idCategory}' does not match the '{artifactType}' type category '{type.Category}'.",
                $"category segment '{type.Category}'", idCategory,
                $"Correct artifactIdentity.documentId so its category segment is '{type.Category}'."));

        return checks;
    }

    private static Check Fail(string message, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = message,
        JsonPath = "/artifactIdentity/documentId",
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~MetadataLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS - MetadataLayerTests (2) and the catalog tests.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Validation/MetadataLayer.cs config/qa-checklist.json fixtures/agr-sample-ds-id-code-mismatch.json tests/Registrar.Tests/MetadataLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat(validation): add cross-field metadata layer (QA-ENG-03)"
```

---

### Task 7: ClauseReferenceLayer and QA-ENG-04 catalog entry

**Files:**
- Create: `src/Registrar.Validation/ClauseReferenceLayer.cs`
- Modify: `config/qa-checklist.json`
- Create: `fixtures/agr-sample-ds-missing-clause.json`
- Test: `tests/Registrar.Tests/ClauseReferenceLayerTests.cs`
- Modify: `tests/Registrar.Tests/QaChecklistCatalogTests.cs`

**Interfaces:**
- Consumes: `SemanticContext`, `ConfigBundle.ClauseIds`, `ArtifactTypeConfig.RequiredClauses`.
- Produces: `sealed class ClauseReferenceLayer : ISemanticLayer` with `const string CheckId = "QA-ENG-04"`. Fails (blocking, `ClauseResolutionError`, layer `content`) when a clauseInsertion references an unknown clauseId, or a required clause is not inserted anywhere.

- [ ] **Step 1: Add the QA-ENG-04 catalog entry**

In `config/qa-checklist.json`, add this object to the `checks` array immediately after the `QA-ENG-03` entry:

```json
    { "checkId": "QA-ENG-04", "layer": "content", "automation": "full", "appliesTo": "all", "rule": "Every clauseInsertion references a clauseId defined in clause-library.json, and all requiredClauses for the artifact type are present as clauseInsertion blocks.", "errorCategory": "ClauseResolutionError", "ruleSource": "ORG-MST-SPEC-001 Section 7" },
```

- [ ] **Step 2: Create the missing-clause fixture**

Create `fixtures/agr-sample-ds-missing-clause.json` as a copy of `agr-sample-ds-draft.json` with the CL-006 clause block removed. In the "Audit, Logging, and Discoverability Validation" section, replace the `{ "blockType": "clauseInsertion", "clauseId": "CL-006" }` block with `{ "blockType": "paragraph", "text": "Audit posture is subject to tenant validation only." }` so the section still has a block but no required clause.

- [ ] **Step 3: Write the failing tests**

Create `tests/Registrar.Tests/ClauseReferenceLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class ClauseReferenceLayerTests
{
    [Fact]
    public void QA_ENG_04_passes_when_required_clause_present_and_valid()
    {
        var checks = new ClauseReferenceLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_04_fails_when_required_clause_missing()
    {
        var checks = new ClauseReferenceLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-missing-clause.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-04", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.ClauseResolutionError, check.ErrorCategory);
        Assert.Contains("CL-006", check.Message);
    }

    [Fact]
    public void QA_ENG_04_fails_when_clause_id_unknown()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"CL-006\"", "\"CL-999\"");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var checks = new ClauseReferenceLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        Assert.Contains(checks, c => c.Actual == "CL-999");
    }
}
```

Add to `tests/Registrar.Tests/QaChecklistCatalogTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void ClauseReferenceLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;
        var ids = checklist["checks"]!.AsArray().Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(ClauseReferenceLayer.CheckId, ids);
    }
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ClauseReferenceLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: FAIL - `ClauseReferenceLayer` does not exist.

- [ ] **Step 5: Implement `ClauseReferenceLayer`**

Create `src/Registrar.Validation/ClauseReferenceLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class ClauseReferenceLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-04";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 7";
    private const string ClauseInsertion = "clauseInsertion";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var inserted = new HashSet<string>();
        var sections = context.Agr.DataInputs.Sections;

        for (var s = 0; s < sections.Count; s++)
        {
            var blocks = sections[s].Blocks;
            for (var b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                if (block.BlockType != ClauseInsertion || block.ClauseId is null) continue;
                inserted.Add(block.ClauseId);
                if (!context.Config.ClauseIds.Contains(block.ClauseId))
                    checks.Add(Fail(
                        $"Clause '{block.ClauseId}' does not exist in the clause library.",
                        $"/dataInputs/sections/{s}/blocks/{b}/clauseId",
                        "a clauseId present in clause-library.json", block.ClauseId,
                        $"Reference an existing clause from config/clause-library.json; '{block.ClauseId}' is not defined."));
            }
        }

        if (context.Config.ArtifactTypesByCode.TryGetValue(context.Agr.Identity.ArtifactType, out var type)
            && type.RequiredClauses is not null)
        {
            foreach (var clauseId in type.RequiredClauses)
                if (!inserted.Contains(clauseId))
                    checks.Add(Fail(
                        $"Required clause '{clauseId}' is not inserted anywhere in the artifact.",
                        "/dataInputs/sections",
                        $"a clauseInsertion block referencing '{clauseId}'", "absent",
                        $"Add a clauseInsertion block with clauseId '{clauseId}' to the appropriate section."));
        }

        return checks;
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

- [ ] **Step 6: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ClauseReferenceLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS - ClauseReferenceLayerTests (3) and the catalog tests.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Validation/ClauseReferenceLayer.cs config/qa-checklist.json fixtures/agr-sample-ds-missing-clause.json tests/Registrar.Tests/ClauseReferenceLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat(validation): add clause-reference layer (QA-ENG-04)"
```

---

### Task 8: OverallResultPolicy

**Files:**
- Create: `src/Registrar.Validation/OverallResultPolicy.cs`
- Test: `tests/Registrar.Tests/OverallResultPolicyTests.cs`

**Interfaces:**
- Consumes: `Check`, `Vocabulary` constants.
- Produces: `static class OverallResultPolicy { string Compute(IReadOnlyList<Check> checks, int? warningThreshold); }` returning `fail` if any blocking fail or the warn count exceeds `warningThreshold`; else `pass-with-warnings` if any warn; else `pass`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/OverallResultPolicyTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class OverallResultPolicyTests
{
    private static Check Make(string result, string severity) => new()
    {
        CheckId = "X", LayerName = Layer.Content, Result = result, SeverityLevel = severity,
        Message = "m"
    };

    [Fact]
    public void All_pass_yields_pass()
    {
        var checks = new[] { Make(CheckResultValue.Pass, Severity.Info) };
        Assert.Equal(OverallResult.Pass, OverallResultPolicy.Compute(checks, null));
    }

    [Fact]
    public void A_warn_yields_pass_with_warnings()
    {
        var checks = new[] { Make(CheckResultValue.Warn, Severity.Warning) };
        Assert.Equal(OverallResult.PassWithWarnings, OverallResultPolicy.Compute(checks, null));
    }

    [Fact]
    public void A_blocking_fail_yields_fail()
    {
        var checks = new[] { Make(CheckResultValue.Fail, Severity.Blocking) };
        Assert.Equal(OverallResult.Fail, OverallResultPolicy.Compute(checks, null));
    }

    [Fact]
    public void Warn_count_over_threshold_yields_fail()
    {
        var checks = new[]
        {
            Make(CheckResultValue.Warn, Severity.Warning),
            Make(CheckResultValue.Warn, Severity.Warning)
        };
        Assert.Equal(OverallResult.Fail, OverallResultPolicy.Compute(checks, 1));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~OverallResultPolicyTests"`
Expected: FAIL - `OverallResultPolicy` does not exist.

- [ ] **Step 3: Implement `OverallResultPolicy`**

Create `src/Registrar.Validation/OverallResultPolicy.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public static class OverallResultPolicy
{
    public static string Compute(IReadOnlyList<Check> checks, int? warningThreshold)
    {
        var hasBlockingFail = checks.Any(c =>
            c.Result == CheckResultValue.Fail && c.SeverityLevel == Severity.Blocking);
        if (hasBlockingFail) return OverallResult.Fail;

        var warnCount = checks.Count(c => c.Result == CheckResultValue.Warn);
        if (warningThreshold is not null && warnCount > warningThreshold.Value)
            return OverallResult.Fail;

        return warnCount > 0 ? OverallResult.PassWithWarnings : OverallResult.Pass;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~OverallResultPolicyTests"`
Expected: PASS - 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Validation/OverallResultPolicy.cs tests/Registrar.Tests/OverallResultPolicyTests.cs
git commit -m "feat(validation): add overall-result policy with pass-with-warnings"
```

---

### Task 9: Wire semantic layers into ValidationService, CLI warn count, and end-to-end acceptance

**Files:**
- Modify: `src/Registrar.Validation/ValidationService.cs`
- Modify: `src/Registrar.Cli/ValidateCommand.cs`
- Test: `tests/Registrar.Tests/ValidationServiceTests.cs`
- Test: `tests/Registrar.Tests/ValidateCommandTests.cs`

**Interfaces:**
- Consumes: `ConfigLoader`, `AgrParser`, all five semantic layers, `OverallResultPolicy`, existing `SchemaValidationLayer`/`SchemaValidationInput`, `IsolatedSchema`.
- Produces: unchanged `ValidationService.Validate(string agrText, string schemaPath)` signature and `ValidateCommand.Run` signature; richer report and summary line.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Registrar.Tests/ValidationServiceTests.cs` (inside the existing class; it already has `FixedClock` and `Paths()`):

```csharp
    [Fact]
    public void Semantic_failure_makes_report_fail_and_includes_semantic_check()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-section-mismatch.json"));

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath);

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.Contains(report.Checks, c => c.CheckId == "QA-ENG-02");
    }

    [Fact]
    public void Schema_failure_gates_out_semantic_layers()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-invalid-schema.json"));

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath);

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.All(report.Checks, c => Assert.Equal("QA-SCHEMA-01", c.CheckId));
    }

    [Fact]
    public void Config_hashes_are_recorded_in_config_versions()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath);

        Assert.Equal(OverallResult.Pass, report.OverallResult);
        Assert.True(report.ConfigVersions.ContainsKey("artifact-types.json"));
        Assert.True(report.ConfigVersions.ContainsKey("clause-library.json"));
    }
```

Add to `tests/Registrar.Tests/ValidateCommandTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void Semantic_invalid_agr_exits_1_with_semantic_check()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-section-mismatch.json");
        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var reportPath = Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json");
        var report = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
        Assert.Equal("fail", (string?)report["overallResult"]);
        Assert.Contains(report["checks"]!.AsArray(), c => (string?)c!["checkId"] == "QA-ENG-02");
    }
```

Note: `agr-sample-ds-section-mismatch.json` must be copied alongside the other fixtures. `CopyToTemp` copies a single file to a temp dir; because `ConfigLocator` walks up to the repo root to find `schemas/` and `config/`, validation still resolves the real config from the repo, not the temp dir.

- [ ] **Step 2: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidationServiceTests|FullyQualifiedName~ValidateCommandTests"`
Expected: FAIL - the new assertions fail (semantic layers not yet wired; e.g. section mismatch currently reports `pass`).

- [ ] **Step 3: Rewrite `ValidationService` to gate and run semantic layers**

Replace the contents of `src/Registrar.Validation/ValidationService.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class ValidationService
{
    private readonly IClock _clock;
    private readonly IValidationLayer _schemaLayer;

    public ValidationService(IClock clock)
    {
        _clock = clock;
        _schemaLayer = new SchemaValidationLayer();
    }

    public ValidationReport Validate(string agrText, string schemaPath)
    {
        var schemaText = File.ReadAllText(schemaPath);
        var schema = IsolatedSchema.FromText(schemaText);
        var schemaRoot = JsonNode.Parse(schemaText)!;

        // FindSchema returns a path under a directory that holds both schemas/
        // and config/, so the repo root (schemaPath's grandparent) always has the
        // config bundle beside it.
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var config = ConfigLoader.Load(repoRoot);

        var configVersions = new Dictionary<string, string>
        {
            ["agr.schema.json"] = Hashing.Sha256Hex(schemaText)
        };
        foreach (var (name, hash) in config.Hashes) configVersions[name] = hash;

        var schemaChecks = _schemaLayer.Validate(new SchemaValidationInput(agrText, schema, schemaRoot));
        var (executionId, documentId, mode) = AgrFacts.Extract(agrText);

        var schemaBlocked = schemaChecks.Any(c =>
            c.Result == CheckResultValue.Fail && c.SeverityLevel == Severity.Blocking);

        IReadOnlyList<Check> checks;
        string overall;
        if (schemaBlocked)
        {
            // Gate: never run semantic layers on a schema-invalid AGR.
            checks = schemaChecks;
            overall = OverallResult.Fail;
        }
        else
        {
            var agr = AgrParser.Parse(agrText);
            var context = new SemanticContext(agr, config, mode, Registry: null);
            var layers = new ISemanticLayer[]
            {
                new SectionSchemaLayer(),
                new PlaceholderLayer(),
                new ReviewDateLayer(_clock),
                new MetadataLayer(),
                new ClauseReferenceLayer()
            };
            var all = new List<Check>(schemaChecks);
            foreach (var layer in layers) all.AddRange(layer.Validate(context));
            checks = all;
            overall = OverallResultPolicy.Compute(all, agr.ValidationPolicy.WarningThreshold);
        }

        return new ValidationReport
        {
            ReportVersion = EngineInfo.ReportVersion,
            ExecutionId = executionId,
            DocumentId = documentId,
            RequestedMode = mode,
            OverallResult = overall,
            EngineVersion = EngineInfo.Version,
            ConfigVersions = configVersions,
            TimestampUtc = _clock.UtcNow.ToString("o"),
            ArtifactSha256 = null,
            Checks = checks
        };
    }
}
```

- [ ] **Step 4: Add the warn count to the CLI summary line**

In `src/Registrar.Cli/ValidateCommand.cs`, replace the summary block (the `failCount`/`stdout.WriteLine` lines) with:

```csharp
        var failCount = report.Checks.Count(c => c.Result == CheckResultValue.Fail);
        var warnCount = report.Checks.Count(c => c.Result == CheckResultValue.Warn);
        stdout.WriteLine(
            $"{report.OverallResult}: {report.DocumentId} ({failCount} failing, {warnCount} warning check(s)). Report: {reportPath}");
```

The exit-code logic below it is unchanged (`report.OverallResult == OverallResult.Fail ? 1 : 0`), which correctly maps `pass-with-warnings` to exit 0.

- [ ] **Step 5: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidationServiceTests|FullyQualifiedName~ValidateCommandTests"`
Expected: PASS - all service and command tests, including the new semantic and gate assertions.

- [ ] **Step 6: Run the full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln`
Expected: PASS - every test (Slice 1 + Slice 2).

- [ ] **Step 7: Verify the CLI end-to-end by hand**

Run: `dotnet run --project src/Registrar.Cli -- validate fixtures/agr-sample-ds-draft.json`
Expected: prints `pass: ORG-GOV-DS-016 (0 failing, 0 warning check(s))...`, exit code 0.

Run: `dotnet run --project src/Registrar.Cli -- validate fixtures/agr-sample-ds-section-mismatch.json`
Expected: prints `fail: ORG-GOV-DS-016 (1 failing, 0 warning check(s))...`, exit code 1.

Clean up the hand-run reports so they are not committed:

```bash
rm -f fixtures/validation-report.json
```

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Validation/ValidationService.cs src/Registrar.Cli/ValidateCommand.cs tests/Registrar.Tests/ValidationServiceTests.cs tests/Registrar.Tests/ValidateCommandTests.cs
git commit -m "feat(validation): gate and run semantic layers in the validate pipeline"
```

---

## Self-Review

**Spec coverage** (each spec section maps to a task):
- Config-only semantic tier scope -> Tasks 3-7 (the five layers).
- Schema-gate pipeline behavior -> Task 9 `ValidationService`.
- `AgrDocument` typed DTO -> Task 2. `ConfigBundle`/`ConfigLoader`/`FindConfig` -> Task 1.
- `ISemanticLayer` + `SemanticContext` + `IIdRegistry?` seam -> Task 3.
- `OverallResultPolicy` (pass/pass-with-warnings/fail + threshold) -> Task 8.
- SectionSchema QA-ENG-02 -> Task 3; Placeholder QA-SPEC-07 -> Task 4; ReviewDate QA-SF-05 -> Task 5; Metadata QA-ENG-03 (new catalog) -> Task 6; ClauseReference QA-ENG-04 (new catalog) -> Task 7.
- Golden fixture upgrade + one invalid fixture per failure category -> Tasks 3-7.
- Per-check pass/fail tests named after the check -> Tasks 3-7; gate test, config-loader test, AgrDocument round-trip, OverallResultPolicy tests -> Tasks 1,2,8,9.
- CLI warn count, exit codes unchanged -> Task 9.
- configVersions carries five hashes -> Task 9 (asserted).
- Interpretation notes (QA-SF-05 today-anchor via IClock; placeholder -> MetadataViolation) -> implemented in Tasks 5 and 4.
- Deferred (registry/lifecycle, post-render, dependency resolution) -> not implemented; `IIdRegistry` marker only (Task 3).

**Placeholder scan:** No TBD/TODO. Every code step shows complete code; every command shows expected output and the `DOTNET_ROLL_FORWARD` prefix.

**Type consistency:** `SemanticContext(AgrDocument, ConfigBundle, string, IIdRegistry?)` defined in Task 3 and constructed identically in Tasks 4-9 (positional `Registry: null` / `null`). Each layer exposes `const string CheckId` used by its tests and the catalog tests (Tasks 6-7). `ConfigBundle` property names (`ArtifactTypesByCode`, `ReviewIntervalMonthsByCategory`, `ClauseIds`, `CanonicalTokens`, `Hashes`) defined in Task 1 and consumed unchanged in Tasks 3-9. `AgrDocument` accessors (`Identity.ArtifactType`, `Identity.DocumentId`, `Context.ReviewDate`, `Context.ReviewDateOverride`, `DataInputs.PlaceholderMap`, `DataInputs.Sections[].Title`/`.Blocks[].BlockType`/`.ClauseId`, `ValidationPolicy.Mode`/`.WarningThreshold`) defined in Task 2 and used consistently. `OverallResultPolicy.Compute(IReadOnlyList<Check>, int?)` defined in Task 8, called in Task 9. `Check` init members (`CheckId`, `LayerName`, `Result`, `SeverityLevel`, `ErrorCategory`, `Message`, `JsonPath`, `Expected`, `Actual`, `Remediation`, `RuleSource`) match the Slice 1 model.

**Known risk:** the config files carry `$comment` keys; only `reviewIntervalMonths` mixes a comment with typed values, handled explicitly in `ConfigLoader.ParseReviewIntervals`. Other `$comment` keys sit at object levels that System.Text.Json ignores as unmapped properties.
