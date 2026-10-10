# Operator Menu, QoL Shell, and Self-Contained Launcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an interactive `menu` mode to `registrar` with operator quality-of-life features, plus a self-contained single-file `registrar.exe` and a double-click launcher.

**Architecture:** Pure, testable units (report presenter, Governor clipboard text, settings, AGR picker) plus a thin I/O shell (`MenuCommand` driven through `IConsole`/`IOperatorShell` seams). The menu reuses the existing `ValidateCommand`/`BuildCommand` verbs unchanged and displays the `validation-report.json` they write; it adds no validation or rendering logic.

**Tech Stack:** .NET 8, C#, System.CommandLine (already referenced), xUnit. No new runtime dependency.

## Global Constraints

- .NET 8. On this machine run tests with `DOTNET_ROLL_FORWARD=LatestMajor dotnet test`.
- The menu is a thin shell over the existing verbs: it calls `ValidateCommand.Run` / `BuildCommand.Run` (writing the same report/DOCX/payload) and displays what they wrote. No new validation/rendering logic; it cannot bypass fail-closed.
- Zero LLM calls; the menu is deterministic console UI.
- No new runtime dependency (hand-rolled console UI; Spectre.Console and similar are out).
- The operator checkpoint stays a human decision: no auto-fix, no auto-submit, no Governor contact. "Copy failures" only places text on the clipboard.
- Settings store paths only (last folder, recent AGR paths) in `%APPDATA%\registrar\settings.json`; never AGR content, client data, or secrets. Missing/corrupt settings load defaults without throwing.
- ASCII only, US English, no emoji. Console color via `Console.ForegroundColor` (green pass / yellow warn / red fail).
- Config ships as editable files next to the exe (rule 3); publish is self-contained single-file with NO trimming.
- The published executable is named `registrar.exe` (`AssemblyName`).
- Source held to escapes-only by `SourceHygieneTests` (no literal non-ASCII in source/tests).

## File Structure

**Create:**
- `src/Registrar.Cli/ReportPresenter.cs` - pure report-to-lines formatter + `PresenterLine`/`LineStyle`
- `src/Registrar.Cli/GovernorClipboardText.cs` - pure failures-to-clipboard-text
- `src/Registrar.Cli/MenuSettings.cs` - settings record + pure recent/folder helpers
- `src/Registrar.Cli/SettingsStore.cs` - load/save settings JSON at an injected path
- `src/Registrar.Cli/AgrPicker.cs` - folder + recent AGR candidate list
- `src/Registrar.Cli/IConsole.cs` - console seam + `SystemConsole` real impl
- `src/Registrar.Cli/IOperatorShell.cs` - OS seam (open file, clipboard) + `OperatorShell` real impl
- `src/Registrar.Cli/MenuCommand.cs` - the interactive loop
- `scripts/publish.ps1` - self-contained publish + zip packaging
- `Registrar.cmd` - double-click launcher
- `tests/Registrar.Tests/Cli/ReportPresenterTests.cs`
- `tests/Registrar.Tests/Cli/GovernorClipboardTextTests.cs`
- `tests/Registrar.Tests/Cli/MenuSettingsTests.cs`
- `tests/Registrar.Tests/Cli/SettingsStoreTests.cs`
- `tests/Registrar.Tests/Cli/AgrPickerTests.cs`
- `tests/Registrar.Tests/Cli/MenuCommandTests.cs` (+ `FakeConsole`, `FakeOperatorShell`)

**Modify:**
- `src/Registrar.Core/ReportSerializer.cs` - add `FromJson`
- `src/Registrar.Cli/Program.cs` - register the `menu` verb
- `src/Registrar.Cli/Registrar.Cli.csproj` - `AssemblyName` = `registrar`
- `tests/Registrar.Tests/Core/ConfigLocatorTests.cs` - packaged-layout resolution test
- `README.md` - "How to run" section

---

### Task 1: ReportSerializer.FromJson

**Files:**
- Modify: `src/Registrar.Core/ReportSerializer.cs`
- Test: `tests/Registrar.Tests/ReportSerializerTests.cs`

