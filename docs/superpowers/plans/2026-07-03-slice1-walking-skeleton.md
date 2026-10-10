# Slice 1: Walking Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `registrar validate <agr.json>`, which validates an Artifact Generation Request against `schemas/agr.schema.json` (draft 2020-12) and writes a conforming `validation-report.json`.

**Architecture:** A .NET 8 solution with three source projects (`Registrar.Core` domain contracts, `Registrar.Validation` validation layers, `Registrar.Cli` console entry) plus an `Registrar.Tests` xUnit project. The CLI reads an AGR, the single `SchemaValidationLayer` evaluates it with JsonSchema.Net, each violation becomes a `QA-SCHEMA-01` check, and the assembled report is serialized next to the input. The `Rendering` project and semantic layers are deferred to later slices.

**Tech Stack:** .NET 8, C#, JsonSchema.Net (json-everything, draft 2020-12), System.Text.Json, System.CommandLine, xUnit.

## Global Constraints

- **.NET 8** target framework (`net8.0`); repo has SDK 8.0.318. Do not use `net9.0`/`net10.0` features (e.g. `[JsonStringEnumMemberName]`).
- **Fail closed:** any blocking check failure yields a non-zero process exit and no downstream artifact.
- **Zero LLM calls at runtime.** Pure deterministic validation.
- **Configuration over code:** `schemas/agr.schema.json` is the authoritative contract. No AGR field names, patterns, or enum values are hard-coded in engine logic except the single `checkId` literal `QA-SCHEMA-01`.
- **Report contract:** the emitted report MUST validate against `schemas/validation-report.schema.json`. `reportVersion` const `"1.0.0"`.
- **Determinism in tests:** time comes from an injected `IClock`; tests use a fixed instant so report bytes are stable.
- **Windows 11 / PowerShell 7.x** dev environment. Paths in docs use `\`; `dotnet` commands are shell-agnostic.
- **Output hygiene:** US English; no emojis, em/en dashes, or decorative unicode in any source, config, or emitted string.
- **Value strings are centralized** in `Registrar.Core` constant classes (see Task 2), never inline magic strings. We use `static class` constant holders rather than CLR enums so the wire format is a direct, converter-free 1:1 with the schema's string values.

---

### Task 1: Solution scaffold, projects, packages, and CI

**Files:**
- Create: `Registrar.sln`
- Create: `src/Registrar.Core/Registrar.Core.csproj`
- Create: `src/Registrar.Validation/Registrar.Validation.csproj`
- Create: `src/Registrar.Cli/Registrar.Cli.csproj`
- Create: `tests/Registrar.Tests/Registrar.Tests.csproj`
- Create: `tests/Registrar.Tests/SmokeTest.cs`
- Create: `.gitignore`
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: the solution structure and project references every later task builds in. Project reference graph: `Cli -> Validation -> Core`; `Tests -> Cli, Validation, Core`.

- [ ] **Step 1: Create the solution and projects**

Run (from repo root `C:\GitRepos\Artifact-Generator`):

```bash
dotnet new sln -n Registrar
dotnet new classlib -n Registrar.Core -o src/Registrar.Core -f net8.0
dotnet new classlib -n Registrar.Validation -o src/Registrar.Validation -f net8.0
dotnet new console -n Registrar.Cli -o src/Registrar.Cli -f net8.0
dotnet new xunit -n Registrar.Tests -o tests/Registrar.Tests -f net8.0
```

Delete the auto-generated placeholder files so they do not collide with later tasks:

```bash
rm src/Registrar.Core/Class1.cs src/Registrar.Validation/Class1.cs
```

- [ ] **Step 2: Wire references and packages**

```bash
dotnet sln add src/Registrar.Core src/Registrar.Validation src/Registrar.Cli tests/Registrar.Tests
dotnet add src/Registrar.Validation reference src/Registrar.Core
dotnet add src/Registrar.Cli reference src/Registrar.Validation src/Registrar.Core
dotnet add tests/Registrar.Tests reference src/Registrar.Cli src/Registrar.Validation src/Registrar.Core
dotnet add src/Registrar.Validation package JsonSchema.Net
dotnet add tests/Registrar.Tests package JsonSchema.Net
dotnet add src/Registrar.Cli package System.CommandLine --prerelease
```

Note: `JsonSchema.Net` latest stable (>= 5.x) supports draft 2020-12 (resolved to 9.2.2 at build time). `System.CommandLine` is prerelease, hence `--prerelease` (resolved to 3.0.0-preview.5); this is the accepted CLI dependency.

In every `.csproj`, ensure these are inside the main `<PropertyGroup>`:

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<LangVersion>latest</LangVersion>
```

(`dotnet new` sets these for net8.0 templates; add any that are missing.)

- [ ] **Step 3: Write the smoke test**

Create `tests/Registrar.Tests/SmokeTest.cs`:

```csharp
namespace Registrar.Tests;

public class SmokeTest
{
    [Fact]
    public void Solution_builds_and_tests_run()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Create `.gitignore`**

Create `.gitignore` at repo root:

```gitignore
# .NET build output
bin/
obj/

# Local scratch
*.user
.vs/
```

- [ ] **Step 5: Create the CI workflow**

Create `.github/workflows/ci.yml`:

```yaml
name: ci
on:
  push:
    branches: [main]
  pull_request:
jobs:
  build-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet build Registrar.sln --configuration Release
      - run: dotnet test Registrar.sln --configuration Release --no-build
```

Note: CI builds and tests only. It never executes real engagements (client data must not leave the tenant); tests run against synthetic fixtures only.

- [ ] **Step 6: Build and test**

Run: `dotnet build Registrar.sln`
Expected: Build succeeded, 0 errors.

Run: `dotnet test Registrar.sln`
Expected: Passed! - 1 test (the smoke test).

- [ ] **Step 7: Commit**

```bash
git add Registrar.sln src/ tests/ .gitignore .github/
git commit -m "chore: scaffold Registrar solution, projects, and CI"
```

---

### Task 2: Core primitives (value constants, clock, hashing, versions, config locator)

**Files:**
- Create: `src/Registrar.Core/Vocabulary.cs`
- Create: `src/Registrar.Core/IClock.cs`
- Create: `src/Registrar.Core/SystemClock.cs`
- Create: `src/Registrar.Core/Hashing.cs`
- Create: `src/Registrar.Core/EngineInfo.cs`
- Create: `src/Registrar.Core/ConfigLocator.cs`
- Test: `tests/Registrar.Tests/Core/HashingTests.cs`
- Test: `tests/Registrar.Tests/Core/ConfigLocatorTests.cs`

**Interfaces:**
- Consumes: nothing beyond Task 1 structure.
- Produces:
  - `Registrar.Core.Vocabulary` static classes: `Layer` (`Input`,`Content`,`Structural`,`Release` = `"input"`,`"content"`,`"structural"`,`"release"`), `CheckResultValue` (`Pass`,`Warn`,`Fail`,`Skipped`), `Severity` (`Blocking`,`Warning`,`Info`), `ErrorCategory` (`SchemaValidationError`,`ConfigurationError`, ... all schema values), `OverallResult` (`Pass`=`"pass"`,`PassWithWarnings`=`"pass-with-warnings"`,`Fail`=`"fail"`), `Mode` (`Draft`,`Promotion`).
  - `interface IClock { DateTimeOffset UtcNow { get; } }`
  - `sealed class SystemClock : IClock` (real wall clock).
  - `static class Hashing { string Sha256Hex(string text); string Sha256HexOfFile(string path); }` (lowercase hex).
  - `static class EngineInfo { const string Version; const string SchemaVersion = "1.0.0"; const string ReportVersion = "1.0.0"; }`
  - `static class ConfigLocator { string FindSchema(string baseDir, string fileName); }` returns an absolute path to `schemas/<fileName>` by walking up from `baseDir` until a directory containing both `schemas/` and `config/` is found; throws `FileNotFoundException` if none.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Core/HashingTests.cs`:

```csharp
using Registrar.Core;

namespace Registrar.Tests.Core;

public class HashingTests
{
    [Fact]
    public void Sha256Hex_returns_known_lowercase_hex_for_empty_string()
    {
        // Well-known SHA-256 of the empty string.
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Hashing.Sha256Hex(string.Empty));
    }

    [Fact]
    public void Sha256Hex_is_stable_for_same_input()
    {
        Assert.Equal(Hashing.Sha256Hex("abc"), Hashing.Sha256Hex("abc"));
    }
}
```

Create `tests/Registrar.Tests/Core/ConfigLocatorTests.cs`:

```csharp
using Registrar.Core;

namespace Registrar.Tests.Core;

public class ConfigLocatorTests
{
    [Fact]
    public void FindSchema_walks_up_to_directory_containing_schemas_and_config()
    {
        // Arrange: temp/root/{schemas,config} and a nested start dir.
        var root = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(root, "schemas"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "schemas", "agr.schema.json"), "{}");
        var nested = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;

        var found = ConfigLocator.FindSchema(nested, "agr.schema.json");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "schemas", "agr.schema.json")),
            Path.GetFullPath(found));
    }

    [Fact]
    public void FindSchema_throws_when_no_repo_root_found()
    {
        var isolated = Directory.CreateTempSubdirectory().FullName;
        Assert.Throws<FileNotFoundException>(
            () => ConfigLocator.FindSchema(isolated, "agr.schema.json"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~Core"`
Expected: FAIL - `Hashing`/`ConfigLocator` do not exist (compile error).

- [ ] **Step 3: Implement the primitives**

Create `src/Registrar.Core/Vocabulary.cs`:

