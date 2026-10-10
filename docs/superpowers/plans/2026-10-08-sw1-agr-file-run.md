# SW-1: Extract the file-based run into Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move everything the simple operator window needs out of `Registrar.Cli` and into `Registrar.Pipeline`, so `Registrar.App` can use it, while the CLI keeps behaving exactly as it does today.

**Architecture:** `BuildCommand` and `ValidateCommand` currently do file IO and write to `TextWriter`s, so nothing can call them for a structured result. A new `AgrFileRun` in Pipeline holds that logic and returns an `AgrFileRunResult` carrying the exit code, the ordered stdout and stderr messages, the report, and the files written. The two CLI commands become wrappers that replay those messages in order, so the CLI is byte-identical by construction. The small pure helper types (`ReportPresenter`, `GovernorClipboardText`, `AgrPicker`, `SettingsStore`, `MenuSettings`, `Doctor`) move to Pipeline with a namespace change only.

**Tech Stack:** .NET 8, C#, xUnit. Spec: `docs/superpowers/specs/2026-10-08-simple-operator-window-design.md`.

## Global Constraints

- The CLI must remain intact as a fallback. `BuildCommandTests`, `ValidateCommandTests`, `MenuCommandTests`, `DoctorTests`, `DoctorCommandTests` and `Cli/BuildOutputFileSetTests` take ZERO edits in this unit. If one needs editing, the extraction changed behavior and is wrong.
- Layering: `Registrar.Cli` must never reference `Registrar.App`, and `Registrar.App` must never reference `Registrar.Cli`. Pipeline references Core, Validation and Rendering only.
- Rule 8: US English, no emojis, no em or en dashes in code, comments, config or docs.
- Rule 2: zero model calls anywhere.
- Build and test with `--configuration Release` before calling any branch green (CI builds Release). Zero warnings.
- Do not terminate processes this session did not start.
- Each unit ships as a PR to `main`, never a direct push.

## File Structure

| File | Responsibility |
|---|---|
| `src/Registrar.Pipeline/AgrFileRun.cs` (create) | `Validate` and `Build` over an AGR path. Port of `CommandSupport.Validate`, `ValidateCommand.Run` and `BuildCommand.Run`, recording messages instead of writing them. |
| `src/Registrar.Pipeline/AgrFileRunResult.cs` (create) | `RunStream`, `RunMessage`, `AgrFileRunResult` (with `Replay` and `ErrorText`). |
| `src/Registrar.Pipeline/ReportPresenter.cs`, `GovernorClipboardText.cs`, `AgrPicker.cs`, `SettingsStore.cs`, `MenuSettings.cs`, `Doctor.cs` (moved) | Same code as in Cli, namespace `Registrar.Pipeline`. |
| `src/Registrar.Cli/ValidateCommand.cs`, `BuildCommand.cs` (modify) | Thin wrappers over `AgrFileRun`. |
| `src/Registrar.Cli/CommandSupport.cs` (delete) | Fully absorbed into `AgrFileRun`. |
| `src/Registrar.Cli/GlobalUsings.cs` (create) | `global using Registrar.Pipeline;` so Cli sources compile unchanged after the move. |
| `tests/Registrar.Tests/Registrar.Tests.csproj` (modify) | `<Using Include="Registrar.Pipeline" />`, same reason, so test files compile unedited. |
| `tests/Registrar.Tests/Cli/CliOutputStreamTests.cs` (create) | Characterization of exact stdout, stderr and exit code, written against the UNMODIFIED CLI first. |
| `tests/Registrar.Tests/Pipeline/AgrFileRunTests.cs` (create) | Behavior of the new structured result. |

`MenuCommand` is not modified. The spec listed the report and DOCX lookup helpers in `MenuCommand` as moving; `AgrFileRunResult` carries the report and document name directly, so the window does not need them and they stay.

One deliberate difference: today the CLI streams its output as it goes. After this unit it buffers messages and replays them at the end. The text, order and exit code are identical. The only observable difference is when the process dies from an exception outside the handled list, where output printed before the crash is no longer printed. That path is a bug either way and is not a supported behavior.

---

### Task 1: Characterize the CLI output before touching it

**Files:**
- Create: `tests/Registrar.Tests/Cli/CliOutputStreamTests.cs`

**Interfaces:**
- Consumes: `BuildCommand.Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? mode = null, string? registryPath = null)` and `ValidateCommand.Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? registryPath = null)`, both returning `int`.
- Produces: a safety net. Nothing later depends on its types.

