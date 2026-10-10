# Slice 3: ID Registry and Lifecycle Enforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After schema validation passes, verify the AGR against the authoritative ID Registry (a CSV export): the Document ID must exist and agree on artifactType/classification (QA-SF-03), and the approval-status transition against the registry's current state must be legal (QA-SF-04).

**Architecture:** A real `IIdRegistry` (`RegistryRecord? Lookup(string)`) with a CSV-backed adapter feeds two new release-tier `ISemanticLayer` implementations (`RegistryLayer`, `LifecycleLayer`) that run after the Slice 2 layers, behind the same schema gate. Registry presence and mode drive whether registry checks run, skip, or fail closed. The lifecycle transition map comes from `vocabularies.json` (config, not code).

**Tech Stack:** .NET 8, C#, System.Text.Json, xUnit. No new NuGet dependencies (a small RFC 4180 CSV parser is hand-rolled and tested).

## Global Constraints

- **.NET 8** target (`net8.0`). No `net9.0`/`net10.0` features.
- **Laptop runtime gap:** this machine has only the .NET 10 runtime. Run tests with `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln` (or a `--filter` subset). Never add `<RollForward>` to any csproj.
- **Branch first:** do all work on a `slice3-registry-lifecycle` branch off `main`.
- **Fail closed:** registry disagreement, illegal transition, terminal-state regeneration, and a malformed registry are all blocking fails. Registry absent + promotion fails closed; registry absent + draft skips.
- **Zero LLM calls at runtime.** Pure deterministic validation.
- **Configuration over code:** the approval-status set and transition map come from `config/vocabularies.json`; the layers hold only their `checkId` literal and rule-source string. No embedded transition logic, no hardcoded status names in transition decisions (terminal = a status whose transition list is empty).
- **Report contract:** the report MUST validate against `schemas/validation-report.schema.json`. Registry checks use `errorCategory` `RegistrySyncError` (QA-SF-03) and `LifecycleTransitionError` (QA-SF-04); both are layer `release`. A `skipped` check uses `result: "skipped"`, `severity: "info"`, no `errorCategory`.
- **Determinism in tests:** time comes from an injected `IClock`; use a fixed instant.
- **Output hygiene:** US English; no emojis, em/en dashes, or decorative unicode in any source, config, fixture, or emitted string.
- **Value strings centralized** in `Registrar.Core` `Vocabulary.cs` (`Layer`, `CheckResultValue`, `Severity`, `ErrorCategory`, `Mode`). `QA-SF-03`/`QA-SF-04` already exist in `config/qa-checklist.json` (release tier) - do NOT add catalog entries, only assert their presence.

## File map

- `src/Registrar.Core/Config/ConfigBundle.cs` - add `ApprovalStatuses`, `Transitions` (Task 1).
- `src/Registrar.Core/Config/ConfigLoader.cs` - parse them from vocabularies.json (Task 1).
- `src/Registrar.Core/Csv.cs` - hand-rolled RFC 4180 parser (Task 2).
- `src/Registrar.Core/Model/RegistryRecord.cs` - the registry row (Task 3).
- `src/Registrar.Validation/IIdRegistry.cs` - replace the empty marker with `Lookup` (Task 3).
- `src/Registrar.Validation/CsvIdRegistry.cs` - CSV adapter + `RegistryFormatException` (Task 3).
- `src/Registrar.Validation/RegistryLayer.cs` - QA-SF-03 (Task 4).
- `src/Registrar.Validation/LifecycleLayer.cs` - QA-SF-04 (Task 5).
- `src/Registrar.Validation/ValidationService.cs` - wire registry + malformed handling (Task 6).
- `src/Registrar.Cli/ValidateCommand.cs`, `Program.cs` - `--registry` option (Task 6).
- `fixtures/registry-sample.csv`, `fixtures/agr-sample-ds-unregistered.json` (Task 6).
- `tests/Registrar.Tests/SemanticTestSupport.cs` - a registry helper (Task 4).

---

### Task 1: Load the approval-status set and transition map into ConfigBundle

**Files:**
- Modify: `src/Registrar.Core/Config/ConfigBundle.cs`
- Modify: `src/Registrar.Core/Config/ConfigLoader.cs`
- Modify: `tests/Registrar.Tests/Config/ConfigLoaderTests.cs`

