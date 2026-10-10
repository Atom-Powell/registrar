# App Shell and Engagement Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Registrar its first window: a WPF host running a Blazor Engagement screen that answers "what does this client still owe."

**Architecture:** All behavior lives in `Registrar.Core`, because a `net8.0` test project cannot reference a `net8.0-windows` one and everything in the app would otherwise be untestable. `EngagementScan` classifies a folder's artifacts; `Registrar.App` is a WPF shell hosting one Razor component that renders the result and enforces nothing.

**Tech Stack:** .NET 8, C#, WPF (`net8.0-windows`), `Microsoft.AspNetCore.Components.WebView.Wpf`, bUnit, xUnit.

## Global Constraints

These bind every task. Copied from `CLAUDE.md` and the design spec.

- **US English.** Write "color", never "colour". The repo has zero instances of the British form and rule 8 requires US English. This has already had to be corrected once in this project.
- **No emojis, no em dashes, no en dashes, no decorative unicode** anywhere: source, markup, comments, test names, commit messages. Use a plain hyphen, straight quotes, and three periods.
- **No non-ASCII character as a literal** in engine or test source. `SourceHygieneTests` enforces this. Build one from a code point if you need it, never a `\uXXXX` escape, because tooling here has been observed rewriting escapes into literals.
- **`Registrar.App` enforces no rules of its own.** Everything it displays comes from Core or Validation. If you find yourself writing a condition in a Razor file that decides something about an artifact, it belongs in Core.
- **Not MAUI.** The Blazor Hybrid host is `Microsoft.AspNetCore.Components.WebView.Wpf`. The MAUI workload must not be pulled in.
- **Zero LLM calls.** Nothing here contacts a model.
- **Configuration over code.** No brand color, artifact constant, or clause text in `src/`.
- **The app never silently degrades a governance control.** A file that cannot be read is shown as `Unreadable`, never omitted.
- Run the full suite with `dotnet test Registrar.sln` from the repo root. It must be green with no new warnings. CI runs `dotnet build Registrar.sln` then `dotnet test`, on `windows-latest`.

---

## File Structure

**Create:**
- `src/Registrar.Core/Engagement/ArtifactState.cs` - the four states. Enum only.
- `src/Registrar.Core/Engagement/EngagementRow.cs` - one row. Record only.
- `src/Registrar.Core/Engagement/EngagementScan.cs` - discovery, reading, classification.
- `src/Registrar.Core/Engagement/DraftFile.cs` - the `.agrdraft` format, read and write, defined once.
- `src/Registrar.Core/WebViewEnvironment.cs` - the user-data folder path. Pure.
- `src/Registrar.App/` - WPF host, one Razor component. No logic.
- `tests/Registrar.App.Tests/` - bUnit harness plus one component test.
- `tests/Registrar.Tests/Engagement/EngagementScanTests.cs`
- `tests/Registrar.Tests/Engagement/DraftFileTests.cs`
- `tests/Registrar.Tests/WebViewEnvironmentTests.cs`

**Modify:**
- `src/Registrar.Cli/Doctor.cs` - add the user-data folder check.
- `tests/Registrar.Tests/DoctorTests.cs` - cover it.
- `Registrar.sln` - add the two new projects.
- `scripts/publish.ps1` - publish the app into the same `runtime/` folder.
- `docs/ROADMAP.md`, `docs/DELIVERED.md` - record GUI-4 and GUI-5.

---

### Task 1: DraftFile and the .agrdraft format

**Files:**
- Create: `src/Registrar.Core/Engagement/DraftFile.cs`
- Test: `tests/Registrar.Tests/Engagement/DraftFileTests.cs`

**Interfaces:**
- Consumes: `AgrDraft` from `Registrar.Core.Model`, `AgrSerializer.Options` from `Registrar.Core`.
- Produces: `DraftFile.Extension` (`".agrdraft"`), `DraftFile.Write(string path, AgrDraft draft)`, `DraftFile.Read(string path) -> AgrDraft?` returning null when the file cannot be read as a draft. Task 2 consumes all three.

**Context you need.** The parent design chose `.agrdraft` rather than `.agr-draft.json` deliberately: `AgrPicker` globs `*.json`, so anything ending in `.json` shows up in the CLI's operator menu as a selectable AGR, and a distinct extension keeps drafts out of it with no change to `AgrPicker`.

Nothing writes a draft yet; GUI-7 will. `Write` exists now because the format must be defined in exactly one place, and because this task's own tests need to produce a draft file to read back. A format defined inside a test is a format that drifts the moment production code writes one.

Use `AgrSerializer.Options` for both directions so the round trip is symmetric. Do not invent a second options instance.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Engagement/DraftFileTests.cs`:

```csharp
using Registrar.Core.Engagement;
using Registrar.Core.Model;
using Xunit;

namespace Registrar.Tests.Engagement;

public class DraftFileTests
{
    private static string TempDir()
        => Directory.CreateTempSubdirectory("registrar-draftfile-").FullName;

    [Fact]
    public void The_extension_keeps_drafts_out_of_the_json_glob()
    {
        // AgrPicker globs *.json. A draft must not match it.
        Assert.Equal(".agrdraft", DraftFile.Extension);
        Assert.False(DraftFile.Extension.EndsWith(".json", StringComparison.Ordinal));
    }

