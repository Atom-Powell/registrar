# Slice 4: Promotion Payload and QA-SPEC-18 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On a passing promotion-mode validate that has a registry, emit `promotion-payload.json` (registry-update payload + repository target) and wire QA-SPEC-18 as an assisted pass-with-attestation check.

**Architecture:** A new release-tier `PromotionLayer` (QA-SPEC-18) joins the pipeline; a `PromotionPayloadBuilder` assembles a typed `PromotionPayload` (validated by a new schema) from the AGR + registry record + a new `config/repository-targets.json`. `ValidationService.Validate` now returns a `ValidationOutcome { Report, Payload? }`; the CLI writes `promotion-payload.json` next to the report only when the payload is present.

**Tech Stack:** .NET 8, C#, System.Text.Json, JsonSchema.Net (payload-schema conformance), xUnit.

## Global Constraints

- **.NET 8** target (`net8.0`). No `net9.0`/`net10.0` features.
- **Laptop runtime gap:** this machine has only the .NET 10 runtime. Run tests with `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln` (or a `--filter` subset). For `dotnet run`, prefix the same var. Never add `<RollForward>` to any csproj.
- **Branch first:** do all work on a `slice4-promotion-payload` branch off `main`.
- **Fail closed:** the payload is emitted ONLY when mode is promotion AND overall result is pass (pass-with-warnings still emits; fail and draft never do). A missing repository target for the artifact's category is a blocking `RegistrySyncError`.
- **Zero LLM calls at runtime.** Pure deterministic.
- **Configuration over code:** repository targets live in `config/repository-targets.json`; `PromotionLayer` holds only its `checkId` literal and rule-source string.
- **Report/payload contract:** the report still validates against `schemas/validation-report.schema.json`; the emitted payload MUST validate against `schemas/promotion-payload.schema.json`. `payloadVersion` const `"1.0.0"`.
- **QA-SPEC-18 already exists** in `config/qa-checklist.json` (release, assisted, RegistrySyncError, ruleSource `"ORG-MST-SPEC-001 Section 9 item 18"`) - do NOT add a catalog entry; assert presence only.
- **Determinism in tests:** time comes from an injected `IClock`; use a fixed instant.
- **Output hygiene:** US English; no emojis, em/en dashes, or decorative unicode in any source, config, schema, fixture, or emitted string.
- **Value strings centralized** in `Registrar.Core` `Vocabulary.cs` (`Layer.Release`, `CheckResultValue.Pass`/`.Skipped`, `Severity.Info`/`.Blocking`, `ErrorCategory.RegistrySyncError`, `Mode.Promotion`).

## File map

- `config/repository-targets.json` - category -> repo path map (Task 1).
- `src/Registrar.Core/Config/ConfigBundle.cs`, `ConfigLoader.cs` - load it (Task 1).
- `src/Registrar.Core/Model/PromotionPayload.cs` - payload model (Task 2).
- `src/Registrar.Core/PromotionPayloadSerializer.cs` - JSON emit (Task 2).
- `src/Registrar.Core/EngineInfo.cs` - add `PayloadVersion` (Task 2).
- `schemas/promotion-payload.schema.json` - payload contract (Task 2).
- `src/Registrar.Validation/PromotionPayloadBuilder.cs` - assembles the payload (Task 3).
- `src/Registrar.Validation/PromotionLayer.cs` - QA-SPEC-18 (Task 4).
- `src/Registrar.Validation/ValidationOutcome.cs`, `ValidationService.cs` - outcome + wiring (Task 5).
- `src/Registrar.Cli/ValidateCommand.cs` - write the payload file (Tasks 5-6).
- `fixtures/agr-sample-ds-promotion.json` - passing promotion AGR (Task 5).

---

### Task 1: repository-targets config

**Files:**
- Create: `config/repository-targets.json`
- Modify: `src/Registrar.Core/Config/ConfigBundle.cs`
- Modify: `src/Registrar.Core/Config/ConfigLoader.cs`
- Modify: `tests/Registrar.Tests/Config/ConfigLoaderTests.cs`

**Interfaces:**
- Consumes: existing `ConfigLoader.Load`, `ConfigLocator.FindConfig`, `Hashing`.
- Produces: `ConfigBundle.RepositoryTargetsByCategory` (`IReadOnlyDictionary<string,string>`), and a `repository-targets.json` entry in `Hashes`.

- [ ] **Step 1: Create the config file**

Create `config/repository-targets.json`:

