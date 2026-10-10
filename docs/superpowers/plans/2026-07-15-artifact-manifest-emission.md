# artifact-manifest.json Emission Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Emit `artifact-manifest.json` on `registrar build` whenever a DOCX is produced, closing the last deferred output-package file.

**Architecture:** A pure `ArtifactManifestBuilder` assembles an `ArtifactManifest` record (Core.Model) from the post-render merged `ValidationReport`, the parsed `AgrDocument`, and the optional `PromotionPayload`. It is serialized by `ArtifactManifestSerializer` (reusing the report serializer's options) and written inline in `BuildCommand` after the DOCX write. The AGR parser is taught to read the `dependencies` array it currently drops. No renderer change (sections render 1:1 from the AGR; the artifact hash is already on the report).

**Tech Stack:** .NET 8, C#, System.Text.Json, xUnit, JsonSchema.Net (`Json.Schema`).

## Global Constraints

- .NET 8 / C#; build/test with `dotnet build` / `dotnet test` from the repo root.
- US English; no emojis; no em/en dashes or decorative unicode in source, config, schema, or generated strings (enforced on engine source by `SourceHygieneTests`; plain ASCII hyphens only).
- Configuration over code: no artifact constants, clause text, or repository paths in core logic.
- Determinism is a governance property: no wall-clock, no GUIDs, no `DateTime.Now`. Timestamps come only from the injected `IClock` already reflected in `ValidationReport.TimestampUtc`.
- JSON field names are camelCase via `[JsonPropertyName]` attributes (serializers set no naming policy).
- Reuse `ReportSerializer.Options` (indented, `JsonIgnoreCondition.WhenWritingNull`, `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`).
- Version constants live in `src/Registrar.Core/EngineInfo.cs`.
- The manifest is emitted ONLY when a DOCX is produced, so `artifactSha256` is always populated in a written manifest.
- Reference design: `docs/superpowers/specs/2026-07-15-artifact-manifest-emission-design.md`.

---

### Task 1: Parse the AGR `dependencies` array

**Files:**
- Modify: `src/Registrar.Core/Model/AgrDocument.cs`
- Test: `tests/Registrar.Tests/Model/AgrParserTests.cs`

**Interfaces:**
- Consumes: `AgrParser.Parse` (existing: `JsonSerializer.Deserialize<AgrDocument>` with default options).
- Produces:
  - `Registrar.Core.Model.AgrDependency` record: `DependencyId` (string), `Kind` (string), `Blocking` (bool), all `required`, with `[JsonPropertyName]` `dependencyId` / `kind` / `blocking`.
  - `AgrDocument.Dependencies` (`IReadOnlyList<AgrDependency>?`, JSON name `dependencies`, null when absent).

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Model/AgrParserTests.cs` (new `[Fact]`s in the existing class; if the file/class does not exist, create it with `namespace Registrar.Tests;` and `using Registrar.Core.Model;`):

```csharp
    [Fact]
    public void Parses_the_dependencies_array()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-aup-draft.json");
        var agr = AgrParser.Parse(text);

        Assert.NotNull(agr.Dependencies);
        var dep = Assert.Single(agr.Dependencies!);
        Assert.Equal("ORG-MST-SCL", dep.DependencyId);
        Assert.Equal("clause-library", dep.Kind);
        Assert.True(dep.Blocking);
    }

    [Fact]
    public void Dependencies_is_null_when_absent()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-aup-draft.json")
            .Replace("\"dependencies\":", "\"dependenciesRenamedAway\":");
        var agr = AgrParser.Parse(text);

        Assert.Null(agr.Dependencies);
    }
```

(If `AgrParserTests` already exists, add only the two methods and reuse its existing usings. `SemanticTestSupport.FixtureText` is the existing helper used across the test project.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~AgrParserTests.Parses_the_dependencies_array`
Expected: BUILD FAILS - `AgrDocument.Dependencies` and `AgrDependency` do not exist.

- [ ] **Step 3: Add the model**

In `src/Registrar.Core/Model/AgrDocument.cs`, add the `Dependencies` property to `AgrDocument` (after `RenderingOptions`):

```csharp
    [JsonPropertyName("dependencies")]     public IReadOnlyList<AgrDependency>? Dependencies { get; init; }
```

And add the new record (place it after `AgrRenderingOptions` at the end of the file):

```csharp
public sealed record AgrDependency
{
    [JsonPropertyName("dependencyId")] public required string DependencyId { get; init; }
    [JsonPropertyName("kind")]         public required string Kind { get; init; }
    [JsonPropertyName("blocking")]     public required bool Blocking { get; init; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~AgrParserTests`