**Interfaces:**
- Consumes: existing `ConfigLoader.Load(string)`, `vocabularies.json`.
- Produces:
  - `ConfigBundle.ApprovalStatuses` (`IReadOnlySet<string>`) - the controlled status values.
  - `ConfigBundle.Transitions` (`IReadOnlyDictionary<string, IReadOnlyList<string>>`) - allowed next states per status; terminal states map to an empty list.

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Config/ConfigLoaderTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void Load_parses_approval_status_values_and_transitions()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Contains("Draft", bundle.ApprovalStatuses);
        Assert.Contains("Retired", bundle.ApprovalStatuses);
        Assert.Equal(5, bundle.ApprovalStatuses.Count);

        Assert.Contains("In Review", bundle.Transitions["Draft"]);
        Assert.Contains("Retired", bundle.Transitions["Draft"]);
        Assert.Empty(bundle.Transitions["Superseded"]);
        Assert.Empty(bundle.Transitions["Retired"]);
        Assert.DoesNotContain("$comment", bundle.Transitions.Keys);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: FAIL - `ApprovalStatuses`/`Transitions` do not exist.

- [ ] **Step 3: Add the properties to `ConfigBundle`**

In `src/Registrar.Core/Config/ConfigBundle.cs`, add these two properties inside the record (alongside the existing ones):

```csharp
    public required IReadOnlySet<string> ApprovalStatuses { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Transitions { get; init; }
```

- [ ] **Step 4: Parse them in `ConfigLoader`**

In `src/Registrar.Core/Config/ConfigLoader.cs`, add a parse helper (next to `ParseReviewIntervals`):

```csharp
    private static (IReadOnlySet<string> Statuses, IReadOnlyDictionary<string, IReadOnlyList<string>> Transitions)
        ParseApprovalStatus(string vocabulariesText)
    {
        var approval = JsonNode.Parse(vocabulariesText)!["approvalStatus"]!;
        var statuses = approval["values"]!.AsArray().Select(n => (string)n!).ToHashSet();

        var transitions = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (key, node) in approval["transitions"]!.AsObject())
        {
            if (key.StartsWith('$')) continue;
            transitions[key] = node!.AsArray().Select(n => (string)n!).ToList();
        }
        return (statuses, transitions);
    }
```

Then, in `Load(...)`, after `ReviewIntervalMonthsByCategory = ParseReviewIntervals(vocabulariesText)` is computed, wire the two new fields into the returned `ConfigBundle`. Change the `return new ConfigBundle { ... }` so it also sets:

```csharp
            ApprovalStatuses = ParseApprovalStatus(vocabulariesText).Statuses,
            Transitions = ParseApprovalStatus(vocabulariesText).Transitions,
```

(For clarity and to parse once, you may hoist `var (statuses, transitions) = ParseApprovalStatus(vocabulariesText);` above the `return` and set `ApprovalStatuses = statuses, Transitions = transitions,`.)

- [ ] **Step 5: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Core/Config/ConfigBundle.cs src/Registrar.Core/Config/ConfigLoader.cs tests/Registrar.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat(core): load approval-status set and transition map into ConfigBundle"
```

---

### Task 2: RFC 4180 CSV parser

**Files:**
- Create: `src/Registrar.Core/Csv.cs`
- Test: `tests/Registrar.Tests/Core/CsvTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class Csv { IReadOnlyList<IReadOnlyList<string>> Parse(string text); }` - splits CSV text into rows of fields; double-quoted fields may contain commas, newlines, and doubled quotes (`""` -> `"`); handles LF and CRLF; a trailing newline does not add an empty row.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Core/CsvTests.cs`:

```csharp
using Registrar.Core;

namespace Registrar.Tests.Core;

public class CsvTests
{
    [Fact]
    public void Parse_reads_simple_rows()
    {
        var rows = Csv.Parse("a,b\n1,2\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "1", "2" }, rows[1]);
    }

    [Fact]
    public void Parse_handles_quoted_field_with_comma()
    {
        var rows = Csv.Parse("id,client\nX,\"Talewind CPA, LLC\"\n");
        Assert.Equal("Talewind CPA, LLC", rows[1][1]);
    }

    [Fact]
    public void Parse_handles_doubled_quotes_and_crlf()
    {
        var rows = Csv.Parse("a\r\n\"he said \"\"hi\"\"\"\r\n");
        Assert.Equal("a", rows[0][0]);
        Assert.Equal("he said \"hi\"", rows[1][0]);
    }

    [Fact]
    public void Parse_final_row_without_trailing_newline()
    {
        var rows = Csv.Parse("a,b\n1,2");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "1", "2" }, rows[1]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~CsvTests"`
Expected: FAIL - `Csv` does not exist.

- [ ] **Step 3: Implement `Csv`**

Create `src/Registrar.Core/Csv.cs`:

```csharp
using System.Text;

namespace Registrar.Core;

// Minimal RFC 4180 parser. Double-quoted fields may contain commas, newlines,
// and doubled quotes ("" -> "). Handles LF and CRLF row endings. A trailing
// newline does not produce an extra empty row.
public static class Csv
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true; i++; break;
                case ',':
                    row.Add(field.ToString()); field.Clear(); i++; break;
                case '\r':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                    i += (i + 1 < text.Length && text[i + 1] == '\n') ? 2 : 1;
                    break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                    i++; break;
                default:
                    field.Append(c); i++; break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~CsvTests"`