```json
{
  "$comment": "Repository targets by category for promotion-payload.json. Provisional paths pending ADR-001 ratification; reconciledAgainstAdr001 stays false until the documentation owner confirms the controlled-documents repository structure.",
  "reconciledAgainstAdr001": false,
  "targetsByCategory": {
    "GOV": "Controlled Documents/Governance",
    "TRN": "Controlled Documents/Training",
    "RSK": "Controlled Documents/Risk",
    "OPS": "Controlled Documents/Operational Artifacts",
    "INT": "Controlled Documents/Internal",
    "MST": "Controlled Documents/Master Templates"
  }
}
```

- [ ] **Step 2: Write the failing test**

Add to `tests/Registrar.Tests/Config/ConfigLoaderTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void Load_parses_repository_targets_by_category()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Equal("Controlled Documents/Governance", bundle.RepositoryTargetsByCategory["GOV"]);
        Assert.Equal(6, bundle.RepositoryTargetsByCategory.Count);
        Assert.True(bundle.Hashes.ContainsKey("repository-targets.json"));
    }
```

Also update the existing hash-count assertion in the class: change `Assert.Equal(4, bundle.Hashes.Count);` to `Assert.Equal(5, bundle.Hashes.Count);` (the fifth config file is now loaded).

- [ ] **Step 3: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: FAIL - `RepositoryTargetsByCategory` does not exist / hash count is 4.

- [ ] **Step 4: Add the property to `ConfigBundle`**

In `src/Registrar.Core/Config/ConfigBundle.cs`, add inside the record:

```csharp
    public required IReadOnlyDictionary<string, string> RepositoryTargetsByCategory { get; init; }
```

- [ ] **Step 5: Load and hash it in `ConfigLoader`**

In `src/Registrar.Core/Config/ConfigLoader.cs`, add a parse helper next to `ParseReviewIntervals`:

```csharp
    private static IReadOnlyDictionary<string, string> ParseRepositoryTargets(string text)
    {
        var node = JsonNode.Parse(text)!["targetsByCategory"]!.AsObject();
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in node)
            result[key] = (string)value!;
        return result;
    }
```

In `Load(...)`, read the file near the other config reads:

```csharp
        var repositoryTargetsText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "repository-targets.json"));
```

Add to the returned `ConfigBundle` initializer:

```csharp
            RepositoryTargetsByCategory = ParseRepositoryTargets(repositoryTargetsText),
```

And add to the `Hashes` dictionary initializer:

```csharp
                ["repository-targets.json"] = Hashing.Sha256Hex(repositoryTargetsText),
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add config/repository-targets.json src/Registrar.Core/Config/ConfigBundle.cs src/Registrar.Core/Config/ConfigLoader.cs tests/Registrar.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat(core): add repository-targets config map"
```

---

### Task 2: PromotionPayload model, serializer, and schema

**Files:**
- Create: `src/Registrar.Core/Model/PromotionPayload.cs`
- Create: `src/Registrar.Core/PromotionPayloadSerializer.cs`
- Modify: `src/Registrar.Core/EngineInfo.cs`
- Create: `schemas/promotion-payload.schema.json`
- Test: `tests/Registrar.Tests/PromotionPayloadSerializerTests.cs`

**Interfaces:**
- Consumes: `ReportSerializer.Options` (existing), `IsolatedSchema` (existing, in `Registrar.Validation`).
- Produces:
  - `record PromotionPayload { PayloadVersion, ExecutionId, DocumentId, ArtifactType, Version, GeneratedUtc (all string); RegistryUpdate RegistryUpdate; string RepositoryTarget; Attestation Attestation }`.
  - `record RegistryUpdate { DocumentId, FromStatus, ToStatus, Version (string) }`.
  - `record Attestation { string CheckId, string Statement, bool RequiresOperatorSignoff }`.
  - `PromotionPayloadSerializer.ToJson(PromotionPayload) -> string`.
  - `EngineInfo.PayloadVersion = "1.0.0"`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/PromotionPayloadSerializerTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;
using Json.Schema;

namespace Registrar.Tests;

public class PromotionPayloadSerializerTests
{
    private static PromotionPayload Sample() => new()
    {
        PayloadVersion = EngineInfo.PayloadVersion,
        ExecutionId = "GOV-2026-07-03-001",
        DocumentId = "ORG-GOV-DS-016",
        ArtifactType = "DS",
        Version = "v1.0",
        GeneratedUtc = "2026-07-03T12:00:00.0000000+00:00",
        RegistryUpdate = new RegistryUpdate
        {
            DocumentId = "ORG-GOV-DS-016", FromStatus = "Draft", ToStatus = "In Review", Version = "v1.0"
        },
        RepositoryTarget = "Controlled Documents/Governance",
        Attestation = new Attestation
        {
            CheckId = "QA-SPEC-18",
            Statement = "Operator must apply this registry update to the PMC Registry sheet and confirm post-update state before final delivery.",
            RequiresOperatorSignoff = true
        }
    };

