# SW-3: The simple operator window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the placeholder Home page with the one-page window the approved spec describes: choose a folder and an optional registry, pick an AGR, press Check or Build, read the result in plain rows, and open the document, the report or the folder or copy the failures for the Governor. Plus a Health check button.

**Architecture:** Everything with a rule in it lives in `Registrar.Pipeline` where the fast suite reaches it: `AgrMode` (read and describe the declared mode), `GuardedRun` and `AgrRunner` (the SW-1 `AgrFileRun` behind an interface, with every exception turned into a readable result), the registry path in `MenuSettings`, and `DoctorPresenter` (window wording for the health check). `Registrar.App` holds only glue: an `IOperatorHost` interface with a thin WPF implementation (native pickers, open a path, clipboard), `AppServices.Register` (so a test can prove the real registrations resolve), and the Blazor `Home` and `ResultPanel` components, which are tested with bUnit against fakes and against the real runner.

**Tech Stack:** .NET 8, WPF hosting Blazor, bUnit, xUnit. Spec: `docs/superpowers/specs/2026-10-08-simple-operator-window-design.md` (see "What the window does" and "Notes for SW-3").

## Global Constraints

- Layering: `Registrar.App` is a peer of `Registrar.Cli`; neither references the other. App references Core, Validation and Pipeline only. No engine logic in App: it may not decide what is valid or what files land.
- The CLI stays intact. `MenuCommandTests`, `BuildCommandTests`, `ValidateCommandTests`, `DoctorTests`, `DoctorCommandTests`, `Cli/BuildOutputFileSetTests` and `Cli/CliOutputStreamTests` take ZERO edits. `MenuCommand` may change only to call the moved `AgrMode.TryRead`; its behavior and output do not change. Doctor's CLI text (including "pass --registry <csv>") does not change.
- The app-wide error net stays (`Routes.razor` ErrorBoundary, `ScreenError`, the dispatcher handler in `App.xaml.cs`). An exception escaping a Blazor event handler in this WPF host takes the process down, so EVERY handler in the new components that does IO, calls the runner, a picker, the clipboard or opens a path is wrapped in its own try/catch that shows the message beside the controls.
- Razor trap, found in GUI-7: a quoted attribute value on a `string` component parameter is a LITERAL, not an expression. Pass strings as `Param="@expr"`. Test through `Home` (the call site), not through the child component alone.
- Rule 8: US English, no emojis, no em or en dashes in code, comments, config or docs. Every color in CSS is a `var(--org-*)` token; no hex literals.
- CSS token rules (from the header of `app.css`): control boundaries use `--org-border`, never `--org-border-neutral`; text uses `--org-heading-navy`, `--org-primary-blue`, `--org-accent-blue`, `--org-secondary-gray`; error text uses `--org-danger`. Warn red is reserved and is not used for warnings on screen.
- Build and test with `--configuration Release`. Zero warnings.
- Do not terminate processes this session did not start. A smoke launch records its own PID and stops only that PID.
- Ship as a PR to `main`, never a direct push. Do not cut a release (spec note (e)).

## Scope decision (recorded)

The spec lists drag and drop of an AGR onto the window. It is NOT in SW-3. The embedded browser (WebView2) swallows OS file drops and only exposes a path through a web-message bridge, which cannot be verified without a person at the window. SW-3 ships an "Add a file..." button (native file picker) that selects the file the same way. Drag and drop can follow as its own small unit once the owner has run the window. Task 4 amends the spec to say so.

## File Structure

| Path | Action | Responsibility |
|---|---|---|
| `src/Registrar.Pipeline/AgrMode.cs` | create | `TryRead(path)` and `Describe(mode)`. |
| `src/Registrar.Pipeline/GuardedRun.cs` | create | Turns any exception from a run into an exit-2 `AgrFileRunResult`. |
| `src/Registrar.Pipeline/AgrRunner.cs` | create | `IAgrRunner` and `AgrRunner` (Check, Build). |
| `src/Registrar.Pipeline/MenuSettings.cs` | modify | Add `RegistryPath` and `WithRegistry`. |
| `src/Registrar.Pipeline/DoctorPresenter.cs` | create | Window wording and styles for a `DoctorReport`. |
| `src/Registrar.Cli/MenuCommand.cs` | modify | Call `AgrMode.TryRead`; delete the private copy. |
| `src/Registrar.App/AppState.cs` | modify | Add `RepoRoot`. |
| `src/Registrar.App/IOperatorHost.cs`, `WpfOperatorHost.cs` | create | The interface and its thin WPF implementation. |
| `src/Registrar.App/AppServices.cs` | create | `Register(IServiceCollection, EngineContext, string repoRoot)`. |
| `src/Registrar.App/MainWindow.xaml.cs` | modify | Call `AppServices.Register`. |
| `src/Registrar.App/Components/Home.razor`, `ResultPanel.razor`, `HealthPanel.razor` | create/replace | The page. |
| `src/Registrar.App/_Imports.razor` | modify | Add the usings the page needs. |
| `src/Registrar.App/wwwroot/app.css` | modify | Page styles, focus style, `color-scheme`. |
| `tests/Registrar.Tests/Pipeline/*Tests.cs` | create | Tests for the Pipeline types. |
| `tests/Registrar.App.Tests/*` | create/replace | `FakeOperatorHost`, `FakeAgrRunner`, `AppServicesTests`, `HomePageTests`; delete `HomePlaceholderTests`. |
| `docs/superpowers/specs/2026-10-08-simple-operator-window-design.md` | modify | Drag and drop deferral. |

---

### Task 1: Pipeline pieces: mode, guarded runner, registry setting

**Files:**
- Create: `src/Registrar.Pipeline/AgrMode.cs`, `GuardedRun.cs`, `AgrRunner.cs`
- Modify: `src/Registrar.Pipeline/MenuSettings.cs`, `src/Registrar.Cli/MenuCommand.cs`
- Test: `tests/Registrar.Tests/Pipeline/AgrModeTests.cs`, `GuardedRunTests.cs`, `AgrRunnerTests.cs`, `MenuSettingsRegistryTests.cs`

**Interfaces:**
- Consumes: `AgrParser.Parse(string)` and `.ValidationPolicy.Mode` (Core), `AgrFileRun.Validate(string, IClock, string?)` and `AgrFileRun.Build(string, IClock, string?, string?)`, `AgrFileRunResult`, `RunMessage`, `RunStream`, `IClock`.
- Produces:
  - `static string? AgrMode.TryRead(string agrPath)` (null when unreadable or unparseable)
  - `static string AgrMode.Describe(string? mode)`
  - `static AgrFileRunResult GuardedRun.Execute(string agrPath, Func<AgrFileRunResult> run)`
  - `interface IAgrRunner { AgrFileRunResult Check(string agrPath, string? registryPath); AgrFileRunResult Build(string agrPath, string? registryPath); }`
  - `sealed class AgrRunner : IAgrRunner` with constructor `AgrRunner(IClock clock)`
  - `MenuSettings.RegistryPath` (`string?`, JSON name `registryPath`) and `MenuSettings WithRegistry(string? path)`

- [ ] **Step 1: Write the failing tests**

`tests/Registrar.Tests/Pipeline/AgrModeTests.cs`:

```csharp
using Registrar.Core;

namespace Registrar.Tests.Pipeline;

public class AgrModeTests
{
    private static string Fixture(string name)
        => Path.Combine(Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName, "fixtures", name);

    [Fact]
    public void TryRead_returns_the_declared_mode()
        => Assert.Equal("draft", AgrMode.TryRead(Fixture("agr-sample-ds-draft.json")));

    [Fact]
    public void TryRead_returns_null_for_a_missing_file()
        => Assert.Null(AgrMode.TryRead(Path.Combine(TempDir.Create(), "nope.json")));

    [Fact]
    public void TryRead_returns_null_for_text_that_is_not_an_agr()
    {
        var path = Path.Combine(TempDir.Create(), "junk.json");
        File.WriteAllText(path, "this is not json");
        Assert.Null(AgrMode.TryRead(path));
    }

    [Theory]
    [InlineData("draft", "Draft: a working copy. Warnings are allowed.")]
    [InlineData("promotion", "Promotion: the controlled copy. A clean pass is required.")]
    [InlineData(null, "Mode unknown. Press Check and the report will say what is wrong with this file.")]
    [InlineData("something-else", "Mode unknown. Press Check and the report will say what is wrong with this file.")]
    public void Describe_explains_the_mode_in_plain_words(string? mode, string expected)
        => Assert.Equal(expected, AgrMode.Describe(mode));
}
```

`tests/Registrar.Tests/Pipeline/GuardedRunTests.cs`:

```csharp
namespace Registrar.Tests.Pipeline;

public class GuardedRunTests
{
    [Fact]
    public void A_normal_result_passes_through_untouched()
    {
        var ok = new AgrFileRunResult(0, Array.Empty<RunMessage>(), null, "d",
            Array.Empty<string>(), Array.Empty<string>(), null);

        Assert.Same(ok, GuardedRun.Execute("x.json", () => ok));
    }

    [Fact]
    public void An_exception_becomes_an_exit_2_result_with_a_readable_error_and_no_report()
    {
        var path = Path.Combine(TempDir.Create(), "a.json");

        var r = GuardedRun.Execute(path, () => throw new InvalidOperationException("config tree is broken"));

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.Contains("config tree is broken", r.ErrorText, StringComparison.Ordinal);
        Assert.StartsWith("error: ", r.ErrorText, StringComparison.Ordinal);
        Assert.Equal(Path.GetDirectoryName(path), r.OutputDirectory);
        Assert.Empty(r.Written);
    }
}
```

`tests/Registrar.Tests/Pipeline/AgrRunnerTests.cs`:

```csharp
using Registrar.Core;

namespace Registrar.Tests.Pipeline;

public class AgrRunnerTests
{
    private static string Copy(string fixtureName)
    {
        var root = Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;
        var dest = Path.Combine(TempDir.Create(), fixtureName);
        File.Copy(Path.Combine(root, "fixtures", fixtureName), dest);
        return dest;
    }

    private static AgrRunner Runner() => new(new SystemClock());

    [Fact]
    public void Check_writes_a_report_and_no_document()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var r = Runner().Check(path, null);

        Assert.Equal(0, r.ExitCode);
        Assert.NotNull(r.Report);
        Assert.Null(r.DocumentFileName);
        Assert.Contains("validation-report.json", r.Written);
    }

    [Fact]
    public void Build_writes_the_document()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var r = Runner().Build(path, null);

        Assert.Equal(0, r.ExitCode);
        Assert.NotNull(r.DocumentFileName);
        Assert.True(File.Exists(Path.Combine(r.OutputDirectory, r.DocumentFileName!)));
    }

    [Fact]
    public void A_missing_file_is_a_readable_exit_2_not_an_exception()
    {
        var r = Runner().Check(Path.Combine(TempDir.Create(), "nope.json"), null);

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.StartsWith("error: AGR file not found:", r.ErrorText, StringComparison.Ordinal);
    }
}
```

`tests/Registrar.Tests/Pipeline/MenuSettingsRegistryTests.cs`:

```csharp
namespace Registrar.Tests.Pipeline;

public class MenuSettingsRegistryTests
{
    [Fact]
    public void WithRegistry_sets_and_clears_the_path()
    {
        var s = new MenuSettings().WithRegistry(@"C:\r\registry.csv");
        Assert.Equal(@"C:\r\registry.csv", s.RegistryPath);
        Assert.Null(s.WithRegistry(null).RegistryPath);
    }

    [Fact]
    public void The_registry_path_survives_a_save_and_load_beside_the_existing_fields()
    {
        var store = new SettingsStore(Path.Combine(TempDir.Create(), "settings.json"));
        store.Save(new MenuSettings().WithFolder(@"C:\work").WithRegistry(@"C:\r\registry.csv").WithRecent(@"C:\work\a.json"));

        var back = store.Load();

        Assert.Equal(@"C:\work", back.LastFolder);
        Assert.Equal(@"C:\r\registry.csv", back.RegistryPath);
        Assert.Equal(new[] { @"C:\work\a.json" }, back.RecentAgrs);
    }

    [Fact]
    public void A_settings_file_written_before_the_field_existed_still_loads()
    {
        var path = Path.Combine(TempDir.Create(), "settings.json");
        File.WriteAllText(path, "{ \"lastFolder\": \"C:\\\\work\", \"recentAgrs\": [] }");

        var back = new SettingsStore(path).Load();

        Assert.Equal(@"C:\work", back.LastFolder);
        Assert.Null(back.RegistryPath);
    }
}
```