Expected: PASS - 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Csv.cs tests/Registrar.Tests/Core/CsvTests.cs
git commit -m "feat(core): add RFC 4180 CSV parser"
```

---

### Task 3: Registry record, interface, and CSV adapter

**Files:**
- Create: `src/Registrar.Core/Model/RegistryRecord.cs`
- Modify: `src/Registrar.Validation/IIdRegistry.cs`
- Create: `src/Registrar.Validation/CsvIdRegistry.cs`
- Test: `tests/Registrar.Tests/CsvIdRegistryTests.cs`

**Interfaces:**
- Consumes: `Csv.Parse` (Task 2).
- Produces:
  - `record RegistryRecord(string DocumentId, string ArtifactType, string Classification, string ApprovalStatus, string Version)`.
  - `interface IIdRegistry { RegistryRecord? Lookup(string documentId); }`.
  - `sealed class CsvIdRegistry : IIdRegistry` with `static CsvIdRegistry FromCsvText(string csvText)`; requires the five columns (case-insensitive headers), ignores extras, indexes by `documentId`, skips blank rows.
  - `sealed class RegistryFormatException : Exception` - thrown on empty CSV, a missing required column, or a duplicate Document ID.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/CsvIdRegistryTests.cs`:

```csharp
using Registrar.Validation;

namespace Registrar.Tests;

public class CsvIdRegistryTests
{
    private const string Sample =
        "documentId,artifactType,classification,approvalStatus,version,client\n" +
        "ORG-GOV-DS-016,DS,Client-Facing,Draft,v1.0,\"Talewind CPA, LLC\"\n" +
        "ORG-GOV-DS-017,DS,Client-Facing,Retired,v2.0,\"Acme, Inc\"\n";

    [Fact]
    public void Lookup_returns_record_ignoring_extra_columns()
    {
        var registry = CsvIdRegistry.FromCsvText(Sample);

        var record = registry.Lookup("ORG-GOV-DS-016");
        Assert.NotNull(record);
        Assert.Equal("DS", record!.ArtifactType);
        Assert.Equal("Client-Facing", record.Classification);
        Assert.Equal("Draft", record.ApprovalStatus);
        Assert.Equal("v1.0", record.Version);
    }

    [Fact]
    public void Lookup_returns_null_for_absent_id()
    {
        Assert.Null(CsvIdRegistry.FromCsvText(Sample).Lookup("ORG-GOV-DS-999"));
    }

    [Fact]
    public void FromCsvText_throws_on_missing_required_column()
    {
        var csv = "documentId,artifactType,classification,version\nORG-GOV-DS-016,DS,Client-Facing,v1.0\n";
        Assert.Throws<RegistryFormatException>(() => CsvIdRegistry.FromCsvText(csv));
    }

    [Fact]
    public void FromCsvText_throws_on_duplicate_document_id()
    {
        var csv = Sample + "ORG-GOV-DS-016,DS,Client-Facing,Approved,v3.0,\"Dup\"\n";
        Assert.Throws<RegistryFormatException>(() => CsvIdRegistry.FromCsvText(csv));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~CsvIdRegistryTests"`
Expected: FAIL - types do not exist.

- [ ] **Step 3: Create `RegistryRecord`**

Create `src/Registrar.Core/Model/RegistryRecord.cs`:

```csharp
namespace Registrar.Core.Model;

public sealed record RegistryRecord(
    string DocumentId,
    string ArtifactType,
    string Classification,
    string ApprovalStatus,
    string Version);
```

- [ ] **Step 4: Replace the `IIdRegistry` marker**

Replace the contents of `src/Registrar.Validation/IIdRegistry.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Validation;

// The authoritative ID Registry, swappable behind this interface. Slice 3 ships
// a CSV-export adapter (CsvIdRegistry); an xlsx or SharePoint adapter slots in
// later without touching the layers that consume it.
public interface IIdRegistry
{
    RegistryRecord? Lookup(string documentId);
}
```

- [ ] **Step 5: Implement `CsvIdRegistry`**