    [Fact]
    public void Serialized_payload_uses_camelCase_field_names()
    {
        var node = JsonNode.Parse(PromotionPayloadSerializer.ToJson(Sample()))!.AsObject();

        Assert.Equal("1.0.0", (string?)node["payloadVersion"]);
        Assert.Equal("ORG-GOV-DS-016", (string?)node["documentId"]);
        Assert.Equal("In Review", (string?)node["registryUpdate"]!["toStatus"]);
        Assert.Equal("Controlled Documents/Governance", (string?)node["repositoryTarget"]);
        Assert.True((bool)node["attestation"]!["requiresOperatorSignoff"]!);
    }

    [Fact]
    public void Serialized_payload_conforms_to_promotion_payload_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "promotion-payload.schema.json");
        var schema = IsolatedSchema.FromFile(schemaPath);
        using var document = JsonDocument.Parse(PromotionPayloadSerializer.ToJson(Sample()));

        var result = schema.Evaluate(document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Emitted payload failed promotion-payload schema validation.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~PromotionPayloadSerializerTests"`
Expected: FAIL - types/schema do not exist.

- [ ] **Step 3: Add `PayloadVersion` to `EngineInfo`**

In `src/Registrar.Core/EngineInfo.cs`, add inside the class:

```csharp
    public const string PayloadVersion = "1.0.0";  // promotion-payload payloadVersion const
```

- [ ] **Step 4: Create the payload model**

Create `src/Registrar.Core/Model/PromotionPayload.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record PromotionPayload
{
    [JsonPropertyName("payloadVersion")]  public required string PayloadVersion { get; init; }
    [JsonPropertyName("executionId")]     public required string ExecutionId { get; init; }
    [JsonPropertyName("documentId")]      public required string DocumentId { get; init; }
    [JsonPropertyName("artifactType")]    public required string ArtifactType { get; init; }
    [JsonPropertyName("version")]         public required string Version { get; init; }
    [JsonPropertyName("generatedUtc")]    public required string GeneratedUtc { get; init; }
    [JsonPropertyName("registryUpdate")]  public required RegistryUpdate RegistryUpdate { get; init; }
    [JsonPropertyName("repositoryTarget")] public required string RepositoryTarget { get; init; }
    [JsonPropertyName("attestation")]     public required Attestation Attestation { get; init; }
}

public sealed record RegistryUpdate
{
    [JsonPropertyName("documentId")] public required string DocumentId { get; init; }
    [JsonPropertyName("fromStatus")] public required string FromStatus { get; init; }
    [JsonPropertyName("toStatus")]   public required string ToStatus { get; init; }
    [JsonPropertyName("version")]    public required string Version { get; init; }
}

public sealed record Attestation
{
    [JsonPropertyName("checkId")]   public required string CheckId { get; init; }
    [JsonPropertyName("statement")] public required string Statement { get; init; }
    [JsonPropertyName("requiresOperatorSignoff")] public required bool RequiresOperatorSignoff { get; init; }
}
```

- [ ] **Step 5: Create the serializer**

Create `src/Registrar.Core/PromotionPayloadSerializer.cs`:

```csharp
using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core;

public static class PromotionPayloadSerializer
{
    // Reuses the report serializer's options (indented, relaxed escaping,
    // omit nulls) so both engine outputs render identically.
    public static string ToJson(PromotionPayload payload)
        => JsonSerializer.Serialize(payload, ReportSerializer.Options);
}
```

- [ ] **Step 6: Create the payload schema**

Create `schemas/promotion-payload.schema.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://ironwright.dev/schemas/registrar/promotion-payload.schema.json",
  "title": "PromotionPayload",
  "description": "Registry-update payload and repository target the operator applies before final delivery. Emitted only when validation passes in promotion mode.",
  "type": "object",
  "additionalProperties": false,
  "required": ["payloadVersion", "executionId", "documentId", "artifactType", "version", "generatedUtc", "registryUpdate", "repositoryTarget", "attestation"],
  "properties": {
    "payloadVersion": { "type": "string", "const": "1.0.0" },
    "executionId": { "type": "string" },
    "documentId": { "type": "string" },
    "artifactType": { "type": "string" },
    "version": { "type": "string" },
    "generatedUtc": { "type": "string", "format": "date-time" },
    "registryUpdate": {
      "type": "object",
      "additionalProperties": false,
      "required": ["documentId", "fromStatus", "toStatus", "version"],
      "properties": {
        "documentId": { "type": "string" },
        "fromStatus": { "type": "string" },
        "toStatus": { "type": "string" },
        "version": { "type": "string" }
      }
    },
    "repositoryTarget": { "type": "string", "minLength": 1 },
    "attestation": {
      "type": "object",
      "additionalProperties": false,
      "required": ["checkId", "statement", "requiresOperatorSignoff"],
      "properties": {
        "checkId": { "type": "string", "const": "QA-SPEC-18" },
        "statement": { "type": "string", "minLength": 1 },
        "requiresOperatorSignoff": { "type": "boolean" }
      }
    }
  }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~PromotionPayloadSerializerTests"`
Expected: PASS - 2 tests.

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Core/Model/PromotionPayload.cs src/Registrar.Core/PromotionPayloadSerializer.cs src/Registrar.Core/EngineInfo.cs schemas/promotion-payload.schema.json tests/Registrar.Tests/PromotionPayloadSerializerTests.cs
git commit -m "feat(core): add promotion payload model, serializer, and schema"
```

---

### Task 3: PromotionPayloadBuilder

**Files:**
- Create: `src/Registrar.Validation/PromotionPayloadBuilder.cs`
- Test: `tests/Registrar.Tests/PromotionPayloadBuilderTests.cs`

**Interfaces:**
- Consumes: `AgrDocument` (Identity: DocumentId/ArtifactType/Version/ApprovalStatus), `RegistryRecord` (ApprovalStatus), `ConfigBundle.RepositoryTargetsByCategory`, `IClock`, `EngineInfo.PayloadVersion`.
- Produces: `static class PromotionPayloadBuilder { PromotionPayload Build(AgrDocument agr, RegistryRecord record, ConfigBundle config, IClock clock, string executionId); }`.

Note on `executionId`: `AgrDocument` does not carry `sourceWorkflowContext`, so the caller (ValidationService, which already extracts it via `AgrFacts.Extract`) passes it in.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/PromotionPayloadBuilderTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class PromotionPayloadBuilderTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void Build_assembles_payload_from_agr_registry_and_config()
    {
        var agr = SemanticTestSupport.Agr("agr-sample-ds-draft.json"); // documentId ORG-GOV-DS-016, DS, v1.0, Draft
        var record = new RegistryRecord("ORG-GOV-DS-016", "DS", "Client-Facing", "Draft", "v1.0");

        var payload = PromotionPayloadBuilder.Build(
            agr, record, SemanticTestSupport.Config(), new FixedClock(), "GOV-2026-07-03-001");

        Assert.Equal(EngineInfo.PayloadVersion, payload.PayloadVersion);
        Assert.Equal("GOV-2026-07-03-001", payload.ExecutionId);
        Assert.Equal("ORG-GOV-DS-016", payload.DocumentId);
        Assert.Equal("Draft", payload.RegistryUpdate.FromStatus);      // registry current
        Assert.Equal("Draft", payload.RegistryUpdate.ToStatus);        // AGR target (golden is Draft)
        Assert.Equal("Controlled Documents/Governance", payload.RepositoryTarget); // GOV
        Assert.Equal("QA-SPEC-18", payload.Attestation.CheckId);
        Assert.True(payload.Attestation.RequiresOperatorSignoff);
        Assert.Equal("2026-07-03T12:00:00.0000000+00:00", payload.GeneratedUtc);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~PromotionPayloadBuilderTests"`
Expected: FAIL - `PromotionPayloadBuilder` does not exist.

- [ ] **Step 3: Implement the builder**

Create `src/Registrar.Validation/PromotionPayloadBuilder.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Validation;

public static class PromotionPayloadBuilder
{
    private const string AttestationStatement =
        "Operator must apply this registry update to the PMC Registry sheet and confirm post-update state before final delivery.";

    public static PromotionPayload Build(
        AgrDocument agr, RegistryRecord record, ConfigBundle config, IClock clock, string executionId)
    {
        var category = agr.Identity.DocumentId.Split('-')[1];

        return new PromotionPayload
        {
            PayloadVersion = EngineInfo.PayloadVersion,
            ExecutionId = executionId,
            DocumentId = agr.Identity.DocumentId,
            ArtifactType = agr.Identity.ArtifactType,
            Version = agr.Identity.Version,
            GeneratedUtc = clock.UtcNow.ToString("o"),
            RegistryUpdate = new RegistryUpdate
            {
                DocumentId = agr.Identity.DocumentId,
                FromStatus = record.ApprovalStatus,
                ToStatus = agr.Identity.ApprovalStatus,
                Version = agr.Identity.Version
            },
            RepositoryTarget = config.RepositoryTargetsByCategory[category],
            Attestation = new Attestation
            {
                CheckId = "QA-SPEC-18",
                Statement = AttestationStatement,
                RequiresOperatorSignoff = true
            }
        };
    }
}
```

Note: `config.RepositoryTargetsByCategory[category]` is a safe indexer here because the builder is called only after a passing promotion run, where `PromotionLayer` (Task 4) has already failed the run if the category has no target.

- [ ] **Step 4: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~PromotionPayloadBuilderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Validation/PromotionPayloadBuilder.cs tests/Registrar.Tests/PromotionPayloadBuilderTests.cs
git commit -m "feat(validation): add promotion payload builder"
```

---

### Task 4: PromotionLayer (QA-SPEC-18)

**Files:**
- Create: `src/Registrar.Validation/PromotionLayer.cs`
- Test: `tests/Registrar.Tests/PromotionLayerTests.cs`
- Modify: `tests/Registrar.Tests/QaChecklistCatalogTests.cs`

**Interfaces:**
- Consumes: `SemanticContext`, `IIdRegistry`, `ConfigBundle.RepositoryTargetsByCategory`, `Vocabulary` constants.
- Produces: `sealed class PromotionLayer : ISemanticLayer` with `const string CheckId = "QA-SPEC-18"`. draft -> skipped; promotion + registry null -> skipped; promotion + ID absent -> skipped; promotion + ID present + no repository target for the category -> blocking `RegistrySyncError`; promotion + ID present + target resolves -> explicit `pass` (Info) carrying the attestation. Layer `release`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/PromotionLayerTests.cs`:

```csharp
using System.Collections.Generic;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class PromotionLayerTests
{
    private static SemanticContext PromotionContext(ConfigBundle config)
        => new(SemanticTestSupport.Agr("agr-sample-ds-draft.json"), config, Mode.Promotion,
               CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv));

    [Fact]
    public void QA_SPEC_18_skips_in_draft()
    {
        var checks = new PromotionLayer().Validate(
            new SemanticContext(SemanticTestSupport.Agr("agr-sample-ds-draft.json"),
                SemanticTestSupport.Config(), Mode.Draft,
                CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv)));

        Assert.Equal(CheckResultValue.Skipped, Assert.Single(checks).Result);
    }

    [Fact]
    public void QA_SPEC_18_passes_with_attestation_at_promotion()
    {
        var check = Assert.Single(new PromotionLayer().Validate(PromotionContext(SemanticTestSupport.Config())));

        Assert.Equal("QA-SPEC-18", check.CheckId);
        Assert.Equal(Layer.Release, check.LayerName);
        Assert.Equal(CheckResultValue.Pass, check.Result);
        Assert.Equal(Severity.Info, check.SeverityLevel);
        Assert.NotNull(check.Remediation);
    }

    [Fact]
    public void QA_SPEC_18_fails_when_no_repository_target_for_category()
    {
        var config = SemanticTestSupport.Config() with
        {
            RepositoryTargetsByCategory = new Dictionary<string, string>()
        };

        var check = Assert.Single(new PromotionLayer().Validate(PromotionContext(config)));
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.RegistrySyncError, check.ErrorCategory);
    }

    [Fact]
    public void QA_SPEC_18_skips_at_promotion_when_registry_absent()
    {
        var context = new SemanticContext(SemanticTestSupport.Agr("agr-sample-ds-draft.json"),
            SemanticTestSupport.Config(), Mode.Promotion, null);

        Assert.Equal(CheckResultValue.Skipped, Assert.Single(new PromotionLayer().Validate(context)).Result);
    }
}
```

Add to `tests/Registrar.Tests/QaChecklistCatalogTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void PromotionLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;
        var ids = checklist["checks"]!.AsArray().Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(PromotionLayer.CheckId, ids);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~PromotionLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: FAIL - `PromotionLayer` does not exist.

- [ ] **Step 3: Implement `PromotionLayer`**

Create `src/Registrar.Validation/PromotionLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class PromotionLayer : ISemanticLayer
{
    public const string CheckId = "QA-SPEC-18";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 9 item 18";
    private const string AttestationRemediation =
        "Apply the emitted promotion-payload.json registry update to the PMC Registry sheet and confirm post-update state before final delivery.";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        if (context.Mode != Mode.Promotion)
            return new[] { Skipped("Registry-update payload applies only at promotion.") };

        if (context.Registry is null)
            return new[] { Skipped("No ID Registry supplied; registry-update payload not evaluated.") };

        var record = context.Registry.Lookup(context.Agr.Identity.DocumentId);
        if (record is null)
            return new[] { Skipped("Document ID not in registry; registry-update payload not evaluated (see QA-SF-03).") };

        var category = context.Agr.Identity.DocumentId.Split('-')[1];
        if (!context.Config.RepositoryTargetsByCategory.ContainsKey(category))
            return new[] { Fail(
                $"No repository target is configured for category '{category}'.",
                $"a target for '{category}' in repository-targets.json", category,
                "Add the category to config/repository-targets.json so the promotion payload can name a repository target.") };

        return new[] { Pass(
            "Registry update required; promotion-payload.json will be emitted for operator application.") };
    }

    private static Check Pass(string message) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Pass,
        SeverityLevel = Severity.Info,
        Message = message,
        Remediation = AttestationRemediation,
        RuleSource = RuleSourceValue
    };

    private static Check Fail(string message, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.RegistrySyncError,
        Message = message,
        JsonPath = "/artifactIdentity/documentId",
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };

    private static Check Skipped(string message) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Skipped,
        SeverityLevel = Severity.Info,
        Message = message,
        RuleSource = RuleSourceValue
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~PromotionLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS - PromotionLayerTests (4) and the catalog tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Validation/PromotionLayer.cs tests/Registrar.Tests/PromotionLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat(validation): add promotion-payload layer (QA-SPEC-18)"
```

---

### Task 5: ValidationOutcome, wire PromotionLayer, build the payload

**Files:**
- Create: `src/Registrar.Validation/ValidationOutcome.cs`
- Modify: `src/Registrar.Validation/ValidationService.cs`
- Modify: `src/Registrar.Cli/ValidateCommand.cs` (call-site adaptation only)
- Create: `fixtures/agr-sample-ds-promotion.json`
- Modify: `tests/Registrar.Tests/ValidationServiceTests.cs`

**Interfaces:**
- Consumes: `PromotionLayer` (Task 4), `PromotionPayloadBuilder` (Task 3), `PromotionPayload` (Task 2), existing pipeline.
- Produces:
  - `sealed record ValidationOutcome(ValidationReport Report, PromotionPayload? Payload)`.
  - `ValidationService.Validate(string agrText, string schemaPath, string? registryCsvText = null) -> ValidationOutcome`.

- [ ] **Step 1: Create the passing promotion fixture**

Create `fixtures/agr-sample-ds-promotion.json` as a copy of `fixtures/agr-sample-ds-draft.json` with exactly these three changes:
- `"approvalStatus": "Draft"` -> `"approvalStatus": "In Review"`
- `"reviewDate": null` -> `"reviewDate": "2026-12-01"`
- `"mode": "draft"` -> `"mode": "promotion"`

(Registry `ORG-GOV-DS-016` is Draft in the sample registry, so Draft -> In Review is a legal transition; the review date is within the GOV six-month interval of the fixed test clock 2026-07-03.)

- [ ] **Step 2: Write the failing tests**

Add to `tests/Registrar.Tests/ValidationServiceTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void Promotion_pass_produces_payload()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-promotion.json"));

        var outcome = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv);

        Assert.Equal(OverallResult.Pass, outcome.Report.OverallResult);
        Assert.NotNull(outcome.Payload);
        Assert.Equal("Draft", outcome.Payload!.RegistryUpdate.FromStatus);
        Assert.Equal("In Review", outcome.Payload.RegistryUpdate.ToStatus);
        Assert.Equal("Controlled Documents/Governance", outcome.Payload.RepositoryTarget);
    }

    [Fact]
    public void Draft_pass_produces_no_payload()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var outcome = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv);

        Assert.Equal(OverallResult.Pass, outcome.Report.OverallResult);
        Assert.Null(outcome.Payload);
    }

    [Fact]
    public void Promotion_fail_produces_no_payload()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-promotion.json"));

        // No registry in promotion -> QA-SF-03 fails the run.
        var outcome = new ValidationService(new FixedClock()).Validate(agr, schemaPath, null);

        Assert.Equal(OverallResult.Fail, outcome.Report.OverallResult);
        Assert.Null(outcome.Payload);
    }