Expected: PASS (the two new tests plus any pre-existing ones).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Model/AgrDocument.cs tests/Registrar.Tests/Model/AgrParserTests.cs
git commit -m "feat: parse the AGR dependencies array into AgrDocument"
```

---

### Task 2: ArtifactManifest model and serializer

**Files:**
- Modify: `src/Registrar.Core/EngineInfo.cs`
- Create: `src/Registrar.Core/Model/ArtifactManifest.cs`
- Create: `src/Registrar.Core/ArtifactManifestSerializer.cs`
- Test: `tests/Registrar.Tests/ArtifactManifestSerializerTests.cs`

**Interfaces:**
- Consumes: `ReportSerializer.Options` (existing, `public static`).
- Produces:
  - `EngineInfo.ManifestVersion` (`const string`, `"1.0.0"`).
  - `Registrar.Core.Model.ArtifactManifest` and nested `ManifestIdentity`, `ManifestContext`, `ManifestSection`, `ManifestDependency`, `ManifestRegistryReference` (all init-only, `[JsonPropertyName]` camelCase).
  - `Registrar.Core.ArtifactManifestSerializer.ToJson(ArtifactManifest) -> string` and `FromJson(string) -> ArtifactManifest`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/ArtifactManifestSerializerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests;

public class ArtifactManifestSerializerTests
{
    internal static ArtifactManifest Sample() => new()
    {
        ManifestVersion = EngineInfo.ManifestVersion,
        ExecutionId = "GOV-2026-07-15-001",
        DocumentId = "ORG-GOV-DS-016",
        EngineVersion = EngineInfo.Version,
        TimestampUtc = "2026-07-15T12:00:00.0000000+00:00",
        Identity = new ManifestIdentity
        {
            ArtifactType = "DS", DocumentId = "ORG-GOV-DS-016", Version = "v1.0",
            Classification = "Client-Facing", ApprovalStatus = "Draft", Layer = "Governance"
        },
        Context = new ManifestContext
        {
            Owner = "Registry Owner", Audience = "Leadership", PreparedBy = "ORG",
            Client = "Talewind CPA", Engagement = "Pilot", ReviewDate = null
        },
        Sections = new[]
        {
            new ManifestSection { Number = 1, Title = "Purpose", BlockCount = 1, ClauseIds = new List<string>() },
            new ManifestSection { Number = 5, Title = "Audit", BlockCount = 2, ClauseIds = new[] { "CL-006" } }
        },
        Dependencies = new[]
        {
            new ManifestDependency { DependencyId = "ORG-MST-SCL", Kind = "clause-library", Blocking = true }
        },
        RegistryReference = null,
        ArtifactSha256 = new string('a', 64)
    };

    [Fact]
    public void Serialized_manifest_uses_camelCase_field_names()
    {
        var node = JsonNode.Parse(ArtifactManifestSerializer.ToJson(Sample()))!.AsObject();

        Assert.Equal("1.0.0", (string?)node["manifestVersion"]);
        Assert.Equal("ORG-GOV-DS-016", (string?)node["documentId"]);
        Assert.Equal("DS", (string?)node["identity"]!["artifactType"]);
        Assert.Equal("Purpose", (string?)node["sections"]!.AsArray()[0]!["title"]);
        Assert.Equal("CL-006", (string?)node["sections"]!.AsArray()[1]!["clauseIds"]!.AsArray()[0]);
        Assert.Equal("clause-library", (string?)node["dependencies"]!.AsArray()[0]!["kind"]);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var restored = ArtifactManifestSerializer.FromJson(ArtifactManifestSerializer.ToJson(Sample()));

        Assert.Equal("GOV-2026-07-15-001", restored.ExecutionId);
        Assert.Equal(2, restored.Sections.Count);
        Assert.Equal("CL-006", restored.Sections[1].ClauseIds[0]);
        Assert.Null(restored.RegistryReference);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~ArtifactManifestSerializerTests`
Expected: BUILD FAILS - the types do not exist yet.

- [ ] **Step 3: Add the ManifestVersion constant**

In `src/Registrar.Core/EngineInfo.cs`, add next to the other version constants:

```csharp
    public const string ManifestVersion = "1.0.0";  // artifact-manifest manifestVersion const
```

- [ ] **Step 4: Create the model**