Create `src/Registrar.Validation/CsvIdRegistry.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class RegistryFormatException : Exception
{
    public RegistryFormatException(string message) : base(message) { }
}

public sealed class CsvIdRegistry : IIdRegistry
{
    private static readonly string[] Required =
        { "documentid", "artifacttype", "classification", "approvalstatus", "version" };

    private readonly IReadOnlyDictionary<string, RegistryRecord> _byId;

    private CsvIdRegistry(IReadOnlyDictionary<string, RegistryRecord> byId) => _byId = byId;

    public RegistryRecord? Lookup(string documentId)
        => _byId.TryGetValue(documentId, out var record) ? record : null;

    public static CsvIdRegistry FromCsvText(string csvText)
    {
        var rows = Csv.Parse(csvText);
        if (rows.Count == 0)
            throw new RegistryFormatException("Registry export is empty (no header row).");

        var index = new Dictionary<string, int>();
        var header = rows[0];
        for (var i = 0; i < header.Count; i++)
            index[header[i].Trim().ToLowerInvariant()] = i;

        foreach (var col in Required)
            if (!index.ContainsKey(col))
                throw new RegistryFormatException($"Registry export is missing the required column '{col}'.");

        var byId = new Dictionary<string, RegistryRecord>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            string Field(string col) => index[col] < row.Count ? row[index[col]].Trim() : string.Empty;

            var record = new RegistryRecord(
                Field("documentid"), Field("artifacttype"), Field("classification"),
                Field("approvalstatus"), Field("version"));

            if (string.IsNullOrEmpty(record.DocumentId)) continue;
            if (byId.ContainsKey(record.DocumentId))
                throw new RegistryFormatException($"Registry export has a duplicate Document ID '{record.DocumentId}'.");
            byId[record.DocumentId] = record;
        }

        return new CsvIdRegistry(byId);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~CsvIdRegistryTests"`
Expected: PASS - 4 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/Model/RegistryRecord.cs src/Registrar.Validation/IIdRegistry.cs src/Registrar.Validation/CsvIdRegistry.cs tests/Registrar.Tests/CsvIdRegistryTests.cs
git commit -m "feat(validation): add registry record, interface, and CSV adapter"
```

---

### Task 4: RegistryLayer (QA-SF-03)

**Files:**
- Create: `src/Registrar.Validation/RegistryLayer.cs`
- Modify: `tests/Registrar.Tests/SemanticTestSupport.cs`
- Test: `tests/Registrar.Tests/RegistryLayerTests.cs`
- Modify: `tests/Registrar.Tests/QaChecklistCatalogTests.cs`

**Interfaces:**
- Consumes: `SemanticContext`, `IIdRegistry`, `RegistryRecord`, `CsvIdRegistry` (Task 3), `Vocabulary` constants.
- Produces: `sealed class RegistryLayer : ISemanticLayer` with `const string CheckId = "QA-SF-03"`. Registry null + promotion -> blocking `RegistrySyncError`; registry null + draft -> `skipped`; Document ID absent -> blocking `RegistrySyncError`; artifactType or classification drift -> blocking `RegistrySyncError` (one check each). Layer `release`.

- [ ] **Step 1: Add a registry helper to the shared harness**

Add to `tests/Registrar.Tests/SemanticTestSupport.cs` (inside the class), and add `using Registrar.Validation;` at the top if not present:

```csharp
    public static SemanticContext ContextWithRegistry(string fixtureName, string mode, string registryCsv)
        => new(Agr(fixtureName), Config(), mode, CsvIdRegistry.FromCsvText(registryCsv));

    public static SemanticContext ContextNoRegistry(string fixtureName, string mode)
        => new(Agr(fixtureName), Config(), mode, null);

    public const string SampleRegistryCsv =
        "documentId,artifactType,classification,approvalStatus,version\n" +
        "ORG-GOV-DS-016,DS,Client-Facing,Draft,v1.0\n" +
        "ORG-GOV-DS-017,DS,Client-Facing,Retired,v2.0\n";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Registrar.Tests/RegistryLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class RegistryLayerTests
{
    [Fact]
    public void QA_SF_03_passes_when_id_present_and_metadata_agrees()
    {
        var checks = new RegistryLayer().Validate(
            SemanticTestSupport.ContextWithRegistry("agr-sample-ds-draft.json", Mode.Draft, SemanticTestSupport.SampleRegistryCsv));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_03_fails_when_id_absent()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("ORG-GOV-DS-016", "ORG-GOV-DS-999");
        var context = new SemanticContext(AgrParser.Parse(text), SemanticTestSupport.Config(),
            Mode.Draft, CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv));

        var check = Assert.Single(new RegistryLayer().Validate(context));
        Assert.Equal("QA-SF-03", check.CheckId);
        Assert.Equal(Layer.Release, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.RegistrySyncError, check.ErrorCategory);
    }

    [Fact]
    public void QA_SF_03_fails_on_classification_drift()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"classification\": \"Client-Facing\"", "\"classification\": \"Controlled Signoff\"");
        var context = new SemanticContext(AgrParser.Parse(text), SemanticTestSupport.Config(),
            Mode.Draft, CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv));

        var checks = new RegistryLayer().Validate(context);
        Assert.Contains(checks, c => c.Result == CheckResultValue.Fail
            && c.JsonPath == "/artifactIdentity/classification");
    }

    [Fact]
    public void QA_SF_03_blocks_promotion_when_registry_absent()
    {
        var check = Assert.Single(new RegistryLayer().Validate(
            SemanticTestSupport.ContextNoRegistry("agr-sample-ds-draft.json", Mode.Promotion)));
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.RegistrySyncError, check.ErrorCategory);
    }

    [Fact]
    public void QA_SF_03_skips_in_draft_when_registry_absent()
    {
        var check = Assert.Single(new RegistryLayer().Validate(
            SemanticTestSupport.ContextNoRegistry("agr-sample-ds-draft.json", Mode.Draft)));
        Assert.Equal(CheckResultValue.Skipped, check.Result);
    }
}
```

Add to `tests/Registrar.Tests/QaChecklistCatalogTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void RegistryLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;
        var ids = checklist["checks"]!.AsArray().Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(RegistryLayer.CheckId, ids);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~RegistryLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: FAIL - `RegistryLayer` does not exist.