```

Then update EVERY existing test in this class that calls `.Validate(...)`: those tests assign the result to `report` and read `report.OverallResult` / `report.Checks` / `report.ConfigVersions` etc. Append `.Report` to each `Validate(...)` call so `report` is the `ValidationReport`. For example:

```csharp
        // before:
        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath);
        // after:
        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath).Report;
```

Apply the same `.Report` suffix to the registry-argument calls (e.g. `.Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv).Report`). There are eight such existing tests; the new three above already use `outcome.Report` directly.

- [ ] **Step 3: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidationServiceTests"`
Expected: FAIL - `Validate` still returns `ValidationReport` (no `.Report`/`.Payload`); the new tests do not compile.

- [ ] **Step 4: Create `ValidationOutcome`**

Create `src/Registrar.Validation/ValidationOutcome.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed record ValidationOutcome(ValidationReport Report, PromotionPayload? Payload);
```

- [ ] **Step 5: Rewrite `ValidationService` to return the outcome and build the payload**

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

    public ValidationOutcome Validate(string agrText, string schemaPath, string? registryCsvText = null)
    {
        var schemaText = File.ReadAllText(schemaPath);
        var schema = IsolatedSchema.FromText(schemaText);
        var schemaRoot = JsonNode.Parse(schemaText)!;

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
        PromotionPayload? payload = null;

        if (schemaBlocked)
        {
            checks = schemaChecks;
            overall = OverallResult.Fail;
        }
        else
        {
            var agr = AgrParser.Parse(agrText);

            IIdRegistry? registry = null;
            string? registryError = null;
            if (registryCsvText is not null)
            {
                try { registry = CsvIdRegistry.FromCsvText(registryCsvText); }
                catch (RegistryFormatException ex) { registryError = ex.Message; }
            }

            var context = new SemanticContext(agr, config, mode, registry);
            var layers = new List<ISemanticLayer>
            {
                new SectionSchemaLayer(),
                new PlaceholderLayer(),
                new ReviewDateLayer(_clock),
                new MetadataLayer(),
                new ClauseReferenceLayer()
            };
            if (registryError is null)
            {
                layers.Add(new RegistryLayer());
                layers.Add(new LifecycleLayer());
                layers.Add(new PromotionLayer());
            }

            var all = new List<Check>(schemaChecks);
            foreach (var layer in layers) all.AddRange(layer.Validate(context));
            if (registryError is not null) all.Add(MalformedRegistryCheck(registryError));

            checks = all;
            overall = OverallResultPolicy.Compute(all, agr.ValidationPolicy.WarningThreshold);

            // Emit the payload only on a passing promotion run with a registry.
            // A pass in promotion guarantees the ID exists and the repository
            // target resolves (QA-SF-03 / QA-SPEC-18 would have failed otherwise).
            if (mode == Mode.Promotion && overall == OverallResult.Pass && registry is not null)
            {
                var record = registry.Lookup(documentId);
                if (record is not null)
                    payload = PromotionPayloadBuilder.Build(agr, record, config, _clock, executionId);
            }
        }

        var report = new ValidationReport
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

        return new ValidationOutcome(report, payload);
    }

    private static Check MalformedRegistryCheck(string reason) => new()
    {
        CheckId = "QA-SF-03",
        LayerName = Layer.Release,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.RegistrySyncError,
        Message = $"The ID Registry export could not be read: {reason}",
        Expected = "a well-formed registry CSV export with the required columns",
        Remediation = "Re-export the PMC Registry sheet to CSV with the required columns and no duplicate Document IDs.",
        RuleSource = "ORG-MST-WFE-001 4.5 item 3"
    };
}
```

- [ ] **Step 6: Adapt the `ValidateCommand` call site**

In `src/Registrar.Cli/ValidateCommand.cs`, change the line that calls `Validate` so it reads the report off the outcome (the payload is written in Task 6). Replace:

```csharp
        var report = new ValidationService(clock).Validate(agrText, schemaPath, registryCsvText);
