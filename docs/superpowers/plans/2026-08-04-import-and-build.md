# Import and Build (GUI-8) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the operator loop: the Governor's reply can be pasted into a draft, and a finished draft can be built into a controlled document, without leaving the GUI.

**Architecture:** One extraction (the output package moves out of `Registrar.Cli` into `Registrar.Pipeline` so the CLI and the GUI share one owner), one new Core rule (`ImportMerge` applies content and reports identity), and app glue plus two screens' worth of markup that hold no rules.

**Tech Stack:** .NET 8, C#, Blazor components hosted in WPF, xUnit, bUnit 1.40.

## Global Constraints

Copied from `CLAUDE.md` and `docs/superpowers/specs/2026-08-04-import-and-build-design.md`.

- **THE CLI IS A FALLBACK AND MUST REMAIN INTACT.** Owner constraint, 2026-08-04. No task may change what the CLI writes, in what order, or what it prints. `BuildCommandTests` has 27 tests; **not one of them may be edited**. If a test needs editing to pass, the extraction changed behavior and is reworked, not accommodated.
- **US English, no emojis, no em dashes or en dashes, no decorative unicode** in any source, config, comment, markup, test name, or commit message. Plain hyphen and straight quotes. `SourceHygieneTests` enforces this: express any non-ASCII character as an escape or code point, never as a literal.
- **Zero LLM calls.** Nothing in this unit contacts a model.
- **Rule 5.** The engine never mints a Document ID. Import must never let a pasted reply change one.
- **Rule 7.** A validation report is emitted on every run. The CLI writes it before parsing precisely so a malformed AGR still produces one; that ordering is load-bearing and must survive the extraction.
- **Registrar.App enforces no rules of its own.** Anything a technician could get wrong belongs in Core or Pipeline where the fast `net8.0` suite reaches it.
- **No colour literals in `app.css`.** Only these tokens exist: `--org-accent-blue`, `--org-border`, `--org-border-neutral`, `--org-danger`, `--org-dch-fill`, `--org-header-right-cell-fill`, `--org-heading-navy`, `--org-primary-blue`, `--org-secondary-gray`, `--org-table-row-alt-fill`, `--org-warn-red-border`, `--org-warn-red-fill`.
- **No changes to** `agr.schema.json`, any validation layer, `AgrPicker`, `EngagementRow`, or any Core type delivered by GUI-3 through GUI-7 except where a task says so explicitly.
- Run the full suite with `dotnet test Registrar.sln`. **Also run it once in Release before the final commit of each task**: CI builds Release and a Debug-only run has already hidden a failure on this project.
- End every commit message with: `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

---

## File Structure

**Create:**
- `src/Registrar.Pipeline/OutputPackage.cs` - the emit-when rules and the writes. `OutputPackage`, `OutputPackageResult`.
- `src/Registrar.Core/Import/ImportMerge.cs` - applies content, reports identity.
- `src/Registrar.App/BuildRunner.cs` - app glue: convert, decide freshness, run, write, report.
- `src/Registrar.App/Components/Import.razor` - `/import`.
- `tests/Registrar.Tests/Cli/BuildOutputFileSetTests.cs` - the characterization guard, written first.
- `tests/Registrar.Tests/Pipeline/OutputPackageTests.cs`
- `tests/Registrar.Tests/Import/ImportMergeTests.cs`
- `tests/Registrar.App.Tests/BuildRunnerTests.cs`
- `tests/Registrar.App.Tests/ImportComponentTests.cs`

**Modify:**
- `src/Registrar.Cli/BuildCommand.cs` - becomes a mapping from `OutputPackageResult` to exit codes and stderr.
- `src/Registrar.Cli/CommandSupport.cs` - `WriteOutputs` delegates to `OutputPackage`.
- `src/Registrar.App/DraftSettler.cs` - retains the document and outcome it validated.
- `src/Registrar.App/Components/Editor.razor` - Build wired, build-result region.
- `src/Registrar.App/Components/EditorHeader.razor` - Import navigates, `BuildWired` set true.
- `src/Registrar.App/Registrar.App.csproj` - project reference to `Registrar.Pipeline`.
- `src/Registrar.App/wwwroot/app.css` - import and build-result styles.
- `docs/superpowers/specs/2026-08-01-operator-gui-design.md`, `docs/ROADMAP.md`.

---

### Task 1: Pin the CLI's output file set, before touching the CLI

**Files:**
- Test: `tests/Registrar.Tests/Cli/BuildOutputFileSetTests.cs`

**Interfaces:**
- Consumes: `BuildCommand.Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr, string? mode = null, string? registryPath = null)` returning `int`.
- Produces: nothing. This task exists to make a later task provably safe.

**Why this task is first.** `BuildCommandTests` checks that the files each case cares about exist. None checks that nothing extra appeared and nothing quietly vanished, so an extraction that emitted one more file, or dropped one on an unnamed branch, would pass all 27. This test closes that gap and **must pass on today's unmodified code**, which is what makes it a characterization test rather than a wish.

- [ ] **Step 1: Read how the existing tests drive a build**

Run: `sed -n '1,55p' tests/Registrar.Tests/BuildCommandTests.cs`

Copy its fixture-copying and temp-directory idiom exactly rather than inventing one. Note how it locates the repo root and which fixture each case uses.

- [ ] **Step 2: Write the characterization test**

Create `tests/Registrar.Tests/Cli/BuildOutputFileSetTests.cs`. Use the same helper shape the existing suite uses to copy a fixture into a temp directory and run a build.

```csharp
using Registrar.Cli;
using Registrar.Core;

namespace Registrar.Tests.Cli;