- [ ] **Step 4: Implement `RegistryLayer`**

Create `src/Registrar.Validation/RegistryLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class RegistryLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-03";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 3";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var id = context.Agr.Identity.DocumentId;

        if (context.Registry is null)
        {
            if (context.Mode == Mode.Promotion)
                return new[] { Fail(
                    "The ID Registry export is required to promote.",
                    "/artifactIdentity/documentId",
                    "a --registry CSV export of the PMC Registry sheet", "no registry supplied",
                    "Supply the registry export with --registry <path> before promotion.") };
            return new[] { Skipped("No ID Registry supplied; registry checks are skipped in draft mode.") };
        }

        var record = context.Registry.Lookup(id);
        if (record is null)
            return new[] { Fail(
                $"Document ID '{id}' is not present in the ID Registry.",
                "/artifactIdentity/documentId",
                "a Document ID issued in the registry", id,
                "Have the Registrar issue this Document ID in the registry, or correct artifactIdentity.documentId.") };

        var checks = new List<Check>();
        if (record.ArtifactType != context.Agr.Identity.ArtifactType)
            checks.Add(Fail(
                $"Artifact type '{context.Agr.Identity.ArtifactType}' disagrees with the registry ('{record.ArtifactType}').",
                "/artifactIdentity/artifactType",
                $"registry artifactType '{record.ArtifactType}'", context.Agr.Identity.ArtifactType,
                "Reconcile artifactIdentity.artifactType with the registry, or correct the registry entry."));

        if (record.Classification != context.Agr.Identity.Classification)
            checks.Add(Fail(
                $"Classification '{context.Agr.Identity.Classification}' disagrees with the registry ('{record.Classification}').",
                "/artifactIdentity/classification",
                $"registry classification '{record.Classification}'", context.Agr.Identity.Classification,
                "Reconcile artifactIdentity.classification with the registry, or correct the registry entry."));

        return checks;
    }

    private static Check Fail(string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.RegistrySyncError,
        Message = message,
        JsonPath = jsonPath,
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

- [ ] **Step 5: Run tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~RegistryLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS - RegistryLayerTests (5) and the catalog tests.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Validation/RegistryLayer.cs tests/Registrar.Tests/SemanticTestSupport.cs tests/Registrar.Tests/RegistryLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat(validation): add registry existence and drift layer (QA-SF-03)"
```

---

### Task 5: LifecycleLayer (QA-SF-04)

**Files:**
- Create: `src/Registrar.Validation/LifecycleLayer.cs`
- Test: `tests/Registrar.Tests/LifecycleLayerTests.cs`
- Modify: `tests/Registrar.Tests/QaChecklistCatalogTests.cs`