```csharp
namespace Registrar.Core;

// Wire-format string values from schemas/validation-report.schema.json.
// Centralized so no engine code carries magic strings.
public static class Layer
{
    public const string Input = "input";
    public const string Content = "content";
    public const string Structural = "structural";
    public const string Release = "release";
}

public static class CheckResultValue
{
    public const string Pass = "pass";
    public const string Warn = "warn";
    public const string Fail = "fail";
    public const string Skipped = "skipped";
}

public static class Severity
{
    public const string Blocking = "blocking";
    public const string Warning = "warning";
    public const string Info = "info";
}

public static class ErrorCategory
{
    public const string ConfigurationError = "ConfigurationError";
    public const string SchemaValidationError = "SchemaValidationError";
    public const string MetadataViolation = "MetadataViolation";
    public const string DependencyResolutionError = "DependencyResolutionError";
    public const string ClauseResolutionError = "ClauseResolutionError";
    public const string RegistrySyncError = "RegistrySyncError";
    public const string LifecycleTransitionError = "LifecycleTransitionError";
    public const string RenderingError = "RenderingError";
    public const string SharePointIntegrationError = "SharePointIntegrationError";
    public const string PromotionPolicyError = "PromotionPolicyError";
}

public static class OverallResult
{
    public const string Pass = "pass";
    public const string PassWithWarnings = "pass-with-warnings";
    public const string Fail = "fail";
}

public static class Mode
{
    public const string Draft = "draft";
    public const string Promotion = "promotion";
}
```

Create `src/Registrar.Core/IClock.cs`:

```csharp
namespace Registrar.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

Create `src/Registrar.Core/SystemClock.cs`:

```csharp
namespace Registrar.Core;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

Create `src/Registrar.Core/Hashing.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Registrar.Core;

public static class Hashing
{
    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Sha256HexOfFile(string path)
        => Sha256Hex(File.ReadAllText(path));
}
```

Create `src/Registrar.Core/EngineInfo.cs`:

```csharp
namespace Registrar.Core;

public static class EngineInfo
{
    public const string Version = "2.0.0-slice1";
    public const string SchemaVersion = "1.0.0";  // AGR schemaVersion const
    public const string ReportVersion = "1.0.0";  // report reportVersion const
}
```

Create `src/Registrar.Core/ConfigLocator.cs`:

```csharp
namespace Registrar.Core;

public static class ConfigLocator
{
    // Walk up from baseDir until a directory holds both schemas/ and config/,
    // then return the absolute path to schemas/<fileName>.
    public static string FindSchema(string baseDir, string fileName)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(baseDir));
        while (dir is not null)
        {
            var schemas = Path.Combine(dir.FullName, "schemas");
            var config = Path.Combine(dir.FullName, "config");
            if (Directory.Exists(schemas) && Directory.Exists(config))
            {
                var candidate = Path.Combine(schemas, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate schemas/{fileName} by walking up from {baseDir}.");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~Core"`
Expected: PASS - 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core tests/Registrar.Tests/Core
git commit -m "feat(core): add vocabulary, clock, hashing, engine info, config locator"
```

---

### Task 3: Validation report model and serializer

**Files:**
- Create: `src/Registrar.Core/Model/Check.cs`
- Create: `src/Registrar.Core/Model/ValidationReport.cs`
- Create: `src/Registrar.Core/ReportSerializer.cs`
- Test: `tests/Registrar.Tests/ReportSerializerTests.cs`

**Interfaces:**
- Consumes: `Vocabulary` constants, `EngineInfo` (Task 2).
- Produces:
  - `record Check` with init properties: `string CheckId`, `string LayerName`, `string Result`, `string SeverityLevel`, `string? ErrorCategory`, `string Message`, `string? JsonPath`, `string? Expected`, `string? Actual`, `string? Remediation`, `string? RuleSource`. (JSON names via `[JsonPropertyName]` map to `checkId`, `layer`, `result`, `severity`, `errorCategory`, `message`, `jsonPath`, `expected`, `actual`, `remediation`, `ruleSource`.)
  - `record ValidationReport` with init properties: `string ReportVersion`, `string ExecutionId`, `string DocumentId`, `string RequestedMode`, `string OverallResult`, `string EngineVersion`, `IReadOnlyDictionary<string,string> ConfigVersions`, `string TimestampUtc`, `string? ArtifactSha256`, `IReadOnlyList<Check> Checks`.
  - `static class ReportSerializer { string ToJson(ValidationReport report); JsonSerializerOptions Options { get; } }`

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/ReportSerializerTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;
using Json.Schema;

namespace Registrar.Tests;

public class ReportSerializerTests
{
    private static ValidationReport SampleReport() => new()
    {
        ReportVersion = EngineInfo.ReportVersion,
        ExecutionId = "GOV-2026-07-03-001",
        DocumentId = "ORG-GOV-DS-016",
        RequestedMode = Mode.Draft,
        OverallResult = OverallResult.Pass,
        EngineVersion = EngineInfo.Version,
        ConfigVersions = new Dictionary<string, string> { ["agr.schema.json"] = "abc123" },
        TimestampUtc = "2026-07-03T12:00:00.0000000+00:00",
        ArtifactSha256 = null,
        Checks = Array.Empty<Check>()
    };

    [Fact]
    public void Serialized_report_uses_camelCase_schema_field_names()
    {
        var json = ReportSerializer.ToJson(SampleReport());
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("1.0.0", (string?)node["reportVersion"]);
        Assert.Equal("draft", (string?)node["requestedMode"]);
        Assert.Equal("pass", (string?)node["overallResult"]);
        Assert.True(node.ContainsKey("configVersions"));
        Assert.True(node.ContainsKey("timestampUtc"));
    }

    [Fact]
    public void Serialized_report_conforms_to_validation_report_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(
            AppContext.BaseDirectory, "validation-report.schema.json");
        var schema = JsonSchema.FromFile(schemaPath);
        var instance = JsonNode.Parse(ReportSerializer.ToJson(SampleReport()));

        var result = schema.Evaluate(instance,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Emitted report failed report schema validation.");
    }
}
```