`BuildOutputFileSetTests` pins which files land. Nothing pins the exact text and ordering of stdout and stderr, which is what the wrappers must reproduce. This task pins it.

- [ ] **Step 0: Branch**

```bash
cd /c/GitRepos/The-Registrar
git checkout docs/simple-operator-window-scope
git checkout -b feat/sw1-agr-file-run
```

The branch starts from the spec branch so the spec and this plan travel with the PR.

- [ ] **Step 1: Record the baseline**

Run: `dotnet test Registrar.sln --configuration Release`
Expected: all pass. Write down the Registrar.Tests and Registrar.App.Tests counts. They are the baseline for every later comparison.

- [ ] **Step 2: Write the characterization tests**

```csharp
using Registrar.Cli;
using Registrar.Core;

namespace Registrar.Tests.Cli;

// The exact text, order and exit code the CLI produces, pinned before the logic moved into
// Registrar.Pipeline.AgrFileRun. BuildOutputFileSetTests pins which files land; this pins
// what the operator reads. Written against the unmodified CLI and never edited afterward.
// If one fails, the extraction changed what the CLI says, which the owner has ruled must
// not happen.
public class CliOutputStreamTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly string NL = Environment.NewLine;

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static string Copy(string fixtureName)
    {
        var dir = TempDir.Create();
        var dest = Path.Combine(dir, fixtureName);
        File.Copy(Path.Combine(RepoRoot(), "fixtures", fixtureName), dest);
        return dest;
    }

    private static (int Exit, string Out, string Err) Validate(string agrPath, string? registry = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var exit = ValidateCommand.Run(agrPath, new FixedClock(), o, e, registry);
        return (exit, o.ToString(), e.ToString());
    }

    private static (int Exit, string Out, string Err) Build(string agrPath, string? mode = null, string? registry = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var exit = BuildCommand.Run(agrPath, new FixedClock(), o, e, mode, registry);
        return (exit, o.ToString(), e.ToString());
    }

    [Fact]
    public void Validate_pass_prints_one_summary_line()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var (exit, o, e) = Validate(path);

        var report = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "validation-report.json");
        Assert.Equal(0, exit);
        Assert.Equal($"Pass: ORG-GOV-DS-016 (0 failing, 0 warning check(s)). Report: {report}{NL}", o);
        Assert.Equal("", e);
    }

    [Fact]
    public void Validate_fail_prints_a_fail_summary_and_exits_1()
    {
        var (exit, o, e) = Validate(Copy("agr-sample-ds-invalid-schema.json"));

        Assert.Equal(1, exit);
        Assert.StartsWith("Fail: ", o);
        Assert.Contains(" failing, ", o);
        Assert.Equal("", e);
    }

    [Fact]
    public void Validate_missing_file_prints_one_error_and_exits_2()
    {
        var path = Path.Combine(TempDir.Create(), "nope.json");
        var (exit, o, e) = Validate(path);

        Assert.Equal(2, exit);
        Assert.Equal("", o);
        Assert.Equal($"error: AGR file not found: {path}{NL}", e);
    }

    [Fact]
    public void Validate_missing_registry_prints_one_error_and_exits_2()
    {
        var (exit, o, e) = Validate(Copy("agr-sample-ds-draft.json"), "does-not-exist.csv");

        Assert.Equal(2, exit);
        Assert.Equal("", o);
        Assert.Equal($"error: registry file not found: does-not-exist.csv{NL}", e);
    }

    [Fact]
    public void Build_pass_prints_one_document_line()
    {
        var (exit, o, e) = Build(Copy("agr-sample-ds-draft.json"));

        Assert.Equal(0, exit);
        Assert.Equal($"Pass: ORG-GOV-DS-016 - ORG - Deployment Summary - Talewind CPA - v1.0.docx{NL}", o);
        Assert.Equal("", e);
    }

    [Fact]
    public void Build_pre_render_failure_prints_the_no_document_line_and_exits_1()
    {
        var (exit, o, e) = Build(Copy("agr-sample-ds-invalid-schema.json"));

        Assert.Equal(1, exit);
        Assert.StartsWith("fail: ", o);
        Assert.EndsWith($" - no document produced (validation failed).{NL}", o);
        Assert.Equal("", e);
    }

    [Fact]
    public void Build_mode_disagreement_prints_one_error_exits_2_and_writes_no_report()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var (exit, o, e) = Build(path, mode: "promotion");

        Assert.Equal(2, exit);
        Assert.Equal("", o);
        Assert.Equal(
            $"error: --mode 'promotion' does not match the AGR validationPolicy.mode 'draft'.{NL}", e);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "validation-report.json")));
    }
}
```