**Interfaces:**
- Consumes: `SemanticContext`, `IIdRegistry`, `ConfigBundle.ApprovalStatuses`/`Transitions` (Task 1), `Vocabulary` constants.
- Produces: `sealed class LifecycleLayer : ISemanticLayer` with `const string CheckId = "QA-SF-04"`. Registry null or Document ID absent -> `skipped`; registry status not controlled -> blocking `LifecycleTransitionError`; terminal registry status (empty transition list) -> blocking; same-state target -> pass; target in the allowed set -> pass; otherwise -> blocking illegal transition. Layer `release`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/LifecycleLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class LifecycleLayerTests
{
    private static SemanticContext Context(string agrText, string mode)
        => new(AgrParser.Parse(agrText), SemanticTestSupport.Config(), mode,
               CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv));

    [Fact]
    public void QA_SF_04_passes_on_same_state_regeneration()
    {
        // Golden AGR approvalStatus is Draft; registry ORG-GOV-DS-016 is Draft.
        var checks = new LifecycleLayer().Validate(
            Context(SemanticTestSupport.FixtureText("agr-sample-ds-draft.json"), Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_04_passes_on_legal_transition()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"approvalStatus\": \"Draft\"", "\"approvalStatus\": \"In Review\"");

        Assert.Empty(new LifecycleLayer().Validate(Context(text, Mode.Draft)));
    }

    [Fact]
    public void QA_SF_04_fails_on_illegal_transition()
    {
        // Draft -> Approved is not in the transition map.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"approvalStatus\": \"Draft\"", "\"approvalStatus\": \"Approved\"");

        var check = Assert.Single(new LifecycleLayer().Validate(Context(text, Mode.Draft)));
        Assert.Equal("QA-SF-04", check.CheckId);
        Assert.Equal(Layer.Release, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.LifecycleTransitionError, check.ErrorCategory);
    }

    [Fact]
    public void QA_SF_04_blocks_regeneration_of_terminal_registry_state()
    {
        // ORG-GOV-DS-017 is Retired in the registry.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("ORG-GOV-DS-016", "ORG-GOV-DS-017");

        var check = Assert.Single(new LifecycleLayer().Validate(Context(text, Mode.Draft)));
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Contains("terminal", check.Message);
    }

    [Fact]
    public void QA_SF_04_skips_when_registry_absent()
    {
        var context = new SemanticContext(
            SemanticTestSupport.Agr("agr-sample-ds-draft.json"), SemanticTestSupport.Config(), Mode.Draft, null);

        var check = Assert.Single(new LifecycleLayer().Validate(context));
        Assert.Equal(CheckResultValue.Skipped, check.Result);
    }
}
```

Add to `tests/Registrar.Tests/QaChecklistCatalogTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void LifecycleLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;
        var ids = checklist["checks"]!.AsArray().Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(LifecycleLayer.CheckId, ids);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~LifecycleLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: FAIL - `LifecycleLayer` does not exist.

- [ ] **Step 3: Implement `LifecycleLayer`**

Create `src/Registrar.Validation/LifecycleLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class LifecycleLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-04";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 4";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        if (context.Registry is null)
            return new[] { Skipped("No ID Registry supplied; lifecycle check skipped.") };

        var record = context.Registry.Lookup(context.Agr.Identity.DocumentId);
        if (record is null)
            return new[] { Skipped("Document ID is not in the registry; lifecycle not evaluated (see QA-SF-03).") };

        var current = record.ApprovalStatus;
        var target = context.Agr.Identity.ApprovalStatus;

        if (!context.Config.ApprovalStatuses.Contains(current))
            return new[] { Fail(
                $"Registry approval status '{current}' is not a controlled status value.",
                $"a controlled status in {Join(context.Config.ApprovalStatuses)}", current,
                "Correct the registry entry's approval status to a controlled value.") };

        context.Config.Transitions.TryGetValue(current, out var allowed);
        allowed ??= Array.Empty<string>();

        if (allowed.Count == 0)
            return new[] { Fail(
                $"Registry status '{current}' is terminal; the artifact cannot be regenerated.",
                "a non-terminal registry status", current,
                "Issue a new Document ID or version for a superseded or retired artifact; terminal artifacts are historical records.") };

        if (target == current)
            return Array.Empty<Check>();     // same-state regeneration, a non-transition

        if (allowed.Contains(target))
            return Array.Empty<Check>();

        return new[] { Fail(
            $"Transition from '{current}' to '{target}' is not a legal approval-status transition.",
            $"a target in {Join(allowed)}", target,
            $"Set artifactIdentity.approvalStatus to a legal next state from '{current}', or leave it unchanged.") };
    }

    private static string Join(IEnumerable<string> items) => "[" + string.Join(", ", items) + "]";

    private static Check Fail(string message, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.LifecycleTransitionError,
        Message = message,
        JsonPath = "/artifactIdentity/approvalStatus",
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

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~LifecycleLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS - LifecycleLayerTests (5) and the catalog tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Validation/LifecycleLayer.cs tests/Registrar.Tests/LifecycleLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat(validation): add lifecycle transition layer (QA-SF-04)"
```

---

### Task 6: Wire the registry into the pipeline, add `--registry`, and prove it end-to-end

**Files:**
- Modify: `src/Registrar.Validation/ValidationService.cs`
- Modify: `src/Registrar.Cli/ValidateCommand.cs`
- Modify: `src/Registrar.Cli/Program.cs`
- Create: `fixtures/registry-sample.csv`
- Create: `fixtures/agr-sample-ds-unregistered.json`
- Test: `tests/Registrar.Tests/ValidationServiceTests.cs`
- Test: `tests/Registrar.Tests/ValidateCommandTests.cs`

**Interfaces:**
- Consumes: `CsvIdRegistry`, `RegistryFormatException`, `RegistryLayer`, `LifecycleLayer` (Tasks 3-5), existing Slice 2 pipeline.
- Produces:
  - `ValidationService.Validate(string agrText, string schemaPath, string? registryCsvText = null)` - optional third parameter; keeps Slice 2 callers working.
  - `ValidateCommand.Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? registryPath = null)` - optional fifth parameter.