Create `src/Registrar.Core/Model/ArtifactManifest.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record ArtifactManifest
{
    [JsonPropertyName("manifestVersion")]  public required string ManifestVersion { get; init; }
    [JsonPropertyName("executionId")]      public required string ExecutionId { get; init; }
    [JsonPropertyName("documentId")]       public required string DocumentId { get; init; }
    [JsonPropertyName("engineVersion")]    public required string EngineVersion { get; init; }
    [JsonPropertyName("timestampUtc")]     public required string TimestampUtc { get; init; }
    [JsonPropertyName("identity")]         public required ManifestIdentity Identity { get; init; }
    [JsonPropertyName("context")]          public required ManifestContext Context { get; init; }
    [JsonPropertyName("sections")]         public required IReadOnlyList<ManifestSection> Sections { get; init; }
    [JsonPropertyName("dependencies")]     public required IReadOnlyList<ManifestDependency> Dependencies { get; init; }
    [JsonPropertyName("registryReference")] public ManifestRegistryReference? RegistryReference { get; init; }
    [JsonPropertyName("artifactSha256")]   public required string ArtifactSha256 { get; init; }
}

public sealed record ManifestIdentity
{
    [JsonPropertyName("artifactType")]   public required string ArtifactType { get; init; }
    [JsonPropertyName("documentId")]     public required string DocumentId { get; init; }
    [JsonPropertyName("version")]        public required string Version { get; init; }
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("approvalStatus")] public required string ApprovalStatus { get; init; }
    [JsonPropertyName("layer")]          public required string Layer { get; init; }
}

public sealed record ManifestContext
{
    [JsonPropertyName("owner")]      public required string Owner { get; init; }
    [JsonPropertyName("audience")]   public required string Audience { get; init; }
    [JsonPropertyName("preparedBy")] public required string PreparedBy { get; init; }
    [JsonPropertyName("client")]     public string? Client { get; init; }
    [JsonPropertyName("engagement")] public string? Engagement { get; init; }
    [JsonPropertyName("reviewDate")] public string? ReviewDate { get; init; }
}

public sealed record ManifestSection
{
    [JsonPropertyName("number")]     public required int Number { get; init; }
    [JsonPropertyName("title")]      public required string Title { get; init; }
    [JsonPropertyName("blockCount")] public required int BlockCount { get; init; }
    [JsonPropertyName("clauseIds")]  public required IReadOnlyList<string> ClauseIds { get; init; }
}

public sealed record ManifestDependency
{
    [JsonPropertyName("dependencyId")] public required string DependencyId { get; init; }
    [JsonPropertyName("kind")]         public required string Kind { get; init; }
    [JsonPropertyName("blocking")]     public required bool Blocking { get; init; }
}

public sealed record ManifestRegistryReference
{
    [JsonPropertyName("documentId")] public required string DocumentId { get; init; }
    [JsonPropertyName("fromStatus")] public required string FromStatus { get; init; }
    [JsonPropertyName("toStatus")]   public required string ToStatus { get; init; }
    [JsonPropertyName("version")]    public required string Version { get; init; }
}
```

- [ ] **Step 5: Create the serializer**

Create `src/Registrar.Core/ArtifactManifestSerializer.cs`:

```csharp
using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core;

public static class ArtifactManifestSerializer
{
    public static string ToJson(ArtifactManifest manifest)
        => JsonSerializer.Serialize(manifest, ReportSerializer.Options);

    public static ArtifactManifest FromJson(string json)
        => JsonSerializer.Deserialize<ArtifactManifest>(json, ReportSerializer.Options)
           ?? throw new JsonException("artifact-manifest.json deserialized to null.");
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~ArtifactManifestSerializerTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/EngineInfo.cs src/Registrar.Core/Model/ArtifactManifest.cs src/Registrar.Core/ArtifactManifestSerializer.cs tests/Registrar.Tests/ArtifactManifestSerializerTests.cs
git commit -m "feat: ArtifactManifest model and serializer"
```

---

### Task 3: ArtifactManifestBuilder

**Files:**
- Create: `src/Registrar.Core/ArtifactManifestBuilder.cs`
- Test: `tests/Registrar.Tests/ArtifactManifestBuilderTests.cs`

**Interfaces:**
- Consumes: `ValidationReport`, `AgrDocument` (with `Dependencies` from Task 1), `PromotionPayload?`, and the model + `EngineInfo.ManifestVersion` from Task 2. `AgrBlock.BlockType == "clauseInsertion"` identifies clause blocks; `AgrBlock.ClauseId` is the id.
- Produces: `Registrar.Core.ArtifactManifestBuilder.Build(ValidationReport mergedReport, AgrDocument agr, PromotionPayload? payload) -> ArtifactManifest`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/ArtifactManifestBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests;