- [ ] **Step 3: Run against the unmodified CLI**

Run: `dotnet test tests/Registrar.Tests --configuration Release --filter "FullyQualifiedName~CliOutputStreamTests"`
Expected: PASS, 7 tests. If one fails, the failure is in the TEST's expected string (an enum name, a document ID, a fixture detail), because the code under test is untouched. Correct the expected string to match the actual output and rerun. Never change `src/` in this task.

- [ ] **Step 4: Commit**

```bash
git add tests/Registrar.Tests/Cli/CliOutputStreamTests.cs
git commit -m "test: pin the exact CLI stdout, stderr and exit codes before extraction

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Move the helper types into Pipeline

**Files:**
- Move (git mv) from `src/Registrar.Cli/` to `src/Registrar.Pipeline/`: `ReportPresenter.cs`, `GovernorClipboardText.cs`, `AgrPicker.cs`, `SettingsStore.cs`, `MenuSettings.cs`, `Doctor.cs`
- Create: `src/Registrar.Cli/GlobalUsings.cs`
- Modify: `tests/Registrar.Tests/Registrar.Tests.csproj`

**Interfaces:**
- Consumes: nothing new.
- Produces, all `public` in namespace `Registrar.Pipeline`: `LineStyle`, `PresenterLine`, `ReportPresenter.Present(ValidationReport) : IReadOnlyList<PresenterLine>`, `GovernorClipboardText.Build(ValidationReport) : string`, `AgrPicker.Candidates(string workingFolder, IReadOnlyList<string> recent) : IReadOnlyList<string>`, `SettingsStore`, `MenuSettings`, `DoctorStatus`, `DoctorCheck`, `DoctorReport`, `Doctor`.

`LineStyle` and `PresenterLine` live inside `ReportPresenter.cs`, and `IConsole` in Cli uses `LineStyle`. The global using keeps Cli compiling without editing `IConsole.cs` or `MenuCommand.cs`.

- [ ] **Step 1: Move the files and change the namespace**

```bash
cd /c/GitRepos/The-Registrar
for f in ReportPresenter GovernorClipboardText AgrPicker SettingsStore MenuSettings Doctor; do
  git mv src/Registrar.Cli/$f.cs src/Registrar.Pipeline/$f.cs
  sed -i 's/^namespace Registrar\.Cli;/namespace Registrar.Pipeline;/' src/Registrar.Pipeline/$f.cs
done
grep -n "^namespace" src/Registrar.Pipeline/{ReportPresenter,GovernorClipboardText,AgrPicker,SettingsStore,MenuSettings,Doctor}.cs
```
Expected: six lines, all `namespace Registrar.Pipeline;`.

- [ ] **Step 2: Add the global usings**

Create `src/Registrar.Cli/GlobalUsings.cs`:

```csharp
// The helper types the console menu uses moved to Registrar.Pipeline so Registrar.App can
// use them too. A global using keeps every Cli source file compiling exactly as written.
global using Registrar.Pipeline;
```

In `tests/Registrar.Tests/Registrar.Tests.csproj`, directly under `<Using Include="Registrar.TestSupport" />` add:

```xml
    <Using Include="Registrar.Pipeline" />
```

- [ ] **Step 3: Build and run the whole suite**

Run: `dotnet build Registrar.sln --configuration Release`
Expected: 0 errors, 0 warnings.

Run: `dotnet test Registrar.sln --configuration Release`
Expected: same counts as the Task 1 baseline plus the 7 new characterization tests, all passing. `git status` must show NO modified file under `tests/` other than the csproj.

If the build reports an ambiguous reference or a missing type, the fix is in `GlobalUsings.cs` or the csproj `Using`, never an edit to an existing test file. If `Doctor.cs` fails to compile in Pipeline for a missing reference, stop and report: it means Doctor has a dependency this plan did not find, and splitting it is a design decision.

- [ ] **Step 4: Commit**

```bash
git add -A src tests/Registrar.Tests/Registrar.Tests.csproj
git commit -m "refactor: move the console helper types into Pipeline

ReportPresenter, GovernorClipboardText, AgrPicker, SettingsStore, MenuSettings
and Doctor move with a namespace change only, so the window can use them.

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: AgrFileRun, the structured file-based run

**Files:**
- Create: `src/Registrar.Pipeline/AgrFileRunResult.cs`
- Create: `src/Registrar.Pipeline/AgrFileRun.cs`
- Create: `tests/Registrar.Tests/Pipeline/AgrFileRunTests.cs`