```

with:

```csharp
        var outcome = new ValidationService(clock).Validate(agrText, schemaPath, registryCsvText);
        var report = outcome.Report;
```

Leave the rest of the method unchanged.

- [ ] **Step 7: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidationServiceTests"`
Expected: PASS - the updated existing tests and the three new payload tests.

- [ ] **Step 8: Run the full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln`
Expected: PASS - all tests (the `ValidationOutcome` change is fully absorbed).

- [ ] **Step 9: Commit**

```bash
git add src/Registrar.Validation/ValidationOutcome.cs src/Registrar.Validation/ValidationService.cs src/Registrar.Cli/ValidateCommand.cs fixtures/agr-sample-ds-promotion.json tests/Registrar.Tests/ValidationServiceTests.cs
git commit -m "feat(validation): return ValidationOutcome and build the promotion payload"
```

---

### Task 6: Emit promotion-payload.json from the CLI, end-to-end

**Files:**
- Modify: `src/Registrar.Cli/ValidateCommand.cs`
- Test: `tests/Registrar.Tests/ValidateCommandTests.cs`

**Interfaces:**
- Consumes: `ValidationOutcome.Payload`, `PromotionPayloadSerializer` (Task 2).
- Produces: `ValidateCommand.Run` writes `promotion-payload.json` next to the report when `outcome.Payload` is non-null.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Registrar.Tests/ValidateCommandTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void Passing_promotion_writes_promotion_payload()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(0, code);
        var payloadPath = Path.Combine(Path.GetDirectoryName(agrPath)!, "promotion-payload.json");
        Assert.True(File.Exists(payloadPath));
        var payload = JsonNode.Parse(File.ReadAllText(payloadPath))!.AsObject();
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void Draft_run_writes_no_promotion_payload()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(0, code);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(agrPath)!, "promotion-payload.json")));
    }

    [Fact]
    public void Failing_promotion_writes_no_promotion_payload()
    {
        // promotion fixture with no registry -> QA-SF-03 fails, no payload
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-promotion.json");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, null);

        Assert.Equal(1, code);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(agrPath)!, "promotion-payload.json")));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidateCommandTests"`