public class ArtifactManifestBuilderTests
{
    private static ValidationReport Report(string sha = "abc") => new()
    {
        ReportVersion = EngineInfo.ReportVersion,
        ExecutionId = "GOV-2026-07-15-003",
        DocumentId = "ORG-GOV-DS-016",
        RequestedMode = "draft",
        OverallResult = "pass",
        EngineVersion = EngineInfo.Version,
        ConfigVersions = new Dictionary<string, string>(),
        TimestampUtc = "2026-07-15T12:00:00.0000000+00:00",
        ArtifactSha256 = sha,
        Checks = System.Array.Empty<Check>()
    };

    private static AgrDocument Agr(IReadOnlyList<AgrDependency>? deps = null) => new()
    {
        Identity = new AgrIdentity
        {
            ArtifactType = "DS", DocumentId = "ORG-GOV-DS-016", Version = "v1.0",
            Classification = "Client-Facing", ApprovalStatus = "Draft", Layer = "Governance"
        },
        Context = new AgrContext
        {
            Owner = "Registry Owner", Audience = "Leadership", PreparedBy = "ORG",
            Client = "Talewind CPA", Engagement = "Pilot", ReviewDate = null, ReviewDateOverride = null
        },
        DataInputs = new AgrDataInputs
        {
            PlaceholderMap = new Dictionary<string, string?>(),
            Sections = new[]
            {
                new AgrSection { Title = "Purpose", DescriptorLine = null, Blocks = new[]
                    { new AgrBlock { BlockType = "paragraph", Text = "x" } } },
                new AgrSection { Title = "Audit", DescriptorLine = null, Blocks = new[]
                    { new AgrBlock { BlockType = "paragraph", Text = "y" },
                      new AgrBlock { BlockType = "clauseInsertion", ClauseId = "CL-006" } } }
            }
        },
        ValidationPolicy = new AgrValidationPolicy { Mode = "draft", WarningThreshold = null },
        RenderingOptions = new AgrRenderingOptions { PackageName = "P", OutputFileName = "o.docx" },
        Dependencies = deps
    };

    private static PromotionPayload Payload() => new()
    {
        PayloadVersion = EngineInfo.PayloadVersion, ExecutionId = "GOV-2026-07-15-003",
        DocumentId = "ORG-GOV-DS-016", ArtifactType = "DS", Version = "v1.0",
        GeneratedUtc = "2026-07-15T12:00:00.0000000+00:00",
        RegistryUpdate = new RegistryUpdate
            { DocumentId = "ORG-GOV-DS-016", FromStatus = "Draft", ToStatus = "In Review", Version = "v1.0" },
        RepositoryTarget = "Controlled Documents/Governance",
        Attestation = new Attestation { CheckId = "QA-SPEC-18", Statement = "s", RequiresOperatorSignoff = true }
    };

    [Fact]
    public void Echoes_identity_context_and_header()
    {
        var m = ArtifactManifestBuilder.Build(Report(), Agr(), null);

        Assert.Equal(EngineInfo.ManifestVersion, m.ManifestVersion);
        Assert.Equal("GOV-2026-07-15-003", m.ExecutionId);
        Assert.Equal("2026-07-15T12:00:00.0000000+00:00", m.TimestampUtc);
        Assert.Equal("DS", m.Identity.ArtifactType);
        Assert.Equal("Registry Owner", m.Context.Owner);
        Assert.Equal("abc", m.ArtifactSha256);
    }

    [Fact]
    public void Builds_a_lean_section_inventory()
    {
        var m = ArtifactManifestBuilder.Build(Report(), Agr(), null);

        Assert.Equal(2, m.Sections.Count);
        Assert.Equal(1, m.Sections[0].Number);
        Assert.Equal("Purpose", m.Sections[0].Title);
        Assert.Equal(1, m.Sections[0].BlockCount);
        Assert.Empty(m.Sections[0].ClauseIds);
        Assert.Equal(2, m.Sections[1].Number);
        Assert.Equal(2, m.Sections[1].BlockCount);
        Assert.Equal(new[] { "CL-006" }, m.Sections[1].ClauseIds);
    }

    [Fact]
    public void Echoes_dependencies_or_empty_when_none()
    {
        var withDeps = ArtifactManifestBuilder.Build(Report(), Agr(new[]
            { new AgrDependency { DependencyId = "ORG-MST-SCL", Kind = "clause-library", Blocking = true } }), null);
        var dep = Assert.Single(withDeps.Dependencies);
        Assert.Equal("ORG-MST-SCL", dep.DependencyId);
        Assert.True(dep.Blocking);

        var noDeps = ArtifactManifestBuilder.Build(Report(), Agr(null), null);
        Assert.Empty(noDeps.Dependencies);
    }