**Interfaces:**
- Produces: `ReportSerializer.FromJson(string json) -> ValidationReport` (round-trips `ToJson`).

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/ReportSerializerTests.cs`:

```csharp
[Fact]
public void FromJson_round_trips_ToJson()
{
    var report = new Registrar.Core.Model.ValidationReport
    {
        ReportVersion = "1.0", ExecutionId = "e", DocumentId = "ORG-GOV-DS-016",
        RequestedMode = "draft", OverallResult = "fail", EngineVersion = "v",
        ConfigVersions = new Dictionary<string, string> { ["a"] = "b" },
        TimestampUtc = "2026-07-06T00:00:00Z",
        Checks = new[]
        {
            new Registrar.Core.Model.Check
            {
                CheckId = "QA-ENG-05", LayerName = "input", Result = "fail",
                SeverityLevel = "blocking", Message = "bad char", Remediation = "fix it"
            }
        }
    };
    var json = Registrar.Core.ReportSerializer.ToJson(report);
    var back = Registrar.Core.ReportSerializer.FromJson(json);
    Assert.Equal("ORG-GOV-DS-016", back.DocumentId);
    Assert.Equal("fail", back.OverallResult);
    Assert.Single(back.Checks);
    Assert.Equal("QA-ENG-05", back.Checks[0].CheckId);
    Assert.Equal("fix it", back.Checks[0].Remediation);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter FromJson_round_trips_ToJson`
Expected: FAIL (no `FromJson`).

- [ ] **Step 3: Add FromJson**

In `src/Registrar.Core/ReportSerializer.cs`, add after `ToJson`:

```csharp
    public static ValidationReport FromJson(string json)
        => JsonSerializer.Deserialize<ValidationReport>(json, Options)
           ?? throw new JsonException("validation-report.json deserialized to null.");
```

- [ ] **Step 4: Run test to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter FromJson_round_trips_ToJson`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/ReportSerializer.cs tests/Registrar.Tests/ReportSerializerTests.cs
git commit -m "feat: ReportSerializer.FromJson to re-read a written validation report"
```

---

### Task 2: ReportPresenter

**Files:**
- Create: `src/Registrar.Cli/ReportPresenter.cs`, `tests/Registrar.Tests/Cli/ReportPresenterTests.cs`

**Interfaces:**
- Consumes: `Registrar.Core.Model.ValidationReport`, `Check`.
- Produces: `enum LineStyle { Normal, Pass, Warn, Fail }`; `sealed record PresenterLine(string Text, LineStyle Style)`; `ReportPresenter.Present(ValidationReport report) -> IReadOnlyList<PresenterLine>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Cli/ReportPresenterTests.cs`:

```csharp
using Registrar.Cli;
using Registrar.Core.Model;

namespace Registrar.Tests.Cli;

public class ReportPresenterTests
{
    private static ValidationReport Report(string overall, params Check[] checks) => new()
    {
        ReportVersion = "1.0", ExecutionId = "e", DocumentId = "ORG-GOV-DS-016",
        RequestedMode = "draft", OverallResult = overall, EngineVersion = "v",
        ConfigVersions = new Dictionary<string, string>(), TimestampUtc = "t", Checks = checks
    };

    [Fact]
    public void Fail_report_shows_banner_and_each_failing_check()
    {
        var report = Report("fail", new Check
        {
            CheckId = "QA-ENG-05", LayerName = "input", Result = "fail", SeverityLevel = "blocking",
            Message = "prohibited character", Remediation = "remove the em dash", JsonPath = "/context/owner"
        });
        var lines = ReportPresenter.Present(report);
        var text = string.Join("\n", lines.Select(l => l.Text));
        Assert.Contains(lines, l => l.Style == LineStyle.Fail && l.Text.Contains("FAIL"));
        Assert.Contains("ORG-GOV-DS-016", text);
        Assert.Contains("QA-ENG-05", text);
        Assert.Contains("remove the em dash", text);
        Assert.Contains("/context/owner", text);
    }

    [Fact]
    public void Pass_report_shows_pass_and_no_failure_blocks()
    {
        var lines = ReportPresenter.Present(Report("pass"));
        Assert.Contains(lines, l => l.Style == LineStyle.Pass && l.Text.Contains("PASS"));
        Assert.DoesNotContain(lines, l => l.Style == LineStyle.Fail);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter ReportPresenterTests`
Expected: FAIL (types not defined).

- [ ] **Step 3: Implement ReportPresenter**

Create `src/Registrar.Cli/ReportPresenter.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Cli;

public enum LineStyle { Normal, Pass, Warn, Fail }

public sealed record PresenterLine(string Text, LineStyle Style);

public static class ReportPresenter
{
    public static IReadOnlyList<PresenterLine> Present(ValidationReport report)
    {
        var lines = new List<PresenterLine>();
        var failing = report.Checks.Where(c => c.Result == CheckResultValue.Fail).ToList();
        var warning = report.Checks.Where(c => c.Result == CheckResultValue.Warn).ToList();

        var (banner, style) = report.OverallResult switch
        {
            OverallResult.Pass => ("PASS", LineStyle.Pass),
            OverallResult.PassWithWarnings => ("PASS WITH WARNINGS", LineStyle.Warn),
            _ => ("FAIL", LineStyle.Fail)
        };
        lines.Add(new PresenterLine(
            $"{banner}  -  {report.DocumentId}  ({report.RequestedMode})", style));
        lines.Add(new PresenterLine(
            $"{report.Checks.Count} check(s): {failing.Count} failing, {warning.Count} warning.", LineStyle.Normal));

        foreach (var c in failing.Concat(warning))
        {
            var s = c.Result == CheckResultValue.Fail ? LineStyle.Fail : LineStyle.Warn;
            lines.Add(new PresenterLine("", LineStyle.Normal));
            lines.Add(new PresenterLine($"[{c.Result}] {c.CheckId} ({c.LayerName}/{c.SeverityLevel})", s));
            lines.Add(new PresenterLine($"  {c.Message}", LineStyle.Normal));
            if (c.JsonPath is not null) lines.Add(new PresenterLine($"  at: {c.JsonPath}", LineStyle.Normal));
            if (c.Expected is not null || c.Actual is not null)
                lines.Add(new PresenterLine($"  expected: {c.Expected}   actual: {c.Actual}", LineStyle.Normal));
            if (c.Remediation is not null) lines.Add(new PresenterLine($"  fix: {c.Remediation}", LineStyle.Normal));
            if (c.RuleSource is not null) lines.Add(new PresenterLine($"  rule: {c.RuleSource}", LineStyle.Normal));
        }
        return lines;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter ReportPresenterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Cli/ReportPresenter.cs tests/Registrar.Tests/Cli/ReportPresenterTests.cs
git commit -m "feat: human-readable validation report presenter"
```

---

### Task 3: GovernorClipboardText

**Files:**
- Create: `src/Registrar.Cli/GovernorClipboardText.cs`, `tests/Registrar.Tests/Cli/GovernorClipboardTextTests.cs`

**Interfaces:**
- Produces: `GovernorClipboardText.Build(ValidationReport report) -> string` (one entry per FAILING check; header names the document).

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Cli/GovernorClipboardTextTests.cs`:

```csharp
using Registrar.Cli;
using Registrar.Core.Model;

namespace Registrar.Tests.Cli;

public class GovernorClipboardTextTests
{
    [Fact]
    public void Build_lists_failing_checks_with_header_and_excludes_passes()
    {
        var report = new ValidationReport
        {
            ReportVersion = "1.0", ExecutionId = "e", DocumentId = "ORG-GOV-DS-016",
            RequestedMode = "promotion", OverallResult = "fail", EngineVersion = "v",
            ConfigVersions = new Dictionary<string, string>(), TimestampUtc = "t",
            Checks = new[]
            {
                new Check { CheckId = "QA-ENG-05", LayerName = "input", Result = "fail",
                    SeverityLevel = "blocking", Message = "bad char", JsonPath = "/context/owner",
                    Remediation = "remove the em dash", RuleSource = "ORG-MST-SPEC-001 Section 4.2" },
                new Check { CheckId = "QA-PASS", LayerName = "input", Result = "pass",
                    SeverityLevel = "info", Message = "fine" }
            }
        };
        var text = GovernorClipboardText.Build(report);
        Assert.Contains("ORG-GOV-DS-016", text);
        Assert.Contains("QA-ENG-05", text);
        Assert.Contains("/context/owner", text);
        Assert.Contains("remove the em dash", text);
        Assert.Contains("ORG-MST-SPEC-001 Section 4.2", text);
        Assert.DoesNotContain("QA-PASS", text);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter GovernorClipboardTextTests`
Expected: FAIL (type not defined).

- [ ] **Step 3: Implement GovernorClipboardText**

Create `src/Registrar.Cli/GovernorClipboardText.cs`:

```csharp
using System.Text;
using Registrar.Core.Model;

namespace Registrar.Cli;

public static class GovernorClipboardText
{
    public static string Build(ValidationReport report)
    {
        var failing = report.Checks.Where(c => c.Result == CheckResultValue.Fail).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Validation failures for {report.DocumentId} ({report.RequestedMode}). "
            + "Regenerate the AGR addressing each item:");
        sb.AppendLine();
        var n = 0;
        foreach (var c in failing)
        {
            n++;
            sb.AppendLine($"{n}. {c.CheckId} at {c.JsonPath ?? "(document)"}");
            if (c.Expected is not null || c.Actual is not null)
                sb.AppendLine($"   expected: {c.Expected}; actual: {c.Actual}");
            if (c.Remediation is not null) sb.AppendLine($"   fix: {c.Remediation}");
            if (c.RuleSource is not null) sb.AppendLine($"   rule source: {c.RuleSource}");
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter GovernorClipboardTextTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Cli/GovernorClipboardText.cs tests/Registrar.Tests/Cli/GovernorClipboardTextTests.cs
git commit -m "feat: Governor-paste text builder for validation failures"
```

---

### Task 4: MenuSettings (record + pure recent/folder helpers)

**Files:**
- Create: `src/Registrar.Cli/MenuSettings.cs`, `tests/Registrar.Tests/Cli/MenuSettingsTests.cs`

**Interfaces:**
- Produces: `sealed record MenuSettings { string? LastFolder; IReadOnlyList<string> RecentAgrs (default empty); }` with pure methods `WithFolder(string) -> MenuSettings` and `WithRecent(string agrPath) -> MenuSettings` (dedup by ordinal-ignore-case, most-recent-first, capped at 8).

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Cli/MenuSettingsTests.cs`:

```csharp
using Registrar.Cli;

namespace Registrar.Tests.Cli;

public class MenuSettingsTests
{
    [Fact]
    public void WithRecent_puts_newest_first_dedups_and_caps_at_8()
    {
        var s = new MenuSettings();
        for (var i = 1; i <= 9; i++) s = s.WithRecent($@"C:\agrs\a{i}.json");
        s = s.WithRecent(@"C:\agrs\a5.json"); // re-add existing -> moves to front, no dup
        Assert.Equal(@"C:\agrs\a5.json", s.RecentAgrs[0]);
        Assert.Equal(8, s.RecentAgrs.Count);
        Assert.Single(s.RecentAgrs.Where(p => p.EndsWith("a5.json")));
        Assert.DoesNotContain(s.RecentAgrs, p => p.EndsWith("a1.json")); // oldest evicted
    }

    [Fact]
    public void WithFolder_sets_last_folder()
    {
        var s = new MenuSettings().WithFolder(@"C:\ORG\AGRs");
        Assert.Equal(@"C:\ORG\AGRs", s.LastFolder);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter MenuSettingsTests`
Expected: FAIL (type not defined).

- [ ] **Step 3: Implement MenuSettings**

Create `src/Registrar.Cli/MenuSettings.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Registrar.Cli;

public sealed record MenuSettings
{
    public const int MaxRecent = 8;

    [JsonPropertyName("lastFolder")] public string? LastFolder { get; init; }
    [JsonPropertyName("recentAgrs")] public IReadOnlyList<string> RecentAgrs { get; init; } = Array.Empty<string>();

    public MenuSettings WithFolder(string folder) => this with { LastFolder = folder };

    public MenuSettings WithRecent(string agrPath)
    {
        var deduped = new List<string> { agrPath };
        deduped.AddRange(RecentAgrs.Where(p => !string.Equals(p, agrPath, StringComparison.OrdinalIgnoreCase)));
        return this with { RecentAgrs = deduped.Take(MaxRecent).ToList() };
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter MenuSettingsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Cli/MenuSettings.cs tests/Registrar.Tests/Cli/MenuSettingsTests.cs
git commit -m "feat: MenuSettings with dedup/capped recent list"
```

---

### Task 5: SettingsStore (load/save at an injected path)

**Files:**
- Create: `src/Registrar.Cli/SettingsStore.cs`, `tests/Registrar.Tests/Cli/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `MenuSettings` (Task 4).
- Produces: `sealed class SettingsStore { SettingsStore(string path); MenuSettings Load(); void Save(MenuSettings s); static string DefaultPath(); }`. `Load` returns defaults on a missing or corrupt file (no throw).

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Cli/SettingsStoreTests.cs`:

```csharp
using Registrar.Cli;

namespace Registrar.Tests.Cli;

public class SettingsStoreTests
{
    private static string TempFile() =>
        Path.Combine(Directory.CreateTempSubdirectory().FullName, "settings.json");

    [Fact]
    public void Save_then_load_round_trips()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        store.Save(new MenuSettings { LastFolder = @"C:\ORG\AGRs" }.WithRecent(@"C:\a.json"));
        var loaded = store.Load();
        Assert.Equal(@"C:\ORG\AGRs", loaded.LastFolder);
        Assert.Contains(@"C:\a.json", loaded.RecentAgrs);
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var loaded = new SettingsStore(TempFile()).Load();
        Assert.Null(loaded.LastFolder);
        Assert.Empty(loaded.RecentAgrs);
    }

    [Fact]
    public void Load_corrupt_file_returns_defaults_without_throwing()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ not valid json ");
        var loaded = new SettingsStore(path).Load();
        Assert.Null(loaded.LastFolder);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter SettingsStoreTests`
Expected: FAIL (type not defined).

- [ ] **Step 3: Implement SettingsStore**

Create `src/Registrar.Cli/SettingsStore.cs`:

```csharp
using System.Text.Json;

namespace Registrar.Cli;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public static string DefaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "registrar", "settings.json");

    public MenuSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new MenuSettings();
            return JsonSerializer.Deserialize<MenuSettings>(File.ReadAllText(_path), Options)
                   ?? new MenuSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new MenuSettings();
        }
    }

    public void Save(MenuSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; never fail the operator's run over them.
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter SettingsStoreTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Cli/SettingsStore.cs tests/Registrar.Tests/Cli/SettingsStoreTests.cs
git commit -m "feat: SettingsStore load/save (defaults on missing or corrupt)"
```

---

### Task 6: AgrPicker

**Files:**
- Create: `src/Registrar.Cli/AgrPicker.cs`, `tests/Registrar.Tests/Cli/AgrPickerTests.cs`

**Interfaces:**
- Produces: `AgrPicker.Candidates(string workingFolder, IReadOnlyList<string> recent) -> IReadOnlyList<string>` - existing recent AGRs first (in recent order), then `*.json` files in the folder not already listed (sorted by name), deduped by full path (ordinal-ignore-case). Nonexistent recent entries are dropped.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Cli/AgrPickerTests.cs`:

```csharp
using Registrar.Cli;

namespace Registrar.Tests.Cli;

public class AgrPickerTests
{
    [Fact]
    public void Candidates_lists_recent_first_then_folder_dropping_missing_and_dupes()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var a = Path.Combine(dir, "a.json"); File.WriteAllText(a, "{}");
        var b = Path.Combine(dir, "b.json"); File.WriteAllText(b, "{}");
        var missing = Path.Combine(dir, "gone.json"); // never created

        var candidates = AgrPicker.Candidates(dir, new[] { b, missing });

        Assert.Equal(b, candidates[0]);                 // recent, exists -> first
        Assert.Contains(a, candidates);                 // folder file included
        Assert.DoesNotContain(missing, candidates);     // missing recent dropped
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter AgrPickerTests`
Expected: FAIL (type not defined).

- [ ] **Step 3: Implement AgrPicker**

Create `src/Registrar.Cli/AgrPicker.cs`:

```csharp
namespace Registrar.Cli;

public static class AgrPicker
{
    public static IReadOnlyList<string> Candidates(string workingFolder, IReadOnlyList<string> recent)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in recent)
        {
            if (File.Exists(path) && seen.Add(Path.GetFullPath(path)))
                result.Add(path);
        }

        if (Directory.Exists(workingFolder))
        {
            foreach (var path in Directory.EnumerateFiles(workingFolder, "*.json").OrderBy(p => p))
            {
                if (seen.Add(Path.GetFullPath(path)))
                    result.Add(path);
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter AgrPickerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Cli/AgrPicker.cs tests/Registrar.Tests/Cli/AgrPickerTests.cs
git commit -m "feat: AGR candidate picker (recent + folder, deduped)"
```

---

### Task 7: ConfigLocator packaged-layout test (no code change)

**Files:**
- Test: `tests/Registrar.Tests/Core/ConfigLocatorTests.cs`

**Interfaces:**
- Consumes: `Registrar.Core.ConfigLocator.FindSchema/FindConfig/FindAsset`.

**Context:** `ConfigLocator.Find` already checks the base directory itself before walking to parents, so the packaged layout (config/schemas/assets next to the exe) resolves with NO code change. This task proves it, guarding the standalone-exe release against a future regression.

- [ ] **Step 1: Write the failing (then passing) test**

Add to `tests/Registrar.Tests/Core/ConfigLocatorTests.cs`:

```csharp
[Fact]
public void Find_resolves_when_base_dir_itself_holds_config_and_schemas()
{
    var root = Directory.CreateTempSubdirectory().FullName;
    Directory.CreateDirectory(Path.Combine(root, "schemas"));
    Directory.CreateDirectory(Path.Combine(root, "config"));
    Directory.CreateDirectory(Path.Combine(root, "assets"));
    File.WriteAllText(Path.Combine(root, "schemas", "agr.schema.json"), "{}");
    File.WriteAllText(Path.Combine(root, "config", "style-rules.json"), "{}");
    File.WriteAllText(Path.Combine(root, "assets", "org-logo.png"), "x");

    // baseDir == the directory that holds config/schemas/assets (packaged layout)
    Assert.EndsWith(Path.Combine("schemas", "agr.schema.json"),
        Registrar.Core.ConfigLocator.FindSchema(root, "agr.schema.json"));
    Assert.EndsWith(Path.Combine("config", "style-rules.json"),
        Registrar.Core.ConfigLocator.FindConfig(root, "style-rules.json"));
    Assert.EndsWith(Path.Combine("assets", "org-logo.png"),
        Registrar.Core.ConfigLocator.FindAsset(root, "org-logo.png"));
}
```

- [ ] **Step 2: Run the test**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter Find_resolves_when_base_dir_itself_holds_config_and_schemas`
Expected: PASS immediately (confirms base-dir resolution; no production code change). If it FAILS, `ConfigLocator.Find` does not check the base dir first - in that case make the loop evaluate `dir` starting at `baseDir` (it already does today), and re-run.

- [ ] **Step 3: Commit**

```bash
git add tests/Registrar.Tests/Core/ConfigLocatorTests.cs
git commit -m "test: ConfigLocator resolves config from the base dir (packaged layout)"
```

---

### Task 8: Console/OS seams + MenuCommand loop + `menu` verb

**Files:**
- Create: `src/Registrar.Cli/IConsole.cs`, `src/Registrar.Cli/IOperatorShell.cs`, `src/Registrar.Cli/MenuCommand.cs`, `tests/Registrar.Tests/Cli/MenuCommandTests.cs`
- Modify: `src/Registrar.Cli/Program.cs`

**Interfaces:**
- Consumes: `ReportPresenter`, `GovernorClipboardText`, `MenuSettings`, `SettingsStore`, `AgrPicker`, `ReportSerializer.FromJson`, `ValidateCommand.Run`, `BuildCommand.Run`, `AgrParser.Parse`, `IClock`.
- Produces: `interface IConsole { void Write(string s); void WriteLine(string s); void WriteLine(string s, LineStyle style); string? ReadLine(); }` (+ `SystemConsole`); `interface IOperatorShell { void OpenFile(string path); void CopyToClipboard(string text); }` (+ `OperatorShell`); `MenuCommand.Run(IConsole console, IOperatorShell shell, IClock clock, string settingsPath) -> int`.

- [ ] **Step 1: Write the seams and real impls**

Create `src/Registrar.Cli/IConsole.cs`:

```csharp
namespace Registrar.Cli;

public interface IConsole
{
    void Write(string text);
    void WriteLine(string text);
    void WriteLine(string text, LineStyle style);
    string? ReadLine();
}

public sealed class SystemConsole : IConsole
{
    public void Write(string text) => Console.Write(text);
    public void WriteLine(string text) => Console.WriteLine(text);

    public void WriteLine(string text, LineStyle style)
    {
        var color = style switch
        {
            LineStyle.Pass => ConsoleColor.Green,
            LineStyle.Warn => ConsoleColor.Yellow,
            LineStyle.Fail => ConsoleColor.Red,
            _ => (ConsoleColor?)null
        };
        if (color is null) { Console.WriteLine(text); return; }
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color.Value;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }

    public string? ReadLine() => Console.ReadLine();
}
```

Create `src/Registrar.Cli/IOperatorShell.cs`:

```csharp
using System.Diagnostics;

namespace Registrar.Cli;

public interface IOperatorShell
{
    void OpenFile(string path);
    void CopyToClipboard(string text);
}

public sealed class OperatorShell : IOperatorShell
{
    public void OpenFile(string path)
        => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    public void CopyToClipboard(string text)
    {
        // Pipe to clip.exe: robust from a console app, no WinForms/STA dependency.
        var psi = new ProcessStartInfo("clip.exe") { RedirectStandardInput = true, UseShellExecute = false };
        using var proc = Process.Start(psi)!;
        proc.StandardInput.Write(text);
        proc.StandardInput.Close();
        proc.WaitForExit();
    }
}
```

- [ ] **Step 2: Write the failing integration tests (with fakes)**

Create `tests/Registrar.Tests/Cli/MenuCommandTests.cs`:

```csharp
using Registrar.Cli;
using Registrar.Core;

namespace Registrar.Tests.Cli;

public class MenuCommandTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeConsole : IConsole
    {
        private readonly Queue<string> _input;
        public readonly List<(string Text, LineStyle Style)> Output = new();
        public FakeConsole(params string[] input) => _input = new Queue<string>(input);
        public void Write(string text) => Output.Add((text, LineStyle.Normal));
        public void WriteLine(string text) => Output.Add((text, LineStyle.Normal));
        public void WriteLine(string text, LineStyle style) => Output.Add((text, style));
        public string? ReadLine() => _input.Count > 0 ? _input.Dequeue() : "0";
        public string AllText => string.Join("\n", Output.Select(o => o.Text));
    }

    private sealed class FakeShell : IOperatorShell
    {
        public readonly List<string> Opened = new();
        public string? LastClipboard;
        public void OpenFile(string path) => Opened.Add(path);
        public void CopyToClipboard(string text) => LastClipboard = text;
    }

    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    private static string WorkingFolderWith(string fixtureName)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.Copy(Path.Combine(RepoRoot(), "fixtures", fixtureName), Path.Combine(dir, fixtureName));
        return dir;
    }

    private static string Settings() =>
        Path.Combine(Directory.CreateTempSubdirectory().FullName, "settings.json");

    [Fact]
    public void Validate_clean_agr_prints_PASS_then_quits()
    {
        var folder = WorkingFolderWith("agr-sample-ds-draft.json");
        var console = new FakeConsole(
            "3", folder,   // 3) change working folder -> folder
            "1", "1",      // 1) validate -> pick candidate #1
            "",            // mode: accept default
            "",            // registry: none
            "",            // post-run: back (empty)
            "0");          // quit
        var code = MenuCommand.Run(console, new FakeShell(), new FixedClock(), Settings());
        Assert.Equal(0, code);
        Assert.Contains(console.Output, o => o.Style == LineStyle.Pass && o.Text.Contains("PASS"));
    }

    [Fact]
    public void Validate_failing_agr_offers_copy_failures_to_clipboard()
    {
        var folder = WorkingFolderWith("agr-sample-ds-prohibited-char.json");
        var shell = new FakeShell();
        var console = new FakeConsole(
            "3", folder,
            "1", "1",
            "",            // mode default
            "",            // no registry
            "c",           // post-run: copy failures
            "",            // post-run: back
            "0");          // quit
        var code = MenuCommand.Run(console, shell, new FixedClock(), Settings());
        Assert.Equal(0, code);
        Assert.Contains(console.Output, o => o.Style == LineStyle.Fail && o.Text.Contains("FAIL"));
        Assert.NotNull(shell.LastClipboard);
        Assert.Contains("QA-ENG-05", shell.LastClipboard!);
    }

    [Fact]
    public void Bad_menu_input_reprompts_then_quits()
    {
        var console = new FakeConsole("99", "x", "0");
        var code = MenuCommand.Run(console, new FakeShell(), new FixedClock(), Settings());
        Assert.Equal(0, code);
        Assert.Contains(console.Output, o => o.Text.Contains("not a valid choice", StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter MenuCommandTests`
Expected: FAIL (`MenuCommand` not defined).

- [ ] **Step 4: Implement MenuCommand**

Create `src/Registrar.Cli/MenuCommand.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Cli;

public static class MenuCommand
{
    public static int Run(IConsole console, IOperatorShell shell, IClock clock, string settingsPath)
    {
        var store = new SettingsStore(settingsPath);
        var settings = store.Load();

        while (true)
        {
            console.WriteLine("");
            console.WriteLine($"registrar  |  working folder: {settings.LastFolder ?? "(not set)"}");
            console.WriteLine("  1) Validate an AGR");
            console.WriteLine("  2) Build an AGR  (validate, then render on pass)");
            console.WriteLine("  3) Change working folder");
            console.WriteLine("  0) Quit");
            console.Write("> ");

            switch ((console.ReadLine() ?? "0").Trim())
            {
                case "1": settings = RunOnAgr(console, shell, clock, store, settings, build: false); break;
                case "2": settings = RunOnAgr(console, shell, clock, store, settings, build: true); break;
                case "3": settings = ChangeFolder(console, store, settings); break;
                case "0": case "": return 0;
                default: console.WriteLine("That is not a valid choice.", LineStyle.Fail); break;
            }
        }
    }

    private static MenuSettings ChangeFolder(IConsole console, SettingsStore store, MenuSettings settings)
    {
        console.Write("Working folder path: ");
        var folder = (console.ReadLine() ?? "").Trim();
        if (folder.Length == 0) return settings;
        if (!Directory.Exists(folder)) { console.WriteLine("No such folder.", LineStyle.Fail); return settings; }
        settings = settings.WithFolder(folder);
        store.Save(settings);
        return settings;
    }

    private static MenuSettings RunOnAgr(IConsole console, IOperatorShell shell, IClock clock,
        SettingsStore store, MenuSettings settings, bool build)
    {
        var folder = settings.LastFolder ?? "";
        var candidates = AgrPicker.Candidates(folder, settings.RecentAgrs);
        if (candidates.Count == 0)
        {
            console.WriteLine("No .json AGRs found. Set a working folder (option 3) first.", LineStyle.Warn);
            return settings;
        }
        for (var i = 0; i < candidates.Count; i++)
            console.WriteLine($"  {i + 1}) {Path.GetFileName(candidates[i])}");
        console.Write("Pick an AGR (number, or Enter to cancel): ");
        var pick = (console.ReadLine() ?? "").Trim();
        if (!int.TryParse(pick, out var idx) || idx < 1 || idx > candidates.Count) return settings;
        var agrPath = candidates[idx - 1];

        var defaultMode = TryReadMode(agrPath) ?? "draft";
        console.Write($"Mode [draft/promotion] (Enter for {defaultMode}): ");
        var mode = (console.ReadLine() ?? "").Trim();
        if (mode.Length == 0) mode = defaultMode;

        console.Write("Registry CSV path (Enter for none): ");
        var registry = (console.ReadLine() ?? "").Trim();
        var registryPath = registry.Length == 0 ? null : registry;

        if (build)
            BuildCommand.Run(agrPath, clock, TextWriter.Null, TextWriter.Null, mode, registryPath);
        else
            ValidateCommand.Run(agrPath, clock, TextWriter.Null, TextWriter.Null, registryPath);

        settings = settings.WithRecent(agrPath);
        store.Save(settings);

        var report = ReadReport(Path.GetDirectoryName(agrPath)!);
        if (report is null)
        {
            console.WriteLine("No report was written (unexpected).", LineStyle.Fail);
            return settings;
        }
        foreach (var line in ReportPresenter.Present(report)) console.WriteLine(line.Text, line.Style);

        PostRun(console, shell, agrPath, report, build);
        return settings;
    }

    private static void PostRun(IConsole console, IOperatorShell shell, string agrPath,
        ValidationReport report, bool build)
    {
        var hasFailures = report.Checks.Any(c => c.Result == CheckResultValue.Fail);
        while (true)
        {
            console.WriteLine("");
            if (build) console.WriteLine("  d) Open the document");
            console.WriteLine("  r) Open the validation report");
            if (hasFailures) console.WriteLine("  c) Copy failures for the Governor");
            console.WriteLine("  Enter) Back to menu");
            console.Write("> ");
            switch ((console.ReadLine() ?? "").Trim().ToLowerInvariant())
            {
                case "d" when build:
                    var docx = FindDocx(agrPath);
                    if (docx is not null) shell.OpenFile(docx);
                    else console.WriteLine("No document was produced.", LineStyle.Warn);
                    break;
                case "r":
                    shell.OpenFile(Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json"));
                    break;
                case "c" when hasFailures:
                    shell.CopyToClipboard(GovernorClipboardText.Build(report));
                    console.WriteLine("Copied failures to the clipboard.", LineStyle.Normal);
                    break;
                case "":
                    return;
                default:
                    console.WriteLine("That is not a valid choice.", LineStyle.Fail);
                    break;
            }
        }
    }

    private static string? TryReadMode(string agrPath)
    {
        try { return AgrParser.Parse(File.ReadAllText(agrPath)).ValidationPolicy.Mode; }
        catch { return null; }
    }

    private static string? FindDocx(string agrPath)
    {
        try
        {
            var name = AgrParser.Parse(File.ReadAllText(agrPath)).RenderingOptions.OutputFileName;
            var path = Path.Combine(Path.GetDirectoryName(agrPath)!, name);
            return File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    private static ValidationReport? ReadReport(string dir)
    {
        var path = Path.Combine(dir, "validation-report.json");
        if (!File.Exists(path)) return null;
        try { return ReportSerializer.FromJson(File.ReadAllText(path)); }
        catch { return null; }
    }
}
```

- [ ] **Step 5: Wire the `menu` verb into Program.cs**

In `src/Registrar.Cli/Program.cs`, before the `root` declaration add:

```csharp
var menu = new Command("menu", "Interactive operator menu (validate/build with quality-of-life features).");
menu.SetAction(_ => MenuCommand.Run(
    new SystemConsole(), new OperatorShell(), new SystemClock(), SettingsStore.DefaultPath()));
```

and add `menu` to the `RootCommand` initializer:

```csharp
var root = new RootCommand("registrar - governed artifact compiler") { validate, build, menu };
```

- [ ] **Step 6: Run to verify tests pass, then the full suite**

Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter MenuCommandTests`
Expected: PASS.
Run: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test`
Expected: PASS (whole suite).

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Cli/IConsole.cs src/Registrar.Cli/IOperatorShell.cs src/Registrar.Cli/MenuCommand.cs src/Registrar.Cli/Program.cs tests/Registrar.Tests/Cli/MenuCommandTests.cs
git commit -m "feat: interactive registrar menu with QoL features over validate/build"
```

---

### Task 9: Self-contained publish, launcher, and run guide

**Files:**
- Modify: `src/Registrar.Cli/Registrar.Cli.csproj` (AssemblyName), `README.md`
- Create: `scripts/publish.ps1`, `Registrar.cmd`

**Interfaces:** none (packaging + docs). Verification is manual (run the script, inspect the zip, launch the exe).

- [ ] **Step 1: Name the executable `registrar`**

In `src/Registrar.Cli/Registrar.Cli.csproj`, inside the existing `<PropertyGroup>`, add:

```xml
    <AssemblyName>registrar</AssemblyName>
```

- [ ] **Step 2: Create the packaging script**

Create `scripts/publish.ps1`:

```powershell
#Requires -Version 7.0
<#
.SYNOPSIS
  Publish a self-contained, single-file registrar.exe and assemble a release zip.
.DESCRIPTION
  Produces dist/registrar/ containing registrar.exe plus editable config/, schemas/,
  assets/, the Registrar.cmd launcher, and README.md, then zips it to dist/registrar.zip.
  No .NET runtime is required on the target machine. No trimming (protects
  System.Text.Json / OpenXml reflection).
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$stage = Join-Path $dist 'registrar'

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

dotnet publish (Join-Path $repo 'src/Registrar.Cli/Registrar.Cli.csproj') `
  -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:PublishTrimmed=false `
  -o $stage

# Ship config as editable data next to the exe (rule 3).
foreach ($dir in 'config','schemas','assets') {
  Copy-Item (Join-Path $repo $dir) (Join-Path $stage $dir) -Recurse -Force
}
Copy-Item (Join-Path $repo 'Registrar.cmd') $stage -Force
Copy-Item (Join-Path $repo 'README.md') $stage -Force

# Keep only the launcher-relevant published files tidy: remove the .pdb if present.
Get-ChildItem $stage -Filter '*.pdb' | Remove-Item -Force -ErrorAction SilentlyContinue

$zip = Join-Path $dist 'registrar.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Published to $stage and zipped to $zip"
```

- [ ] **Step 3: Create the launcher**

Create `Registrar.cmd`:

```bat
@echo off
cd /d "%~dp0"
registrar.exe menu
```

- [ ] **Step 4: Add the run guide to README**

Add a `## Running registrar` section to `README.md` (US English, no emoji):

```markdown
## Running registrar

For everyday use, run the interactive menu:

- Double-click `Registrar.cmd`, or run `registrar menu` from a terminal.

Pick an AGR from your working folder, choose validate or build, and read the
result. Failing checks can be copied to the clipboard formatted for the Governor
chat; a produced document can be opened directly.

The packaged release is self-contained: it needs no .NET installation. Keep the
`config/`, `schemas/`, and `assets/` folders next to `registrar.exe` (they are the
versioned template pack, editable without a rebuild).

For scripting or CI, the direct verbs remain:

    registrar validate <agr.json> [--registry <registry.csv>]
    registrar build <agr.json> --mode draft|promotion [--registry <registry.csv>]

To build a release locally: `pwsh scripts/publish.ps1` (produces `dist/registrar.zip`).
```

- [ ] **Step 5: Verify the packaged build by hand**

Run: `pwsh scripts/publish.ps1`
Expected: `dist/registrar/registrar.exe` exists alongside `config/`, `schemas/`, `assets/`, `Registrar.cmd`, `README.md`; `dist/registrar.zip` is produced.
Then, from `dist/registrar`, copy a fixture in and drive one run:

```
dist\registrar\registrar.exe validate ..\..\fixtures\agr-sample-ds-draft.json
```

Expected: exits 0 and writes a `validation-report.json` (proves config/schema/asset resolution from the exe's own folder). Optionally double-click `Registrar.cmd` and validate an AGR through the menu to confirm the human-readable report renders.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Cli/Registrar.Cli.csproj scripts/publish.ps1 Registrar.cmd README.md
git commit -m "feat: self-contained registrar.exe publish, launcher, and run guide"
```

Note: add `dist/` to `.gitignore` if it is not already ignored (build output, not source).

---

## Self-Review

**Spec coverage:**
- Interactive `menu` mode, numbered loop: Task 8.
- Thin shell over existing verbs (reuse `ValidateCommand`/`BuildCommand`, re-read the written report): Task 1 (`FromJson`) + Task 8.
- Human-readable report: Task 2, rendered in Task 8.
- Copy failures for the Governor: Task 3, wired in Task 8.
- Auto-open outputs: `IOperatorShell.OpenFile` (Task 8) via the post-run menu.
- Remember folder + recent AGRs: Tasks 4-5 (settings) + Task 6 (picker), used in Task 8.
- Console UX, hand-rolled, no new dependency, ASCII + color: `IConsole`/`SystemConsole` (Task 8).
- Settings store paths only, `%APPDATA%\registrar`, defaults on missing/corrupt: Task 5.
- Self-contained single-file publish, no trimming, `registrar.exe`, config as editable files, `.cmd` launcher, PS7 packaging script, run guide: Task 9.
- ConfigLocator resolves from the exe folder (packaged layout): Task 7 (confirmed, no code change).

**Placeholder scan:** No TBD/TODO. Every code step shows complete code. The `try/catch` blocks are intentional narrow defenses (corrupt settings, unparseable AGR for display niceties), not "add error handling" placeholders.

**Type consistency:** `MenuSettings.WithRecent`/`WithFolder` (Task 4) used by Task 5/8. `SettingsStore(path)`, `Load`/`Save`, `DefaultPath()` consistent Tasks 5/8. `AgrPicker.Candidates(folder, recent)` consistent Tasks 6/8. `ReportPresenter.Present -> IReadOnlyList<PresenterLine>` with `LineStyle` consistent Tasks 2/8. `GovernorClipboardText.Build` consistent Tasks 3/8. `IConsole`/`IOperatorShell` method names match between the real impls, the fakes, and `MenuCommand`. `ReportSerializer.FromJson` consistent Tasks 1/8. `MenuCommand.Run(IConsole, IOperatorShell, IClock, string)` consistent between Task 8 and the Program.cs wiring.
