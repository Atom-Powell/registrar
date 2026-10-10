# generation-log.json Emission Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Emit `generation-log.json` on `registrar build` whenever rendering was attempted, persisting the orphaned `RenderResult.ClauseAudits` (tech-debt item 11).

**Architecture:** A new `GenerationLog` record (Core.Model) is assembled by a pure `GenerationLogBuilder` from the post-render merged `ValidationReport` plus the renderer's `ClauseAudits`, serialized by `GenerationLogSerializer` (reusing the report serializer's options), and written inline in `BuildCommand` before any outcome branch. A new `schemas/generation-log.schema.json` describes the file and is enforced by a conformance test. No renderer changes; `ClauseAudits` already flows to `BuildCommand`.

**Tech Stack:** .NET 8, C#, System.Text.Json, xUnit, JsonSchema.Net (`Json.Schema`), DocumentFormat.OpenXml (existing test dependency only).

## Global Constraints

- .NET 8 / C#; target OS Windows 11; dev shell PowerShell 7.x. Build/test with `dotnet build` / `dotnet test` from the repo root.
- US English; no emojis; no em/en dashes or decorative unicode in any source, config, or generated string (CLAUDE.md rule 8; enforced on engine source by `SourceHygieneTests`).
- Configuration over code: no artifact constants, clause text, or repository paths in core logic.
- Determinism is a governance property: no wall-clock durations, no `DateTime.Now`. Timestamps come only from the injected `IClock` already reflected in `ValidationReport.TimestampUtc`.
- JSON field names are camelCase, produced via `[JsonPropertyName]` attributes (the serializer does not set a naming policy).
- Serializer options are shared: reuse `ReportSerializer.Options` (indented, `JsonIgnoreCondition.WhenWritingNull`, `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`).
- Version constants live in `src/Registrar.Core/EngineInfo.cs`.
- Reference design: `docs/superpowers/specs/2026-07-15-generation-log-emission-design.md`.

---

### Task 1: GenerationLog model and serializer

**Files:**
- Modify: `src/Registrar.Core/EngineInfo.cs`
- Create: `src/Registrar.Core/Model/ClauseInsertion.cs`
- Create: `src/Registrar.Core/Model/GenerationLog.cs`
- Create: `src/Registrar.Core/GenerationLogSerializer.cs`
- Test: `tests/Registrar.Tests/GenerationLogSerializerTests.cs`

**Interfaces:**
- Consumes: `EngineInfo`, `ReportSerializer.Options` (existing, `public static`).
- Produces:
  - `EngineInfo.LogVersion` (`const string`, `"1.0.0"`).
  - `Registrar.Core.Model.ClauseInsertion` record with init-only properties `ClauseId` (string), `ExpectedSha` (string), `ActualSha` (string), `Verified` (bool), `TokensResolved` (int).
  - `Registrar.Core.Model.GenerationLog` record with init-only properties `LogVersion` (string), `ExecutionId` (string), `DocumentId` (string), `EngineVersion` (string), `RequestedMode` (string), `Outcome` (string), `TimestampUtc` (string), `ConfigVersions` (`IReadOnlyDictionary<string,string>`), `RulesApplied` (`IReadOnlyList<string>`), `ClauseInsertions` (`IReadOnlyList<ClauseInsertion>`).
  - `Registrar.Core.GenerationLogSerializer.ToJson(GenerationLog) -> string` and `FromJson(string) -> GenerationLog`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/GenerationLogSerializerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests;

public class GenerationLogSerializerTests
{
    private static GenerationLog Sample() => new()
    {
        LogVersion = EngineInfo.LogVersion,
        ExecutionId = "GOV-2026-07-15-001",
        DocumentId = "ORG-GOV-DS-016",
        EngineVersion = EngineInfo.Version,
        RequestedMode = "draft",
        Outcome = "pass",
        TimestampUtc = "2026-07-15T12:00:00.0000000+00:00",
        ConfigVersions = new Dictionary<string, string> { ["clause-library.json"] = "abc123" },
        RulesApplied = new[] { "QA-SF-08", "QA-ENG-05" },
        ClauseInsertions = new[]
        {
            new ClauseInsertion
            {
                ClauseId = "CL-001", ExpectedSha = "deadbeef", ActualSha = "deadbeef",
                Verified = true, TokensResolved = 3
            }
        }
    };