**Interfaces:**
- Consumes: `OutputPackage.WriteValidation(ValidationOutcome, string) : OutputPackageResult`, `OutputPackage.WriteArtifacts(BuildResult, string, string) : OutputPackageResult`, `BuildPipeline.Run(ValidationOutcome, AgrDocument, EngineContext, byte[]) : BuildResult`, `ValidationService(IClock).Validate(string agrText, EngineContext, string? registryCsvText) : ValidationOutcome`, `EngineContext.Load(string repoRoot)`, `ConfigLocator.FindSchema(string, string)`, `ConfigLocator.FindAsset(string, string)`, `AgrParser.Parse(string)`.
- Produces:
  - `enum RunStream { Out, Err }`
  - `record RunMessage(RunStream Stream, string Text)`
  - `record AgrFileRunResult(int ExitCode, IReadOnlyList<RunMessage> Messages, ValidationReport? Report, string OutputDirectory, IReadOnlyList<string> Written, IReadOnlyList<string> Withheld, string? DocumentFileName)` with `string ErrorText { get; }` and `void Replay(TextWriter stdout, TextWriter stderr)`
  - `static AgrFileRunResult AgrFileRun.Validate(string agrPath, IClock clock, string? registryPath = null)`
  - `static AgrFileRunResult AgrFileRun.Build(string agrPath, IClock clock, string? mode = null, string? registryPath = null)`

Field meanings, which the window and the wrappers rely on:
- `ExitCode`: 0 pass, 1 validation fail, 2 usage or IO error, exactly as the CLI.
- `Report`: the report written to disk this run. Null when the run ended before any report was written (missing file, unreadable registry, mode disagreement). Callers must never substitute an older report from disk.
- `OutputDirectory`: the AGR's own directory, empty when the run could not locate one.
- `DocumentFileName`: the DOCX file name, set only when that file landed.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Pipeline/AgrFileRunTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests.Pipeline;