    [Fact]
    public void RegistryReference_is_populated_from_payload_and_null_without_one()
    {
        var withPayload = ArtifactManifestBuilder.Build(Report(), Agr(), Payload());
        Assert.NotNull(withPayload.RegistryReference);
        Assert.Equal("Draft", withPayload.RegistryReference!.FromStatus);
        Assert.Equal("In Review", withPayload.RegistryReference.ToStatus);

        var noPayload = ArtifactManifestBuilder.Build(Report(), Agr(), null);
        Assert.Null(noPayload.RegistryReference);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~ArtifactManifestBuilderTests`
Expected: BUILD FAILS - `ArtifactManifestBuilder` does not exist.

- [ ] **Step 3: Write the builder**

Create `src/Registrar.Core/ArtifactManifestBuilder.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Core;

// Pure assembly of the artifact manifest from the post-render merged report, the
// parsed AGR, and the optional promotion payload. No clock, no I/O, no RenderResult:
// the artifact hash is already stamped on the report, and sections render 1:1 from
// the AGR, so every field is a pure function of these three inputs.
public static class ArtifactManifestBuilder
{
    public static ArtifactManifest Build(
        ValidationReport mergedReport, AgrDocument agr, PromotionPayload? payload)
        => new()
        {
            ManifestVersion = EngineInfo.ManifestVersion,
            ExecutionId = mergedReport.ExecutionId,
            DocumentId = mergedReport.DocumentId,
            EngineVersion = mergedReport.EngineVersion,
            TimestampUtc = mergedReport.TimestampUtc,
            Identity = new ManifestIdentity
            {
                ArtifactType = agr.Identity.ArtifactType,
                DocumentId = agr.Identity.DocumentId,
                Version = agr.Identity.Version,
                Classification = agr.Identity.Classification,
                ApprovalStatus = agr.Identity.ApprovalStatus,
                Layer = agr.Identity.Layer
            },
            Context = new ManifestContext
            {
                Owner = agr.Context.Owner,
                Audience = agr.Context.Audience,
                PreparedBy = agr.Context.PreparedBy,
                Client = agr.Context.Client,
                Engagement = agr.Context.Engagement,
                ReviewDate = agr.Context.ReviewDate
            },
            Sections = agr.DataInputs.Sections.Select((section, index) => new ManifestSection
            {
                Number = index + 1,
                Title = section.Title,
                BlockCount = section.Blocks.Count,
                ClauseIds = section.Blocks
                    .Where(b => b.BlockType == "clauseInsertion" && b.ClauseId is not null)
                    .Select(b => b.ClauseId!)
                    .ToList()
            }).ToList(),
            Dependencies = (agr.Dependencies ?? Array.Empty<AgrDependency>())
                .Select(d => new ManifestDependency
                {
                    DependencyId = d.DependencyId, Kind = d.Kind, Blocking = d.Blocking
                }).ToList(),
            RegistryReference = payload is null ? null : new ManifestRegistryReference
            {
                DocumentId = payload.RegistryUpdate.DocumentId,
                FromStatus = payload.RegistryUpdate.FromStatus,
                ToStatus = payload.RegistryUpdate.ToStatus,
                Version = payload.RegistryUpdate.Version
            },
            // Guaranteed non-null: the manifest is built only after a DOCX was written,
            // which is exactly when PostRenderMerge stamped artifactSha256 on the report.
            ArtifactSha256 = mergedReport.ArtifactSha256
                ?? throw new InvalidOperationException(
                    "artifact manifest requires a stamped artifactSha256; build it only when a DOCX was produced.")
        };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~ArtifactManifestBuilderTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/ArtifactManifestBuilder.cs tests/Registrar.Tests/ArtifactManifestBuilderTests.cs
git commit -m "feat: ArtifactManifestBuilder assembles the manifest from report, AGR, and payload"
```

---

### Task 4: artifact-manifest.schema.json and conformance test

**Files:**
- Create: `schemas/artifact-manifest.schema.json`
- Test: `tests/Registrar.Tests/ArtifactManifestSerializerTests.cs` (add one test to the Task 2 class)

**Interfaces:**
- Consumes: `ArtifactManifestSerializer.ToJson` (Task 2), `ConfigLocator.FindSchema`, `Registrar.Validation.IsolatedSchema.FromFile`, `Json.Schema`.
- Produces: `schemas/artifact-manifest.schema.json`, discoverable via `ConfigLocator.FindSchema(AppContext.BaseDirectory, "artifact-manifest.schema.json")`.

- [ ] **Step 1: Write the failing test**

Add to the existing `ArtifactManifestSerializerTests` class. Add these usings to the top of the file if not present: `using System.Text.Json;`, `using Json.Schema;`, `using Registrar.Validation;`.

```csharp
    [Fact]
    public void Serialized_manifest_conforms_to_artifact_manifest_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "artifact-manifest.schema.json");
        var schema = IsolatedSchema.FromFile(schemaPath);
        using var document = JsonDocument.Parse(ArtifactManifestSerializer.ToJson(Sample()));

        var result = schema.Evaluate(document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Emitted artifact manifest failed artifact-manifest schema validation.");
    }
```

Note: `IsolatedSchema` lives in `Registrar.Validation` (it builds against a fresh `SchemaRegistry`); resolve it via `using Registrar.Validation;`, exactly as `PromotionPayloadSerializerTests` and `GenerationLogSerializerTests` do. Do NOT create a second `IsolatedSchema` type.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ArtifactManifestSerializerTests.Serialized_manifest_conforms_to_artifact_manifest_schema"`
Expected: FAIL - `ConfigLocator.FindSchema` throws `FileNotFoundException` (schema absent).

- [ ] **Step 3: Author the schema**

Create `schemas/artifact-manifest.schema.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://ironwright.dev/schemas/registrar/artifact-manifest.schema.json",
  "title": "ArtifactManifest",
  "description": "Output-package file describing a produced artifact: resolved metadata, a lean section inventory, the declared dependency map, the promotion registry reference, and the artifact SHA-256. Emitted on build only when a DOCX is produced. Correlated to the other package files by executionId. Reproducible under a fixed clock.",
  "type": "object",
  "additionalProperties": false,
  "required": ["manifestVersion", "executionId", "documentId", "engineVersion", "timestampUtc", "identity", "context", "sections", "dependencies", "artifactSha256"],
  "properties": {
    "manifestVersion": { "type": "string", "const": "1.0.0" },
    "executionId": { "type": "string", "description": "Echoes sourceWorkflowContext.executionId; correlates to the other package files." },
    "documentId": { "type": "string" },
    "engineVersion": { "type": "string" },
    "timestampUtc": { "type": "string", "format": "date-time" },
    "identity": {
      "type": "object",
      "additionalProperties": false,
      "required": ["artifactType", "documentId", "version", "classification", "approvalStatus", "layer"],
      "properties": {
        "artifactType": { "type": "string" },
        "documentId": { "type": "string" },
        "version": { "type": "string" },
        "classification": { "type": "string" },
        "approvalStatus": { "type": "string" },
        "layer": { "type": "string" }
      }
    },
    "context": {
      "type": "object",
      "additionalProperties": false,
      "required": ["owner", "audience", "preparedBy"],
      "properties": {
        "owner": { "type": "string" },
        "audience": { "type": "string" },
        "preparedBy": { "type": "string" },
        "client": { "type": ["string", "null"] },
        "engagement": { "type": ["string", "null"] },
        "reviewDate": { "type": ["string", "null"] }
      }
    },
    "sections": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["number", "title", "blockCount", "clauseIds"],
        "properties": {
          "number": { "type": "integer", "minimum": 1 },
          "title": { "type": "string" },
          "blockCount": { "type": "integer", "minimum": 0 },
          "clauseIds": { "type": "array", "items": { "type": "string" } }
        }
      }
    },
    "dependencies": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["dependencyId", "kind", "blocking"],
        "properties": {
          "dependencyId": { "type": "string" },
          "kind": { "type": "string" },
          "blocking": { "type": "boolean" }
        }
      }
    },
    "registryReference": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "required": ["documentId", "fromStatus", "toStatus", "version"],
      "properties": {
        "documentId": { "type": "string" },
        "fromStatus": { "type": "string" },
        "toStatus": { "type": "string" },
        "version": { "type": "string" }
      }
    },
    "artifactSha256": { "type": "string", "description": "SHA-256 of the emitted DOCX. Always present because the manifest is emitted only when a DOCX is produced." }
  }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~ArtifactManifestSerializerTests`
Expected: PASS (3 tests in the class).

- [ ] **Step 5: Commit**

```bash
git add schemas/artifact-manifest.schema.json tests/Registrar.Tests/ArtifactManifestSerializerTests.cs
git commit -m "feat: artifact-manifest.schema.json and conformance test"
```

---

### Task 5: Emit artifact-manifest.json from BuildCommand

**Files:**
- Modify: `src/Registrar.Cli/BuildCommand.cs`
- Test: `tests/Registrar.Tests/BuildCommandTests.cs`

**Interfaces:**
- Consumes: `ArtifactManifestBuilder.Build` (Task 3), `ArtifactManifestSerializer.ToJson` (Task 2), the in-scope `mergedReport` (`ValidationReport`), `agr` (`AgrDocument`, parsed at BuildCommand.cs:52), and `merged.Payload` (`PromotionPayload?`).
- Produces: `artifact-manifest.json` written to the AGR's directory whenever a DOCX is written.

- [ ] **Step 1: Write the failing tests**

Add to the existing `BuildCommandTests` class (reuses its `FixedClock`, `RepoRoot`, `CopyToTemp` helpers; the file already has `using System.Text.Json.Nodes;`):

```csharp
    [Fact]
    public void Draft_build_writes_manifest_with_sections_and_null_registry_reference()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var manifestPath = Path.Combine(dir, "artifact-manifest.json");
        Assert.True(File.Exists(manifestPath));

        var m = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        Assert.Equal("1.0.0", (string?)m["manifestVersion"]);
        Assert.Equal("AUP", (string?)m["identity"]!["artifactType"]);
        Assert.NotEmpty(m["sections"]!.AsArray());
        Assert.Equal(1, (int)m["sections"]!.AsArray()[0]!["number"]!);
        Assert.Null(m["registryReference"]);
        // artifactSha256 must equal the DOCX on disk (same fact the report pins).
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        Assert.Equal(Hashing.Sha256Hex(File.ReadAllBytes(docx)), (string?)m["artifactSha256"]);
    }

    [Fact]
    public void Promotion_build_writes_manifest_with_registry_reference()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            "promotion", registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var m = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "artifact-manifest.json")))!.AsObject();
        Assert.Equal("In Review", (string?)m["registryReference"]!["toStatus"]);
        Assert.Equal("ORG-GOV-DS-016", (string?)m["registryReference"]!["documentId"]);
    }

    [Fact]
    public void Pre_render_validation_failure_writes_no_manifest()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-prohibited-char.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(1, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.False(File.Exists(Path.Combine(dir, "artifact-manifest.json")));
    }

    [Fact]
    public void Manifest_is_byte_identical_across_runs_under_a_fixed_clock()
    {
        var a = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        Assert.Equal(0, BuildCommand.Run(a, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var mA = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(a)!, "artifact-manifest.json"));

        var b = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        Assert.Equal(0, BuildCommand.Run(b, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var mB = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(b)!, "artifact-manifest.json"));

        Assert.Equal(mA, mB);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BuildCommandTests.Draft_build_writes_manifest_with_sections_and_null_registry_reference|FullyQualifiedName~BuildCommandTests.Promotion_build_writes_manifest_with_registry_reference|FullyQualifiedName~BuildCommandTests.Manifest_is_byte_identical_across_runs_under_a_fixed_clock"`
Expected: FAIL - no `artifact-manifest.json` is written. (The pre-render test passes trivially.)

- [ ] **Step 3: Wire the write into BuildCommand**

In `src/Registrar.Cli/BuildCommand.cs`, find the `docxEligible` DOCX-write block (currently lines 90-103):

```csharp
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
```

Add the manifest write immediately after the successful DOCX write, inside the `try`, so the manifest is written only once the DOCX itself is on disk:

```csharp
        if (docxEligible)
        {
            try
            {
                var docxPath = Path.Combine(dir, agr.RenderingOptions.OutputFileName);
                File.WriteAllBytes(docxPath, rendered.Bytes);
                stdout.WriteLine($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - {Path.GetFileName(docxPath)}");

                // The manifest describes the artifact just written, so it is emitted here
                // and nowhere else: no DOCX means no manifest.
                var manifest = ArtifactManifestBuilder.Build(mergedReport, agr, merged.Payload);
                File.WriteAllText(Path.Combine(dir, "artifact-manifest.json"),
                    ArtifactManifestSerializer.ToJson(manifest));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: cannot write document: {ex.Message}");
                return 2;
            }
        }
```

`using Registrar.Core;` is already present at the top of `BuildCommand.cs` (it brings in `ArtifactManifestBuilder` and `ArtifactManifestSerializer`); `merged` is the `ValidationOutcome` in scope, so `merged.Payload` is available. No new using is required.

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BuildCommandTests.Draft_build_writes_manifest_with_sections_and_null_registry_reference|FullyQualifiedName~BuildCommandTests.Promotion_build_writes_manifest_with_registry_reference|FullyQualifiedName~BuildCommandTests.Pre_render_validation_failure_writes_no_manifest|FullyQualifiedName~BuildCommandTests.Manifest_is_byte_identical_across_runs_under_a_fixed_clock"`
Expected: PASS (4 tests).

- [ ] **Step 5: Run the full suite to confirm no regressions**

Run: `dotnet test`
Expected: PASS - all existing tests plus the new ones, build clean, 0 warnings. (The `FixtureTestRunner` is unaffected: it compares only `validation-report.json` and `promotion-payload.json`, so the extra `artifact-manifest.json` in each fixture's temp work dir is ignored.)

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Cli/BuildCommand.cs tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "feat: emit artifact-manifest.json on build when a DOCX is produced"
```

---

### Task 6: Documentation reconciliation

**Files:**
- Modify: `CLAUDE.md` (Output package section)
- Modify: `docs/ROADMAP.md`, `docs/governor-briefing.md`, `docs/v2-acceptance-roadmap.md`, `docs/v2-coding-agent-directive.md`

**Interfaces:**
- Consumes: nothing (documentation only). Produces: updated status text. No code, no tests.

- [ ] **Step 1: Update CLAUDE.md**

In `CLAUDE.md`, under "Output package (per run)", move `artifact-manifest.json` from deferred into the emitted set. The package is now FIVE files (DOCX, `validation-report.json`, `promotion-payload.json`, `generation-log.json`, `artifact-manifest.json`); nothing in the output package remains deferred. State that the manifest is emitted only when a DOCX is produced and conforms to `schemas/artifact-manifest.schema.json`. Update the intro count and remove the "Deferred to post-v2" output-package entry (or reword it, since `generation-log.json` and `artifact-manifest.json` are now both shipped). Keep the surrounding wording style; plain ASCII hyphens only.

- [ ] **Step 2: Reconcile the other docs**

Following the "update live, annotate historical" convention already used for `generation-log.json` in these files:
- `docs/ROADMAP.md`: mark the "Output-package completion" item complete (both files shipped 2026-07-15); remove or close the `artifact-manifest.json` open work.
- `docs/governor-briefing.md`: move `artifact-manifest.json` from the deferred/planned grouping into the emitted output-package description.
- `docs/v2-acceptance-roadmap.md` (line ~273 "Post-v2 Backlog (Deferred)") and `docs/v2-coding-agent-directive.md`: these are historical v2-milestone records - annotate the `artifact-manifest.json` references with a brief "(shipped 2026-07-15, post-v2)" note rather than rewriting the original deferral.

Use the date 2026-07-15 for shipped references. If the pre-commit AI-tell lint hook rejects a commit, read its message and fix the prose (do NOT use `--no-verify`).

- [ ] **Step 3: Verify the tree is still green**

Run: `dotnet test`
Expected: PASS (documentation changes do not affect tests; this confirms the tree before commit).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md docs/ROADMAP.md docs/governor-briefing.md docs/v2-acceptance-roadmap.md docs/v2-coding-agent-directive.md
git commit -m "docs: mark artifact-manifest.json emitted; output package complete"
```

---

## Self-Review

**Spec coverage:**
- Content table (header, resolved metadata, section inventory, dependency map, registry reference, artifact SHA) -> Task 2 (model) + Task 3 (builder). Covered.
- Section inventory lean shape (number, title, blockCount, clauseIds) -> Task 3 builder + test. Covered.
- Dependencies parsed and echoed -> Task 1 (parser) + Task 3 (echo). Covered.
- registryReference from payload at promotion, null in draft -> Task 3 test + Task 5 integration. Covered.
- Emission only when a DOCX is produced -> Task 5 Step 3 placement (inside `docxEligible` after DOCX write) + Step 1 tests (no manifest on pre-render fail). Covered.
- Schema + conformance -> Task 4. Covered.
- Determinism (byte-identical under fixed clock) -> Task 5 determinism test. Covered.
- Self-contained header + executionId correlation -> Task 2 model. Covered.
- artifactSha256 always populated -> Task 3 builder guard + schema `required` non-null. Covered.
- Docs reconciliation (package now five files) -> Task 6. Covered.

**Placeholder scan:** No TBD/TODO/"add appropriate X"; every code and test step shows complete content. Clean.

**Type consistency:** `ArtifactManifest` and its nested records, `ArtifactManifestBuilder.Build(ValidationReport, AgrDocument, PromotionPayload?)`, `ArtifactManifestSerializer.ToJson/FromJson`, `EngineInfo.ManifestVersion`, and `AgrDependency` / `AgrDocument.Dependencies` are named identically across Tasks 1-5. The builder consumes real model shapes: `AgrIdentity`/`AgrContext`/`AgrSection`/`AgrBlock` (`BlockType`, `ClauseId`), `RegistryUpdate` (`DocumentId`/`FromStatus`/`ToStatus`/`Version`), and `ValidationReport.ArtifactSha256` (`string?`). Consistent.