    [Fact]
    public void Serialized_log_uses_camelCase_field_names()
    {
        var node = JsonNode.Parse(GenerationLogSerializer.ToJson(Sample()))!.AsObject();

        Assert.Equal("1.0.0", (string?)node["logVersion"]);
        Assert.Equal("ORG-GOV-DS-016", (string?)node["documentId"]);
        Assert.Equal("pass", (string?)node["outcome"]);
        Assert.Equal("QA-SF-08", (string?)node["rulesApplied"]!.AsArray()[0]);
        Assert.Equal("CL-001", (string?)node["clauseInsertions"]!.AsArray()[0]!["clauseId"]);
        Assert.True((bool)node["clauseInsertions"]!.AsArray()[0]!["verified"]!);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var restored = GenerationLogSerializer.FromJson(GenerationLogSerializer.ToJson(Sample()));

        Assert.Equal("GOV-2026-07-15-001", restored.ExecutionId);
        Assert.Single(restored.ClauseInsertions);
        Assert.Equal("CL-001", restored.ClauseInsertions[0].ClauseId);
        Assert.Equal(3, restored.ClauseInsertions[0].TokensResolved);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~GenerationLogSerializerTests`
Expected: BUILD FAILS - `EngineInfo.LogVersion`, `GenerationLog`, `ClauseInsertion`, and `GenerationLogSerializer` do not exist yet.

- [ ] **Step 3: Add the LogVersion constant**

In `src/Registrar.Core/EngineInfo.cs`, add the constant next to the existing version constants:

```csharp
    public const string LogVersion = "1.0.0";     // generation-log logVersion const
```

- [ ] **Step 4: Create the ClauseInsertion record**

Create `src/Registrar.Core/Model/ClauseInsertion.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

// The serialized shape of one clause insertion in generation-log.json. Kept
// distinct from the renderer-side ClauseInsertionAudit so the wire contract does
// not depend on the internal record's positional layout.
public sealed record ClauseInsertion
{
    [JsonPropertyName("clauseId")]       public required string ClauseId { get; init; }
    [JsonPropertyName("expectedSha")]    public required string ExpectedSha { get; init; }
    [JsonPropertyName("actualSha")]      public required string ActualSha { get; init; }
    [JsonPropertyName("verified")]       public required bool Verified { get; init; }
    [JsonPropertyName("tokensResolved")] public required int TokensResolved { get; init; }
}
```

- [ ] **Step 5: Create the GenerationLog record**

Create `src/Registrar.Core/Model/GenerationLog.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record GenerationLog
{
    [JsonPropertyName("logVersion")]       public required string LogVersion { get; init; }
    [JsonPropertyName("executionId")]      public required string ExecutionId { get; init; }
    [JsonPropertyName("documentId")]       public required string DocumentId { get; init; }
    [JsonPropertyName("engineVersion")]    public required string EngineVersion { get; init; }
    [JsonPropertyName("requestedMode")]    public required string RequestedMode { get; init; }
    [JsonPropertyName("outcome")]          public required string Outcome { get; init; }
    [JsonPropertyName("timestampUtc")]     public required string TimestampUtc { get; init; }
    [JsonPropertyName("configVersions")]   public required IReadOnlyDictionary<string, string> ConfigVersions { get; init; }
    [JsonPropertyName("rulesApplied")]     public required IReadOnlyList<string> RulesApplied { get; init; }
    [JsonPropertyName("clauseInsertions")] public required IReadOnlyList<ClauseInsertion> ClauseInsertions { get; init; }
}
```

- [ ] **Step 6: Create the serializer**

Create `src/Registrar.Core/GenerationLogSerializer.cs`:

```csharp
using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core;

public static class GenerationLogSerializer
{
    // Reuse the report serializer's options: indented, ignore-null, relaxed
    // escaping. One options instance keeps every output-package file byte-consistent.
    public static string ToJson(GenerationLog log)
        => JsonSerializer.Serialize(log, ReportSerializer.Options);

    public static GenerationLog FromJson(string json)
        => JsonSerializer.Deserialize<GenerationLog>(json, ReportSerializer.Options)
           ?? throw new JsonException("generation-log.json deserialized to null.");
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~GenerationLogSerializerTests`
Expected: PASS (2 tests).

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Core/EngineInfo.cs src/Registrar.Core/Model/ClauseInsertion.cs src/Registrar.Core/Model/GenerationLog.cs src/Registrar.Core/GenerationLogSerializer.cs tests/Registrar.Tests/GenerationLogSerializerTests.cs
git commit -m "feat: GenerationLog model and serializer"
```

---

### Task 2: GenerationLogBuilder

**Files:**
- Create: `src/Registrar.Core/GenerationLogBuilder.cs`
- Test: `tests/Registrar.Tests/GenerationLogBuilderTests.cs`

**Interfaces:**
- Consumes: `Registrar.Core.Model.ValidationReport` (existing), `Registrar.Core.Model.ClauseInsertionAudit` (existing: positional record `(string ClauseId, string ExpectedSha, string ActualSha, bool Verified, int TokensResolved)`), `EngineInfo.LogVersion`, `GenerationLog`, `ClauseInsertion` (Task 1).
- Produces: `Registrar.Core.GenerationLogBuilder.Build(ValidationReport mergedReport, IReadOnlyList<ClauseInsertionAudit> audits) -> GenerationLog`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/GenerationLogBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests;

public class GenerationLogBuilderTests
{
    private static ValidationReport Report(params Check[] checks) => new()
    {
        ReportVersion = EngineInfo.ReportVersion,
        ExecutionId = "GOV-2026-07-15-002",
        DocumentId = "ORG-GOV-AUP-004",
        RequestedMode = "draft",
        OverallResult = "pass",
        EngineVersion = EngineInfo.Version,
        ConfigVersions = new Dictionary<string, string> { ["clause-library.json"] = "abc123" },
        TimestampUtc = "2026-07-15T12:00:00.0000000+00:00",
        Checks = checks
    };

    private static Check Check(string id) => new()
    {
        CheckId = id, LayerName = "content", Result = "pass",
        SeverityLevel = "info", Message = "ok"
    };

    [Fact]
    public void Carries_header_and_provenance_from_the_report()
    {
        var log = GenerationLogBuilder.Build(Report(Check("QA-SF-08")), new List<ClauseInsertionAudit>());

        Assert.Equal(EngineInfo.LogVersion, log.LogVersion);
        Assert.Equal("GOV-2026-07-15-002", log.ExecutionId);
        Assert.Equal("ORG-GOV-AUP-004", log.DocumentId);
        Assert.Equal("draft", log.RequestedMode);
        Assert.Equal("pass", log.Outcome);
        Assert.Equal("2026-07-15T12:00:00.0000000+00:00", log.TimestampUtc);
        Assert.Equal("abc123", log.ConfigVersions["clause-library.json"]);
    }

    [Fact]
    public void RulesApplied_is_the_distinct_checkId_set_in_order()
    {
        var log = GenerationLogBuilder.Build(
            Report(Check("QA-SF-08"), Check("QA-ENG-05"), Check("QA-SF-08")),
            new List<ClauseInsertionAudit>());

        Assert.Equal(new[] { "QA-SF-08", "QA-ENG-05" }, log.RulesApplied);
    }

    [Fact]
    public void Maps_clause_audits_one_to_one()
    {
        var audits = new List<ClauseInsertionAudit>
        {
            new("CL-001", "sha-a", "sha-a", true, 2),
            new("CL-004", "sha-b", "sha-c", false, 0)
        };

        var log = GenerationLogBuilder.Build(Report(Check("QA-SF-08")), audits);

        Assert.Equal(2, log.ClauseInsertions.Count);
        Assert.Equal("CL-001", log.ClauseInsertions[0].ClauseId);
        Assert.True(log.ClauseInsertions[0].Verified);
        Assert.Equal(2, log.ClauseInsertions[0].TokensResolved);
        Assert.Equal("CL-004", log.ClauseInsertions[1].ClauseId);
        Assert.False(log.ClauseInsertions[1].Verified);
    }

    [Fact]
    public void Empty_audits_yield_empty_clauseInsertions()
    {
        var log = GenerationLogBuilder.Build(Report(Check("QA-SF-08")), new List<ClauseInsertionAudit>());
        Assert.Empty(log.ClauseInsertions);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~GenerationLogBuilderTests`
Expected: BUILD FAILS - `GenerationLogBuilder` does not exist.

- [ ] **Step 3: Write the builder**

Create `src/Registrar.Core/GenerationLogBuilder.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Core;

// Pure assembly of the generation log from the post-render merged report and the
// renderer's clause-insertion audits. No clock, no I/O: every field is carried
// from the report (already stamped from the injected IClock) or from the audits,
// so the log is reproducible under a fixed clock.
public static class GenerationLogBuilder
{
    public static GenerationLog Build(
        ValidationReport mergedReport, IReadOnlyList<ClauseInsertionAudit> audits)
        => new()
        {
            LogVersion = EngineInfo.LogVersion,
            ExecutionId = mergedReport.ExecutionId,
            DocumentId = mergedReport.DocumentId,
            EngineVersion = mergedReport.EngineVersion,
            RequestedMode = mergedReport.RequestedMode,
            Outcome = mergedReport.OverallResult,
            TimestampUtc = mergedReport.TimestampUtc,
            ConfigVersions = mergedReport.ConfigVersions,
            // Distinct() over LINQ-to-objects preserves first-seen order, so the
            // rule list is deterministic. Full per-check detail stays in the report.
            RulesApplied = mergedReport.Checks.Select(c => c.CheckId).Distinct().ToList(),
            ClauseInsertions = audits.Select(a => new ClauseInsertion
            {
                ClauseId = a.ClauseId,
                ExpectedSha = a.ExpectedSha,
                ActualSha = a.ActualSha,
                Verified = a.Verified,
                TokensResolved = a.TokensResolved
            }).ToList()
        };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~GenerationLogBuilderTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/GenerationLogBuilder.cs tests/Registrar.Tests/GenerationLogBuilderTests.cs
git commit -m "feat: GenerationLogBuilder assembles the log from report and clause audits"
```

---

### Task 3: generation-log.schema.json and conformance test

**Files:**
- Create: `schemas/generation-log.schema.json`
- Test: `tests/Registrar.Tests/GenerationLogSerializerTests.cs` (add one test to the existing class from Task 1)

**Interfaces:**
- Consumes: `GenerationLogSerializer.ToJson` (Task 1), `ConfigLocator.FindSchema` (existing), `IsolatedSchema.FromFile` (existing test helper), `Json.Schema` (existing test dependency).
- Produces: `schemas/generation-log.schema.json`, discoverable by `ConfigLocator.FindSchema(AppContext.BaseDirectory, "generation-log.schema.json")`.

- [ ] **Step 1: Write the failing test**

Add this test to the existing `GenerationLogSerializerTests` class in `tests/Registrar.Tests/GenerationLogSerializerTests.cs`. Also add these `using`s at the top of that file if not already present: `using System.Text.Json;` and `using Json.Schema;`.

```csharp
    [Fact]
    public void Serialized_log_conforms_to_generation_log_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "generation-log.schema.json");
        var schema = IsolatedSchema.FromFile(schemaPath);
        using var document = JsonDocument.Parse(GenerationLogSerializer.ToJson(Sample()));

        var result = schema.Evaluate(document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Emitted generation log failed generation-log schema validation.");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~GenerationLogSerializerTests.Serialized_log_conforms_to_generation_log_schema"`
Expected: FAIL - `ConfigLocator.FindSchema` throws `FileNotFoundException` because the schema file does not exist yet.

- [ ] **Step 3: Author the schema**

Create `schemas/generation-log.schema.json`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://ironwright.dev/schemas/registrar/generation-log.schema.json",
  "title": "GenerationLog",
  "description": "Output-package file recording a generation attempt: config provenance, the rules that ran, the clause insertions performed, and the run timestamp. Emitted on build whenever rendering was attempted. Correlated to validation-report.json by executionId. Reproducible under a fixed clock (no wall-clock durations).",
  "type": "object",
  "additionalProperties": false,
  "required": ["logVersion", "executionId", "documentId", "engineVersion", "requestedMode", "outcome", "timestampUtc", "configVersions", "rulesApplied", "clauseInsertions"],
  "properties": {
    "logVersion": { "type": "string", "const": "1.0.0" },
    "executionId": { "type": "string", "description": "Echoes sourceWorkflowContext.executionId from the AGR; correlates to validation-report.json." },
    "documentId": { "type": "string" },
    "engineVersion": { "type": "string" },
    "requestedMode": { "type": "string", "enum": ["draft", "promotion"] },
    "outcome": { "type": "string", "enum": ["pass", "pass-with-warnings", "fail"] },
    "timestampUtc": { "type": "string", "format": "date-time" },
    "configVersions": {
      "type": "object",
      "description": "Exact config versions loaded for this run, for audit reproducibility.",
      "additionalProperties": { "type": "string" }
    },
    "rulesApplied": {
      "type": "array",
      "description": "Distinct checkIds evaluated during the run. Full per-check detail lives in validation-report.json.",
      "items": { "type": "string" }
    },
    "clauseInsertions": {
      "type": "array",
      "description": "Per-clause insertion audit from the renderer. Empty when the rendered document had no clauseInsertion blocks.",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["clauseId", "expectedSha", "actualSha", "verified", "tokensResolved"],
        "properties": {
          "clauseId": { "type": "string" },
          "expectedSha": { "type": "string", "description": "Canonical SHA-256 of the clause text from clause-library.json." },
          "actualSha": { "type": "string", "description": "SHA-256 of the text actually inserted." },
          "verified": { "type": "boolean", "description": "True when expectedSha equals actualSha." },
          "tokensResolved": { "type": "integer", "minimum": 0 }
        }
      }
    }
  }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~GenerationLogSerializerTests`
Expected: PASS (3 tests now in the class).

- [ ] **Step 5: Commit**

```bash
git add schemas/generation-log.schema.json tests/Registrar.Tests/GenerationLogSerializerTests.cs
git commit -m "feat: generation-log.schema.json and conformance test"
```

---

### Task 4: Emit generation-log.json from BuildCommand

**Files:**
- Modify: `src/Registrar.Cli/BuildCommand.cs` (insert after the `WriteOutputs(merged, ...)` call, currently line 81-82, before the `overallResult == Fail` check at line 84)
- Test: `tests/Registrar.Tests/BuildCommandTests.cs` (add tests to the existing class)

**Interfaces:**
- Consumes: `GenerationLogBuilder.Build` (Task 2), `GenerationLogSerializer.ToJson` (Task 1), the in-scope `mergedReport` (`ValidationReport`, defined at line 79) and `rendered` (`RenderResult`, whose `ClauseAudits` is `IReadOnlyList<ClauseInsertionAudit>`).
- Produces: `generation-log.json` written to the AGR's directory whenever rendering was attempted.

- [ ] **Step 1: Write the failing tests**

Add these three tests to the existing `BuildCommandTests` class in `tests/Registrar.Tests/BuildCommandTests.cs`. They reuse the class's existing `FixedClock`, `RepoRoot`, and `CopyToTemp` helpers.

```csharp
    [Fact]
    public void Passing_build_writes_generation_log_with_clause_insertions()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var logPath = Path.Combine(dir, "generation-log.json");
        Assert.True(File.Exists(logPath));

        var log = JsonNode.Parse(File.ReadAllText(logPath))!.AsObject();
        Assert.Equal("1.0.0", (string?)log["logVersion"]);

        // Outcome mirrors the run's overall result recorded in the report.
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "validation-report.json")))!.AsObject();
        Assert.Equal((string?)report["overallResult"], (string?)log["outcome"]);

        Assert.NotEmpty(log["rulesApplied"]!.AsArray());

        // AUP embeds core clauses inserted by reference, so the audit is non-empty
        // and every insertion is verified (expected SHA equals actual SHA).
        var insertions = log["clauseInsertions"]!.AsArray();
        Assert.NotEmpty(insertions);
        foreach (var ins in insertions)
            Assert.True((bool)ins!["verified"]!);
    }

    [Fact]
    public void Pre_render_validation_failure_writes_no_generation_log()
    {
        // Fails closed pre-render (QA-ENG-05 prohibited character), so rendering
        // never runs and there is no generation attempt to log.
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-prohibited-char.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(1, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.False(File.Exists(Path.Combine(dir, "generation-log.json")));
    }

    [Fact]
    public void Generation_log_is_byte_identical_across_runs_under_a_fixed_clock()
    {
        var agrPathA = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        Assert.Equal(0, BuildCommand.Run(agrPathA, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var logA = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(agrPathA)!, "generation-log.json"));

        var agrPathB = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        Assert.Equal(0, BuildCommand.Run(agrPathB, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var logB = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(agrPathB)!, "generation-log.json"));

        Assert.Equal(logA, logB);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BuildCommandTests.Passing_build_writes_generation_log_with_clause_insertions|FullyQualifiedName~BuildCommandTests.Pre_render_validation_failure_writes_no_generation_log|FullyQualifiedName~BuildCommandTests.Generation_log_is_byte_identical_across_runs_under_a_fixed_clock"`
Expected: FAIL - the passing-build and determinism tests fail because `generation-log.json` is never written. (The pre-render test passes trivially, since the file already does not exist.)

- [ ] **Step 3: Wire the write into BuildCommand**

In `src/Registrar.Cli/BuildCommand.cs`, locate this block (lines 79-84):

```csharp
        var mergedReport = merged.Report;

        var writeMerged = CommandSupport.WriteOutputs(merged, dir, stderr);
        if (writeMerged is not null) return writeMerged.Value;

        if (mergedReport.OverallResult == OverallResult.Fail)
```

Insert the generation-log write between the `WriteOutputs` guard and the `OverallResult.Fail` check, so it runs before any outcome branch:

```csharp
        var mergedReport = merged.Report;

        var writeMerged = CommandSupport.WriteOutputs(merged, dir, stderr);
        if (writeMerged is not null) return writeMerged.Value;

        // Emit the generation log now - before any outcome branch - so it records
        // every run that reached rendering, whether the DOCX is produced or a
        // post-render check withholds it. Pre-render failures return earlier and
        // never reach this point, which is the intended gating.
        try
        {
            var logPath = Path.Combine(dir, "generation-log.json");
            var log = GenerationLogBuilder.Build(mergedReport, rendered.ClauseAudits);
            File.WriteAllText(logPath, GenerationLogSerializer.ToJson(log));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"error: cannot write generation log: {ex.Message}");
            return 2;
        }

        if (mergedReport.OverallResult == OverallResult.Fail)
```

The file already has `using Registrar.Core;` (line 1), which brings in `GenerationLogBuilder` and `GenerationLogSerializer`. No new `using` is required.

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~BuildCommandTests.Passing_build_writes_generation_log_with_clause_insertions|FullyQualifiedName~BuildCommandTests.Pre_render_validation_failure_writes_no_generation_log|FullyQualifiedName~BuildCommandTests.Generation_log_is_byte_identical_across_runs_under_a_fixed_clock"`
Expected: PASS (3 tests).

- [ ] **Step 5: Run the full suite to confirm no regressions**

Run: `dotnet test`
Expected: PASS - all existing tests plus the 9 new ones (2 serializer + 1 conformance + 4 builder + 3 build-command... note: serializer class holds 3 after Task 3), build clean, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Cli/BuildCommand.cs tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "feat: emit generation-log.json on build whenever rendering was attempted"
```

---

### Task 5: Documentation reconciliation

**Files:**
- Modify: `docs/tech-debt.md` (item 11)
- Modify: `CLAUDE.md` (Output package section: `generation-log.json` no longer deferred)

**Interfaces:**
- Consumes: nothing (documentation only).
- Produces: updated status text. No code, no tests.

- [ ] **Step 1: Update tech-debt item 11**

In `docs/tech-debt.md`, mark item 11 (the "Clause audit sink") resolved, following the existing resolved-item convention in that file (e.g. the `~~strikethrough~~ ... RESOLVED <date>` style used by item 29). State that `generation-log.json` now persists `RenderResult.ClauseAudits`, emitted by `BuildCommand` whenever rendering was attempted, and that `artifact-manifest.json` remains deferred.

- [ ] **Step 2: Update CLAUDE.md output-package section**

In `CLAUDE.md`, under "Output package (per run)", move `generation-log.json` out of the deferred list: the package is now four files (DOCX, `validation-report.json`, `promotion-payload.json`, `generation-log.json`), with only `artifact-manifest.json` still deferred. Keep the wording consistent with the existing section and note that the log is emitted whenever rendering was attempted.

- [ ] **Step 3: Verify no AI-tell lint breakage**

Run: `dotnet test`
Expected: PASS (documentation changes do not affect tests; this confirms the tree is still green before commit).

- [ ] **Step 4: Commit**

```bash
git add docs/tech-debt.md CLAUDE.md
git commit -m "docs: mark generation-log.json emitted; close tech-debt item 11"
```

---

## Self-Review

**Spec coverage:**
- Content table (10 fields, CLAUDE.md element mapping) -> Task 1 (`GenerationLog` record) + Task 2 (`GenerationLogBuilder` maps each field). Covered.
- `ClauseInsertion` DTO distinct from `ClauseInsertionAudit` -> Task 1 Step 4. Covered.
- Serializer reusing `ReportSerializer.Options` -> Task 1 Step 6. Covered.
- Schema authoring + conformance test -> Task 3. Covered.
- Emission gating (write before every outcome branch; pre-render failures excluded) -> Task 4 Step 3 placement + Step 1 tests. Covered.
- Determinism (byte-identical under fixed clock) -> Task 4 determinism test. Covered.
- Error handling (same IOException/UnauthorizedAccessException pattern, exit 2) -> Task 4 Step 3. Covered.
- Post-render-fail branch covered structurally, not by fixture -> spec Testing section updated; Task 4 Step 3 comment states the placement rationale. Covered (as a documented decision).
- Documentation reconciliation (tech-debt 11, CLAUDE.md) -> Task 5. Covered.

**Placeholder scan:** No TBD/TODO/"add appropriate X"; every code and test step shows complete content. Clean.

**Type consistency:** `GenerationLog`, `ClauseInsertion`, `GenerationLogBuilder.Build(ValidationReport, IReadOnlyList<ClauseInsertionAudit>)`, `GenerationLogSerializer.ToJson/FromJson`, and `EngineInfo.LogVersion` are named identically across Tasks 1-4. `ClauseInsertionAudit` is consumed with its real positional shape `(ClauseId, ExpectedSha, ActualSha, Verified, TokensResolved)`. `ValidationReport` and `Check` property names (`ExecutionId`, `DocumentId`, `EngineVersion`, `RequestedMode`, `OverallResult`, `TimestampUtc`, `ConfigVersions`, `Checks`, `CheckId`) match the real records. Consistent.