Note: `ConfigLocator.FindSchema` walks up from the test's `AppContext.BaseDirectory` (under `tests/Registrar.Tests/bin/...`) to the repo root, which contains `schemas/` and `config/`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ReportSerializerTests"`
Expected: FAIL - `Check`/`ValidationReport`/`ReportSerializer` do not exist.

- [ ] **Step 3: Implement the model and serializer**

Create `src/Registrar.Core/Model/Check.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record Check
{
    [JsonPropertyName("checkId")]    public required string CheckId { get; init; }
    [JsonPropertyName("layer")]      public required string LayerName { get; init; }
    [JsonPropertyName("result")]     public required string Result { get; init; }
    [JsonPropertyName("severity")]   public required string SeverityLevel { get; init; }
    [JsonPropertyName("errorCategory")] public string? ErrorCategory { get; init; }
    [JsonPropertyName("message")]    public required string Message { get; init; }
    [JsonPropertyName("jsonPath")]   public string? JsonPath { get; init; }
    [JsonPropertyName("expected")]   public string? Expected { get; init; }
    [JsonPropertyName("actual")]     public string? Actual { get; init; }
    [JsonPropertyName("remediation")] public string? Remediation { get; init; }
    [JsonPropertyName("ruleSource")] public string? RuleSource { get; init; }
}
```

Create `src/Registrar.Core/Model/ValidationReport.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record ValidationReport
{
    [JsonPropertyName("reportVersion")]  public required string ReportVersion { get; init; }
    [JsonPropertyName("executionId")]    public required string ExecutionId { get; init; }
    [JsonPropertyName("documentId")]     public required string DocumentId { get; init; }
    [JsonPropertyName("requestedMode")]  public required string RequestedMode { get; init; }
    [JsonPropertyName("overallResult")]  public required string OverallResult { get; init; }
    [JsonPropertyName("engineVersion")]  public required string EngineVersion { get; init; }
    [JsonPropertyName("configVersions")] public required IReadOnlyDictionary<string, string> ConfigVersions { get; init; }
    [JsonPropertyName("timestampUtc")]   public required string TimestampUtc { get; init; }
    [JsonPropertyName("artifactSha256")] public string? ArtifactSha256 { get; init; }
    [JsonPropertyName("checks")]         public required IReadOnlyList<Check> Checks { get; init; }
}
```

Create `src/Registrar.Core/ReportSerializer.cs`:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core;

public static class ReportSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToJson(ValidationReport report)
        => JsonSerializer.Serialize(report, Options);
}
```

Note: `WhenWritingNull` omits optional null fields (e.g. `artifactSha256`, `expected`); required fields are never null. `UnsafeRelaxedJsonEscaping` keeps regex patterns readable in the report. `[JsonPropertyName]` attributes give the exact camelCase field names, so no naming policy is needed.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ReportSerializerTests"`
Expected: PASS - 2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Model src/Registrar.Core/ReportSerializer.cs tests/Registrar.Tests/ReportSerializerTests.cs
git commit -m "feat(core): add validation report model and schema-conforming serializer"
```

---

### Task 4: Schema validation layer

**Files:**
- Create: `src/Registrar.Validation/IValidationLayer.cs`
- Create: `src/Registrar.Validation/SchemaConstraint.cs`
- Create: `src/Registrar.Validation/SchemaValidationLayer.cs`
- Modify: `config/qa-checklist.json` (add the `QA-SCHEMA-01` entry)
- Test: `tests/Registrar.Tests/SchemaValidationLayerTests.cs`
- Test: `tests/Registrar.Tests/QaChecklistCatalogTests.cs`

**Interfaces:**
- Consumes: `Check`, `Vocabulary`, `ErrorCategory` (Tasks 2-3); `Json.Schema` (JsonSchema.Net).
- Produces:
  - `interface IValidationLayer { IReadOnlyList<Check> Validate(SchemaValidationInput input); }`
  - `sealed record SchemaValidationInput(string AgrText, JsonSchema Schema, JsonNode SchemaRoot)`.
  - `sealed class SchemaValidationLayer : IValidationLayer` returning zero checks when valid, or one `QA-SCHEMA-01` fail per violation. On unparseable JSON, one `QA-SCHEMA-01` fail with `JsonPath = null`.
  - `const string SchemaValidationLayer.CheckId = "QA-SCHEMA-01"`.
  - `static class SchemaConstraint { string? Lookup(JsonNode schemaRoot, Uri schemaLocation, string keyword); }`

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/SchemaValidationLayerTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Validation;
using Json.Schema;

namespace Registrar.Tests;

public class SchemaValidationLayerTests
{
    private static SchemaValidationInput InputFor(string agrText)
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var schema = JsonSchema.FromFile(schemaPath);
        var schemaRoot = JsonNode.Parse(File.ReadAllText(schemaPath))!;
        return new SchemaValidationInput(agrText, schema, schemaRoot);
    }

    private static string ValidAgr()
    {
        var fixturePath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(fixturePath)!)!.FullName;
        return File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));
    }

    [Fact]
    public void Valid_agr_produces_no_checks()
    {
        var layer = new SchemaValidationLayer();
        var checks = layer.Validate(InputFor(ValidAgr()));
        Assert.Empty(checks);
    }

    [Fact]
    public void Bad_document_id_produces_qa_schema_01_fail_with_path_and_values()
    {
        // Break the documentId pattern (trailing 2-digit sequence instead of 3).
        var broken = ValidAgr().Replace("ORG-GOV-DS-016", "ORG-GOV-DS-16");
        var layer = new SchemaValidationLayer();

        var checks = layer.Validate(InputFor(broken));

        var check = Assert.Single(checks, c => c.JsonPath == "/artifactIdentity/documentId");
        Assert.Equal("QA-SCHEMA-01", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.SchemaValidationError, check.ErrorCategory);
        Assert.Equal("agr.schema.json", check.RuleSource);
        Assert.Contains("ORG-", check.Expected); // pattern constraint text
        Assert.Equal("ORG-GOV-DS-16", check.Actual);
        Assert.NotNull(check.Remediation);
    }

    [Fact]
    public void Malformed_json_produces_single_fail_with_null_path()
    {
        var layer = new SchemaValidationLayer();
        var checks = layer.Validate(InputFor("{ not valid json "));

        var check = Assert.Single(checks);
        Assert.Equal("QA-SCHEMA-01", check.CheckId);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Null(check.JsonPath);
        Assert.Equal(ErrorCategory.SchemaValidationError, check.ErrorCategory);
    }
}
```