// The complete set of files a build leaves behind, per outcome. BuildCommandTests asserts
// that the files each case cares about exist; nothing asserts that nothing ELSE appeared or
// that nothing quietly stopped appearing. An extraction of the output-package writing could
// therefore emit an extra file, or drop one on a branch no test names, and stay green.
//
// Written against the CLI before that extraction and never edited afterward. If a change
// makes one of these fail, the change altered what the CLI produces, which the owner has
// ruled must not happen: the CLI is the fallback.
public class BuildOutputFileSetTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    // Returns the file names present after the build, sorted, plus the exit code.
    private static (int Exit, string[] Files) Build(string fixtureName, string? mode = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "file-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var source = Path.Combine(RepoRoot(), "fixtures", fixtureName);
        var agrPath = Path.Combine(dir, fixtureName);
        File.Copy(source, agrPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = BuildCommand.Run(agrPath, new FixedClock(), stdout, stderr, mode);

        var files = Directory.GetFiles(dir)
            .Select(Path.GetFileName)
            .Where(n => n != fixtureName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray()!;

        return (exit, files);
    }

    [Fact]
    public void A_passing_draft_build_writes_report_log_manifest_and_document()
    {
        var (exit, files) = Build("agr-sample-ds-draft.json");

        Assert.Equal(0, exit);
        Assert.Equal(
            new[]
            {
                "ORG - Deployment Summary - Talewind CPA - v1.0.docx",
                "artifact-manifest.json",
                "generation-log.json",
                "validation-report.json"
            },
            files);
    }

    [Fact]
    public void A_pre_render_failure_writes_the_report_and_nothing_else()
    {
        var (exit, files) = Build("agr-sample-ds-invalid.json");

        Assert.Equal(1, exit);
        Assert.Equal(new[] { "validation-report.json" }, files);
    }

    [Fact]
    public void A_passing_promotion_build_also_writes_the_payload()
    {
        var (exit, files) = Build("agr-sample-ds-promotion.json");

        Assert.Equal(0, exit);
        Assert.Contains("promotion-payload.json", files);
        Assert.Contains("validation-report.json", files);
        Assert.Contains("generation-log.json", files);
        Assert.Contains("artifact-manifest.json", files);
    }
}
```

The exact fixture names and the exact DOCX file name must match what this repo actually has. Before running, confirm each fixture exists and confirm the expected DOCX name:

Run: `ls fixtures/ | grep -E "ds-draft|ds-invalid|ds-promotion"`
Run: `grep -n "outputFileName" fixtures/agr-sample-ds-draft.json`

Use the real values. If a fixture named above does not exist, pick the closest equivalent from `fixtures/` and say which in your report. If the invalid fixture produces exit 1 with a different file set than a bare report, assert what it actually produces: the point is to record today's behavior, not to assert an ideal.

- [ ] **Step 3: Run it against the unmodified CLI**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~BuildOutputFileSetTests`
Expected: PASS, all three, with no source change anywhere.

**If any of these fail, do not "fix" the CLI.** Adjust the test to record what the CLI actually does today, and note the difference in your report. A characterization test that fails on unmodified code is describing the wrong thing.

- [ ] **Step 4: Run the full suite in Release**

Run: `dotnet test Registrar.sln --configuration Release`
Expected: green, zero warnings.

- [ ] **Step 5: Commit**

```bash
git add tests/Registrar.Tests/Cli/BuildOutputFileSetTests.cs
git commit -m "test: pin the exact file set each CLI build outcome produces"
```

---

### Task 2: Extract OutputPackage into Registrar.Pipeline

**Files:**
- Create: `src/Registrar.Pipeline/OutputPackage.cs`
- Create: `tests/Registrar.Tests/Pipeline/OutputPackageTests.cs`
- Modify: `src/Registrar.Cli/CommandSupport.cs` (the `WriteOutputs` body)
- Modify: `src/Registrar.Cli/BuildCommand.cs` (the writing blocks only)

**Interfaces:**
- Consumes: `BuildResult(ValidationReport Report, PromotionPayload? Payload, byte[]? DocxBytes, GenerationLog? GenerationLog, ArtifactManifest? Manifest, bool DocxEligible, bool ReachedRendering)`; `ValidationOutcome(ValidationReport Report, PromotionPayload? Payload)`; the serializers `ReportSerializer.ToJson`, `PromotionPayloadSerializer.ToJson`, `GenerationLogSerializer.ToJson`, `ArtifactManifestSerializer.ToJson`.
- Produces: `OutputPackageResult(IReadOnlyList<string> Written, IReadOnlyList<string> Withheld, string? Failure)`; `OutputPackage.WriteValidation(ValidationOutcome outcome, string directory)`; `OutputPackage.WriteArtifacts(BuildResult result, string directory, string outputFileName)`.

**The ordering that must survive.** `BuildCommand` calls `WriteOutputs` **twice**: once before `AgrParser.Parse`, so a report exists even when parsing would throw on a malformed AGR (rule 7, and the comment in the file says so), and again after rendering with the merged report. That is why this extraction is two methods rather than one. Do not collapse them.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Pipeline/OutputPackageTests.cs`:

```csharp
using Registrar.Core.Model;
using Registrar.Pipeline;
using Registrar.Validation;

namespace Registrar.Tests.Pipeline;

// The emit-when rules, which CLAUDE.md specifies and which lived inside BuildCommand until
// this unit. They are asserted here rather than only through the CLI so a second caller
// cannot restate them differently.
public class OutputPackageTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ValidationReport Report(string overall) => new()
    {
        ReportVersion = "2.0.0",
        ExecutionId = "00000000-0000-0000-0000-000000000001",
        DocumentId = "ORG-GOV-DS-016",
        RequestedMode = Mode.Draft,
        OverallResult = overall,
        EngineVersion = "test",
        ConfigVersions = new Dictionary<string, string>(),
        TimestampUtc = "2026-08-04T12:00:00Z",
        Checks = Array.Empty<Check>()
    };

    private static string[] FilesIn(string dir)
        => Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void Validation_writes_the_report_and_no_payload_when_there_is_none()
    {
        var dir = TempDir();

        var result = OutputPackage.WriteValidation(new ValidationOutcome(Report(OverallResult.Fail), null), dir);

        Assert.Null(result.Failure);
        Assert.Equal(new[] { "validation-report.json" }, FilesIn(dir));
    }

    [Fact]
    public void A_run_that_never_reached_rendering_emits_no_generation_log()
    {
        var dir = TempDir();
        var build = new BuildResult(Report(OverallResult.Fail), null, null, null, null, false, ReachedRendering: false);

        var result = OutputPackage.WriteArtifacts(build, dir, "out.docx");

        Assert.Null(result.Failure);
        Assert.Empty(FilesIn(dir));
    }

    // The generation log records every run that reached rendering, including one whose
    // document a post-render check withheld. That is the case the log exists for.
    // A real BuildResult, produced by actually running the pipeline over a fixture. Hand
    // building a GenerationLog and an ArtifactManifest would mean inventing values for
    // engine models this test has no business knowing the shape of, and a fabricated one
    // that drifted from the real thing would make these tests pass against a package the
    // engine would never produce.
    private static BuildResult RealBuild()
    {
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

        var engine = EngineContext.Load(repoRoot);
        var agrText = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));
        var outcome = new ValidationService(new FixedClock()).Validate(agrText, engine, null);
        var agr = AgrParser.Parse(agrText);
        var logo = File.ReadAllBytes(ConfigLocator.FindAsset(repoRoot, engine.Config.Style.Wordmark.Asset));

        return BuildPipeline.Run(outcome, agr, engine, logo);
    }

    [Fact]
    public void A_withheld_document_still_emits_the_generation_log_and_no_manifest()
    {
        var dir = TempDir();
        // Same real build, with only eligibility flipped: this is the post-render-withhold
        // shape, where a document was rendered and then held back.
        var build = RealBuild() with { DocxEligible = false };

        var result = OutputPackage.WriteArtifacts(build, dir, "out.docx");

        Assert.Equal(new[] { "generation-log.json" }, FilesIn(dir));
        Assert.Contains(result.Withheld, w => w.Contains("out.docx", StringComparison.Ordinal));
    }

    // No DOCX means no manifest: a manifest describes an artifact that was actually written.
    [Fact]
    public void An_eligible_document_emits_the_docx_and_the_manifest_together()
    {
        var dir = TempDir();
        var build = RealBuild();
        Assert.True(build.DocxEligible, "the fixture must build cleanly for this test to mean anything");

        var result = OutputPackage.WriteArtifacts(build, dir, "out.docx");

        Assert.Null(result.Failure);
        Assert.Equal(new[] { "artifact-manifest.json", "generation-log.json", "out.docx" }, FilesIn(dir));
    }

    [Fact]
    public void A_write_that_fails_is_reported_rather_than_thrown()
    {
        var dir = TempDir();
        // A directory standing where the file must go makes the write fail without
        // depending on permissions, which vary by machine and by CI image.
        Directory.CreateDirectory(Path.Combine(dir, "out.docx"));

        var result = OutputPackage.WriteArtifacts(RealBuild(), dir, "out.docx");

        Assert.NotNull(result.Failure);
        Assert.Contains("cannot write document", result.Failure!, StringComparison.Ordinal);
        // What did land must still be reported, so a partial package never reads as complete.
        Assert.Contains("generation-log.json", result.Written);
    }
}
```

Replace the two fixture helpers with real minimal instances. Read the record definitions first and populate only their required members:

Run: `grep -n "public required\|public sealed record" src/Registrar.Core/Model/GenerationLog.cs src/Registrar.Core/Model/ArtifactManifest.cs`

If an existing test already builds one of these, reuse that helper rather than writing a second.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~OutputPackageTests`
Expected: build failure, `OutputPackage` does not exist.