    [Fact]
    public void A_draft_survives_a_write_then_read()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "sample" + DraftFile.Extension);
            var original = new AgrDraft
            {
                ArtifactType = "DS",
                DocumentId = "ORG-GOV-DS-016",
                Client = "Talewind CPA",
                Owner = "Registry Owner"
            };

            DraftFile.Write(path, original);
            var read = DraftFile.Read(path);

            Assert.NotNull(read);
            Assert.Equal("DS", read!.ArtifactType);
            Assert.Equal("ORG-GOV-DS-016", read.DocumentId);
            Assert.Equal("Talewind CPA", read.Client);
            Assert.Equal("Registry Owner", read.Owner);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_corrupt_draft_reads_as_null_rather_than_throwing()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "broken" + DraftFile.Extension);
            File.WriteAllText(path, "{ this is not json");

            Assert.Null(DraftFile.Read(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_draft_holding_valid_json_that_is_not_an_object_reads_as_null()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "array" + DraftFile.Extension);
            File.WriteAllText(path, "[1, 2, 3]");

            Assert.Null(DraftFile.Read(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_missing_file_reads_as_null()
    {
        Assert.Null(DraftFile.Read(Path.Combine(TempDir(), "absent" + DraftFile.Extension)));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~DraftFileTests"`
Expected: FAIL to compile, "The type or namespace name 'Engagement' does not exist".

- [ ] **Step 3: Implement**

Create `src/Registrar.Core/Engagement/DraftFile.cs`:

```csharp
using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core.Engagement;

// The on-disk form of a work-in-progress artifact.
//
// The extension is deliberately not ".json". AgrPicker globs "*.json" to build the
// CLI's operator menu, so a draft ending in .json would appear there as a selectable
// AGR. A distinct extension keeps drafts invisible to it without changing AgrPicker.
//
// Read and Write live together so the format is defined once. Nothing writes a draft
// in this unit; GUI-7 does. Write exists now because a format first defined inside a
// test drifts the moment production code writes one.
public static class DraftFile
{
    public const string Extension = ".agrdraft";

    public static void Write(string path, AgrDraft draft)
        => File.WriteAllText(path, JsonSerializer.Serialize(draft, AgrSerializer.Options));

    // Returns null rather than throwing for every failure a folder scan can meet: the
    // file is gone, it is not JSON, or it is JSON of the wrong shape. The caller turns
    // null into an Unreadable row, which is what the operator needs to see.
    public static AgrDraft? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<AgrDraft>(File.ReadAllText(path), AgrSerializer.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS.

If `A_missing_file_reads_as_null` fails with `FileNotFoundException` escaping, note that `FileNotFoundException` and `DirectoryNotFoundException` both derive from `IOException`, so the existing catch covers them; investigate rather than adding a broad `catch (Exception)`.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Engagement/DraftFile.cs tests/Registrar.Tests/Engagement/DraftFileTests.cs
git commit -m "feat: define the agrdraft file format"
```

---

### Task 2: EngagementScan

**Files:**
- Create: `src/Registrar.Core/Engagement/ArtifactState.cs`
- Create: `src/Registrar.Core/Engagement/EngagementRow.cs`
- Create: `src/Registrar.Core/Engagement/EngagementScan.cs`
- Test: `tests/Registrar.Tests/Engagement/EngagementScanTests.cs`

**Interfaces:**
- Consumes: `DraftFile.Extension`, `DraftFile.Read` from Task 1. `ImportParser.Parse(string pastedText, ConfigBundle config) -> ImportResult` with a nullable `Draft`, from `Registrar.Core.Import`. `AgrAuthoring.ToDocument(AgrDraft draft, ConfigBundle config, JsonNode agrSchema, IClock clock) -> AuthoringResult` with `Document` and `Gaps`, from `Registrar.Core.Authoring`.
- Produces: `EngagementScan.Run(string folder, ConfigBundle config, JsonNode agrSchema, IClock clock) -> IReadOnlyList<EngagementRow>`. Task 4 consumes it.

**Context you need, and the two things most likely to go wrong.**

`ImportParser.Parse` was built in GUI-4 to recover an AGR from a chat paste. It is reused here because it is exactly a tolerant reader: it never throws and returns a draft or null. Do not use `AgrParser.Parse`, which throws on a missing required member and would take the whole screen down on the first malformed file in a folder.

**A built folder contains output files that are not AGRs.** After a build, the folder holds `validation-report.json`, `artifact-manifest.json`, `generation-log.json` and `promotion-payload.json`. `AgrPicker` globs `*.json` with no filter and therefore already lists those as selectable AGRs in the CLI menu; that is a pre-existing defect and is not yours to fix, but the scan must not repeat it.

Exclude a `.json` file when **either** test says it is not an AGR: the file name is one of those four, or the parsed root object has no `artifactIdentity` member. Two tests because the name check is obvious and cheap while the structural check survives a renamed output file.

**A file too malformed for the structural test is kept, not excluded.** It reaches the reader and becomes `Unreadable`. Hiding a broken AGR from the one screen that reports what is outstanding is the silent degradation the design forbids.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Engagement/EngagementScanTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Engagement;
using Registrar.Core.Model;
using Xunit;

namespace Registrar.Tests.Engagement;

public class EngagementScanTests
{
    private static string TempDir()
        => Directory.CreateTempSubdirectory("registrar-engagement-").FullName;

    private static JsonNode Schema()
        => JsonNode.Parse(File.ReadAllText(
            ConfigLocator.FindSchema(SemanticTestSupport.RepoRoot(), "agr.schema.json")))!;

    private static IReadOnlyList<EngagementRow> Scan(string folder)
        => EngagementScan.Run(folder, SemanticTestSupport.Config(), Schema(), new FixedClock());

    // A complete document, written as a .json AGR, is the fixture the CLI already ships.
    private static AgrDocument CompleteDocument()
        => SemanticTestSupport.Agr("agr-sample-ds-draft.json");

    [Fact]
    public void An_empty_folder_yields_no_rows()
    {
        var dir = TempDir();
        try { Assert.Empty(Scan(dir)); }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_missing_folder_yields_no_rows_rather_than_throwing()
    {
        Assert.Empty(Scan(Path.Combine(TempDir(), "does-not-exist")));
    }

    // The rule AgrPicker gets wrong. All four output names, and nothing else.
    [Fact]
    public void Build_output_files_are_not_listed_as_artifacts()
    {
        var dir = TempDir();
        try
        {
            foreach (var name in new[]
            {
                "validation-report.json",
                "artifact-manifest.json",
                "generation-log.json",
                "promotion-payload.json"
            })
            {
                File.WriteAllText(Path.Combine(dir, name), "{\"checks\": []}");
            }

            Assert.Empty(Scan(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_json_file_without_artifactIdentity_is_not_listed()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "notes.json"), "{\"something\": \"else\"}");

            Assert.Empty(Scan(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Kept rather than excluded: the structural test cannot run, so it must be shown.
    [Fact]
    public void A_json_file_too_malformed_to_classify_is_listed_as_unreadable()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json at all");

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Unreadable, row.State);
            Assert.Equal("broken.json", row.FileName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_corrupt_draft_is_listed_as_unreadable()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "wip" + DraftFile.Extension), "{ broken");

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Unreadable, row.State);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_incomplete_draft_is_listed_as_draft_and_says_what_is_missing()
    {
        var dir = TempDir();
        try
        {
            DraftFile.Write(
                Path.Combine(dir, "wip" + DraftFile.Extension),
                new AgrDraft { ArtifactType = "DS" });

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Draft, row.State);
            Assert.Equal("DS", row.ArtifactType);
            Assert.NotEmpty(row.Detail);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_complete_artifact_with_no_docx_is_ready()
    {
        var dir = TempDir();
        try
        {
            var document = CompleteDocument();
            File.WriteAllText(Path.Combine(dir, "ds.json"), AgrSerializer.ToJson(document));

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Ready, row.State);
            Assert.Equal("DS", row.ArtifactType);
            Assert.Equal(document.Identity.DocumentId, row.DocumentId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_complete_artifact_whose_docx_exists_is_built()
    {
        var dir = TempDir();
        try
        {
            var document = CompleteDocument();
            File.WriteAllText(Path.Combine(dir, "ds.json"), AgrSerializer.ToJson(document));
            File.WriteAllText(
                Path.Combine(dir, document.RenderingOptions.OutputFileName), "not a real docx");

            var row = Assert.Single(Scan(dir));
            Assert.Equal(ArtifactState.Built, row.State);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Every_row_carries_a_detail_line()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ds.json"), AgrSerializer.ToJson(CompleteDocument()));
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ nope");
            DraftFile.Write(Path.Combine(dir, "wip" + DraftFile.Extension), new AgrDraft());

            var rows = Scan(dir);

            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Detail)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rows_are_ordered_by_file_name_so_the_screen_is_stable()
    {
        var dir = TempDir();
        try
        {
            foreach (var name in new[] { "c.json", "a.json", "b.json" })
                File.WriteAllText(Path.Combine(dir, name), AgrSerializer.ToJson(CompleteDocument()));

            var rows = Scan(dir);

            Assert.Equal(new[] { "a.json", "b.json", "c.json" }, rows.Select(r => r.FileName));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
```

`FixedClock` already exists as a **private nested class** in both `AgrAuthoringTests` and `BuildCommandTests`, so it cannot be reused from here. Follow the same convention and declare a private nested `FixedClock : IClock` inside `EngagementScanTests`, matching how those two files do it. Do not promote the existing ones to a shared type: that would drag two unrelated test files into this task for a cleanup nobody asked for. The duplication is pre-existing and belongs in tech debt, not in this diff.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~EngagementScanTests"`
Expected: FAIL to compile, "The name 'EngagementScan' does not exist".

- [ ] **Step 3: Create the two value types**

Create `src/Registrar.Core/Engagement/ArtifactState.cs`:

```csharp
namespace Registrar.Core.Engagement;

public enum ArtifactState
{
    // The draft is missing at least one required value.
    Draft,

    // Every required value is present, so the artifact converts to a document. This
    // does not claim validation passes: Build still runs the real layers and can still
    // refuse. It claims only that nothing is missing.
    Ready,

    // A document of the expected output file name is present in the folder.
    Built,

    // The file could not be read. Shown rather than omitted, because hiding a broken
    // artifact from the screen that reports what is outstanding is exactly the silent
    // degradation the design forbids.
    Unreadable
}
```

Create `src/Registrar.Core/Engagement/EngagementRow.cs`:

```csharp
namespace Registrar.Core.Engagement;

// One artifact in an engagement folder. Detail is always populated so a row is
// actionable without opening the artifact.
public sealed record EngagementRow(
    string FileName,
    string? ArtifactType,
    string? DocumentId,
    string? Client,
    ArtifactState State,
    string Detail);
```

- [ ] **Step 4: Implement the scan**

Create `src/Registrar.Core/Engagement/EngagementScan.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Import;
using Registrar.Core.Model;

namespace Registrar.Core.Engagement;

// Classifies the artifacts in an engagement folder so the Engagement screen can answer
// "what does this client still owe".
//
// This lives in Core rather than in the app for a concrete reason: a net8.0 test
// project cannot reference a net8.0-windows one, so logic placed in Registrar.App
// would be unreachable by the suite. Keeping it here is also what lets the app enforce
// no rules of its own.
public static class EngagementScan
{
    // A built folder carries these beside the artifacts. They are not AGRs, and
    // AgrPicker's failure to exclude them is why the CLI menu offers them.
    private static readonly HashSet<string> OutputFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "validation-report.json",
        "artifact-manifest.json",
        "generation-log.json",
        "promotion-payload.json"
    };

    public static IReadOnlyList<EngagementRow> Run(
        string folder, ConfigBundle config, JsonNode agrSchema, IClock clock)
    {
        if (!Directory.Exists(folder)) return Array.Empty<EngagementRow>();

        var rows = new List<EngagementRow>();

        foreach (var path in Directory.EnumerateFiles(folder, "*" + DraftFile.Extension))
            rows.Add(Classify(path, DraftFile.Read(path), folder, config, agrSchema, clock));

        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            if (!LooksLikeAgr(path)) continue;

            var draft = ImportParser.Parse(File.ReadAllText(path), config).Draft;
            rows.Add(Classify(path, draft, folder, config, agrSchema, clock));
        }

        return rows.OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Two tests, either of which excludes the file. The name check is cheap and covers
    // the common case; the structural check still holds when an output file has been
    // renamed. A file too malformed for the structural test is KEPT, so it reaches the
    // reader and is reported as Unreadable rather than disappearing.
    private static bool LooksLikeAgr(string path)
    {
        if (OutputFileNames.Contains(Path.GetFileName(path))) return false;

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root
                || root["artifactIdentity"] is not null;
        }
        catch (JsonException)
        {
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static EngagementRow Classify(
        string path,
        AgrDraft? draft,
        string folder,
        ConfigBundle config,
        JsonNode agrSchema,
        IClock clock)
    {
        var name = Path.GetFileName(path);

        if (draft is null)
        {
            return new EngagementRow(name, null, null, null, ArtifactState.Unreadable,
                "Could not be read. Open it to see what is wrong, or ask for it again.");
        }

        var result = AgrAuthoring.ToDocument(draft, config, agrSchema, clock);

        if (result.Document is null)
        {
            var first = result.Gaps.Count > 0 ? result.Gaps[0].Message : "Not yet complete.";
            return new EngagementRow(name, draft.ArtifactType, draft.DocumentId, draft.Client,
                ArtifactState.Draft,
                $"{result.Gaps.Count} field(s) still to fill. First: {first}");
        }

        var document = result.Document;
        var outputPath = Path.Combine(folder, document.RenderingOptions.OutputFileName);

        return File.Exists(outputPath)
            ? new EngagementRow(name, document.Identity.ArtifactType, document.Identity.DocumentId,
                document.Context.Client, ArtifactState.Built,
                $"Built as {document.RenderingOptions.OutputFileName}")
            : new EngagementRow(name, document.Identity.ArtifactType, document.Identity.DocumentId,
                document.Context.Client, ArtifactState.Ready,
                "Complete and ready to build.");
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS.

If `A_complete_artifact_with_no_docx_is_ready` reports `Draft` instead, the fixture is producing gaps. Print `result.Gaps` and report which ones rather than adjusting the assertion: a committed fixture that will not convert is a finding about `AgrAuthoring`, not about this test.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Core/Engagement/ tests/Registrar.Tests/Engagement/EngagementScanTests.cs
git commit -m "feat: classify the artifacts in an engagement folder"
```

---

### Task 3: The WebView2 user-data folder and its doctor check

**Files:**
- Create: `src/Registrar.Core/WebViewEnvironment.cs`
- Create: `tests/Registrar.Tests/WebViewEnvironmentTests.cs`
- Modify: `src/Registrar.Cli/Doctor.cs`
- Modify: `tests/Registrar.Tests/DoctorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `WebViewEnvironment.UserDataFolder(string localAppDataRoot) -> string`. Task 4 consumes it.

**Context you need.** WebView2 defaults its user-data folder to a directory beside the executable. If the app is extracted somewhere read-only, or run from a network path, it fails to start and the window comes up blank with no explanation. The parent spec records this as the platform's one real trap and asks for both a fix in the app and a check in `doctor`.

Splitting it by testability is the whole point: computing the path is pure and gets a test; setting the environment variable is one line in the app where nothing is left to get wrong.

**Scope note, deliberate.** `doctor` checks that the user-data folder is writable. It does **not** check that the WebView2 runtime is installed. Runtime detection would mean pulling a WebView2 package into `Registrar.Cli`, which targets `net8.0`, for a condition the parent spec already records as verified on ORG workstations. The writable-folder half is the failure mode the spec actually describes. Do not add the runtime check.

`Doctor.Run` builds a `List<DoctorCheck>` and each check is a `DoctorCheck(string Name, DoctorStatus Status, string Detail)`. Statuses are `Ok`, `Info`, `Warn`, `Fail`. Read `src/Registrar.Cli/Doctor.cs` before editing so your check matches the surrounding style.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/WebViewEnvironmentTests.cs`:

```csharp
using Registrar.Core;
using Xunit;

namespace Registrar.Tests;

public class WebViewEnvironmentTests
{
    [Fact]
    public void The_folder_sits_under_the_given_root()
    {
        var path = WebViewEnvironment.UserDataFolder(Path.Combine("C:", "Users", "t", "AppData", "Local"));

        Assert.StartsWith(Path.Combine("C:", "Users", "t", "AppData", "Local"), path, StringComparison.Ordinal);
    }

    [Fact]
    public void The_folder_is_named_for_this_application()
    {
        var path = WebViewEnvironment.UserDataFolder(Path.Combine("C:", "root"));

        Assert.Contains("Registrar", path, StringComparison.Ordinal);
    }

    // A trailing separator on the root is a real shape: Environment.GetFolderPath can
    // return one, and naive concatenation would produce a doubled separator.
    [Fact]
    public void A_trailing_separator_on_the_root_does_not_double_up()
    {
        var withSlash = WebViewEnvironment.UserDataFolder(Path.Combine("C:", "root") + Path.DirectorySeparatorChar);
        var without = WebViewEnvironment.UserDataFolder(Path.Combine("C:", "root"));

        Assert.Equal(without, withSlash);
    }

    [Fact]
    public void An_empty_root_is_rejected_rather_than_producing_a_relative_path()
    {
        Assert.Throws<ArgumentException>(() => WebViewEnvironment.UserDataFolder(""));
    }
}
```

Append to the existing class in `tests/Registrar.Tests/DoctorTests.cs`:

```csharp
    [Fact]
    public void Doctor_reports_the_webview_user_data_folder_as_writable()
    {
        var report = Doctor.Run(SemanticTestSupport.RepoRoot(), registryPath: null);

        var check = Assert.Single(report.Checks, c => c.Name == "webview user data");
        Assert.Equal(DoctorStatus.Ok, check.Status);
    }
```

Read the top of `DoctorTests.cs` first and match whatever `using` directives and helper it already uses to call `Doctor.Run`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~WebViewEnvironmentTests|FullyQualifiedName~DoctorTests"`
Expected: FAIL to compile, "The name 'WebViewEnvironment' does not exist".

- [ ] **Step 3: Implement the path helper**

Create `src/Registrar.Core/WebViewEnvironment.cs`:

```csharp
namespace Registrar.Core;

// WebView2 defaults its user-data folder to a directory beside the executable. When the
// application is extracted somewhere read-only, or run from a network path, that
// directory cannot be created and the window comes up blank with no error. Pointing it
// at the user's local application data instead is the documented fix.
//
// Only the path calculation lives here. Setting the environment variable is one line in
// Registrar.App, because that line cannot be exercised by a net8.0 test project and
// there is nothing in it left to get wrong once the path is proven.
public static class WebViewEnvironment
{
    public const string VariableName = "WEBVIEW2_USER_DATA_FOLDER";

    private const string FolderName = "Registrar";
    private const string SubFolderName = "WebView2";

    public static string UserDataFolder(string localAppDataRoot)
    {
        if (string.IsNullOrWhiteSpace(localAppDataRoot))
        {
            throw new ArgumentException(
                "A local application data root is required; an empty one would resolve to a "
                    + "relative path beside the executable, which is the failure this avoids.",
                nameof(localAppDataRoot));
        }

        return Path.Combine(localAppDataRoot, FolderName, SubFolderName);
    }
}
```

`Path.Combine` already collapses a trailing separator on the first segment, which is what the trailing-separator test asserts.

- [ ] **Step 4: Add the doctor check**

In `src/Registrar.Cli/Doctor.cs`, add this private method beside the other check builders:

```csharp
    // The WebView2 user-data folder is where a read-only or network install fails, and
    // it fails as a blank window with no message. Checking it here means a technician
    // whose window will not open has a command that says why.
    private static DoctorCheck WebViewUserData()
    {
        string folder;
        try
        {
            folder = WebViewEnvironment.UserDataFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }
        catch (ArgumentException ex)
        {
            return new DoctorCheck("webview user data", DoctorStatus.Fail,
                $"cannot determine the user data folder: {ex.Message}");
        }

        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".registrar-write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return new DoctorCheck("webview user data", DoctorStatus.Ok, $"writable: {folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DoctorCheck("webview user data", DoctorStatus.Fail,
                $"not writable, so the app window will not open: {folder} ({ex.Message})");
        }
    }
```

Then add `checks.Add(WebViewUserData());` to `Doctor.Run`, immediately before the `return new DoctorReport(checks);` line, so it reports last.

Add `using Registrar.Core;` at the top of the file if it is not already there.

- [ ] **Step 5: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS.

- [ ] **Step 6: Confirm doctor still reports healthy end to end**

Run: `dotnet run --project src/Registrar.Cli -- doctor`
Expected: the new `webview user data` line appears with `OK`, and the command still ends `doctor: healthy`. Paste that output into your report.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/WebViewEnvironment.cs src/Registrar.Cli/Doctor.cs tests/Registrar.Tests/WebViewEnvironmentTests.cs tests/Registrar.Tests/DoctorTests.cs
git commit -m "feat: check the WebView2 user data folder is writable"
```

---

### Task 4: The Registrar.App shell and the Engagement screen

**Files:**
- Create: `src/Registrar.App/Registrar.App.csproj`
- Create: `src/Registrar.App/App.xaml`, `src/Registrar.App/App.xaml.cs`
- Create: `src/Registrar.App/MainWindow.xaml`, `src/Registrar.App/MainWindow.xaml.cs`
- Create: `src/Registrar.App/Components/Routes.razor`, `src/Registrar.App/Components/Engagement.razor`
- Create: `src/Registrar.App/wwwroot/index.html`
- Create: `src/Registrar.App/_Imports.razor`
- Modify: `Registrar.sln`

**Interfaces:**
- Consumes: `EngagementScan.Run` and `EngagementRow` from Task 2, `WebViewEnvironment.UserDataFolder` and `WebViewEnvironment.VariableName` from Task 3.
- Produces: `Engagement.razor` as a component, consumed by Task 5's bUnit test. It must accept its rows as a parameter rather than scanning internally, or the test cannot drive it.

**Context you need.**

This project targets `net8.0-windows` with `UseWPF`. That is why nothing testable may live here: `Registrar.Tests` targets `net8.0` and cannot reference it.

The Blazor Hybrid host package is `Microsoft.AspNetCore.Components.WebView.Wpf`. **It is not MAUI and the MAUI workload must not be pulled in.** If you find yourself installing a workload, stop and report.

`wwwroot/index.html` must reference the generated stylesheet at `config/generated/org-tokens.css`, produced by `registrar emit-tokens` in the previous unit, so the screen is on brand because it reads the brand rather than because it was decorated. Work out the correct relative path from where the app runs and say in your report how you resolved it; if a relative reference cannot work, report that rather than hardcoding a color.

**The component takes its rows as a parameter.** `Engagement.razor` must declare `[Parameter] public IReadOnlyList<EngagementRow> Rows { get; set; }` and render what it is given. The folder picking and the call to `EngagementScan.Run` belong in the page that hosts it. A component that scans a folder internally cannot be tested, and Task 5 depends on this.

- [ ] **Step 1: Create the project and add it to the solution**

```bash
dotnet new wpf -o src/Registrar.App -n Registrar.App --framework net8.0
dotnet sln Registrar.sln add src/Registrar.App/Registrar.App.csproj
dotnet add src/Registrar.App/Registrar.App.csproj package Microsoft.AspNetCore.Components.WebView.Wpf
dotnet add src/Registrar.App/Registrar.App.csproj reference src/Registrar.Core/Registrar.Core.csproj
dotnet add src/Registrar.App/Registrar.App.csproj reference src/Registrar.Validation/Registrar.Validation.csproj
```

Then open `src/Registrar.App/Registrar.App.csproj` and confirm `TargetFramework` is `net8.0-windows`, `UseWPF` is `true`, and `Nullable` is `enable` to match the other projects. Add `Nullable` if the template omitted it.

- [ ] **Step 2: Verify the solution still builds and the suite is untouched**

Run: `dotnet build Registrar.sln` then `dotnet test Registrar.sln`
Expected: build succeeds, tests unchanged in count from Task 3.

This step exists because adding a `net8.0-windows` project to a solution the CI builds is the single riskiest moment in this unit. If the build breaks, stop and report before writing any UI.

- [ ] **Step 3: Set the WebView2 user-data folder before anything starts**

Replace the body of `src/Registrar.App/App.xaml.cs` with:

```csharp
using System.Windows;
using Registrar.Core;

namespace Registrar.App;

public partial class App : Application
{
    // Must run before any BlazorWebView initializes. WebView2 otherwise puts its
    // user-data folder beside the executable, and a read-only or network install then
    // fails to start with a blank window and no message. The path itself is computed
    // and tested in Registrar.Core; this is the one line that cannot be.
    protected override void OnStartup(StartupEventArgs e)
    {
        Environment.SetEnvironmentVariable(
            WebViewEnvironment.VariableName,
            WebViewEnvironment.UserDataFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

        base.OnStartup(e);
    }
}
```

- [ ] **Step 4: Write the shell and the screen**

Create `src/Registrar.App/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Components.Web
@using Registrar.Core.Engagement
```

Create `src/Registrar.App/wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Registrar</title>
    <base href="/" />
    <link rel="stylesheet" href="org-tokens.css" />
</head>
<body>
    <div id="app">Loading...</div>
    <div id="blazor-error-ui">
        An unexpected error has occurred. Reload to continue.
    </div>
    <script src="_framework/blazor.webview.js" autostart="false"></script>
</body>
</html>
```

Copy the generated stylesheet into `wwwroot` as part of the build by adding this to `Registrar.App.csproj` inside a `PropertyGroup`-sibling `ItemGroup`:

```xml
  <ItemGroup>
    <Content Include="..\..\config\generated\org-tokens.css"
             Link="wwwroot\org-tokens.css"
             CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

Create `src/Registrar.App/Components/Engagement.razor`:

```razor
@* Renders what it is given and decides nothing. Every judgement about an artifact was
   made by EngagementScan in Registrar.Core, which is where it can be tested. *@

<h1>Engagement</h1>

@if (Rows.Count == 0)
{
    <p class="empty">No artifacts in this folder yet.</p>
}
else
{
    <table>
        <thead>
            <tr>
                <th>Artifact</th>
                <th>Type</th>
                <th>Document ID</th>
                <th>Client</th>
                <th>State</th>
                <th>Detail</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var row in Rows)
            {
                <tr>
                    <td>@row.FileName</td>
                    <td>@row.ArtifactType</td>
                    <td>@row.DocumentId</td>
                    <td>@row.Client</td>
                    <td class="state">@row.State</td>
                    <td>@row.Detail</td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    [Parameter]
    public IReadOnlyList<EngagementRow> Rows { get; set; } = Array.Empty<EngagementRow>();
}
```

Create `src/Registrar.App/Components/Routes.razor`:

```razor
@using Microsoft.AspNetCore.Components.Routing

<Router AppAssembly="@typeof(Routes).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" />
    </Found>
    <NotFound>
        <p>Not found.</p>
    </NotFound>
</Router>
```

Create `src/Registrar.App/Components/Home.razor`, which owns the folder and the scan so the component above does not:

```razor
@page "/"
@using System.Text.Json.Nodes
@using Registrar.Core
@using Registrar.Core.Config

<div class="toolbar">
    <input @bind="folder" placeholder="Engagement folder" />
    <button @onclick="Rescan">Open</button>
</div>

@if (error is not null)
{
    <p class="error">@error</p>
}

<Engagement Rows="@rows" />

@code {
    private string folder = string.Empty;
    private IReadOnlyList<EngagementRow> rows = Array.Empty<EngagementRow>();
    private string? error;

    private void Rescan()
    {
        error = null;
        try
        {
            var repoRoot = Directory.GetParent(
                Path.GetDirectoryName(
                    ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

            var config = ConfigLoader.Load(repoRoot);
            var schema = JsonNode.Parse(
                File.ReadAllText(ConfigLocator.FindSchema(repoRoot, "agr.schema.json")))!;

            rows = EngagementScan.Run(folder, config, schema, new SystemClock());
        }
        catch (Exception ex)
        {
            rows = Array.Empty<EngagementRow>();
            error = ex.Message;
        }
    }
}
```

`SystemClock` in `Registrar.Core` is the real implementation of `IClock`, confirmed, so `new SystemClock()` is correct as written.

Replace `src/Registrar.App/MainWindow.xaml` with a window hosting the view:

```xml
<Window x:Class="Registrar.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:blazor="clr-namespace:Microsoft.AspNetCore.Components.WebView.Wpf;assembly=Microsoft.AspNetCore.Components.WebView.Wpf"
        xmlns:local="clr-namespace:Registrar.App"
        Title="Registrar" Height="720" Width="1100">
    <Grid>
        <blazor:BlazorWebView HostPage="wwwroot\index.html" Services="{DynamicResource services}">
            <blazor:RootComponent Selector="#app" ComponentType="{x:Type local:Components.Routes}" />
        </blazor:BlazorWebView>
    </Grid>
</Window>
```

Wire the service collection in `MainWindow.xaml.cs`:

```csharp
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Registrar.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        Resources.Add("services", services.BuildServiceProvider());

        InitializeComponent();
    }
}
```

- [ ] **Step 5: Build and run it**

Run: `dotnet build Registrar.sln`
Expected: succeeds with no warnings.

Then: `dotnet run --project src/Registrar.App`
Expected: a window opens showing the Engagement heading and an empty-state message. Type the repo's `fixtures` folder into the box and press Open; rows appear.

**Report what you actually saw**, including whether the window rendered or came up blank. A blank window is the WebView2 user-data symptom and means Step 3 did not take effect.

- [ ] **Step 6: Run the suite**

Run: `dotnet test Registrar.sln`
Expected: PASS, unchanged in count. This task adds no tests; Task 5 does.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.App Registrar.sln
git commit -m "feat: WPF host and Blazor Engagement screen"
```

---

### Task 5: The bUnit harness

**Files:**
- Create: `tests/Registrar.App.Tests/Registrar.App.Tests.csproj`
- Create: `tests/Registrar.App.Tests/EngagementComponentTests.cs`
- Modify: `Registrar.sln`

**Interfaces:**
- Consumes: `Engagement.razor` and its `Rows` parameter from Task 4, `EngagementRow` and `ArtifactState` from Task 2.
- Produces: nothing consumed later in this plan. GUI-7 inherits the harness.

**Context you need.** GUI-7 carries three block editors with real interaction logic. Building the UI test harness and those editors in the same unit means neither is finished while the other is blocked. One passing test here proves the harness works, so GUI-7 starts from something known good.

This project must target `net8.0-windows`, because that is the only way it can reference `Registrar.App`.

Keep the test to what a component test can honestly prove: that the component renders the rows it is handed. The rules were tested in Core. Do not restate them here.

- [ ] **Step 1: Create the project**

```bash
dotnet new xunit -o tests/Registrar.App.Tests -n Registrar.App.Tests --framework net8.0
dotnet sln Registrar.sln add tests/Registrar.App.Tests/Registrar.App.Tests.csproj
dotnet add tests/Registrar.App.Tests/Registrar.App.Tests.csproj package bunit
dotnet add tests/Registrar.App.Tests/Registrar.App.Tests.csproj reference src/Registrar.App/Registrar.App.csproj
```

Then edit `tests/Registrar.App.Tests/Registrar.App.Tests.csproj`: set `TargetFramework` to `net8.0-windows`, and add `<UseWPF>true</UseWPF>` so it can reference the app. Set `Nullable` to `enable` to match the other projects.

- [ ] **Step 2: Write the failing test**

Create `tests/Registrar.App.Tests/EngagementComponentTests.cs`:

```csharp
using Bunit;
using Registrar.App.Components;
using Registrar.Core.Engagement;
using Xunit;

namespace Registrar.App.Tests;

// Proves the component renders what Core hands it. The rules themselves are tested in
// Registrar.Tests against EngagementScan; repeating them here would test the same thing
// twice and leave both copies free to disagree.
public class EngagementComponentTests : TestContext
{
    private static EngagementRow Row(string file, ArtifactState state)
        => new(file, "DS", "ORG-GOV-DS-016", "Talewind CPA", state, "detail for " + file);

    [Fact]
    public void Every_row_it_is_given_is_rendered()
    {
        var rows = new[]
        {
            Row("a.json", ArtifactState.Ready),
            Row("b.json", ArtifactState.Built),
            Row("c.agrdraft", ArtifactState.Draft)
        };

        var cut = RenderComponent<Engagement>(p => p.Add(c => c.Rows, rows));

        Assert.Equal(3, cut.FindAll("tbody tr").Count);
        Assert.Contains("a.json", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Built", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Draft", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_folder_renders_an_empty_state_rather_than_a_bare_table()
    {
        var cut = RenderComponent<Engagement>(p =>
            p.Add(c => c.Rows, Array.Empty<EngagementRow>()));

        Assert.Empty(cut.FindAll("tbody tr"));
        Assert.Contains("No artifacts", cut.Markup, StringComparison.Ordinal);
    }

    // Unreadable is the state most likely to be quietly dropped from the UI, and hiding
    // it is the silent degradation the design forbids.
    [Fact]
    public void An_unreadable_artifact_is_shown_rather_than_hidden()
    {
        var rows = new[] { Row("broken.json", ArtifactState.Unreadable) };

        var cut = RenderComponent<Engagement>(p => p.Add(c => c.Rows, rows));

        Assert.Single(cut.FindAll("tbody tr"));
        Assert.Contains("Unreadable", cut.Markup, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run it**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~EngagementComponentTests"`
Expected: PASS, 3 tests.

If the component cannot be referenced, the most likely cause is the test project's `TargetFramework`; it must be `net8.0-windows`. Report rather than working around it by moving the component.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test Registrar.sln`
Expected: PASS. The total rises by 3 over Task 4, and both test projects run.

Confirm in your report that **two** test projects ran, not one. `dotnet test` on a solution runs every test project, and a silently skipped project would make this harness look green while proving nothing.

- [ ] **Step 5: Commit**

```bash
git add tests/Registrar.App.Tests Registrar.sln
git commit -m "test: bUnit harness and Engagement component tests"
```

---

### Task 6: Packaging and the roadmap records

**Files:**
- Modify: `scripts/publish.ps1`
- Modify: `docs/ROADMAP.md`
- Modify: `docs/DELIVERED.md`

**Interfaces:**
- Consumes: the `Registrar.App` project from Task 4.
- Produces: nothing consumed later.

**Context you need.** `scripts/publish.ps1` already stages `dist/registrar/runtime/registrar.exe` with editable `config/`, `schemas/` and `assets/` one level above, because the engine resolves those by walking up from the executable. The app resolves its inputs the same way, so publishing it into the same `runtime/` folder needs no code change in either binary. Read the script before editing; it is short and its comments explain the layout.

The roadmap and `DELIVERED.md` still show GUI-4 and GUI-5 as not started, because both merged while this unit was being designed. Correcting the record here keeps it accurate before a sixth unit lands on top of it.

- [ ] **Step 1: Publish the app alongside the CLI**

In `scripts/publish.ps1`, immediately after the existing `dotnet publish` block for `Registrar.Cli` and its `$LASTEXITCODE` check, add:

```powershell
# The app resolves config/schemas/assets by walking up from its own location, exactly
# as the CLI does, so it ships into the same runtime/ folder and shares one payload.
# Not self-contained single-file: WPF plus WebView2 does not support single-file
# packaging, and the CLI's self-contained runtime is already present in this folder.
dotnet publish (Join-Path $repo 'src/Registrar.App/Registrar.App.csproj') `
  -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=false -p:PublishTrimmed=false `
  -o $runtime

if ($LASTEXITCODE -ne 0) { throw "dotnet publish (app) failed with exit code $LASTEXITCODE" }
```

- [ ] **Step 2: Verify the published payload actually runs**

```bash
pwsh scripts/publish.ps1
```

Then confirm both executables exist in `dist/registrar/runtime/`, and run the CLI from there:

```bash
./dist/registrar/runtime/registrar.exe doctor
```

Expected: `doctor: healthy`, including the `webview user data` check from Task 3.

**Report the output.** If publishing the app overwrites or breaks `registrar.exe`, stop and report rather than reordering the publishes to hide it: two publishes into one folder is the arrangement being tested, and a collision is a real finding about it.

- [ ] **Step 3: Correct the roadmap**

In `docs/ROADMAP.md`, change the GUI-4 and GUI-5 rows in the unit table so their Status column reads `**Done** (PR #58)` and `**Done** (PR #59)` respectively, matching the format the GUI-1 through GUI-3 rows already use.

Then find the paragraph naming which units are unblocked and correct it: GUI-4 and GUI-5 are delivered, and GUI-6 is the unit now in progress.

- [ ] **Step 4: Record both units as delivered**

Append to `docs/DELIVERED.md`, following the existing section format:

```markdown
## Delivered 2026-08-03

- **GovernorBrief and ImportParser (PR #58, GUI-4).** Both directions of the Governor
  handoff. `GovernorBrief` is extracted from `DrafterProjection` rather than derived a
  second time, so the clipboard brief and the generated agent knowledge are one code
  path. `ImportParser` recovers an AGR from a chat paste, populates `AgrDraft`
  tolerantly so a partial reply still opens, and scans the whole payload for prohibited
  characters. That scan closes a real gap: QA-ENG-05 covers context fields and
  placeholder values but not section body text, which was caught only after rendering
  by QA-SF-08.
- **Design system generated from config (PR #59, GUI-5).** `registrar emit-tokens`
  derives CSS custom properties from `config/style-rules.json`, so the operator GUI and
  the DOCX deliverables cannot drift onto different palettes. A `screen` block adds the
  two colors that are correct in print and fail WCAG on a monitor: `danger #A3282B`,
  a new brand value approved because warn red is reserved by QA-SF-09, and
  `border #7E8B9C`. `Contrast` measures WCAG ratios in the engine and a test asserts
  every screen token meets the threshold for its role.
```

- [ ] **Step 5: Run the suite and commit**

Run: `dotnet test Registrar.sln`
Expected: PASS.

```bash
git add scripts/publish.ps1 docs/ROADMAP.md docs/DELIVERED.md
git commit -m "feat: publish the app beside the CLI and record GUI-4 and GUI-5"
```

---

## Definition of done

- `dotnet build Registrar.sln` and `dotnet test Registrar.sln` both green, no new warnings, with **two** test projects running.
- `dotnet run --project src/Registrar.App` opens a window that renders, not a blank one.
- `registrar doctor` reports `webview user data` and still ends healthy.
- `pwsh scripts/publish.ps1` produces one folder with both executables, and the CLI runs from it.
- No logic in `src/Registrar.App` that decides anything about an artifact.
- No brand color literal anywhere in `src/`.
- `docs/ROADMAP.md` and `docs/DELIVERED.md` reflect GUI-4 and GUI-5 as delivered.