Expected: FAIL - `Passing_promotion_writes_promotion_payload` fails (no payload file written yet).

- [ ] **Step 3: Write the payload file in `ValidateCommand`**

In `src/Registrar.Cli/ValidateCommand.cs`, immediately after the block that writes `validation-report.json` (the `File.WriteAllText(reportPath, ReportSerializer.ToJson(report))` and its surrounding try/catch), insert:

```csharp
        if (outcome.Payload is not null)
        {
            var payloadPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(agrPath))!, "promotion-payload.json");
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
```

`PromotionPayloadSerializer` is in `Registrar.Core`, already imported by this file via the existing `using Registrar.Core;` (used for `ReportSerializer`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidateCommandTests"`
Expected: PASS.

- [ ] **Step 5: Run the full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln`
Expected: PASS - all tests.

- [ ] **Step 6: Verify the CLI end-to-end by hand**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project src/Registrar.Cli -- validate fixtures/agr-sample-ds-promotion.json --registry fixtures/registry-sample.csv`
Expected: prints `pass: ORG-GOV-DS-016 (0 failing, 0 warning check(s))...`, exit 0, and writes `fixtures/promotion-payload.json` with `"toStatus": "In Review"`.

Confirm the payload was written, then clean up the hand-run outputs so they are not committed:

```bash
rm -f fixtures/validation-report.json fixtures/promotion-payload.json
```

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Cli/ValidateCommand.cs tests/Registrar.Tests/ValidateCommandTests.cs
git commit -m "feat(cli): emit promotion-payload.json on a passing promotion run"
```

---

## Self-Review

**Spec coverage** (each spec section maps to a task):
- Repository-target config map -> Task 1. ConfigBundle load + hash -> Task 1.
- PromotionPayload model + schema + serializer + payloadVersion const -> Task 2.
- PromotionPayloadBuilder (identity, from/to status, target, attestation) -> Task 3.
- QA-SPEC-18 assisted layer (draft skipped / promotion pass-with-attestation / missing-target blocking) -> Task 4.
- ValidationOutcome return-type change + wire PromotionLayer + build payload on promotion+pass -> Task 5.
- CLI writes promotion-payload.json only when payload present; fail closed -> Task 6.
- Fixtures (passing promotion AGR) -> Task 5; reuses registry-sample.csv.
- Testing: per-check pass/fail (QA-SPEC-18 pass + missing-target fail + draft-skipped) -> Task 4; builder, config, schema conformance, service payload gating, CLI emission -> Tasks 1-6.
- Deferred (version-supersede, post-update verify, manifest/log/renderer) -> not implemented.

**Placeholder scan:** No TBD/TODO. Every code step shows complete code; every command shows expected output and the `DOTNET_ROLL_FORWARD` prefix.

**Type consistency:** `ValidationOutcome(ValidationReport, PromotionPayload?)` defined in Task 5, consumed in Tasks 5-6 (`outcome.Report`, `outcome.Payload`). `PromotionPayload`/`RegistryUpdate`/`Attestation` field names (Task 2) used identically by the builder (Task 3), the schema (Task 2), and the tests. `PromotionPayloadBuilder.Build(AgrDocument, RegistryRecord, ConfigBundle, IClock, string)` defined in Task 3, called in Task 5 with `registry.Lookup(documentId)` and `executionId` from `AgrFacts.Extract`. `ConfigBundle.RepositoryTargetsByCategory` defined in Task 1, consumed in Tasks 3-4. `PromotionLayer.CheckId` used by its catalog test. `PromotionPayloadSerializer.ToJson` defined in Task 2, consumed in Task 6. `EngineInfo.PayloadVersion` defined in Task 2, consumed in Tasks 2-3. Vocabulary constants (`Layer.Release`, `CheckResultValue.Pass`/`.Skipped`, `Severity.Info`/`.Blocking`, `ErrorCategory.RegistrySyncError`, `Mode.Promotion`) are pre-existing.

**Known risk:** the `ValidationOutcome` return-type change is the widest surface; it touches every existing `ValidationServiceTests` call site (mechanical `.Report` suffix, enumerated in Task 5) and one `ValidateCommand` call site. `ValidateCommandTests` go through `Run` (which returns an exit code) and read the report file, so they are unaffected by the return-type change; the full-suite run in Task 5 Step 8 surfaces any missed call site immediately.