- [ ] **Step 3: Write OutputPackage**

Create `src/Registrar.Pipeline/OutputPackage.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Pipeline;

// What a build left on disk. Written is every file that landed, Withheld is every file the
// run deliberately did not produce with the reason, and Failure is the first IO failure, or
// null. Failure is a message rather than an exception because the caller has to report a
// PARTIAL package: a validation report without its document is evidence of something that
// did not happen, so "what landed" must survive the failure.
//
// The message is phrased so a caller can prefix it directly, which is how BuildCommand keeps
// its stderr text identical to what it printed before this type existed.
public sealed record OutputPackageResult(
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Withheld,
    string? Failure);

// Which of the five files land, and when. CLAUDE.md's output-package section is the
// specification; this is the only implementation of it. It lived inside Registrar.Cli until
// GUI-8 needed a second caller, and a second copy of these rules would be a second engine.
public static class OutputPackage
{
    // Named for the artifact rather than dated, so a rebuild of the same artifact replaces
    // its own package instead of accumulating. The GUI writes each build into one of these;
    // the CLI does not use it and keeps its flat layout.
    public static string DirectoryNameFor(string documentId, string version)
        => $"{documentId} - {version}";

    // Report and payload. Called before rendering as well as after: the CLI writes a report
    // before AgrParser.Parse so a malformed AGR still produces one (rule 7), then again with
    // the merged post-render report.
    public static OutputPackageResult WriteValidation(ValidationOutcome outcome, string directory)
    {
        var written = new List<string>();

        var failure = TryWriteText(directory, "validation-report.json",
            ReportSerializer.ToJson(outcome.Report), "validation report", written);
        if (failure is not null) return new OutputPackageResult(written, Array.Empty<string>(), failure);

        if (outcome.Payload is not null)
        {
            failure = TryWriteText(directory, "promotion-payload.json",
                PromotionPayloadSerializer.ToJson(outcome.Payload), "promotion payload", written);
            if (failure is not null) return new OutputPackageResult(written, Array.Empty<string>(), failure);
        }

        return new OutputPackageResult(written, Array.Empty<string>(), null);
    }

    // Generation log, document, manifest.
    public static OutputPackageResult WriteArtifacts(BuildResult result, string directory, string outputFileName)
    {
        var written = new List<string>();
        var withheld = new List<string>();

        // A run that never reached rendering has nothing to log: pre-render failures are
        // fully described by the report alone.
        if (!result.ReachedRendering)
            return new OutputPackageResult(written, withheld, null);

        // Emitted before any outcome branch, so it records a run whose document a
        // post-render check withheld exactly as faithfully as one that succeeded.
        var failure = TryWriteText(directory, "generation-log.json",
            GenerationLogSerializer.ToJson(result.GenerationLog!), "generation log", written);
        if (failure is not null) return new OutputPackageResult(written, withheld, failure);

        if (!result.DocxEligible)
        {
            withheld.Add($"{outputFileName}: no document was produced.");
            withheld.Add("artifact-manifest.json: no document was produced.");
            return new OutputPackageResult(written, withheld, null);
        }

        failure = TryWriteBytes(directory, outputFileName, result.DocxBytes!, "document", written);
        if (failure is not null) return new OutputPackageResult(written, withheld, failure);

        // The manifest describes the artifact just written, so it is emitted here and
        // nowhere else: no DOCX means no manifest.
        failure = TryWriteText(directory, "artifact-manifest.json",
            ArtifactManifestSerializer.ToJson(result.Manifest!), "artifact manifest", written);
        if (failure is not null) return new OutputPackageResult(written, withheld, failure);

        return new OutputPackageResult(written, withheld, null);
    }

    private static string? TryWriteText(string dir, string name, string content, string label, List<string> written)
    {
        try
        {
            File.WriteAllText(Path.Combine(dir, name), content);
            written.Add(name);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"cannot write {label}: {ex.Message}";
        }
    }

    private static string? TryWriteBytes(string dir, string name, byte[] content, string label, List<string> written)
    {
        try
        {
            File.WriteAllBytes(Path.Combine(dir, name), content);
            written.Add(name);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"cannot write {label}: {ex.Message}";
        }
    }
}
```

- [ ] **Step 4: Run the new tests**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~OutputPackageTests`
Expected: 5 passed.

- [ ] **Step 5: Point the CLI at it, changing no behavior**

In `src/Registrar.Cli/CommandSupport.cs`, replace the body of `WriteOutputs` so it delegates and keeps the exact stderr text and return values:

```csharp
    public static int? WriteOutputs(ValidationOutcome outcome, string dir, TextWriter stderr)
    {
        var package = OutputPackage.WriteValidation(outcome, dir);
        if (package.Failure is null) return null;

        stderr.WriteLine($"error: {package.Failure}");
        return 2;
    }