public class AgrFileRunTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static string Copy(string fixtureName)
    {
        var dir = TempDir.Create();
        var dest = Path.Combine(dir, fixtureName);
        File.Copy(Path.Combine(RepoRoot(), "fixtures", fixtureName), dest);
        return dest;
    }

    [Fact]
    public void Validate_pass_returns_the_written_report_and_no_document()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var r = AgrFileRun.Validate(path, new FixedClock());

        Assert.Equal(0, r.ExitCode);
        Assert.NotNull(r.Report);
        Assert.Equal(OverallResult.Pass, r.Report!.OverallResult);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(path)), r.OutputDirectory);
        Assert.Contains("validation-report.json", r.Written);
        Assert.Null(r.DocumentFileName);
        Assert.Equal("", r.ErrorText);
    }

    [Fact]
    public void Validate_missing_file_reports_the_error_and_no_report()
    {
        var path = Path.Combine(TempDir.Create(), "nope.json");
        var r = AgrFileRun.Validate(path, new FixedClock());

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.Equal($"error: AGR file not found: {path}", r.ErrorText);
        Assert.Empty(r.Written);
    }

    [Fact]
    public void Build_pass_names_the_document_and_lists_every_file_written()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var r = AgrFileRun.Build(path, new FixedClock());

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("ORG - Deployment Summary - Talewind CPA - v1.0.docx", r.DocumentFileName);
        Assert.Equal(
            new[]
            {
                "ORG - Deployment Summary - Talewind CPA - v1.0.docx",
                "artifact-manifest.json",
                "generation-log.json",
                "validation-report.json"
            },
            r.Written.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.True(File.Exists(Path.Combine(r.OutputDirectory, r.DocumentFileName!)));
    }

    [Fact]
    public void Build_pre_render_failure_yields_exit_1_a_failing_report_and_no_document()
    {
        var r = AgrFileRun.Build(Copy("agr-sample-ds-invalid-schema.json"), new FixedClock());

        Assert.Equal(1, r.ExitCode);
        Assert.Equal(OverallResult.Fail, r.Report!.OverallResult);
        Assert.Null(r.DocumentFileName);
        Assert.Equal(new[] { "validation-report.json" }, r.Written.ToArray());
    }

    [Fact]
    public void Build_mode_disagreement_is_a_usage_error_with_no_report()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var r = AgrFileRun.Build(path, new FixedClock(), mode: "promotion");

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.Contains("does not match the AGR validationPolicy.mode 'draft'", r.ErrorText);
        Assert.Empty(r.Written);
        Assert.False(File.Exists(Path.Combine(r.OutputDirectory, "validation-report.json")));
    }

    [Fact]
    public void Replay_writes_stdout_and_stderr_messages_in_recorded_order()
    {
        var r = new AgrFileRunResult(
            0,
            new[]
            {
                new RunMessage(RunStream.Out, "one"),
                new RunMessage(RunStream.Err, "two"),
                new RunMessage(RunStream.Out, "three")
            },
            null, "", Array.Empty<string>(), Array.Empty<string>(), null);

        var combined = new StringWriter();
        r.Replay(combined, combined);

        Assert.Equal($"one{Environment.NewLine}two{Environment.NewLine}three{Environment.NewLine}", combined.ToString());
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Registrar.Tests --configuration Release --filter "FullyQualifiedName~AgrFileRunTests"`
Expected: build FAIL, `AgrFileRun`, `AgrFileRunResult`, `RunMessage` and `RunStream` do not exist.

- [ ] **Step 3: Write the result types**

Create `src/Registrar.Pipeline/AgrFileRunResult.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Pipeline;

public enum RunStream { Out, Err }

// One line the CLI would have printed, in the order it would have printed it. Kept as an
// ordered list rather than two strings because the CLI interleaves the streams on one path
// (a manifest-write failure prints a stdout line and then a stderr line), and a wrapper
// that replays them in order reproduces that exactly.
public sealed record RunMessage(RunStream Stream, string Text);

// What one file-based validate or build left behind, for any caller: the console wrappers
// replay Messages and return ExitCode, the operator window reads the structured fields.
//
// Report is the report written to disk THIS run. It is null when the run ended before any
// report was written (missing AGR, unreadable registry, mode disagreement), and a caller
// must never fall back to an older validation-report.json sitting in the same folder.
public sealed record AgrFileRunResult(
    int ExitCode,
    IReadOnlyList<RunMessage> Messages,
    ValidationReport? Report,
    string OutputDirectory,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Withheld,
    string? DocumentFileName)
{
    // Everything the run sent to stderr, joined and trimmed. This is the text an operator
    // reads when ExitCode is 2.
    public string ErrorText => string.Join(
        Environment.NewLine,
        Messages.Where(m => m.Stream == RunStream.Err).Select(m => m.Text)).Trim();

    public void Replay(TextWriter stdout, TextWriter stderr)
    {
        foreach (var m in Messages)
            (m.Stream == RunStream.Out ? stdout : stderr).WriteLine(m.Text);
    }
}
```

- [ ] **Step 4: Write AgrFileRun**

Create `src/Registrar.Pipeline/AgrFileRun.cs`. This is a faithful port of `CommandSupport.Validate`, `ValidateCommand.Run` and `BuildCommand.Run`: every message string, every branch and every ordering is kept, and `stdout.WriteLine` and `stderr.WriteLine` become `Out` and `Err` on the recorder.

```csharp
using System.Text.Json;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Pipeline;

// The file-based validate and build, lifted out of Registrar.Cli so a second caller (the
// operator window) can run them without a console. This is a port, not a redesign: every
// branch, message and ordering below is what CommandSupport, ValidateCommand and
// BuildCommand did, with the writes to stdout and stderr recorded instead of performed.
// The CLI commands are now wrappers that replay the recording, which is what makes the CLI
// provably unchanged rather than merely tested. A second copy of these rules would be a
// second engine.
public static class AgrFileRun
{
    // Exit codes: 0 pass, 1 validation fail, 2 usage/IO error.
    public static AgrFileRunResult Validate(string agrPath, IClock clock, string? registryPath = null)
    {
        var rec = new Recorder();
        var prepared = Prepare(agrPath, clock, registryPath, rec);
        if (prepared is null) return rec.Finish(2);

        rec.OutputDirectory = prepared.Dir;

        var write = WriteValidation(prepared.Outcome, prepared.Dir, rec);
        if (write is not null) return rec.Finish(write.Value);

        var report = prepared.Outcome.Report;
        rec.Report = report;

        var failCount = report.Checks.Count(c => c.Result == CheckResultValue.Fail);
        var warnCount = report.Checks.Count(c => c.Result == CheckResultValue.Warn);
        rec.Out(
            $"{report.OverallResult}: {report.DocumentId} ({failCount} failing, {warnCount} warning check(s)). Report: {Path.Combine(prepared.Dir, "validation-report.json")}");

        return rec.Finish(report.OverallResult == OverallResult.Fail ? 1 : 0);
    }

    public static AgrFileRunResult Build(string agrPath, IClock clock, string? mode = null, string? registryPath = null)
    {
        var rec = new Recorder();
        var prepared = Prepare(agrPath, clock, registryPath, rec);
        if (prepared is null) return rec.Finish(2);

        rec.OutputDirectory = prepared.Dir;
        var outcome = prepared.Outcome;
        var report = outcome.Report;

        // mode, when supplied, must agree with the AGR's declared mode. This is a usage
        // error and must be caught before any rendering is attempted, and before any report
        // is written: Report stays null so no caller mistakes an older file for this run.
        if (mode is not null && !string.Equals(mode, report.RequestedMode, StringComparison.Ordinal))
        {
            rec.Err($"error: --mode '{mode}' does not match the AGR validationPolicy.mode '{report.RequestedMode}'.");
            return rec.Finish(2);
        }

        // The report is complete for every pre-render path, including a hard schema
        // failure, so it is written before AgrParser.Parse is ever called. AgrParser's own
        // contract is "call only after schema validation has passed": a malformed AGR
        // could otherwise throw a JsonException with no report on disk. Writing first and
        // returning on Fail before parsing keeps rule 7 (a report, never a stack trace) and
        // the "validation-report.json emitted on every run" contract true unconditionally.
        var writeInitial = WriteValidation(outcome, prepared.Dir, rec);
        if (writeInitial is not null) return rec.Finish(writeInitial.Value);
        rec.Report = report;

        if (report.OverallResult == OverallResult.Fail)
        {
            rec.Out($"fail: {report.DocumentId} - no document produced (validation failed).");
            return rec.Finish(1);
        }

        // Reaching here means schema validation passed, so ValidationService already
        // parsed this AGR once successfully. Re-parsing is therefore safe; the exception
        // guard below is defense in depth, not the primary fix.
        AgrDocument agr;
        BuildResult result;
        try
        {
            agr = AgrParser.Parse(prepared.AgrText);
            var logoBytes = File.ReadAllBytes(
                ConfigLocator.FindAsset(prepared.RepoRoot, prepared.Engine.Config.Style.Wordmark.Asset));
            result = BuildPipeline.Run(outcome, agr, prepared.Engine, logoBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or JsonException)
        {
            rec.Err($"error: cannot render document: {ex.Message}");
            return rec.Finish(2);
        }

        var mergedReport = result.Report;
        rec.Report = mergedReport;

        var writeMerged = WriteValidation(new ValidationOutcome(result.Report, result.Payload), prepared.Dir, rec);
        if (writeMerged is not null) return rec.Finish(writeMerged.Value);

        // No render was attempted (for example PassWithWarnings in promotion mode, which is
        // not eligible to render): the report above is unchanged from what was already
        // written, and there is nothing further to emit.
        if (!result.ReachedRendering)
        {
            rec.Out($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - no document (promotion requires a clean pass).");
            return rec.Finish(0);
        }

        var outputFileName = agr.RenderingOptions.OutputFileName;
        var package = OutputPackage.WriteArtifacts(result, prepared.Dir, outputFileName);
        rec.Note(package);
        if (package.Written.Contains(outputFileName)) rec.DocumentFileName = outputFileName;

        if (package.Failure is not null)
        {
            // A manifest-write failure lands here after the DOCX write already succeeded.
            // WriteArtifacts performs both writes before returning, so the stdout document
            // line the CLI printed at the point of that success is reconstructed here from
            // what the package reports as written, ahead of the stderr error.
            if (package.Written.Contains(outputFileName))
                rec.Out($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - {outputFileName}");

            rec.Err($"error: {package.Failure}");
            return rec.Finish(2);
        }

        if (mergedReport.OverallResult == OverallResult.Fail)
        {
            rec.Out($"fail: {mergedReport.DocumentId} - no document produced (post-render validation failed).");
            return rec.Finish(1);
        }

        if (result.DocxEligible)
            rec.Out($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - {outputFileName}");
        else
            rec.Out($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - no document (promotion requires a clean pass).");

        return rec.Finish(0);
    }

    private sealed record Prepared(
        ValidationOutcome Outcome, string Dir, string RepoRoot, string AgrText, EngineContext Engine);

    // Port of CommandSupport.Validate. Returns null after recording the error; the caller
    // finishes the run with exit 2.
    private static Prepared? Prepare(string agrPath, IClock clock, string? registryPath, Recorder rec)
    {
        if (!File.Exists(agrPath))
        {
            rec.Err($"error: AGR file not found: {agrPath}");
            return null;
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
                rec.Err($"error: {ex.Message}");
                return null;
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
            rec.Err($"error: cannot read AGR: {ex.Message}");
            return null;
        }

        string? registryCsvText = null;
        if (registryPath is not null)
        {
            if (!File.Exists(registryPath))
            {
                rec.Err($"error: registry file not found: {registryPath}");
                return null;
            }
            try
            {
                registryCsvText = File.ReadAllText(registryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                rec.Err($"error: cannot read registry: {ex.Message}");
                return null;
            }
        }

        var engine = EngineContext.Load(repoRoot);
        var outcome = new ValidationService(clock).Validate(agrText, engine, registryCsvText);
        return new Prepared(outcome, dir, repoRoot, agrText, engine);
    }

    // Port of CommandSupport.WriteOutputs: null on success, exit 2 after recording a failure.
    private static int? WriteValidation(ValidationOutcome outcome, string dir, Recorder rec)
    {
        var package = OutputPackage.WriteValidation(outcome, dir);
        rec.Note(package);
        if (package.Failure is null) return null;

        rec.Err($"error: {package.Failure}");
        return 2;
    }

    private sealed class Recorder
    {
        private readonly List<RunMessage> _log = new();
        private readonly List<string> _written = new();
        private readonly List<string> _withheld = new();

        public ValidationReport? Report { get; set; }
        public string OutputDirectory { get; set; } = "";
        public string? DocumentFileName { get; set; }

        public void Out(string text) => _log.Add(new RunMessage(RunStream.Out, text));
        public void Err(string text) => _log.Add(new RunMessage(RunStream.Err, text));

        // The report is written twice on a build (pre-render, then merged), so a file name
        // can arrive more than once. The caller wants the set, not the history.
        public void Note(OutputPackageResult package)
        {
            foreach (var w in package.Written) if (!_written.Contains(w)) _written.Add(w);
            foreach (var w in package.Withheld) if (!_withheld.Contains(w)) _withheld.Add(w);
        }

        public AgrFileRunResult Finish(int exitCode)
            => new(exitCode, _log.ToList(), Report, OutputDirectory,
                _written.ToList(), _withheld.ToList(), DocumentFileName);
    }
}
```

- [ ] **Step 5: Run the new tests**

Run: `dotnet test tests/Registrar.Tests --configuration Release --filter "FullyQualifiedName~AgrFileRunTests"`
Expected: PASS, 6 tests.

If `Build_pass_names_the_document_and_lists_every_file_written` fails on the written list, print `r.Written` and compare to the per-file expectations in `Cli/BuildOutputFileSetTests`; the lists must agree, and a mismatch means `Note` dropped or added a file.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Pipeline tests/Registrar.Tests/Pipeline/AgrFileRunTests.cs
git commit -m "feat: add AgrFileRun, the structured file-based validate and build

A port of the CLI's CommandSupport, ValidateCommand and BuildCommand that
records stdout and stderr in order instead of writing them. Not yet used by
the CLI.

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Make the CLI commands wrappers

**Files:**
- Modify: `src/Registrar.Cli/ValidateCommand.cs`
- Modify: `src/Registrar.Cli/BuildCommand.cs`
- Delete: `src/Registrar.Cli/CommandSupport.cs`

**Interfaces:**
- Consumes: `AgrFileRun.Validate`, `AgrFileRun.Build`, `AgrFileRunResult.Replay`, `AgrFileRunResult.ExitCode`.
- Produces: unchanged public signatures `ValidateCommand.Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? registryPath = null) : int` and `BuildCommand.Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? mode = null, string? registryPath = null) : int`.

- [ ] **Step 1: Replace ValidateCommand**

Overwrite `src/Registrar.Cli/ValidateCommand.cs`:

```csharp
using Registrar.Core;

namespace Registrar.Cli;

public static class ValidateCommand
{
    // Exit codes: 0 pass, 1 validation fail, 2 usage/IO error.
    // The logic lives in Registrar.Pipeline.AgrFileRun so the operator window runs the same
    // code; this wrapper replays its recorded output so the console is unchanged.
    public static int Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? registryPath = null)
    {
        var result = AgrFileRun.Validate(agrPath, clock, registryPath);
        result.Replay(stdout, stderr);
        return result.ExitCode;
    }
}
```

- [ ] **Step 2: Replace BuildCommand**

Overwrite `src/Registrar.Cli/BuildCommand.cs`:

```csharp
using Registrar.Core;

namespace Registrar.Cli;

public static class BuildCommand
{
    // Exit codes: 0 built or cleanly not built, 1 validation fail, 2 usage/IO error.
    // The logic lives in Registrar.Pipeline.AgrFileRun so the operator window runs the same
    // code; this wrapper replays its recorded output so the console is unchanged.
    public static int Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr,
        string? mode = null, string? registryPath = null)
    {
        var result = AgrFileRun.Build(agrPath, clock, mode, registryPath);
        result.Replay(stdout, stderr);
        return result.ExitCode;
    }
}
```

- [ ] **Step 3: Delete CommandSupport**

```bash
git rm src/Registrar.Cli/CommandSupport.cs
```

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build Registrar.sln --configuration Release`
Expected: 0 errors, 0 warnings. If something other than the two commands referenced `CommandSupport`, the compiler will name it; stop and report rather than restoring the file.

Run: `dotnet test Registrar.sln --configuration Release`
Expected: every test passes, including all 7 `CliOutputStreamTests`, `Cli/BuildOutputFileSetTests`, `BuildCommandTests`, `ValidateCommandTests` and `Cli/MenuCommandTests`.

- [ ] **Step 5: Prove the guards held**

Run: `git diff --stat main -- tests/`
Expected: only `Registrar.Tests.csproj` (modified), `Cli/CliOutputStreamTests.cs` (new) and `Pipeline/AgrFileRunTests.cs` (new). If any pre-existing test file appears, the extraction changed behavior: revert that edit and fix the code instead.

- [ ] **Step 6: Exercise the real binary**

```bash
cd /c/GitRepos/The-Registrar
tmp=$(mktemp -d)
cp fixtures/agr-sample-ds-draft.json "$tmp/"
dotnet run --project src/Registrar.Cli --configuration Release -- build "$tmp/agr-sample-ds-draft.json"; echo "exit=$?"
dotnet run --project src/Registrar.Cli --configuration Release -- doctor; echo "exit=$?"
```
Expected: `Pass: ORG-GOV-DS-016 - ORG - Deployment Summary - Talewind CPA - v1.0.docx`, exit 0, then a healthy doctor with exit 0.

- [ ] **Step 7: Commit**

```bash
git add -A src
git commit -m "refactor: make ValidateCommand and BuildCommand wrappers over AgrFileRun

CommandSupport is absorbed into AgrFileRun. CLI output and exit codes are
unchanged; the pinned stream tests and every existing CLI test pass unedited.

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Close out SW-1

**Files:**
- Modify: `docs/superpowers/specs/2026-10-08-simple-operator-window-design.md`

- [ ] **Step 1: Correct the spec**

In the Architecture section the spec lists "the report and DOCX lookup helpers in `MenuCommand`" among the things that move, and says `Doctor` moves "if it has no console dependency". Replace both with what happened: the lookup helpers stay in `MenuCommand` because `AgrFileRunResult` carries the report and the document file name directly, and `Doctor` moved unchanged with no split needed.

- [ ] **Step 2: Final verification**

Run: `dotnet test Registrar.sln --configuration Release`
Expected: pass; Registrar.Tests = baseline + 13 (7 characterization + 6 AgrFileRun), Registrar.App.Tests = baseline. Zero warnings.

Run: `grep -rn "Registrar.App" src/Registrar.Cli src/Registrar.Pipeline --include=*.csproj --include=*.cs`
Expected: no output (layering intact).

- [ ] **Step 3: Commit and open the PR**

```bash
git add docs/superpowers/specs/2026-10-08-simple-operator-window-design.md
git commit -m "docs: record what SW-1 actually moved

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
git push -u origin feat/sw1-agr-file-run
gh pr create --base main --title "SW-1: extract the file-based run into Pipeline" --body "<summary, test counts, and the guard evidence from Task 4 Step 5>

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```

The PR carries the scope spec and this plan as well as the code. Do not merge it; the owner reviews and merges.

---

## Self-review

**Spec coverage.** Spec "Architecture" bullets: the file-based run (Task 3), thin wrappers (Task 4), helper types moving (Task 2), Doctor (Task 2). The two CLI-intact guards (Tasks 1 and 4 Step 5). Units SW-2 to SW-4 are out of scope for this plan by design; they get their own plans because SW-3's page depends on the final `AgrFileRunResult` shape.

**Placeholders.** None. Every code step carries the code.

**Type consistency.** `AgrFileRunResult` fields (`ExitCode`, `Messages`, `Report`, `OutputDirectory`, `Written`, `Withheld`, `DocumentFileName`) are identical in Task 3's tests, types and recorder, and in Task 4's wrappers. `Validate` and `Build` signatures match between Task 3 and Task 4.