Create `tests/Registrar.Tests/QaChecklistCatalogTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class QaChecklistCatalogTests
{
    [Fact]
    public void SchemaValidationLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;

        var ids = checklist["checks"]!.AsArray()
            .Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(SchemaValidationLayer.CheckId, ids);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~SchemaValidationLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: FAIL - types missing and `QA-SCHEMA-01` not yet in the checklist.

- [ ] **Step 3: Add the QA-SCHEMA-01 catalog entry**

In `config/qa-checklist.json`, add this object as the FIRST element of the `checks` array (before `QA-SF-01`):

```json
{ "checkId": "QA-SCHEMA-01", "layer": "input", "automation": "full", "appliesTo": "all", "rule": "AGR conforms to agr.schema.json (draft 2020-12): required fields present, enums/patterns/const satisfied, no additional properties.", "errorCategory": "SchemaValidationError", "ruleSource": "agr.schema.json" },
```

- [ ] **Step 4: Implement the interface and constraint lookup**

Create `src/Registrar.Validation/IValidationLayer.cs`:

```csharp
using Registrar.Core.Model;
using Json.Schema;
using System.Text.Json.Nodes;

namespace Registrar.Validation;

public sealed record SchemaValidationInput(string AgrText, JsonSchema Schema, JsonNode SchemaRoot);

public interface IValidationLayer
{
    IReadOnlyList<Check> Validate(SchemaValidationInput input);
}
```

Create `src/Registrar.Validation/SchemaConstraint.cs`:

```csharp
using System.Text.Json.Nodes;
using Json.Pointer;

namespace Registrar.Validation;

// Resolves the constraint value for a failing keyword by navigating the schema
// document at the error's schema location. Returns e.g. the pattern regex for a
// "pattern" failure, or the allowed list for an "enum" failure.
public static class SchemaConstraint
{
    public static string? Lookup(JsonNode schemaRoot, Uri schemaLocation, string keyword)
    {
        // schemaLocation fragment is a JSON Pointer into the schema, e.g.
        // "#/properties/artifactIdentity/properties/documentId".
        var fragment = schemaLocation.Fragment;
        if (string.IsNullOrEmpty(fragment)) return null;
        var pointerText = fragment.StartsWith("#") ? fragment[1..] : fragment;

        if (!JsonPointer.TryParse(pointerText, out var pointer)) return null;
        var subSchema = pointer.Evaluate(schemaRoot);
        var constraint = subSchema?[keyword];
        return constraint?.ToJsonString();
    }
}
```

Create `src/Registrar.Validation/SchemaValidationLayer.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;
using Json.Schema;

namespace Registrar.Validation;

public sealed class SchemaValidationLayer : IValidationLayer
{
    public const string CheckId = "QA-SCHEMA-01";
    private const string RuleSourceValue = "agr.schema.json";