- [ ] **Step 1: Create the registry fixture and the unregistered AGR fixture**

Create `fixtures/registry-sample.csv` (note the quoted client column with an embedded comma, which the adapter must tolerate and ignore):

```csv
documentId,artifactType,classification,approvalStatus,version,client
ORG-GOV-DS-016,DS,Client-Facing,Draft,v1.0,"Talewind CPA, LLC"
ORG-GOV-DS-017,DS,Client-Facing,Retired,v2.0,"Acme, Inc"
```

Create `fixtures/agr-sample-ds-unregistered.json` as a copy of `fixtures/agr-sample-ds-draft.json` with the `documentId` changed from `ORG-GOV-DS-016` to `ORG-GOV-DS-999` (a schema-valid ID that is not in the registry fixture).

- [ ] **Step 2: Write the failing tests**

Add to `tests/Registrar.Tests/ValidationServiceTests.cs` (inside the existing class; it already has `FixedClock` and `Paths()`):

```csharp
    [Fact]
    public void Valid_agr_with_registry_passes()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var report = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv);

        Assert.Equal(OverallResult.Pass, report.OverallResult);
        Assert.DoesNotContain(report.Checks, c => c.Result == CheckResultValue.Fail);
    }

    [Fact]
    public void Unregistered_id_with_registry_fails_with_qa_sf_03()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-unregistered.json"));

        var report = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv);

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.Contains(report.Checks, c => c.CheckId == "QA-SF-03" && c.Result == CheckResultValue.Fail);
    }

    [Fact]
    public void Malformed_registry_fails_with_qa_sf_03()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var report = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, "documentId,artifactType\nORG-GOV-DS-016,DS\n");

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.Contains(report.Checks,
            c => c.CheckId == "QA-SF-03" && c.Result == CheckResultValue.Fail
                 && c.ErrorCategory == ErrorCategory.RegistrySyncError);
    }
```

Add to `tests/Registrar.Tests/ValidateCommandTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void Valid_agr_with_registry_flag_exits_0()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Unregistered_agr_with_registry_flag_exits_1_with_qa_sf_03()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-unregistered.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(1, code);
        var report = JsonNode.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json")))!.AsObject();
        Assert.Contains(report["checks"]!.AsArray(), c => (string?)c!["checkId"] == "QA-SF-03");
    }

    [Fact]
    public void Missing_registry_path_exits_2()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var missing = Path.Combine(Path.GetDirectoryName(agrPath)!, "no-registry.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, missing);

        Assert.Equal(2, code);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidationServiceTests|FullyQualifiedName~ValidateCommandTests"`
Expected: FAIL - the `Validate`/`Run` overloads with a registry argument do not exist yet.

- [ ] **Step 4: Rewrite `ValidationService` to load the registry and run the release layers**

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

    public ValidationReport Validate(string agrText, string schemaPath, string? registryCsvText = null)
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
            // A malformed registry cannot back the release layers; the single
            // RegistrySyncError below stands in for them.
            if (registryError is null)
            {
                layers.Add(new RegistryLayer());
                layers.Add(new LifecycleLayer());
            }

            var all = new List<Check>(schemaChecks);
            foreach (var layer in layers) all.AddRange(layer.Validate(context));
            if (registryError is not null) all.Add(MalformedRegistryCheck(registryError));

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

- [ ] **Step 5: Add the `--registry` read to `ValidateCommand`**

In `src/Registrar.Cli/ValidateCommand.cs`, change the method signature to add the optional parameter:

```csharp
    public static int Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? registryPath = null)
```

Immediately before the `var report = new ValidationService(clock).Validate(...)` line, insert the registry read:

```csharp
        string? registryCsvText = null;
        if (registryPath is not null)
        {
            if (!File.Exists(registryPath))
            {
                stderr.WriteLine($"error: registry file not found: {registryPath}");
                return 2;
            }
            try
            {
                registryCsvText = File.ReadAllText(registryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: cannot read registry: {ex.Message}");
                return 2;
            }
        }
```

And change the `Validate` call to pass it:

```csharp
        var report = new ValidationService(clock).Validate(agrText, schemaPath, registryCsvText);
```

- [ ] **Step 6: Add the `--registry` option in `Program.cs`**

In `src/Registrar.Cli/Program.cs`, add the option and update the handler:

```csharp
var registryOption = new Option<string?>(
    "--registry", "Path to a CSV export of the ID Registry (the PMC Registry sheet).");
validate.AddOption(registryOption);

validate.SetHandler((string agrPath, string? registryPath) =>
{
    var code = ValidateCommand.Run(agrPath, new SystemClock(), Console.Out, Console.Error, registryPath);
    Environment.ExitCode = code;
}, agrArgument, registryOption);
```