(`Registrar.Pipeline` types resolve through the global `Using` already in `Registrar.Tests.csproj`.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Registrar.Tests --configuration Release --filter "FullyQualifiedName~Pipeline.AgrModeTests|FullyQualifiedName~Pipeline.GuardedRunTests|FullyQualifiedName~Pipeline.AgrRunnerTests|FullyQualifiedName~MenuSettingsRegistryTests"`
Expected: build FAIL (`AgrMode`, `GuardedRun`, `AgrRunner`, `RegistryPath` do not exist).

- [ ] **Step 3: Write the Pipeline code**

`src/Registrar.Pipeline/AgrMode.cs`:

```csharp
using Registrar.Core;

namespace Registrar.Pipeline;

// What an AGR declares as its mode, read without running anything. The console menu used a
// private copy of TryRead to pre-fill its mode prompt; the window shows the same value
// read-only, because a mode that disagrees with the AGR is a usage error in the CLI and a
// control that can only produce an error is the confusion this window exists to remove.
public static class AgrMode
{
    // Null for a file that cannot be read or parsed. The caller shows Describe(null), and
    // the report from Check says what is wrong; this reader never judges validity.
    public static string? TryRead(string agrPath)
    {
        try { return AgrParser.Parse(File.ReadAllText(agrPath)).ValidationPolicy.Mode; }
        catch { return null; }
    }

    public static string Describe(string? mode) => mode switch
    {
        "draft" => "Draft: a working copy. Warnings are allowed.",
        "promotion" => "Promotion: the controlled copy. A clean pass is required.",
        _ => "Mode unknown. Press Check and the report will say what is wrong with this file."
    };
}
```

`src/Registrar.Pipeline/GuardedRun.cs`:

```csharp
namespace Registrar.Pipeline;

// AgrFileRun is not total: a broken config or schema tree throws out of EngineContext.Load,
// exactly as it does in the CLI, where the exception ends the process with a message. In the
// window an exception that escapes a Blazor handler takes the whole process down, so this is
// the boundary that turns any failure into the same shape every other failure has: exit 2,
// no report, an "error:" line the operator can read.
//
// Catching Exception here is deliberate and narrow in purpose: it is the last frame before
// the UI, it records the message rather than swallowing it, and it reports that nothing was
// written by the failed step.
public static class GuardedRun
{
    public static AgrFileRunResult Execute(string agrPath, Func<AgrFileRunResult> run)
    {
        try
        {
            return run();
        }
        catch (Exception ex)
        {
            return new AgrFileRunResult(
                2,
                new[] { new RunMessage(RunStream.Err, $"error: the run could not complete: {ex.Message}") },
                null,
                DirectoryOf(agrPath),
                Array.Empty<string>(),
                Array.Empty<string>(),
                null);
        }
    }

    private static string DirectoryOf(string agrPath)
    {
        try { return Path.GetDirectoryName(Path.GetFullPath(agrPath)) ?? ""; }
        catch { return ""; }
    }
}
```

`src/Registrar.Pipeline/AgrRunner.cs`:

```csharp
using Registrar.Core;

namespace Registrar.Pipeline;

// The two things the window can do to an AGR, behind an interface so the page is tested
// against a fake for the failure shapes and against the real runner for the happy paths.
public interface IAgrRunner
{
    AgrFileRunResult Check(string agrPath, string? registryPath);
    AgrFileRunResult Build(string agrPath, string? registryPath);
}

public sealed class AgrRunner : IAgrRunner
{
    private readonly IClock _clock;

    public AgrRunner(IClock clock) => _clock = clock;

    public AgrFileRunResult Check(string agrPath, string? registryPath)
        => GuardedRun.Execute(agrPath, () => AgrFileRun.Validate(agrPath, _clock, registryPath));

    // mode is passed as null on purpose: the window shows the AGR's own declared mode and
    // builds in it, so a disagreement cannot be expressed.
    public AgrFileRunResult Build(string agrPath, string? registryPath)
        => GuardedRun.Execute(agrPath, () => AgrFileRun.Build(agrPath, _clock, null, registryPath));
}
```

In `src/Registrar.Pipeline/MenuSettings.cs` add the property and method (keep the existing members as they are):

```csharp
    [JsonPropertyName("registryPath")] public string? RegistryPath { get; init; }

    public MenuSettings WithRegistry(string? path) => this with { RegistryPath = path };
```

- [ ] **Step 4: Make MenuCommand call AgrMode**

In `src/Registrar.Cli/MenuCommand.cs` replace `TryReadMode(agrPath)` (line ~62) with `AgrMode.TryRead(agrPath)` and delete the private `TryReadMode` method (line ~148). Nothing else in the file changes.

- [ ] **Step 5: Run the new tests, then the whole suite**

Run: `dotnet test tests/Registrar.Tests --configuration Release --filter "FullyQualifiedName~Pipeline.AgrModeTests|FullyQualifiedName~Pipeline.GuardedRunTests|FullyQualifiedName~Pipeline.AgrRunnerTests|FullyQualifiedName~MenuSettingsRegistryTests"`
Expected: PASS (13 tests).

Run: `dotnet test Registrar.sln --configuration Release`
Expected: all pass, 0 warnings; `git diff --stat main -- tests/` shows only the four new files plus Task-1-only additions, and NO pre-existing test file.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Pipeline src/Registrar.Cli/MenuCommand.cs tests/Registrar.Tests/Pipeline
git commit -m "feat: add AgrMode, GuardedRun and AgrRunner to Pipeline for the window

MenuCommand now reads the declared mode through AgrMode; its behavior is unchanged.
MenuSettings gains an optional registry path.

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: App services and the host interface

**Files:**
- Create: `src/Registrar.App/IOperatorHost.cs`, `src/Registrar.App/WpfOperatorHost.cs`, `src/Registrar.App/AppServices.cs`
- Modify: `src/Registrar.App/AppState.cs`, `src/Registrar.App/MainWindow.xaml.cs`
- Create: `tests/Registrar.App.Tests/AppServicesTests.cs`, `tests/Registrar.App.Tests/FakeOperatorHost.cs`, `tests/Registrar.App.Tests/FakeAgrRunner.cs`
- Modify: `tests/Registrar.App.Tests/HomePlaceholderTests.cs` (constructor change only)

**Interfaces:**
- Consumes: `IAgrRunner`, `AgrRunner`, `SettingsStore` (Task 1 and SW-1), `EngineContext`, `IClock`, `SystemClock`.
- Produces:
  - `AppState(EngineContext engine, string repoRoot)` with `string RepoRoot { get; }` plus the existing `Engine`, `Folder`, `RegistryCsvPath`
  - `interface IOperatorHost { string? PickFolder(string? startFolder); string? PickAgrFile(string? startFolder); string? PickRegistryFile(string? startFolder); void OpenPath(string path); void CopyText(string text); }`
  - `static void AppServices.Register(IServiceCollection services, EngineContext engine, string repoRoot)` registering `EngineContext`, `IClock`, `AppState`, `IAgrRunner`, `IOperatorHost`, `SettingsStore` as singletons
  - test fakes `FakeOperatorHost` and `FakeAgrRunner` (below)

- [ ] **Step 1: Write the failing test and fakes**

`tests/Registrar.App.Tests/AppServicesTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Registrar.Core;
using Registrar.Pipeline;
using Registrar.Validation;
using Xunit;

namespace Registrar.App.Tests;

// MainWindow wires the real services and cannot be started by the suite. This proves the
// registrations themselves resolve, which is the failure a fake-based page test cannot see:
// a service the page injects that nobody registered fails only when the window opens.
public class AppServicesTests
{
    internal static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    internal static EngineContext LoadEngine() => EngineContext.Load(RepoRoot());

    [Fact]
    public void Every_service_the_page_injects_resolves()
    {
        var services = new ServiceCollection();
        AppServices.Register(services, LoadEngine(), RepoRoot());

        using var provider = services.BuildServiceProvider(validateScopes: true);

        foreach (var type in new[]
        {
            typeof(AppState), typeof(IAgrRunner), typeof(IOperatorHost),
            typeof(SettingsStore), typeof(IClock), typeof(EngineContext)
        })
            Assert.NotNull(provider.GetRequiredService(type));
    }

    [Fact]
    public void AppState_carries_the_repo_root()
    {
        var services = new ServiceCollection();
        AppServices.Register(services, LoadEngine(), RepoRoot());
        using var provider = services.BuildServiceProvider();

        Assert.Equal(RepoRoot(), provider.GetRequiredService<AppState>().RepoRoot);
    }
}
```

`tests/Registrar.App.Tests/FakeOperatorHost.cs`:

```csharp
namespace Registrar.App.Tests;

// Records what the page asked the shell to do and returns whatever a test queued. The WPF
// implementation is thin enough to be glue; the page's behavior around it is what is tested.
internal sealed class FakeOperatorHost : IOperatorHost
{
    public string? NextFolder { get; set; }
    public string? NextAgrFile { get; set; }
    public string? NextRegistryFile { get; set; }

    public List<string> Opened { get; } = new();
    public string? CopiedText { get; private set; }

    // Set to make the matching call throw, as a locked clipboard or a missing handler does.
    public Exception? OpenFails { get; set; }
    public Exception? CopyFails { get; set; }

    public string? PickFolder(string? startFolder) => NextFolder;
    public string? PickAgrFile(string? startFolder) => NextAgrFile;
    public string? PickRegistryFile(string? startFolder) => NextRegistryFile;

    public void OpenPath(string path)
    {
        if (OpenFails is not null) throw OpenFails;
        Opened.Add(path);
    }

    public void CopyText(string text)
    {
        if (CopyFails is not null) throw CopyFails;
        CopiedText = text;
    }
}
```

`tests/Registrar.App.Tests/FakeAgrRunner.cs`:

```csharp
using Registrar.Pipeline;

namespace Registrar.App.Tests;

// For the shapes the real runner makes awkward to produce: a canned exit 2, or a runner
// that throws despite GuardedRun (the page must survive that too).
internal sealed class FakeAgrRunner : IAgrRunner
{
    public AgrFileRunResult? Canned { get; set; }
    public Exception? Throws { get; set; }
    public List<string> Calls { get; } = new();

    public AgrFileRunResult Check(string agrPath, string? registryPath) => Respond("check", agrPath);
    public AgrFileRunResult Build(string agrPath, string? registryPath) => Respond("build", agrPath);

    private AgrFileRunResult Respond(string verb, string agrPath)
    {
        Calls.Add(verb + ":" + agrPath);
        if (Throws is not null) throw Throws;
        return Canned ?? throw new InvalidOperationException("FakeAgrRunner has no canned result.");
    }

    public static AgrFileRunResult UsageError(string text, string dir) => new(
        2, new[] { new RunMessage(RunStream.Err, text) }, null, dir,
        Array.Empty<string>(), Array.Empty<string>(), null);
}
```

In `tests/Registrar.App.Tests/HomePlaceholderTests.cs` change `new AppState(LoadEngine())` to `new AppState(LoadEngine(), AppServicesTests.RepoRoot())` (the file is deleted in Task 3; this keeps the project compiling meanwhile).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Registrar.App.Tests --configuration Release`
Expected: build FAIL (`AppServices`, `IOperatorHost`, the two-argument `AppState` do not exist).

- [ ] **Step 3: Write the App code**

`src/Registrar.App/AppState.cs` (full replacement):

```csharp
using Registrar.Validation;

namespace Registrar.App;

// What the open window needs to remember. A bag of values with no rules: every decision
// about validity and output belongs to Registrar.Core and Registrar.Pipeline, and a
// condition that lived here would be one the fast suite cannot reach.
public sealed class AppState
{
    public AppState(EngineContext engine, string repoRoot)
    {
        Engine = engine;
        RepoRoot = repoRoot;
    }

    public EngineContext Engine { get; }

    // The folder that holds schemas/ and config/, resolved once at startup. The health
    // check needs it; nothing else in the window should.
    public string RepoRoot { get; }

    // The working folder the operator chose, if any.
    public string? Folder { get; set; }

    // The ID Registry export the operator chose, if any. Null means no registry, which
    // the registry layer already reports as a skipped check.
    public string? RegistryCsvPath { get; set; }
}
```

`src/Registrar.App/IOperatorHost.cs`:

```csharp
namespace Registrar.App;

// Everything the page needs from the machine it runs on: native pickers, opening a file or
// folder, and the clipboard. An interface so the page is tested without a desktop, and so
// the WPF specifics sit in one thin class that holds no rules.
public interface IOperatorHost
{
    // Each picker returns null when the operator cancels.
    string? PickFolder(string? startFolder);
    string? PickAgrFile(string? startFolder);
    string? PickRegistryFile(string? startFolder);

    // Open a document, report or folder in the shell's default handler.
    void OpenPath(string path);

    void CopyText(string text);
}
```

`src/Registrar.App/WpfOperatorHost.cs`:

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Registrar.App;

// The WPF implementation of IOperatorHost. Glue only: it decides nothing, and the suite
// cannot reach it (a net8.0 test project cannot start a desktop), which is why every rule
// the window has lives in Registrar.Pipeline instead. Callers wrap each method in a catch,
// because the clipboard can be locked by another program and a missing file handler throws.
public sealed class WpfOperatorHost : IOperatorHost
{
    public string? PickFolder(string? startFolder)
    {
        var dialog = new OpenFolderDialog { Title = "Choose your working folder" };
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
            dialog.InitialDirectory = startFolder;
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FolderName : null;
    }

    public string? PickAgrFile(string? startFolder)
        => PickFile("Add a file", "AGR files (*.json)|*.json|All files (*.*)|*.*", startFolder);

    public string? PickRegistryFile(string? startFolder)
        => PickFile("Choose the ID registry export", "Registry export (*.csv)|*.csv|All files (*.*)|*.*", startFolder);

    private static string? PickFile(string title, string filter, string? startFolder)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
            dialog.InitialDirectory = startFolder;
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public void OpenPath(string path)
        => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    public void CopyText(string text) => Clipboard.SetText(text);
}
```

`src/Registrar.App/AppServices.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Registrar.Core;
using Registrar.Pipeline;
using Registrar.Validation;

namespace Registrar.App;

// The window's service registrations in one testable place. MainWindow calls this and adds
// the WebView services itself; AppServicesTests proves everything the page injects resolves.
public static class AppServices
{
    public static void Register(IServiceCollection services, EngineContext engine, string repoRoot)
    {
        services.AddSingleton(engine);
        services.AddSingleton<IClock>(new SystemClock());
        services.AddSingleton(new AppState(engine, repoRoot));
        services.AddSingleton<IAgrRunner>(sp => new AgrRunner(sp.GetRequiredService<IClock>()));
        services.AddSingleton<IOperatorHost, WpfOperatorHost>();
        services.AddSingleton(new SettingsStore(SettingsStore.DefaultPath()));
    }
}
```

`src/Registrar.App/MainWindow.xaml.cs`: hoist `repoRoot` out of the `try` and replace the four `services.Add...` lines after the catch with one call. The block becomes:

```csharp
        EngineContext engine;
        string repoRoot;
        try
        {
            repoRoot = Directory.GetParent(Path.GetDirectoryName(
                ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

            engine = EngineContext.Load(repoRoot);
        }
        catch (Exception ex)
        {
            ShowStartupFailure(ex);
            Environment.Exit(1);
            return;
        }

        AppServices.Register(services, engine, repoRoot);

        Resources.Add("services", services.BuildServiceProvider());
```

Leave `services.AddWpfBlazorWebView();`, the startup-guard comments and `ShowStartupFailure` exactly as they are. (The existing comment block above the `try` stays; only reword its sentence about per-keystroke validation if it is still there.)

- [ ] **Step 4: Run the tests**

Run: `dotnet build Registrar.sln --configuration Release` then `dotnet test Registrar.sln --configuration Release`
Expected: 0 warnings; the two new AppServices tests pass; everything else unchanged from Task 1.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.App tests/Registrar.App.Tests
git commit -m "feat: add the window's service registrations and host interface

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The page: folder, registry, file list, mode, Check, Build, result

**Files:**
- Replace: `src/Registrar.App/Components/Home.razor`
- Create: `src/Registrar.App/Components/ResultPanel.razor`
- Modify: `src/Registrar.App/_Imports.razor`, `src/Registrar.App/wwwroot/app.css`
- Delete: `tests/Registrar.App.Tests/HomePlaceholderTests.cs`
- Create: `tests/Registrar.App.Tests/HomePageTests.cs`

**Interfaces:**
- Consumes: `AppState`, `IAgrRunner`, `IOperatorHost`, `SettingsStore`, `MenuSettings` (`LastFolder`, `RecentAgrs`, `RegistryPath`, `WithFolder`, `WithRecent`, `WithRegistry`), `AgrPicker.Candidates(string, IReadOnlyList<string>)`, `AgrMode.TryRead/Describe`, `ReportPresenter.Present(ValidationReport) : IReadOnlyList<PresenterLine>` with `LineStyle { Normal, Pass, Warn, Fail }`, `GovernorClipboardText.Build(ValidationReport)`, `AgrFileRunResult`.
- Produces: CSS and DOM hooks the tests and Task 4 rely on. Buttons: `button.choose-folder`, `button.choose-registry`, `button.clear-registry`, `button.add-file`, `button.check`, `button.build`; in the result panel `button.open-document`, `button.open-report`, `button.open-folder`, `button.copy-failures`. Elements: `label.file` (each contains a radio input), `.mode`, `.banner`, `.report .line`, `.result .error`, `.problem`, `.notice`. `ResultPanel` has one parameter, `AgrFileRunResult Result`.

- [ ] **Step 1: Write the failing page tests**

Delete `tests/Registrar.App.Tests/HomePlaceholderTests.cs` (`git rm`). Create `tests/Registrar.App.Tests/HomePageTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Registrar.App.Components;
using Registrar.Core;
using Registrar.Pipeline;
using Xunit;

namespace Registrar.App.Tests;

// Rendered through Home, the call site, never through a child alone: a component test hands
// its parameters in through the test framework's builder, so it proves the component and
// never the markup that feeds it (GUI-7 shipped a permanent red error that way).
public class HomePageTests : TestContext
{
    private readonly FakeOperatorHost host = new();
    private readonly FakeAgrRunner fake = new();
    private readonly string settingsPath = Path.Combine(TempDir.Create(), "settings.json");

    private static string Fixture(string name)
        => Path.Combine(AppServicesTests.RepoRoot(), "fixtures", name);

    private static string FolderWith(params string[] fixtures)
    {
        var dir = TempDir.Create();
        foreach (var f in fixtures) File.Copy(Fixture(f), Path.Combine(dir, f));
        return dir;
    }

    private IRenderedComponent<Home> Render(string? folder = null, string? registry = null, bool useFake = false)
    {
        var store = new SettingsStore(settingsPath);
        var seed = new MenuSettings();
        if (folder is not null) seed = seed.WithFolder(folder);
        if (registry is not null) seed = seed.WithRegistry(registry);
        store.Save(seed);

        Services.AddSingleton(new AppState(AppServicesTests.LoadEngine(), AppServicesTests.RepoRoot()));
        Services.AddSingleton<IOperatorHost>(host);
        Services.AddSingleton(store);
        if (useFake) Services.AddSingleton<IAgrRunner>(fake);
        else Services.AddSingleton<IAgrRunner>(new AgrRunner(new SystemClock()));
        return RenderComponent<Home>();
    }

    [Fact]
    public void It_lists_the_json_files_in_the_remembered_folder_and_selects_the_first()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft.json", "agr-sample-aup-draft.json"));

        Assert.Equal(2, cut.FindAll("label.file").Count);
        Assert.Single(cut.FindAll("label.file input[checked]"));
    }

    [Fact]
    public void It_says_when_the_folder_has_no_files()
    {
        var cut = Render(TempDir.Create());

        Assert.Empty(cut.FindAll("label.file"));
        Assert.Contains("No .json files", cut.Find(".notice").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_and_Build_are_disabled_until_a_file_is_selected()
    {
        var cut = Render(TempDir.Create());

        Assert.True(cut.Find("button.check").HasAttribute("disabled"));
        Assert.True(cut.Find("button.build").HasAttribute("disabled"));
    }

    [Fact]
    public void It_shows_the_declared_mode_in_plain_words_and_offers_no_mode_control()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft.json"));

        Assert.Contains("Draft: a working copy", cut.Find(".mode").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("select"));
    }

    [Fact]
    public void Check_shows_a_pass_banner_and_no_document_button()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft.json"));

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("PASS", cut.Find(".banner").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("button.open-document"));
        Assert.NotEmpty(cut.FindAll("button.open-report"));
    }

    [Fact]
    public void Build_produces_the_document_and_the_open_button_opens_it()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        var cut = Render(folder);

        cut.Find("button.build").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.open-document")));

        cut.Find("button.open-document").Click();

        var opened = Assert.Single(host.Opened);
        Assert.EndsWith(".docx", opened, StringComparison.Ordinal);
        Assert.True(File.Exists(opened));
    }

    [Fact]
    public void A_failing_file_shows_the_fix_instructions_and_copies_them_for_the_Governor()
    {
        var cut = Render(FolderWith("agr-sample-ds-invalid-schema.json"));

        cut.Find("button.build").Click();
        cut.WaitForAssertion(() =>
            Assert.Contains("FAIL", cut.Find(".banner").TextContent, StringComparison.Ordinal));

        Assert.Contains("fix:", cut.Find(".report").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button.open-document"));

        cut.Find("button.copy-failures").Click();

        Assert.Contains("Validation failures for", host.CopiedText, StringComparison.Ordinal);
        Assert.Contains("Copied", cut.Find(".notice").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_usage_error_shows_its_text_and_no_report_buttons()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        fake.Canned = FakeAgrRunner.UsageError("error: registry file not found: x.csv", folder);
        var cut = Render(folder, useFake: true);

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("registry file not found", cut.Find(".result .error").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".banner"));
        Assert.Empty(cut.FindAll("button.open-report"));
        Assert.Empty(cut.FindAll("button.copy-failures"));
    }

    [Fact]
    public void A_runner_that_throws_is_reported_beside_the_controls_not_allowed_to_escape()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        fake.Throws = new InvalidOperationException("boom");
        var cut = Render(folder, useFake: true);

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("boom", cut.Find(".problem").TextContent, StringComparison.Ordinal));
        Assert.False(cut.Find("button.check").HasAttribute("disabled"));
    }

    [Fact]
    public void A_locked_clipboard_is_reported_not_thrown()
    {
        var cut = Render(FolderWith("agr-sample-ds-invalid-schema.json"));
        cut.Find("button.check").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button.copy-failures")));

        host.CopyFails = new InvalidOperationException("clipboard is in use");
        cut.Find("button.copy-failures").Click();

        Assert.Contains("clipboard is in use", cut.Find(".problem").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Choosing_a_folder_lists_its_files_and_remembers_the_choice()
    {
        var cut = Render(TempDir.Create());
        var chosen = FolderWith("agr-sample-ds-draft.json");
        host.NextFolder = chosen;

        cut.Find("button.choose-folder").Click();

        Assert.Single(cut.FindAll("label.file"));
        Assert.Equal(chosen, new SettingsStore(settingsPath).Load().LastFolder);
    }

    [Fact]
    public void Cancelling_the_folder_picker_changes_nothing()
    {
        var original = FolderWith("agr-sample-ds-draft.json");
        var cut = Render(original);
        host.NextFolder = null;

        cut.Find("button.choose-folder").Click();

        Assert.Single(cut.FindAll("label.file"));
        Assert.Equal(original, new SettingsStore(settingsPath).Load().LastFolder);
    }

    [Fact]
    public void Adding_a_file_selects_it()
    {
        var cut = Render(TempDir.Create());
        var elsewhere = FolderWith("agr-sample-aup-draft.json");
        host.NextAgrFile = Path.Combine(elsewhere, "agr-sample-aup-draft.json");

        cut.Find("button.add-file").Click();

        Assert.Single(cut.FindAll("label.file"));
        Assert.False(cut.Find("button.check").HasAttribute("disabled"));
    }

    [Fact]
    public void The_registry_can_be_chosen_remembered_and_cleared()
    {
        var cut = Render(TempDir.Create());
        host.NextRegistryFile = Fixture("registry-sample.csv");

        cut.Find("button.choose-registry").Click();

        Assert.Equal(Fixture("registry-sample.csv"), new SettingsStore(settingsPath).Load().RegistryPath);
        Assert.Contains("registry-sample.csv", cut.Markup, StringComparison.Ordinal);

        cut.Find("button.clear-registry").Click();

        Assert.Null(new SettingsStore(settingsPath).Load().RegistryPath);
    }

    [Fact]
    public void No_registry_is_stated_plainly()
    {
        var cut = Render(TempDir.Create());

        Assert.Contains("No registry chosen", cut.Markup, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Registrar.App.Tests --configuration Release --filter "FullyQualifiedName~HomePageTests"`
Expected: FAIL: the current `Home.razor` is the placeholder (no `label.file`, no buttons), so the first assertions throw `ElementNotFoundException`.

- [ ] **Step 3: Write the components**

`src/Registrar.App/_Imports.razor` becomes:

```razor
@using Microsoft.AspNetCore.Components.Web
@using Registrar.App
@using Registrar.Core
@using Registrar.Core.Model
@using Registrar.Pipeline
```

`src/Registrar.App/Components/Home.razor`:

```razor
@page "/"
@using System.IO
@inject AppState State
@inject IAgrRunner Runner
@inject IOperatorHost Host
@inject SettingsStore Store

@* The whole window. Everything with a rule in it lives in Registrar.Pipeline; this page
   chooses, calls and shows. Every handler that touches a picker, the runner, a settings
   file, the clipboard or a path catches and says so beside the controls, because an
   exception escaping a Blazor handler in this WPF host takes the process down. *@

<h1>The Registrar</h1>

<section class="step" aria-labelledby="step-folder">
    <h2 id="step-folder">1. Your working folder</h2>
    <p class="path">@(folder ?? "No folder chosen yet.")</p>
    <button type="button" class="choose-folder" @onclick="ChooseFolder">Choose folder</button>
</section>

<section class="step" aria-labelledby="step-registry">
    <h2 id="step-registry">2. ID registry (optional)</h2>
    <p class="path">@(registry ?? "No registry chosen.")</p>
    @if (registry is null)
    {
        <p class="hint">Without one, the registry check is skipped.</p>
    }
    <button type="button" class="choose-registry" @onclick="ChooseRegistry">Choose registry file</button>
    @if (registry is not null)
    {
        <button type="button" class="clear-registry" @onclick="ClearRegistry">Clear</button>
    }
</section>

<fieldset class="step files">
    <legend>3. The file to check</legend>
    @if (files.Count == 0)
    {
        <p class="notice">No .json files in this folder yet. Choose another folder, or add a file.</p>
    }
    @foreach (var file in files)
    {
        var path = file;
        <label class="file">
            <input type="radio" name="agr" checked="@(path == selected)" @onchange="() => Select(path)" />
            @Path.GetFileName(path)
        </label>
    }
    <button type="button" class="add-file" @onclick="AddFile">Add a file...</button>
</fieldset>

@if (selected is not null)
{
    <p class="mode">@AgrMode.Describe(mode)</p>
}

<div class="actions">
    <button type="button" class="check primary" disabled="@(!CanRun)" @onclick="() => Run(false)">Check</button>
    <button type="button" class="build primary" disabled="@(!CanRun)" @onclick="() => Run(true)">Build</button>
</div>

@if (problem is not null)
{
    <p class="problem error" role="alert">@problem</p>
}

@if (running)
{
    <p class="notice" role="status">Working...</p>
}

@if (result is not null)
{
    <ResultPanel Result="@result" />
}

@code {
    private MenuSettings settings = new();
    private string? folder;
    private string? registry;
    private List<string> files = new();
    private string? selected;
    private string? mode;
    private bool running;
    private AgrFileRunResult? result;
    private string? problem;

    private bool CanRun => selected is not null && !running;

    protected override void OnInitialized()
    {
        try
        {
            settings = Store.Load();
            folder = settings.LastFolder;
            registry = settings.RegistryPath;
            Rescan();
        }
        catch (Exception ex)
        {
            problem = "Your saved settings could not be read: " + ex.Message;
        }
    }

    // Recent files first, then the folder's own .json files; a pure listing in Pipeline.
    private void Rescan()
    {
        files = AgrPicker.Candidates(folder ?? "", settings.RecentAgrs).ToList();
        if (selected is null || !files.Contains(selected))
            Select(files.Count > 0 ? files[0] : null);
    }

    private void Select(string? path)
    {
        selected = path;
        mode = path is null ? null : AgrMode.TryRead(path);
        result = null;
    }

    private void Remember(MenuSettings next)
    {
        settings = next;
        Store.Save(settings);
    }

    private void ChooseFolder()
    {
        problem = null;
        try
        {
            var chosen = Host.PickFolder(folder);
            if (chosen is null) return;
            folder = chosen;
            State.Folder = chosen;
            Remember(settings.WithFolder(chosen));
            Rescan();
        }
        catch (Exception ex) { problem = "The folder could not be chosen: " + ex.Message; }
    }

    private void ChooseRegistry()
    {
        problem = null;
        try
        {
            var chosen = Host.PickRegistryFile(folder);
            if (chosen is null) return;
            registry = chosen;
            State.RegistryCsvPath = chosen;
            Remember(settings.WithRegistry(chosen));
        }
        catch (Exception ex) { problem = "The registry file could not be chosen: " + ex.Message; }
    }

    private void ClearRegistry()
    {
        problem = null;
        try
        {
            registry = null;
            State.RegistryCsvPath = null;
            Remember(settings.WithRegistry(null));
        }
        catch (Exception ex) { problem = "The registry setting could not be cleared: " + ex.Message; }
    }

    private void AddFile()
    {
        problem = null;
        try
        {
            var chosen = Host.PickAgrFile(folder);
            if (chosen is null) return;
            Remember(settings.WithRecent(chosen));
            Rescan();
            Select(chosen);
        }
        catch (Exception ex) { problem = "The file could not be added: " + ex.Message; }
    }

    private async Task Run(bool build)
    {
        if (selected is null || running) return;

        problem = null;
        result = null;
        running = true;
        var path = selected;
        var registryPath = registry;

        try
        {
            // The engine is synchronous and a build renders a document, so it runs off the
            // UI thread; the buttons stay disabled until it returns.
            result = await Task.Run(() => build ? Runner.Build(path, registryPath) : Runner.Check(path, registryPath));
            Remember(settings.WithRecent(path));
            files = AgrPicker.Candidates(folder ?? "", settings.RecentAgrs).ToList();
        }
        catch (Exception ex)
        {
            problem = "The run could not be shown: " + ex.Message;
        }
        finally
        {
            running = false;
        }
    }
}
```

Note for the implementer: `Select` clears `result`, and `Rescan` calls `Select` only when the selection is missing, so a run's result is not wiped by the post-run list refresh. The post-run refresh in `Run` deliberately rebuilds `files` without calling `Rescan`.

`src/Registrar.App/Components/ResultPanel.razor`:

```razor
@inject IOperatorHost Host

@* What a run said, in the same words the console prints: ReportPresenter builds the lines,
   so the window and the CLI cannot describe a result differently. Actions are shown only
   when they are real: a document button only if the DOCX landed this run, a copy button
   only if something failed. *@

<section class="result" aria-live="polite">
    @if (Result.Report is not null)
    {
        <div class="report">
            @foreach (var (line, index) in lines.Select((l, i) => (l, i)))
            {
                <div class="line @StyleClass(line.Style) @(index == 0 ? "banner" : "")">@line.Text</div>
            }
        </div>
    }

    @foreach (var message in Result.Messages.Where(m => m.Stream == RunStream.Out))
    {
        <p class="summary">@message.Text</p>
    }

    @if (Result.ErrorText.Length > 0)
    {
        <p class="error" role="alert">@Result.ErrorText</p>
    }

    <div class="actions">
        @if (Result.DocumentFileName is not null)
        {
            <button type="button" class="open-document primary" @onclick="OpenDocument">Open document</button>
        }
        @if (Result.Report is not null && Result.Written.Contains("validation-report.json"))
        {
            <button type="button" class="open-report" @onclick="OpenReport">Open report</button>
        }
        @if (Result.OutputDirectory.Length > 0)
        {
            <button type="button" class="open-folder" @onclick="OpenFolder">Open folder</button>
        }
        @if (HasFailures)
        {
            <button type="button" class="copy-failures" @onclick="CopyFailures">Copy failures for the Governor</button>
        }
    </div>

    @if (problem is not null)
    {
        <p class="problem error" role="alert">@problem</p>
    }
    @if (copied is not null)
    {
        <p class="notice" role="status">@copied</p>
    }
</section>

@code {
    [Parameter, EditorRequired] public AgrFileRunResult Result { get; set; } = default!;

    private IReadOnlyList<PresenterLine> lines = Array.Empty<PresenterLine>();
    private string? problem;
    private string? copied;

    private bool HasFailures =>
        Result.Report is not null && Result.Report.Checks.Any(c => c.Result == CheckResultValue.Fail);

    protected override void OnParametersSet()
    {
        lines = Result.Report is null ? Array.Empty<PresenterLine>() : ReportPresenter.Present(Result.Report);
        problem = null;
        copied = null;
    }

    private static string StyleClass(LineStyle style) => style switch
    {
        LineStyle.Pass => "pass",
        LineStyle.Warn => "warn",
        LineStyle.Fail => "fail",
        _ => ""
    };

    private void OpenDocument() => Act(() => Host.OpenPath(Path.Combine(Result.OutputDirectory, Result.DocumentFileName!)),
        "The document could not be opened: ");

    private void OpenReport() => Act(() => Host.OpenPath(Path.Combine(Result.OutputDirectory, "validation-report.json")),
        "The report could not be opened: ");

    private void OpenFolder() => Act(() => Host.OpenPath(Result.OutputDirectory),
        "The folder could not be opened: ");

    private void CopyFailures() => Act(() =>
    {
        Host.CopyText(GovernorClipboardText.Build(Result.Report!));
        copied = "Copied the failures. Paste them into the Governor chat.";
    }, "The failures could not be copied: ");

    // Every action here talks to the machine, so each one catches; an escape would end the
    // process.
    private void Act(Action action, string failurePrefix)
    {
        problem = null;
        copied = null;
        try { action(); }
        catch (Exception ex) { problem = failurePrefix + ex.Message; }
    }
}
```

Implementer notes: add `@using System.IO` to `ResultPanel.razor` if the build asks for it. The copy handler sets `copied` inside the lambda, which `Act` clears first and the lambda then sets; keep that order. `Result.ErrorText` is a property computed from the messages, so it needs no extra guard. `CheckResultValue` is a static class of string constants in Registrar.Core (compare with `==`).

`src/Registrar.App/wwwroot/app.css`: append, using only `var(--org-*)` tokens (no hex), and add `:root { color-scheme: light; }` near the top of the file. Required rules:

- `.step`, `.actions`, `.result`: spacing only (margins and padding).
- `.path`: monospace, `--org-secondary-gray`, `word-break: break-all`.
- `.hint`: `--org-secondary-gray`.
- Buttons: border `1px solid var(--org-border)`; `button.primary` filled with `--org-primary-blue` and white text; `button:disabled` visibly dimmed with `cursor: not-allowed`. A visible focus style for ALL buttons and radio inputs: `button:focus-visible, input:focus-visible { outline: 3px solid var(--org-accent-blue); outline-offset: 2px; }`.
- `label.file`: block, padded, easy to hit (at least 2rem tall).
- `.mode`: `--org-heading-navy`, slightly emphasized.
- `.report .line`: `white-space: pre-wrap`; `.line.fail` uses `--org-danger`; `.line.pass` uses `--org-primary-blue`; `.line.warn` uses `--org-heading-navy` with `font-weight: 600` (warn red is reserved).
- `.banner`: larger and bold, with a left border in the matching token.
- `.summary`: normal text.
- Keep the existing `.error` and `.notice` rules (the page uses both). `.problem` needs no extra rule beyond `.error`.

- [ ] **Step 4: Run the page tests, then everything**

Run: `dotnet test tests/Registrar.App.Tests --configuration Release`
Expected: PASS (all `HomePageTests`, `AppServicesTests`, `ScreenErrorTests`).

Run: `dotnet build Registrar.sln --configuration Release` then `dotnet test Registrar.sln --configuration Release`
Expected: 0 warnings, all pass.

If a test finds the page wrong, fix the page. If a test's selector is wrong against valid markup (for example the `input[checked]` attribute selector in the first test, since Blazor renders `checked` as a property), adjust the selector or assert on the radio's `checked` attribute through `cut.FindAll("label.file input")` and report which you changed and why. Do not weaken an assertion about behavior.

- [ ] **Step 5: Commit**

```bash
git add -A src/Registrar.App tests/Registrar.App.Tests
git status --short
git commit -m "feat: the one-page operator window

Folder, optional registry, file list, read-only mode, Check and Build, a result
panel built from the same presenter the console uses, and open or copy actions.
Every handler that touches the machine catches and reports beside the controls.

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```
`git status --short` before committing must show only `src/Registrar.App` and `tests/Registrar.App.Tests` changes plus the untracked `fixtures/ORG-GOV-AUP-004.agrdraft`, which must not be staged.

---

### Task 4: Health check, spec amendment, smoke launch

**Files:**
- Create: `src/Registrar.Pipeline/DoctorPresenter.cs`, `src/Registrar.App/Components/HealthPanel.razor`
- Modify: `src/Registrar.App/Components/Home.razor`, `src/Registrar.App/wwwroot/app.css`, `docs/superpowers/specs/2026-10-08-simple-operator-window-design.md`
- Test: `tests/Registrar.Tests/Pipeline/DoctorPresenterTests.cs`; add tests to `tests/Registrar.App.Tests/HomePageTests.cs`

**Interfaces:**
- Consumes: `Doctor.Run(string repoRoot, string? registryPath) : DoctorReport`, `DoctorReport { Checks, Healthy }`, `DoctorCheck(Name, Status, Detail)`, `DoctorStatus { Ok, Info, Warn, Fail }`, `PresenterLine`, `LineStyle`, `AppState.RepoRoot`.
- Produces: `static IReadOnlyList<PresenterLine> DoctorPresenter.Present(DoctorReport report, bool registryChosen)`. `HealthPanel` with parameter `string? RegistryPath` and a button `button.health`; output in `.health .line`.

- [ ] **Step 1: Write the failing tests**

`tests/Registrar.Tests/Pipeline/DoctorPresenterTests.cs`:

```csharp
namespace Registrar.Tests.Pipeline;

public class DoctorPresenterTests
{
    private static DoctorReport Report(params DoctorCheck[] checks) => new(checks);

    [Fact]
    public void A_healthy_report_ends_with_a_healthy_line_in_pass_style()
    {
        var lines = DoctorPresenter.Present(
            Report(new DoctorCheck("config load", DoctorStatus.Ok, "loaded 6 config file(s)")), registryChosen: false);

        Assert.Contains(lines, l => l.Text.StartsWith("OK", StringComparison.Ordinal) && l.Text.Contains("config load"));
        var last = lines[^1];
        Assert.Equal(LineStyle.Pass, last.Style);
        Assert.Equal("Healthy: everything the Registrar needs is in place.", last.Text);
    }

    [Fact]
    public void A_failing_check_makes_the_report_not_healthy_and_is_styled_as_a_failure()
    {
        var lines = DoctorPresenter.Present(
            Report(new DoctorCheck("schemas", DoctorStatus.Fail, "cannot find agr.schema.json")), registryChosen: true);

        Assert.Contains(lines, l => l.Style == LineStyle.Fail && l.Text.Contains("cannot find agr.schema.json"));
        Assert.Equal(LineStyle.Fail, lines[^1].Style);
        Assert.StartsWith("Not healthy", lines[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_line_registry_hint_is_replaced_with_window_wording()
    {
        var cliHint = new DoctorCheck("registry", DoctorStatus.Info,
            "no registry supplied (file-system mode); pass --registry <csv> to check an export");

        var lines = DoctorPresenter.Present(Report(cliHint), registryChosen: false);

        Assert.DoesNotContain(lines, l => l.Text.Contains("--registry", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Text.Contains("none chosen", StringComparison.Ordinal));
    }

    [Fact]
    public void Warnings_are_styled_as_warnings_and_do_not_fail_the_report()
    {
        var lines = DoctorPresenter.Present(
            Report(new DoctorCheck("webview user data", DoctorStatus.Warn, "folder is not writable")), registryChosen: false);

        Assert.Contains(lines, l => l.Style == LineStyle.Warn);
        Assert.Equal(LineStyle.Pass, lines[^1].Style);
    }
}
```

Append to `tests/Registrar.App.Tests/HomePageTests.cs`:

```csharp
    [Fact]
    public void The_health_check_runs_and_shows_one_line_per_check_with_a_verdict()
    {
        var cut = Render(TempDir.Create());

        cut.Find("button.health").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".health .line")));
        var text = cut.Find(".health").TextContent;
        Assert.Contains("config load", text, StringComparison.Ordinal);
        Assert.DoesNotContain("--registry", text, StringComparison.Ordinal);
        Assert.Matches("Healthy|Not healthy", text);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --configuration Release --filter "FullyQualifiedName~DoctorPresenterTests|FullyQualifiedName~The_health_check"`
Expected: build FAIL (`DoctorPresenter` does not exist).

- [ ] **Step 3: Write the code**

`src/Registrar.Pipeline/DoctorPresenter.cs`:

```csharp
namespace Registrar.Pipeline;

// How the health check reads in the window. The console prints Doctor's own text, which
// tells the reader to pass a command-line option; a window operator cannot act on that, so
// the one hint that names a CLI flag is replaced here. The CLI output is untouched because
// this class is only called by the window.
public static class DoctorPresenter
{
    private const string CliRegistryHint = "--registry";

    public static IReadOnlyList<PresenterLine> Present(DoctorReport report, bool registryChosen)
    {
        var lines = new List<PresenterLine>();

        foreach (var check in report.Checks)
        {
            var (tag, style) = check.Status switch
            {
                DoctorStatus.Ok => ("OK", LineStyle.Pass),
                DoctorStatus.Info => ("INFO", LineStyle.Normal),
                DoctorStatus.Warn => ("WARN", LineStyle.Warn),
                _ => ("FAIL", LineStyle.Fail)
            };

            var detail = check.Status == DoctorStatus.Info
                && !registryChosen
                && check.Detail.Contains(CliRegistryHint, StringComparison.Ordinal)
                    ? "none chosen (optional). Choose a registry file above to check it."
                    : check.Detail;

            lines.Add(new PresenterLine($"{tag,-5} {check.Name,-22} {detail}", style));
        }

        lines.Add(new PresenterLine("", LineStyle.Normal));
        lines.Add(report.Healthy
            ? new PresenterLine("Healthy: everything the Registrar needs is in place.", LineStyle.Pass)
            : new PresenterLine("Not healthy: one or more checks failed. Send this list to whoever supports you.", LineStyle.Fail));

        return lines;
    }
}
```

`src/Registrar.App/Components/HealthPanel.razor`:

```razor
@inject AppState State

@* A button that runs the same self-check the console's doctor verb runs, in window wording. *@

<section class="health-check">
    <button type="button" class="health" @onclick="RunHealth">Health check</button>

    @if (running)
    {
        <p class="notice" role="status">Checking...</p>
    }
    @if (problem is not null)
    {
        <p class="problem error" role="alert">@problem</p>
    }
    @if (lines is not null)
    {
        <div class="health" aria-live="polite">
            @foreach (var line in lines)
            {
                <div class="line @StyleClass(line.Style)">@line.Text</div>
            }
        </div>
    }
</section>

@code {
    [Parameter] public string? RegistryPath { get; set; }

    private IReadOnlyList<PresenterLine>? lines;
    private string? problem;
    private bool running;

    private static string StyleClass(LineStyle style) => style switch
    {
        LineStyle.Pass => "pass",
        LineStyle.Warn => "warn",
        LineStyle.Fail => "fail",
        _ => ""
    };

    private async Task RunHealth()
    {
        if (running) return;
        problem = null;
        lines = null;
        running = true;
        var registryPath = RegistryPath;
        try
        {
            var report = await Task.Run(() => Doctor.Run(State.RepoRoot, registryPath));
            lines = DoctorPresenter.Present(report, registryChosen: registryPath is not null);
        }
        catch (Exception ex)
        {
            problem = "The health check could not run: " + ex.Message;
        }
        finally
        {
            running = false;
        }
    }
}
```

Caution: the button has class `health` and so does the output `div`. The test selects `button.health` and `.health .line`; keep the output container `div.health` and the button `button.health` distinct by tag in selectors, or rename the container class to `health-output` and update the test selector `.health-output .line` and `.health-output` accordingly. Prefer the rename, and make the test and CSS agree.

In `Home.razor` add, after the `<div class="actions">` block and before the `problem` paragraph:

```razor
<HealthPanel RegistryPath="@registry" />
```

(`RegistryPath="@registry"` is an expression; a quoted bare word on a `string` parameter would be a literal.)

CSS: add `.health-output .line` styles reusing the report line rules (pre-wrap, monospace, the same pass/warn/fail colors) and spacing for `.health-check`.

- [ ] **Step 4: Amend the spec**

In `docs/superpowers/specs/2026-10-08-simple-operator-window-design.md`, "What the window does" item 3: replace the drag-and-drop sentence with "An Add a file button opens a native file picker and selects the chosen file. Drag and drop is deferred: the embedded browser swallows OS file drops and only exposes a path through a web-message bridge that cannot be verified without a person at the window, so it is its own small unit after the owner has run the window." Add a line to the Units table or below it: `SW-3 shipped without drag and drop (see item 3).` Keep ASCII, no dashes.

- [ ] **Step 5: Build, test, smoke-launch**

Run: `dotnet build Registrar.sln --configuration Release` then `dotnet test Registrar.sln --configuration Release`
Expected: 0 warnings, all pass. Report both project counts.

Smoke launch (PowerShell). Record the PID you start and stop only that PID:

```powershell
$exe = Get-ChildItem -Recurse -Path src/Registrar.App/bin/Release -Filter Registrar.App.exe | Select-Object -First 1
$p = Start-Process -FilePath $exe.FullName -PassThru
Start-Sleep -Seconds 10
$alive = -not $p.HasExited
$title = $p.MainWindowTitle
if ($alive) { Stop-Process -Id $p.Id }
"alive=$alive title='$title' pid=$($p.Id)"
```
Expected: `alive=True`, non-empty title. If the process exited, report the exit code and STOP. The smoke proves the window opens and every real registration resolves at startup (the page injects them on first render), but it cannot see the page: the owner's by-hand run is the acceptance.

- [ ] **Step 6: Commit**

```bash
git add -A src/Registrar.App src/Registrar.Pipeline tests docs/superpowers/specs/2026-10-08-simple-operator-window-design.md
git status --short
git commit -m "feat: add the window's health check and defer drag and drop

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```
Check `git status --short` first: nothing outside those paths may be staged and `fixtures/ORG-GOV-AUP-004.agrdraft` must remain untracked.

---

## Owner acceptance (by hand, after merge of this PR's branch build)

Not a task for the agents. The spec's acceptance requires a person to run the window. Steps for the owner: run `Registrar.App.exe`; choose a folder with a fixture AGR; press Check and read the rows; press Build and open the document; break the file and press Build to see the fix instructions and Copy failures; press Health check. Anything confusing is a defect to report before SW-4 writes the docs.

## Self-review

**Spec coverage.** Working folder (Task 3), registry remembered (Tasks 1, 3), AGR list most recent first and drag and drop (list: Task 3; drop: deferred with an Add a file button and the spec amended in Task 4), read-only mode (Tasks 1, 3), Check and Build (Tasks 1, 3), result panel from the presenter (Task 3), Open document or report or folder and Copy failures (Task 3), Health check (Task 4). "Notes for SW-3": wrap the call (`GuardedRun`), Doctor wording (`DoctorPresenter`), razor usings, focus style, `color-scheme`, `TryReadMode` moved (Task 1). Spec Acceptance points a-b are covered by tests; the by-hand run is the owner's.

**Placeholders.** None. Two conditional notes (Blazor `checked` selector; `health` class collision) say what to check and what to do.

**Type consistency.** `IAgrRunner.Check/Build(string, string?)` match between Task 1, the fakes, `AppServices` and `Home`. `AppState(EngineContext, string)` matches `AppServices`, the placeholder test fix and `HomePageTests`. `MenuSettings.WithRegistry` and `RegistryPath` match Tasks 1 and 3. `ResultPanel` parameter `Result` matches `Home`. `HealthPanel.RegistryPath` matches Home's use.