    public IReadOnlyList<Check> Validate(SchemaValidationInput input)
    {
        JsonNode? instance;
        try
        {
            instance = JsonNode.Parse(input.AgrText);
        }
        catch (JsonException ex)
        {
            return new[] { MalformedJsonCheck(ex.Message) };
        }

        var results = input.Schema.Evaluate(
            instance, new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (results.IsValid) return Array.Empty<Check>();

        var checks = new List<Check>();
        foreach (var node in results.Details)
        {
            if (!node.HasErrors || node.Errors is null) continue;
            foreach (var (keyword, message) in node.Errors)
                checks.Add(BuildCheck(input, node, keyword, message, instance));
        }

        // Fallback: schema said invalid but produced no per-node errors.
        if (checks.Count == 0)
            checks.Add(MalformedJsonCheck("AGR failed schema validation."));

        return checks;
    }

    private static Check BuildCheck(
        SchemaValidationInput input, EvaluationResults node,
        string keyword, string message, JsonNode? instance)
    {
        var jsonPath = node.InstanceLocation.ToString();
        var constraint = SchemaConstraint.Lookup(input.SchemaRoot, node.SchemaLocation, keyword);
        var expected = constraint is null ? keyword : $"{keyword} {constraint}";
        var actual = ValueAt(instance, node.InstanceLocation);

        return new Check
        {
            CheckId = CheckId,
            LayerName = Layer.Input,
            Result = CheckResultValue.Fail,
            SeverityLevel = Severity.Blocking,
            ErrorCategory = ErrorCategory.SchemaValidationError,
            Message = message,
            JsonPath = string.IsNullOrEmpty(jsonPath) ? "/" : jsonPath,
            Expected = expected,
            Actual = actual,
            Remediation =
                $"Correct the value at {(_JsonPathOrRoot(jsonPath))} in the AGR to satisfy {expected} (see agr.schema.json).",
            RuleSource = RuleSourceValue
        };
    }

    private static string _JsonPathOrRoot(string jsonPath)
        => string.IsNullOrEmpty(jsonPath) ? "the document root" : jsonPath;

    private static string? ValueAt(JsonNode? instance, Json.Pointer.JsonPointer location)
    {
        if (instance is null) return null;
        var node = location.Evaluate(instance);
        if (node is null) return null;
        return node is JsonValue v ? v.ToString() : node.ToJsonString();
    }

    private static Check MalformedJsonCheck(string message) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.SchemaValidationError,
        Message = $"AGR is not valid JSON: {message}",
        JsonPath = null,
        Expected = "well-formed JSON conforming to agr.schema.json",
        Actual = null,
        Remediation = "Emit a syntactically valid AGR JSON document.",
        RuleSource = RuleSourceValue
    };
}
```

Note on the JsonSchema.Net API: `schema.Evaluate(node, options)` returns `EvaluationResults`; with `OutputFormat.List`, `results.Details` is a flat list. Each node exposes `HasErrors`, `Errors` (`IReadOnlyDictionary<string,string>` keyword->message), `InstanceLocation` (`Json.Pointer.JsonPointer`), and `SchemaLocation` (`Uri`). If your resolved package version differs, adjust member names but keep the mapping: instance location -> `jsonPath`, keyword+constraint -> `expected`, instance value -> `actual`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~SchemaValidationLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS - 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Validation config/qa-checklist.json tests/Registrar.Tests/SchemaValidationLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat(validation): add schema validation layer emitting QA-SCHEMA-01 checks"
```

---

### Task 5: Report assembly service

**Files:**
- Create: `src/Registrar.Validation/AgrFacts.cs`
- Create: `src/Registrar.Validation/ValidationService.cs`
- Test: `tests/Registrar.Tests/ValidationServiceTests.cs`

**Interfaces:**
- Consumes: `SchemaValidationLayer`, `ValidationReport`, `Check`, `IClock`, `Hashing`, `EngineInfo`, `Vocabulary`.
- Produces:
  - `static class AgrFacts { (string executionId, string documentId, string mode) Extract(string agrText); }` - safely reads `sourceWorkflowContext.executionId`, `artifactIdentity.documentId`, `validationPolicy.mode` from the AGR text; returns `"unknown"`/`"unknown"`/`"draft"` fallbacks when absent or unparseable.
  - `sealed class ValidationService(IClock clock)` with `ValidationReport Validate(string agrText, string schemaPath)` - loads the schema, runs the layer, extracts facts, computes overall result, records the schema content hash in `configVersions`, stamps the clock, and returns the report.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/ValidationServiceTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class ValidationServiceTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    private static (string schemaPath, string repoRoot) Paths()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        return (schemaPath, repoRoot);
    }

    [Fact]
    public void Valid_agr_yields_pass_report_with_no_fail_checks()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath);

        Assert.Equal(OverallResult.Pass, report.OverallResult);
        Assert.DoesNotContain(report.Checks, c => c.Result == CheckResultValue.Fail);
        Assert.Equal("GOV-2026-07-03-001", report.ExecutionId);
        Assert.Equal("ORG-GOV-DS-016", report.DocumentId);
        Assert.Equal(Mode.Draft, report.RequestedMode);
        Assert.Equal("2026-07-03T12:00:00.0000000+00:00", report.TimestampUtc);
        Assert.True(report.ConfigVersions.ContainsKey("agr.schema.json"));
        Assert.Null(report.ArtifactSha256);
    }

    [Fact]
    public void Invalid_agr_yields_fail_report()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"))
            .Replace("ORG-GOV-DS-016", "ORG-GOV-DS-16");

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath);

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.Contains(report.Checks, c => c.Result == CheckResultValue.Fail);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidationServiceTests"`
Expected: FAIL - `ValidationService`/`AgrFacts` do not exist.

- [ ] **Step 3: Implement AgrFacts and ValidationService**