Replace the existing single-argument `validate.SetHandler(...)` call with the two-symbol handler above. Keep the `agrArgument` and `validate.AddArgument(agrArgument)` lines as they are. Note: `System.CommandLine` is prerelease; if the resolved `SetHandler` overload differs, keep the contract - `validate` takes the AGR argument plus an optional `--registry` option and forwards both to `ValidateCommand.Run`.

- [ ] **Step 7: Run the new tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test tests/Registrar.Tests --filter "FullyQualifiedName~ValidationServiceTests|FullyQualifiedName~ValidateCommandTests"`
Expected: PASS.

- [ ] **Step 8: Run the full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test Registrar.sln`
Expected: PASS - every test (Slice 1 + Slice 2 + Slice 3).

- [ ] **Step 9: Verify the CLI end-to-end by hand**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project src/Registrar.Cli -- validate fixtures/agr-sample-ds-draft.json --registry fixtures/registry-sample.csv`
Expected: prints `pass: ORG-GOV-DS-016 (0 failing, 0 warning check(s))...`, exit code 0.

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet run --project src/Registrar.Cli -- validate fixtures/agr-sample-ds-unregistered.json --registry fixtures/registry-sample.csv`
Expected: prints `fail: ORG-GOV-DS-999 (1 failing, 0 warning check(s))...`, exit code 1.

Clean up the hand-run reports so they are not committed:

```bash
rm -f fixtures/validation-report.json
```

- [ ] **Step 10: Commit**

```bash
git add src/Registrar.Validation/ValidationService.cs src/Registrar.Cli/ValidateCommand.cs src/Registrar.Cli/Program.cs fixtures/registry-sample.csv fixtures/agr-sample-ds-unregistered.json tests/Registrar.Tests/ValidationServiceTests.cs tests/Registrar.Tests/ValidateCommandTests.cs
git commit -m "feat(validation): wire the ID registry and release-tier layers into validate"
```

---

## Self-Review

**Spec coverage** (each spec section maps to a task):
- Registry adapter + `IIdRegistry` (real) + `RegistryRecord` -> Task 3. CSV parsing -> Task 2.
- Extended record (documentId, artifactType, classification, approvalStatus, version) -> Task 3.
- ConfigBundle status set + transition map -> Task 1.
- QA-SF-03 existence + type/classification drift -> Task 4 (`RegistryLayer`).
- QA-SF-04 status alignment, legal transition, same-state pass, terminal block -> Task 5 (`LifecycleLayer`).
- Registry absent: promotion -> QA-SF-03 blocking fail, QA-SF-04 skipped; draft -> both skipped -> Tasks 4-5.
- Malformed registry -> blocking QA-SF-03 RegistrySyncError -> Task 6 (`ValidationService`).
- CSV transport / `--registry` provisioning; missing path -> exit 2 -> Task 6 (CLI).
- Release-tier layers run after the schema gate alongside Slice 2 layers -> Task 6.
- Testing (per-check pass/fail, mode/presence, adapter, config, CLI) -> Tasks 1-6; fixtures (registry CSV, malformed via inline strings, unregistered AGR) -> Tasks 3, 6.
- Deferred (QA-SF-18 payload, xlsx/SharePoint adapters) -> not implemented; interface is swappable (Task 3).

**Placeholder scan:** No TBD/TODO. Every code step shows complete code; every command shows expected output and the `DOTNET_ROLL_FORWARD` prefix.

**Type consistency:** `IIdRegistry.Lookup(string) -> RegistryRecord?` defined in Task 3, consumed identically in Tasks 4-6. `RegistryRecord(DocumentId, ArtifactType, Classification, ApprovalStatus, Version)` fields used consistently in the drift checks (Task 4) and lifecycle checks (Task 5). `ConfigBundle.ApprovalStatuses` / `Transitions` defined in Task 1, consumed in Task 5. `SemanticContext(AgrDocument, ConfigBundle, string, IIdRegistry?)` (Slice 2) constructed with a non-null registry in Tasks 4-6. `CsvIdRegistry.FromCsvText(string)` and `RegistryFormatException` defined in Task 3, consumed in Task 6. `Validate(agrText, schemaPath, registryCsvText?)` and `Run(..., registryPath?)` optional parameters keep Slice 2 call sites compiling. Every layer exposes `const string CheckId` used by its catalog test. `Layer.Release`, `CheckResultValue.Skipped`, `Severity.Info`, `ErrorCategory.RegistrySyncError`, `ErrorCategory.LifecycleTransitionError` are pre-existing Slice 1 vocabulary constants.

**Known risk:** the CSV parser and `System.CommandLine`'s two-symbol `SetHandler` are the version/parse-sensitive surfaces. The CSV parser is covered by Task 2's unit tests; the `SetHandler` change is behind `ValidateCommand.Run`, which is fully tested, so a differing beta API surfaces immediately at Step 7.