```

In `src/Registrar.Cli/BuildCommand.cs`, replace the generation-log, DOCX, and manifest blocks with a single call, preserving both the stdout lines and the exit codes exactly:

```csharp
        var package = OutputPackage.WriteArtifacts(result, dir, agr.RenderingOptions.OutputFileName);
        if (package.Failure is not null)
        {
            stderr.WriteLine($"error: {package.Failure}");
            return 2;
        }

        if (mergedReport.OverallResult == OverallResult.Fail)
        {
            stdout.WriteLine($"fail: {mergedReport.DocumentId} - no document produced (post-render validation failed).");
            return 1;
        }

        if (result.DocxEligible)
        {
            stdout.WriteLine($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - {agr.RenderingOptions.OutputFileName}");
        }
        else
        {
            stdout.WriteLine($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - no document (promotion requires a clean pass).");
        }

        return 0;
```

Keep the `!result.ReachedRendering` early return and its stdout line exactly where they are; `WriteArtifacts` also returns early in that case, so the two agree.

Add the `Registrar.Pipeline` using directive to `CommandSupport.cs` if it is not already there.

- [ ] **Step 6: Prove the CLI is unchanged**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~BuildCommandTests`
Expected: 27 passed, with **no edits to that file**.

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~BuildOutputFileSetTests`
Expected: 3 passed, with **no edits to that file**.

Run: `git diff --stat tests/`
Expected: only `tests/Registrar.Tests/Pipeline/OutputPackageTests.cs` appears. If any other test file shows a change, revert it and rework the extraction.

- [ ] **Step 7: Full suite, both configurations**

Run: `dotnet test Registrar.sln`
Run: `dotnet test Registrar.sln --configuration Release`
Expected: green both times, zero warnings.

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Pipeline/OutputPackage.cs tests/Registrar.Tests/Pipeline/OutputPackageTests.cs src/Registrar.Cli
git commit -m "refactor: give the output package one owner, shared by the CLI and the GUI"
```

---

### Task 3: ImportMerge

**Files:**
- Create: `src/Registrar.Core/Import/ImportMerge.cs`
- Create: `tests/Registrar.Tests/Import/ImportMergeTests.cs`

**Interfaces:**
- Consumes: `AgrDraft`; `ImportFinding(string? JsonPath, string Message, ImportFindingKind Kind)`; `ImportFindingKind`; `FieldPath` for pointers.
- Produces: `ImportMerge.Apply(AgrDraft open, AgrDraft imported, ArtifactTypeConfig type)` returning `ImportMergeResult(AgrDraft Draft, IReadOnlyList<ImportFinding> Findings)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Import/ImportMergeTests.cs`:

```csharp
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Import;
using Registrar.Core.Model;

namespace Registrar.Tests.Import;

// What a pasted reply is allowed to change. Content, and nothing else. The Document ID was
// issued by Registry Owner and chosen by the operator; a chat reply must not move it,
// and CLAUDE.md rule 5 plus the division of labor say the engine never trusts the Governor.
public class ImportMergeTests
{
    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static AgrSection Section(string title, string text) => new()
    {
        Title = title,
        DescriptorLine = null,
        Blocks = new[] { new AgrBlock { BlockType = "paragraph", Text = text } }
    };

    private static AgrDraft Open() => new()
    {
        ArtifactType = "DS",
        DocumentId = "ORG-GOV-DS-016",
        Owner = "Registry Owner",
        Client = "Talewind CPA"
    };

    [Fact]
    public void Sections_and_placeholders_are_applied()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var imported = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[] { Section(titles[0], "drafted by the Governor") },
            PlaceholderMap = new Dictionary<string, string?> { ["[TOOL / CONNECTOR NAME]"] = "Copilot Chat" }
        };

        var result = ImportMerge.Apply(Open(), imported, type);

        Assert.Equal("drafted by the Governor", result.Draft.Sections.Single().Blocks.Single().Text);
        Assert.Equal("Copilot Chat", result.Draft.PlaceholderMap["[TOOL / CONNECTOR NAME]"]);
    }

    [Fact]
    public void A_disagreeing_document_id_is_reported_and_not_applied()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];

        var imported = new AgrDraft { ArtifactType = "DS", DocumentId = "ORG-GOV-DS-999" };

        var result = ImportMerge.Apply(Open(), imported, type);

        Assert.Equal("ORG-GOV-DS-016", result.Draft.DocumentId);
        Assert.Contains(result.Findings, f =>
            f.JsonPath == FieldPath.DocumentId && f.Message.Contains("ORG-GOV-DS-999", StringComparison.Ordinal));
    }

    [Fact]
    public void A_disagreeing_artifact_type_is_reported_and_not_applied()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];

        var imported = new AgrDraft { ArtifactType = "AUP" };

        var result = ImportMerge.Apply(Open(), imported, type);

        Assert.Equal("DS", result.Draft.ArtifactType);
        Assert.Contains(result.Findings, f => f.JsonPath == FieldPath.ArtifactType);
    }

    // A reply that simply did not echo a field is not a disagreement. Reporting one would
    // train the operator to ignore the findings list.
    [Fact]
    public void A_field_the_reply_omitted_is_not_a_finding()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];

        var result = ImportMerge.Apply(Open(), new AgrDraft(), type);

        Assert.Empty(result.Findings);
        Assert.Equal("ORG-GOV-DS-016", result.Draft.DocumentId);
        Assert.Equal("Registry Owner", result.Draft.Owner);
    }

    // An echo that matches is not a disagreement either.
    [Fact]
    public void A_field_the_reply_echoed_correctly_is_not_a_finding()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];

        var imported = new AgrDraft { ArtifactType = "DS", DocumentId = "ORG-GOV-DS-016" };

        var result = ImportMerge.Apply(Open(), imported, type);

        Assert.Empty(result.Findings);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~ImportMergeTests`
Expected: build failure, `ImportMerge` does not exist.

- [ ] **Step 3: Write ImportMerge**

Create `src/Registrar.Core/Import/ImportMerge.cs`:

```csharp
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Core.Import;

public sealed record ImportMergeResult(AgrDraft Draft, IReadOnlyList<ImportFinding> Findings);

// What a pasted Governor reply is allowed to change about an open draft: its content, and
// nothing else.
//
// ImportParser returns a whole AgrDraft, so a reply can carry an artifact type, Document ID,
// classification or owner. Applying those would let a chat reply move an identifier that
// Registry Owner issued and an operator chose, which is precisely what CLAUDE.md rule 5
// and the division of labor forbid: the engine never trusts the Governor.
//
// A disagreement is reported rather than dropped, because a reply carrying the wrong Document
// ID usually means the operator pasted the wrong reply, and that is worth seeing. An omitted
// field is not a disagreement: most replies will not echo identity at all, and reporting
// those would train the operator to ignore the list.
public static class ImportMerge
{
    public static ImportMergeResult Apply(AgrDraft open, AgrDraft imported, ArtifactTypeConfig type)
    {
        var findings = new List<ImportFinding>();

        void Compare(string? theirs, string? ours, string jsonPath, string label)
        {
            if (string.IsNullOrWhiteSpace(theirs)) return;
            if (string.Equals(theirs, ours, StringComparison.Ordinal)) return;

            findings.Add(new ImportFinding(
                jsonPath,
                $"The reply carried {label} '{theirs}', which does not match '{ours}' on this draft. "
                + "It was not applied.",
                ImportFindingKind.Unreadable));
        }

        Compare(imported.ArtifactType, open.ArtifactType, FieldPath.ArtifactType, "artifact type");
        Compare(imported.DocumentId, open.DocumentId, FieldPath.DocumentId, "Document ID");
        Compare(imported.Owner, open.Owner, FieldPath.Owner, "owner");
        Compare(imported.Client, open.Client, FieldPath.Client, "client");

        // Content only. Sections replace rather than merge: the Governor drafts a whole
        // artifact, and interleaving its sections with whatever was already there would
        // produce a document neither party wrote.
        var merged = open with
        {
            Sections = imported.Sections.Count > 0 ? imported.Sections : open.Sections,
            PlaceholderMap = imported.PlaceholderMap.Count > 0 ? imported.PlaceholderMap : open.PlaceholderMap
        };

        return new ImportMergeResult(DraftEdits.NormalizeSections(merged, type), findings);
    }
}
```

`ImportFindingKind.Unreadable` is the closest existing kind: the member was present and its content was not used. If reading `ImportFindingKind` shows a better fit, use it and say which in your report. Do not add a new enum member without noting it, since the enum is part of GUI-4's contract.

- [ ] **Step 4: Run the tests**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~ImportMergeTests`
Expected: 5 passed.

- [ ] **Step 5: Full suite, both configurations, then commit**

Run: `dotnet test Registrar.sln` and `dotnet test Registrar.sln --configuration Release`

```bash
git add src/Registrar.Core/Import/ImportMerge.cs tests/Registrar.Tests/Import/ImportMergeTests.cs
git commit -m "feat: a pasted reply changes content, never identity"
```

---

### Task 4: DraftSettler retains what it validated

**Files:**
- Modify: `src/Registrar.App/DraftSettler.cs`
- Modify: `tests/Registrar.App.Tests/DraftSettlerTests.cs`

**Interfaces:**
- Produces: `sealed record ValidatedDocument(string AgrJson, AgrDocument Document, ValidationOutcome Outcome)`; `DraftSettler.LastValidated { get; }` returning `ValidatedDocument?`.

**Why.** `BuildPipeline.Run` takes an already-computed outcome so a GUI validating continuously does not revalidate on every Build press. Reusing it is only sound if it describes the document being built, and the settler currently throws away both the document and the outcome. Retaining the serialized text makes the freshness question a string comparison rather than a claim about timers.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Registrar.App.Tests/DraftSettlerTests.cs` (inside the class):

```csharp
    [Fact]
    public void A_draft_that_validates_retains_the_document_and_outcome()
    {
        var settler = new DraftSettler(new FixedClock());

        settler.Validate(CompleteDraft(), Engine(), null);

        Assert.NotNull(settler.LastValidated);
        Assert.Equal("ORG-GOV-DS-016", settler.LastValidated!.Document.Identity.DocumentId);
        Assert.Contains("ORG-GOV-DS-016", settler.LastValidated.AgrJson, StringComparison.Ordinal);
        Assert.NotNull(settler.LastValidated.Outcome);
    }

    // A draft that does not convert has no document, so there is nothing to build from and
    // nothing may be left over from an earlier draft that did convert.
    [Fact]
    public void A_draft_that_does_not_convert_clears_what_was_retained()
    {
        var settler = new DraftSettler(new FixedClock());
        settler.Validate(CompleteDraft(), Engine(), null);
        Assert.NotNull(settler.LastValidated);

        settler.Validate(new AgrDraft { ArtifactType = "DS" }, Engine(), null);

        Assert.Null(settler.LastValidated);
    }
```

`CompleteDraft()` and `Engine()` are helpers this test file already has, or close to it. Read the file first and reuse its existing helpers; if the equivalents are named differently, use the real names. A complete draft here means one that converts, so it must carry `DocumentId`, `Owner`, `SourceWorkflow`, and content in every declared section, or `ToDocument` returns early at `AgrAuthoring.cs:76-77` and the test proves nothing.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~DraftSettlerTests`
Expected: build failure, `LastValidated` does not exist.

- [ ] **Step 3: Implement**

In `src/Registrar.App/DraftSettler.cs`, add the record above the class:

```csharp
// The document a validation ran against, the text that was validated, and the outcome, kept
// together because they are only meaningful as a set. AgrJson is retained so a later Build
// can decide whether the outcome still describes the document it is about to build by
// comparing text, rather than by reasoning about how long ago the last edit was.
public sealed record ValidatedDocument(string AgrJson, AgrDocument Document, ValidationOutcome Outcome);
```

Add the property and populate it inside `Validate`:

```csharp
    public ValidatedDocument? LastValidated { get; private set; }
```

In `Validate`, set `LastValidated = null;` at the top, then after the outcome is computed:

```csharp
            var agrJson = AgrSerializer.ToJson(result.Document);
            var outcome = new ValidationService(clock).Validate(agrJson, engine, registryCsv);

            LastValidated = new ValidatedDocument(agrJson, result.Document, outcome);
            Rail = EditorRail.FromChecks(outcome.Report.Checks, outcome.Report.OverallResult);
```

Setting it to null at the top of `Validate` is what makes the gaps path and the exception path both clear it, without a second assignment in each branch.

- [ ] **Step 4: Run the tests**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~DraftSettlerTests`
Expected: all pass, including the file's existing tests unchanged.

- [ ] **Step 5: Full suite, both configurations, then commit**

```bash
git add src/Registrar.App/DraftSettler.cs tests/Registrar.App.Tests/DraftSettlerTests.cs
git commit -m "feat: keep the document and outcome a validation ran against"
```

---

### Task 5: BuildRunner

**Files:**
- Create: `src/Registrar.App/BuildRunner.cs`
- Create: `tests/Registrar.App.Tests/BuildRunnerTests.cs`
- Modify: `src/Registrar.App/Registrar.App.csproj`

**Interfaces:**
- Consumes: `ValidatedDocument` (Task 4); `OutputPackage.WriteValidation`, `WriteArtifacts`, `DirectoryNameFor` (Task 2); `BuildPipeline.Run(ValidationOutcome, AgrDocument, EngineContext, byte[])`.
- Produces: `BuildRunner.Run(AgrDraft draft, string engagementFolder, DraftSettler settler, EngineContext engine, string? registryCsvPath, string repoRoot)` returning `BuildRunResult(bool Ran, string? Folder, ValidationReport? Report, IReadOnlyList<string> Written, IReadOnlyList<string> Withheld, string? Failure, bool Revalidated)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.App.Tests/BuildRunnerTests.cs`:

```csharp
using Registrar.App;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;
using Xunit;

namespace Registrar.App.Tests;

public class BuildRunnerTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // A draft that converts and passes. Build it from a fixture AGR so it is genuinely
    // complete rather than hand-assembled and quietly missing a required field.
    private static AgrDraft BuildableDraft()
    {
        var agr = AgrParser.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "fixtures", "agr-sample-ds-draft.json")));
        return AgrAuthoring.FromDocument(agr);
    }

    [Fact]
    public void A_build_writes_its_package_into_a_per_artifact_subfolder()
    {
        var engine = EngineContext.Load(RepoRoot());
        var settler = new DraftSettler(new FixedClock());
        var draft = BuildableDraft();
        var folder = TempFolder();

        settler.Validate(draft, engine, null);
        var result = BuildRunner.Run(draft, folder, settler, engine, null, RepoRoot());

        Assert.True(result.Ran);
        Assert.NotNull(result.Folder);
        // The engagement folder itself gains a directory, not a scatter of files.
        Assert.Empty(Directory.GetFiles(folder));
        Assert.Single(Directory.GetDirectories(folder));
        Assert.Contains("validation-report.json", result.Written);
    }

    // The outcome the settler holds is reused only when it describes the document being
    // built. Proven by changing the document, not by waiting.
    [Fact]
    public void An_outcome_for_a_different_document_is_not_reused()
    {
        var engine = EngineContext.Load(RepoRoot());
        var settler = new DraftSettler(new FixedClock());
        var draft = BuildableDraft();

        settler.Validate(draft, engine, null);

        var edited = draft with { Client = "A Different Client" };
        var result = BuildRunner.Run(edited, TempFolder(), settler, engine, null, RepoRoot());

        Assert.True(result.Ran);
        Assert.True(result.Revalidated);
    }

    [Fact]
    public void An_outcome_for_the_same_document_is_reused()
    {
        var engine = EngineContext.Load(RepoRoot());
        var settler = new DraftSettler(new FixedClock());
        var draft = BuildableDraft();

        settler.Validate(draft, engine, null);
        var result = BuildRunner.Run(draft, TempFolder(), settler, engine, null, RepoRoot());

        Assert.True(result.Ran);
        Assert.False(result.Revalidated);
    }

    // A draft that does not convert cannot be built, and saying so is not the same as
    // failing: the rail already explains what is missing.
    [Fact]
    public void A_draft_that_does_not_convert_does_not_run()
    {
        var engine = EngineContext.Load(RepoRoot());
        var settler = new DraftSettler(new FixedClock());

        var result = BuildRunner.Run(
            new AgrDraft { ArtifactType = "DS" }, TempFolder(), settler, engine, null, RepoRoot());

        Assert.False(result.Ran);
        Assert.Null(result.Folder);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~BuildRunnerTests`
Expected: build failure, `BuildRunner` does not exist.

- [ ] **Step 3: Add the project reference**

In `src/Registrar.App/Registrar.App.csproj`, alongside the existing Core and Validation references:

```xml
    <ProjectReference Include="..\Registrar.Pipeline\Registrar.Pipeline.csproj" />
```

- [ ] **Step 4: Write BuildRunner**

Create `src/Registrar.App/BuildRunner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Model;
using Registrar.Pipeline;
using Registrar.Validation;

namespace Registrar.App;

// What a Build press produced. Ran is false when the draft does not convert, which is not a
// failure: the rail already says what is missing, and Build is disabled in that state anyway.
public sealed record BuildRunResult(
    bool Ran,
    string? Folder,
    ValidationReport? Report,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Withheld,
    string? Failure,
    bool Revalidated);

// Glue, in the same sense DraftSettler is glue: it sequences Core and Pipeline calls and
// holds no judgment about what is valid. Every rule it appears to apply belongs to something
// it calls.
public static class BuildRunner
{
    public static BuildRunResult Run(
        AgrDraft draft,
        string engagementFolder,
        DraftSettler settler,
        EngineContext engine,
        string? registryCsvPath,
        string repoRoot)
    {
        var empty = Array.Empty<string>();

        var authoring = AgrAuthoring.ToDocument(draft, engine.Config, engine.SchemaRoot, new SystemClock());
        if (authoring.Document is null)
            return new BuildRunResult(false, null, null, empty, empty, null, false);

        var document = authoring.Document;
        var agrJson = AgrSerializer.ToJson(document);

        // Reuse the settler's outcome only when it was produced from this exact text.
        // Anything else revalidates. The comparison is on the serialized document rather
        // than on elapsed time, because "nothing has changed since" is a claim about a
        // debounce timer and this is a claim about the document.
        var held = settler.LastValidated;
        var reused = held is not null && string.Equals(held.AgrJson, agrJson, StringComparison.Ordinal);

        ValidationOutcome outcome;
        if (reused)
        {
            outcome = held!.Outcome;
        }
        else
        {
            var registryCsv = registryCsvPath is not null && File.Exists(registryCsvPath)
                ? File.ReadAllText(registryCsvPath)
                : null;
            outcome = new ValidationService(new SystemClock()).Validate(agrJson, engine, registryCsv);
        }

        var folderName = OutputPackage.DirectoryNameFor(
            document.Identity.DocumentId, document.Identity.Version);
        var folder = Path.Combine(engagementFolder, folderName);

        try
        {
            // Replaced wholesale rather than merged, so a file left by an earlier build of
            // this artifact cannot masquerade as part of this one.
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);

            // The AGR JSON is a build intermediate for a GUI-authored artifact. It goes in
            // the package folder rather than beside the draft, because EngagementScan globs
            // "*.json" in the engagement folder and an AGR written flat would appear as a
            // second row for an artifact the operator already has open.
            File.WriteAllText(Path.Combine(folder, document.Identity.DocumentId + ".json"), agrJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BuildRunResult(true, folder, outcome.Report, empty, empty,
                $"cannot prepare the output folder: {ex.Message}", !reused);
        }

        var validationWrite = OutputPackage.WriteValidation(outcome, folder);
        if (validationWrite.Failure is not null)
            return new BuildRunResult(true, folder, outcome.Report, validationWrite.Written,
                validationWrite.Withheld, validationWrite.Failure, !reused);

        byte[] logoBytes;
        try
        {
            logoBytes = File.ReadAllBytes(
                ConfigLocator.FindAsset(repoRoot, engine.Config.Style.Wordmark.Asset));
        }
        catch (Exception ex)
        {
            return new BuildRunResult(true, folder, outcome.Report, validationWrite.Written,
                validationWrite.Withheld,
                $"cannot read the wordmark asset '{engine.Config.Style.Wordmark.Asset}': {ex.Message}",
                !reused);
        }

        var build = BuildPipeline.Run(outcome, document, engine, logoBytes);

        // The merged post-render report supersedes the pre-render one, exactly as it does
        // in the CLI.
        var merged = OutputPackage.WriteValidation(new ValidationOutcome(build.Report, build.Payload), folder);
        var artifacts = OutputPackage.WriteArtifacts(build, folder, document.RenderingOptions.OutputFileName);

        var written = new List<string>(validationWrite.Written);
        foreach (var name in merged.Written) if (!written.Contains(name)) written.Add(name);
        written.AddRange(artifacts.Written);

        return new BuildRunResult(
            true, folder, build.Report, written, artifacts.Withheld,
            merged.Failure ?? artifacts.Failure, !reused);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~BuildRunnerTests`
Expected: 4 passed.

- [ ] **Step 6: Full suite, both configurations, then commit**

```bash
git add src/Registrar.App/BuildRunner.cs src/Registrar.App/Registrar.App.csproj tests/Registrar.App.Tests/BuildRunnerTests.cs
git commit -m "feat: BuildRunner converts, checks freshness, builds, and writes the package"
```

---

### Task 6: The Import screen

**Files:**
- Create: `src/Registrar.App/Components/Import.razor`
- Create: `tests/Registrar.App.Tests/ImportComponentTests.cs`
- Modify: `src/Registrar.App/wwwroot/app.css`

**Interfaces:**
- Consumes: `GovernorBrief.For(ArtifactTypeConfig type, ConfigBundle config)` returning `string`; `ImportParser.Parse(string pastedText, ConfigBundle config)` returning `ImportResult(AgrDraft? Draft, IReadOnlyList<ImportFinding> Findings)`; `ImportParser.Normalize(string pastedText, ConfigBundle config)` returning `NormalizeResult(string Text, int Repaired, IReadOnlyList<string> Collisions)`; `ImportMerge.Apply` (Task 3); `AppState`.
- Produces: the `/import` route.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.App.Tests/ImportComponentTests.cs`:

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

public class ImportComponentTests : TestContext
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private AppState Wire(AgrDraft draft)
    {
        var state = new AppState(EngineContext.Load(RepoRoot())) { Draft = draft };
        Services.AddSingleton(state);
        Services.AddSingleton<IClock>(new FixedClock());
        JSInterop.Mode = JSRuntimeMode.Loose;
        return state;
    }

    private static AgrDraft OpenDraft() => new()
    {
        ArtifactType = "DS",
        DocumentId = "ORG-GOV-DS-016",
        Owner = "Registry Owner"
    };

    // The brief is what the operator hands the Governor, so it has to be on the screen
    // rather than described.
    [Fact]
    public void The_screen_shows_the_brief_for_the_open_artifact_type()
    {
        var state = Wire(OpenDraft());
        var expected = GovernorBrief.For(state.Engine.Config.ArtifactTypesByCode["DS"], state.Engine.Config);

        var cut = RenderComponent<Import>();

        Assert.Contains(expected.Split('\n')[0], cut.Find(".brief").TextContent, StringComparison.Ordinal);
    }

    // Nothing is written until the operator accepts. This is the parent design's rule and
    // the reason Import is a screen rather than an action.
    [Fact]
    public void Parsing_a_paste_reports_without_changing_the_draft()
    {
        var state = Wire(OpenDraft());
        var before = state.Draft;

        var cut = RenderComponent<Import>();
        cut.Find("textarea.paste").Change("this is not JSON at all");
        cut.Find("button.parse").Click();

        Assert.Same(before, state.Draft);
        Assert.NotEmpty(cut.FindAll(".finding"));
    }

    // Opening a draft validates without writing, deliberately. An import arrives through
    // that same path, so without this flag the Governor's whole draft would live only in
    // memory and closing the window would lose it.
    [Fact]
    public void Accepting_marks_the_draft_as_needing_a_save()
    {
        var state = Wire(OpenDraft());
        var titles = state.Engine.Config.ArtifactTypesByCode["DS"].SectionSchema!.OrderedSections!;

        var reply = "{\"artifactIdentity\":{\"artifactType\":\"DS\"},"
            + "\"dataInputs\":{\"sections\":[{\"title\":\"" + titles[0] + "\",\"descriptorLine\":null,"
            + "\"blocks\":[{\"blockType\":\"paragraph\",\"text\":\"from the Governor\"}]}]}}";

        var cut = RenderComponent<Import>();
        cut.Find("textarea.paste").Change(reply);
        cut.Find("button.parse").Click();
        Assert.False(state.DraftNeedsSave);

        cut.Find("button.accept").Click();

        Assert.True(state.DraftNeedsSave);
    }

    [Fact]
    public void Accepting_applies_sections_and_leaves_identity_alone()
    {
        var state = Wire(OpenDraft());
        var config = state.Engine.Config;
        var titles = config.ArtifactTypesByCode["DS"].SectionSchema!.OrderedSections!;

        var reply = "{\"artifactIdentity\":{\"artifactType\":\"DS\",\"documentId\":\"ORG-GOV-DS-999\"},"
            + "\"dataInputs\":{\"sections\":[{\"title\":\"" + titles[0] + "\",\"descriptorLine\":null,"
            + "\"blocks\":[{\"blockType\":\"paragraph\",\"text\":\"from the Governor\"}]}]}}";

        var cut = RenderComponent<Import>();
        cut.Find("textarea.paste").Change(reply);
        cut.Find("button.parse").Click();
        cut.Find("button.accept").Click();

        Assert.Equal("ORG-GOV-DS-016", state.Draft!.DocumentId);
        Assert.Contains(state.Draft.Sections,
            s => s.Blocks.Any(b => b.Text == "from the Governor"));
    }
}
```

The reply JSON above must be shaped the way `ImportParser` expects. Before writing the component, read how `ImportParserTests` builds a valid reply and copy that shape exactly rather than guessing at the envelope.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~ImportComponentTests`
Expected: build failure, `Import` does not exist.

- [ ] **Step 3: Write the component**

Create `src/Registrar.App/Components/Import.razor`:

```razor
@page "/import"
@inject AppState State
@inject NavigationManager Nav

@* Paste, then a report, then a decision. Nothing reaches the draft until Accept, because a
   reply that turns out to be for a different artifact should cost the operator nothing. *@

@if (State.Draft is null || type is null)
{
    <p class="prompt">No artifact is open. Open one before importing a reply.</p>
}
else
{
    <h1>Import from Governor</h1>

    <section class="brief-block">
        <h2>Send this to the Governor</h2>
        <pre class="brief">@brief</pre>
    </section>

    <section class="paste-block">
        <h2>Paste the reply</h2>
        <textarea class="paste" rows="12" @bind="pasted"></textarea>
        <button class="parse" @onclick="ParseReply">Read the reply</button>
    </section>

    @if (parsed is not null)
    {
        <section class="report">
            <h2>What arrived</h2>

            @if (parsed.Draft is null)
            {
                <p class="nothing">No artifact could be read from that reply. Nothing has changed.</p>
            }
            else
            {
                <p class="summary">@parsed.Draft.Sections.Count section(s) read.</p>
            }

            @foreach (var finding in AllFindings())
            {
                <p class="finding">@finding.Message</p>
            }

            @if (normalizeMessage is not null)
            {
                <p class="normalize-result">@normalizeMessage</p>
            }

            <div class="actions">
                <button class="accept" disabled="@(parsed.Draft is null)" @onclick="Accept">Accept</button>
                <button class="normalize" @onclick="Repair">Try transport repair</button>
                <button class="back" @onclick="Back">Back without importing</button>
            </div>
        </section>
    }
}

@code {
    private ArtifactTypeConfig? type;
    private string brief = string.Empty;
    private string pasted = string.Empty;
    private ImportResult? parsed;
    private ImportMergeResult? preview;
    private string? normalizeMessage;

    protected override void OnInitialized()
    {
        if (State.Draft?.ArtifactType is null) return;
        State.Engine.Config.ArtifactTypesByCode.TryGetValue(State.Draft.ArtifactType, out var found);
        type = found;
        if (type is not null) brief = GovernorBrief.For(type, State.Engine.Config);
    }

    // The identity findings come from the merge rather than the parse, so they are computed
    // here and shown alongside the parser's own findings. Computing the merge now is what
    // makes the report a preview: it is the same call Accept applies.
    private void ParseReply()
    {
        normalizeMessage = null;
        parsed = ImportParser.Parse(pasted, State.Engine.Config);
        preview = parsed.Draft is null
            ? null
            : ImportMerge.Apply(State.Draft!, parsed.Draft, type!);
    }

    private IEnumerable<ImportFinding> AllFindings()
        => (parsed?.Findings ?? Array.Empty<ImportFinding>())
            .Concat(preview?.Findings ?? Array.Empty<ImportFinding>());

    // Never automatic. Repair rewrites the operator's payload, and a repair they did not ask
    // for that quietly dropped a placeholder would leave them believing the document was
    // clean.
    private void Repair()
    {
        var result = ImportParser.Normalize(pasted, State.Engine.Config);

        if (result.Collisions.Count > 0)
        {
            normalizeMessage = "Repair was refused: " + string.Join("; ", result.Collisions)
                + ". Nothing was changed. Ask the Governor to resend the reply.";
            return;
        }

        pasted = result.Text;
        normalizeMessage = $"{result.Repaired} character(s) repaired. Read the reply again to see what arrived.";
        parsed = null;
        preview = null;
    }

    // DraftNeedsSave matters more than it looks. Opening a draft deliberately validates
    // without writing (GUI-7 Task 8), so that merely looking at a colleague's draft does not
    // rewrite it. An import arrives through that same path: without this flag the editor
    // would validate the imported draft and never write it, and an operator who imported the
    // Governor's whole draft and then closed the window would lose all of it.
    private void Accept()
    {
        if (preview is null) return;
        State.Draft = preview.Draft;
        State.DraftNeedsSave = true;
        Nav.NavigateTo("/editor");
    }

    private void Back() => Nav.NavigateTo("/editor");
}
```

Add the flag to `src/Registrar.App/AppState.cs`, beside the other values:

```csharp
    // Set when a draft reaches AppState already changed, which today means an accepted
    // import. The editor validates without writing when it opens, so without this an
    // imported draft would live only in memory until the next keystroke.
    public bool DraftNeedsSave { get; set; }
```

Add to `src/Registrar.App/wwwroot/app.css`:

```css
.brief-block pre.brief { background: var(--org-table-row-alt-fill); padding: 1rem; white-space: pre-wrap; }
.paste-block textarea.paste { width: 100%; font-family: monospace; }
.report .finding { color: var(--org-danger); }
.report .nothing { color: var(--org-secondary-gray); }
.report .normalize-result { color: var(--org-primary-blue); }
.report .actions { display: flex; gap: 0.75rem; margin-top: 1rem; }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~ImportComponentTests`
Expected: 3 passed.

- [ ] **Step 5: Full suite, both configurations, then commit**

```bash
git add src/Registrar.App tests/Registrar.App.Tests/ImportComponentTests.cs
git commit -m "feat: the Import screen reports a reply before anything is written"
```

---

### Task 7: Wire Build and Import into the editor

**Files:**
- Modify: `src/Registrar.App/Components/EditorHeader.razor`
- Modify: `src/Registrar.App/Components/Editor.razor`
- Modify: `src/Registrar.App/wwwroot/app.css`
- Modify: `tests/Registrar.App.Tests/ValidationRailComponentTests.cs`
- Modify: `tests/Registrar.App.Tests/EditorShellComponentTests.cs`

**Interfaces:**
- Consumes: `BuildRunner.Run` (Task 5), `BuildRunResult`; `EditorHeader.BuildWired`.
- Produces: nothing later depends on.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Registrar.App.Tests/EditorShellComponentTests.cs` (inside the class):

```csharp
    // The unavailable-because-not-wired message was correct for GUI-7 and is now a lie.
    [Fact]
    public void Build_is_offered_on_a_ready_draft_now_that_it_is_wired()
    {
        Wire(CompleteDraft());

        var cut = RenderComponent<Editor>();

        Assert.Null(cut.Find("button.build").GetAttribute("disabled"));
        Assert.DoesNotContain("arrives with the Import screen", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_navigates_to_the_import_screen()
    {
        Wire(CompleteDraft());
        var nav = Services.GetRequiredService<NavigationManager>();

        var cut = RenderComponent<Editor>();
        cut.Find("button.import").Click();

        Assert.EndsWith("/import", nav.Uri, StringComparison.Ordinal);
    }

    // The other half of the import save path. Opening normally must still not write, and an
    // imported draft must be written the moment the editor takes it, because the operator
    // may close the window without touching a key.
    [Fact]
    public void A_draft_that_arrived_needing_a_save_is_written_when_the_editor_opens()
    {
        var path = TempDraftPath();
        var state = Wire(CompleteDraft(), path);
        state.DraftNeedsSave = true;

        RenderComponent<Editor>();

        Assert.True(File.Exists(path));
        Assert.False(state.DraftNeedsSave);
    }

    [Fact]
    public void Opening_a_draft_that_did_not_change_still_writes_nothing()
    {
        var path = TempDraftPath();
        var state = Wire(CompleteDraft(), path);

        RenderComponent<Editor>();

        Assert.False(File.Exists(path));
    }
```

`CompleteDraft()` must be a draft that converts and leaves the rail unblocked, so Build is genuinely enabled. Build it from a fixture with `AgrAuthoring.FromDocument` the way `BuildRunnerTests` does rather than hand-assembling one.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EditorShellComponentTests`
Expected: failures on both new tests: Build is still disabled, Import is still inert.

- [ ] **Step 3: Wire the header**

In `src/Registrar.App/Components/EditorHeader.razor`, replace the inert Import button and add a Build handler:

```razor
    <button class="import" @onclick="OnImport">Import from Governor</button>
    <button class="build" disabled="@(!Rail.BuildEnabled || !BuildWired)" @onclick="OnBuild">Build</button>
```

Add the two callbacks to the `@code` block:

```razor
    [Parameter] public EventCallback OnImport { get; set; }
    [Parameter] public EventCallback OnBuild { get; set; }
```

- [ ] **Step 4: Wire the editor**

In `src/Registrar.App/Components/Editor.razor`, pass the callbacks and set `BuildWired`:

```razor
    <EditorHeader Rail="rail"
                  CurrentMode="@State.Draft.Mode"
                  OnModeChanged="ModeChanged"
                  SaveError="@saveError"
                  BuildWired="true"
                  OnImport="GoToImport"
                  OnBuild="RunBuild" />
```

Every one of those values is bound correctly above; note that `CurrentMode` and `SaveError` carry a leading `@` because they are string parameters, and Razor treats a quoted value on a string parameter as a literal. Do not drop them.

Add the build-result region after the notice block:

```razor
    @if (build is not null)
    {
        <section class="build-result">
            <h2>Build</h2>
            <p class="overall">@build.Report?.OverallResult</p>
            <p class="folder">@build.Folder</p>

            @foreach (var file in build.Written)
            {
                <p class="written">@file</p>
            }

            @foreach (var item in build.Withheld)
            {
                <p class="withheld">@item</p>
            }

            @if (build.Failure is not null)
            {
                <p class="build-failure">The package is incomplete: @build.Failure</p>
            }
        </section>
    }
```

And the handlers:

```razor
    private BuildRunResult? build;

    private void GoToImport() => Nav.NavigateTo("/import");

    private void RunBuild()
    {
        if (State.Draft is null || State.Folder is null) return;

        var repoRoot = Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

        build = BuildRunner.Run(State.Draft, State.Folder, settler, State.Engine, State.RegistryCsvPath, repoRoot);
    }
```

Add `@inject NavigationManager Nav` and the `System.IO` and `Registrar.Core.Config` usings if the file does not already have them. `settler` is the existing `DraftSettler` field the component already holds; confirm its real name before using it.

Then honor the import save flag in `OnInitialized`. It currently calls `Settle(save: false)`, which is right for opening a colleague's draft and wrong for one that arrived already changed:

```razor
    protected override void OnInitialized()
    {
        Rebuild();

        // Opening does not write, so that merely looking at a draft does not rewrite it. An
        // accepted import is the exception: it reaches the editor already changed, and the
        // operator may close the window without touching a key.
        var needsSave = State.DraftNeedsSave;
        State.DraftNeedsSave = false;
        Settle(save: needsSave);
    }
```

Match the real signature of the existing settle call rather than assuming `save:` is its parameter name; read the method first.

Add to `src/Registrar.App/wwwroot/app.css`:

```css
.build-result { border: 1px solid var(--org-border-neutral); padding: 1rem; margin-bottom: 1rem; }
.build-result .written { color: var(--org-primary-blue); }
.build-result .withheld { color: var(--org-secondary-gray); }
.build-result .build-failure { color: var(--org-danger); }
```

- [ ] **Step 5: Update the GUI-7 test that pinned the not-wired state**

`ValidationRailComponentTests.Build_stays_disabled_and_says_so_while_it_is_not_wired_up` asserts behavior this task deliberately changes. It tested `EditorHeader` with `BuildWired` defaulted to false, which is still a real state of that component, so **keep the test and keep it passing**: it now documents what the header does when a caller has not wired Build, which remains true. Do not delete it, and do not change its assertions.

If it fails, the header's default changed, which it should not: `BuildWired` stays `false` by default and `Editor.razor` opts in.

- [ ] **Step 6: Run the tests**

Run: `dotnet test Registrar.sln --filter FullyQualifiedName~EditorShellComponentTests`
Run: `dotnet test Registrar.sln --filter FullyQualifiedName~ValidationRailComponentTests`
Expected: both green, including the GUI-7 test unchanged.

- [ ] **Step 7: Full suite, both configurations, then commit**

```bash
git add src/Registrar.App tests/Registrar.App.Tests
git commit -m "feat: Build produces a document and Import is reachable"
```

---

### Task 8: The documentation this unit owes

**Files:**
- Modify: `docs/superpowers/specs/2026-08-01-operator-gui-design.md`
- Modify: `docs/ROADMAP.md`

- [ ] **Step 1: Amend the parent design**

Under `### Amendments`, add two bullets in the voice of the existing ones, dated 2026-08-04 and attributed to GUI-8:

- `ImportParser` returns an entire `AgrDraft`, not `sections[]` as the Data flows section describes. Importing into an open draft is therefore a merge, and identity carried by a reply is reported rather than applied, because a chat reply must not move a Document ID that Registry Owner issued.
- A GUI build writes its output package into a per-artifact subfolder rather than flat. Four of the five output filenames are fixed, an engagement folder holds many artifacts, and `EngagementScan` globs `*.json` non-recursively, so a flat build would both overwrite an earlier build's evidence and add four unreadable rows to the operator's artifact list per build. The CLI's flat layout is unchanged.

- [ ] **Step 2: Update the roadmap**

Set the GUI-8 row's status to `**Done** (PR #NN)`, leaving the literal `PR #NN` for the controller to substitute. Then update the prose line naming what is delivered so it reads that GUI-1 through GUI-8 are complete and the operator GUI is finished as scoped. Read the surrounding prose and match its voice.

- [ ] **Step 3: Verify**

Run: `dotnet test Registrar.sln --configuration Release`
Expected: unchanged from Task 7. Nothing here touches code.

Run the ASCII scan over everything this branch changed:

```bash
git diff --name-only origin/main...HEAD | while read -r f; do
  [ -f "$f" ] && LC_ALL=C grep -Hn '[^ -~]' "$f"
done
```

Expected: no output.

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs: record the GUI-8 amendments and close the operator GUI roadmap"
```

---

## Definition of done

- `dotnet test Registrar.sln` and `dotnet test Registrar.sln --configuration Release` both green, zero warnings.
- **`BuildCommandTests` has 27 passing tests and zero edits.** `BuildOutputFileSetTests` passes unedited since Task 1. `git diff origin/main...HEAD -- tests/Registrar.Tests/BuildCommandTests.cs` is empty.
- A technician can open an artifact, press Import, paste a Governor reply, see what arrived before anything changes, accept it, and press Build to produce a document.
- A build writes one subfolder per artifact containing the AGR intermediate and the output package, and adds nothing to the engagement folder's artifact list.
- A reply carrying a different Document ID reports it and does not apply it.
- The emit-when rules for the five output files exist in exactly one place, `OutputPackage`, and both the CLI and the GUI call it.