Create `src/Registrar.Validation/AgrFacts.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;

namespace Registrar.Validation;

public static class AgrFacts
{
    public static (string executionId, string documentId, string mode) Extract(string agrText)
    {
        try
        {
            var root = JsonNode.Parse(agrText)?.AsObject();
            var executionId = (string?)root?["sourceWorkflowContext"]?["executionId"] ?? "unknown";
            var documentId = (string?)root?["artifactIdentity"]?["documentId"] ?? "unknown";
            var mode = (string?)root?["validationPolicy"]?["mode"] ?? Mode.Draft;
            return (executionId, documentId, mode);
        }
        catch (JsonException)
        {
            return ("unknown", "unknown", Mode.Draft);
        }
    }
}
```

Create `src/Registrar.Validation/ValidationService.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;
using Json.Schema;

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
        var schema = JsonSchema.FromText(schemaText);
        var schemaRoot = JsonNode.Parse(schemaText)!;

        var checks = _schemaLayer.Validate(new SchemaValidationInput(agrText, schema, schemaRoot));
        var (executionId, documentId, mode) = AgrFacts.Extract(agrText);

        var overall = checks.Any(c => c.Result == CheckResultValue.Fail
                                       && c.SeverityLevel == Severity.Blocking)
            ? OverallResult.Fail
            : OverallResult.Pass;

        return new ValidationReport
        {
            ReportVersion = EngineInfo.ReportVersion,
            ExecutionId = executionId,
            DocumentId = documentId,
            RequestedMode = mode,
            OverallResult = overall,
            EngineVersion = EngineInfo.Version,
            ConfigVersions = new Dictionary<string, string>
            {
                ["agr.schema.json"] = Hashing.Sha256Hex(schemaText)
            },
            TimestampUtc = _clock.UtcNow.ToString("o"),
            ArtifactSha256 = null,
            Checks = checks
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidationServiceTests"`
Expected: PASS - 2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Validation/AgrFacts.cs src/Registrar.Validation/ValidationService.cs tests/Registrar.Tests/ValidationServiceTests.cs
git commit -m "feat(validation): assemble validation report from schema-layer checks"
```

---

### Task 6: CLI `validate` verb, exit codes, and end-to-end acceptance

**Files:**
- Create: `src/Registrar.Cli/Program.cs` (replace template content)
- Create: `src/Registrar.Cli/ValidateCommand.cs`
- Create: `fixtures/agr-sample-ds-invalid-schema.json`
- Test: `tests/Registrar.Tests/ValidateCommandTests.cs`

**Interfaces:**
- Consumes: `ValidationService`, `ReportSerializer`, `ConfigLocator`, `SystemClock`, `OverallResult`, `CheckResultValue`.
- Produces:
  - `static class ValidateCommand { int Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr); }` - returns the process exit code (0 pass, 1 fail, 2 usage/IO error), writes `validation-report.json` next to the AGR on codes 0/1, prints a summary.
  - `Program.Main` wires `System.CommandLine` `validate <agr.json>` to `ValidateCommand.Run` with a `SystemClock`.

- [ ] **Step 1: Create the invalid fixture**

Create `fixtures/agr-sample-ds-invalid-schema.json` as a copy of `fixtures/agr-sample-ds-draft.json` with the `documentId` changed to break the pattern:

```json
{
  "schemaVersion": "1.0.0",
  "artifactIdentity": {
    "artifactType": "DS",
    "documentId": "ORG-GOV-DS-16",
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
      "[TOOL / CONNECTOR NAME]": "Microsoft Copilot Chat",
      "[DATE]": null
    },
    "sections": [
      {
        "title": "Purpose",
        "descriptorLine": null,
        "blocks": [
          { "blockType": "paragraph", "text": "This summary documents the approved pilot deployment posture." },
          { "blockType": "clauseInsertion", "clauseId": "CL-006" }
        ]
      }
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

- [ ] **Step 2: Write the failing test**

Create `tests/Registrar.Tests/ValidateCommandTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Cli;
using Registrar.Core;

namespace Registrar.Tests;

public class ValidateCommandTests
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
    public void Valid_agr_exits_0_and_writes_pass_report()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, code);
        var reportPath = Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json");
        Assert.True(File.Exists(reportPath));
        var report = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
        Assert.Equal("pass", (string?)report["overallResult"]);
    }

    [Fact]
    public void Invalid_agr_exits_1_and_writes_fail_report_with_qa_schema_01()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-invalid-schema.json");
        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var reportPath = Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json");
        var report = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
        Assert.Equal("fail", (string?)report["overallResult"]);
        Assert.Contains(report["checks"]!.AsArray(),
            c => (string?)c!["checkId"] == "QA-SCHEMA-01");
    }

    [Fact]
    public void Missing_file_exits_2_and_writes_no_report()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var missing = Path.Combine(dir, "nope.json");
        var code = ValidateCommand.Run(missing, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(2, code);
        Assert.False(File.Exists(Path.Combine(dir, "validation-report.json")));
    }

    [Fact]
    public void Malformed_json_exits_1_with_single_fail_check()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var agrPath = Path.Combine(dir, "bad.json");
        File.WriteAllText(agrPath, "{ not json ");
        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var report = JsonNode.Parse(
            File.ReadAllText(Path.Combine(dir, "validation-report.json")))!.AsObject();
        Assert.Single(report["checks"]!.AsArray());
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidateCommandTests"`
Expected: FAIL - `Registrar.Cli.ValidateCommand` does not exist.

- [ ] **Step 4: Implement the command and program**

Create `src/Registrar.Cli/ValidateCommand.cs`:

```csharp
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Cli;

public static class ValidateCommand
{
    // Exit codes: 0 pass, 1 validation fail, 2 usage/IO error.
    public static int Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr)
    {
        if (!File.Exists(agrPath))
        {
            stderr.WriteLine($"error: AGR file not found: {agrPath}");
            return 2;
        }

        string schemaPath;
        try
        {
            schemaPath = ConfigLocator.FindSchema(
                Path.GetDirectoryName(Path.GetFullPath(agrPath))!, "agr.schema.json");
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
                return 2;
            }
        }

        string agrText;
        try
        {
            agrText = File.ReadAllText(agrPath);
        }
        catch (IOException ex)
        {
            stderr.WriteLine($"error: cannot read AGR: {ex.Message}");
            return 2;
        }

        var report = new ValidationService(clock).Validate(agrText, schemaPath);
        var reportPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(agrPath))!, "validation-report.json");
        File.WriteAllText(reportPath, ReportSerializer.ToJson(report));

        var failCount = report.Checks.Count(c => c.Result == CheckResultValue.Fail);
        stdout.WriteLine(
            $"{report.OverallResult}: {report.DocumentId} ({failCount} failing check(s)). Report: {reportPath}");

        return report.OverallResult == OverallResult.Fail ? 1 : 0;
    }
}
```

Create `src/Registrar.Cli/Program.cs` (replace the template's content entirely):

```csharp
using System.CommandLine;
using Registrar.Cli;
using Registrar.Core;

var agrArgument = new Argument<string>("agr.json", "Path to the AGR JSON file to validate.");

var validate = new Command("validate", "Validate an AGR against agr.schema.json.");
validate.AddArgument(agrArgument);
validate.SetHandler(agrPath =>
{
    var code = ValidateCommand.Run(agrPath, new SystemClock(), Console.Out, Console.Error);
    Environment.ExitCode = code;
}, agrArgument);

var root = new RootCommand("registrar - governed artifact compiler") { validate };
return await root.InvokeAsync(args);
```

Note: if the resolved `System.CommandLine` API differs (the beta surface shifts), keep the contract: a `validate` command taking one path argument that calls `ValidateCommand.Run` and returns its exit code. All behavior is tested through `ValidateCommand.Run`, so `Program.cs` stays a thin adapter.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidateCommandTests"`
Expected: PASS - 4 tests.

- [ ] **Step 6: Verify the CLI end-to-end by hand**

Run: `dotnet run --project src/Registrar.Cli -- validate fixtures/agr-sample-ds-draft.json`
Expected: prints `pass: ORG-GOV-DS-016 (0 failing check(s))...`, exit code 0, and writes `fixtures/validation-report.json`.

Run: `dotnet run --project src/Registrar.Cli -- validate fixtures/agr-sample-ds-invalid-schema.json`
Expected: prints `fail: ORG-GOV-DS-16 (1 failing check(s))...`, exit code 1.

Clean up the hand-run report so it is not committed:

```bash
rm -f fixtures/validation-report.json
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test Registrar.sln`
Expected: PASS - all tests (smoke + Core + serializer + schema layer + catalog + service + command).

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Cli fixtures/agr-sample-ds-invalid-schema.json tests/Registrar.Tests/ValidateCommandTests.cs
git commit -m "feat(cli): add validate verb with fail-closed exit codes"
```

---

## Self-Review

**Spec coverage** (each spec section maps to a task):
- Projects (Core/Validation/Cli/Tests) -> Task 1.
- Core primitives, `IClock`, content-hash config versions, `ConfigLocator` -> Task 2.
- Report DTOs + serializer + report-schema conformance -> Task 3.
- `SchemaValidationLayer`, QA-SCHEMA-01 mapping (jsonPath/expected/actual/remediation/ruleSource), catalog entry, malformed JSON handling -> Task 4.
- Report field sourcing table (executionId/documentId/requestedMode fallbacks, overallResult, configVersions, timestampUtc, artifactSha256=null) -> Task 5.
- CLI `validate`, exit-code table (0/1/2), report written next to input, new invalid fixture -> Task 6.
- CI (build+test, synthetic fixtures only) -> Task 1.
- Acceptance criteria 1-5 -> covered by `ValidateCommandTests` + Task 6 Step 6 manual run.

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every command shows expected output.

**Type consistency:** `ValidationService(IClock)` used identically in Tasks 5-6. `SchemaValidationInput(AgrText, Schema, SchemaRoot)` defined in Task 4, consumed in Task 5. `ValidateCommand.Run(string, IClock, TextWriter, TextWriter)` defined and used consistently in Task 6. `SchemaValidationLayer.CheckId` referenced in the Task 4 catalog test. `Check` property names (`LayerName`, `SeverityLevel`, `Result`) consistent across Tasks 3-5.

**Known risk:** JsonSchema.Net / System.CommandLine are the only version-sensitive surfaces. Both tasks include a note to preserve the mapping/contract if member names differ in the resolved package version; all behavior is asserted through tests, so a broken API surfaces immediately at Step 2/3.
