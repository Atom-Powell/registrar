# SW-2: Retire the authoring screens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the GUI-6 through GUI-8 authoring screens, their draft and build glue, and their tests from `Registrar.App`, leaving a window that starts, shows a placeholder page, and still has the app-wide error net, so SW-3 can build the real page on a clean base.

**Architecture:** Pure deletion plus the minimum edits that keep the remaining shell compiling and running. The shell that stays is the WPF host (`App.xaml`, `MainWindow`), the Blazor `Routes` with its `ErrorBoundary` and `ScreenError`, `wwwroot/index.html`, the design-token stylesheet link, and a reduced `AppState`. Core authoring code (`AgrAuthoring`, `DraftEdits`, `EditorForm`, `EditorRail`, `DraftCreation`, `FieldPath`, `EngagementScan`, the `.agrdraft` format, `GovernorBrief`, `ImportParser`) is NOT touched here; it is dead code for a follow-up PR with its own review.

**Tech Stack:** .NET 8, WPF hosting Blazor (`Microsoft.NET.Sdk.Razor`), bUnit, xUnit. Spec: `docs/superpowers/specs/2026-10-08-simple-operator-window-design.md` (Retirement section).

## Global Constraints

- Layering: `Registrar.App` is a peer of `Registrar.Cli`; neither may reference the other. App references Core, Validation and Pipeline only.
- Do NOT delete or edit anything under `src/Registrar.Core`, `src/Registrar.Cli`, `src/Registrar.Pipeline` or `tests/Registrar.Tests`. SW-2 touches `src/Registrar.App`, `tests/Registrar.App.Tests`, and the doc notes named below.
- The app-wide error net stays: `Routes.razor`'s `ErrorBoundary` with `ScreenError`, and `App.xaml.cs`'s dispatcher handler. An exception escaping a Blazor event handler in this WPF host takes the process down, and the later page depends on this net.
- The startup config guard in `MainWindow.xaml.cs` and the WebView2 user-data-folder setup in `App.xaml.cs` stay unchanged.
- Rule 8: US English, no emojis, no em or en dashes in code, comments, config or docs.
- Build and test with `--configuration Release`. Zero warnings.
- Do not terminate processes this session did not start. A smoke launch of the app must record its own PID and stop only that PID.
- Ship as a PR to `main`, never a direct push.

## File Structure

| Path | Action |
|---|---|
| `src/Registrar.App/Components/Editor.razor`, `EditorHeader.razor`, `NewArtifact.razor`, `Engagement.razor`, `Import.razor`, `IdentityPane.razor`, `SectionPanel.razor`, `ProseBlock.razor`, `RadioBlock.razor`, `ClauseBlock.razor`, `ReadOnlyBlock.razor`, `ValidationRail.razor` | delete |
| `src/Registrar.App/DraftSettler.cs`, `src/Registrar.App/BuildRunner.cs` | delete |
| `src/Registrar.App/wwwroot/editor.js` | delete |
| `src/Registrar.App/Components/Home.razor` | replace with a placeholder page |
| `src/Registrar.App/AppState.cs` | reduce to `Engine`, `Folder`, `RegistryCsvPath` |
| `src/Registrar.App/Components/ScreenError.razor` | reword (no draft or engagement list) |
| `src/Registrar.App/Components/Routes.razor` | stale comment edits only; behavior unchanged |
| `src/Registrar.App/wwwroot/index.html` | drop the `editor.js` script tag |
| `src/Registrar.App/wwwroot/app.css` | remove rules whose only users were the deleted components |
| `tests/Registrar.App.Tests/*` | delete every test file except `ScreenErrorTests.cs`; add `HomePlaceholderTests.cs` |
| `docs/tech-debt.md` | mark items about deleted code as obsolete (note only) |

---

### Task 1: Delete the retired screens, glue and tests; keep the shell compiling

**Files:**
- Delete: the Components, `DraftSettler.cs`, `BuildRunner.cs` and test files listed above
- Modify: `src/Registrar.App/AppState.cs`, `src/Registrar.App/Components/Home.razor`, `src/Registrar.App/Components/ScreenError.razor`, `src/Registrar.App/Components/Routes.razor`
- Create: `tests/Registrar.App.Tests/HomePlaceholderTests.cs`

**Interfaces:**
- Consumes: `EngineContext` (Registrar.Validation) already registered as a singleton in `MainWindow`.
- Produces: `AppState` with `EngineContext Engine { get; }`, `string? Folder { get; set; }`, `string? RegistryCsvPath { get; set; }` (SW-3 builds on exactly these three). `Home` at route `/` rendering an element with class `placeholder`. `ScreenError` keeps parameters `Exception Error` and `EventCallback OnRecover`, root `section.screen-error`, message `.what`, button `button.recover`.

- [ ] **Step 0: Baseline**

Run: `dotnet test Registrar.sln --configuration Release`
Record the Registrar.Tests and Registrar.App.Tests counts. Registrar.Tests must not change by the end of this unit. Registrar.App.Tests will fall to the ScreenErrorTests count plus the new Home test.

- [ ] **Step 1: Write the failing Home placeholder test**

Create `tests/Registrar.App.Tests/HomePlaceholderTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Registrar.App.Components;
using Registrar.Core;
using Registrar.Validation;
using Xunit;

namespace Registrar.App.Tests;

// The page that stands in until SW-3 lands. It exists so the window has a route to render
// and so the app-wide error net in Routes.razor has something to wrap. It must say plainly
// that nothing is wired up yet, rather than offer controls that do nothing.
public class HomePlaceholderTests : TestContext
{
    private static EngineContext LoadEngine()
        => EngineContext.Load(Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName);

    [Fact]
    public void Home_renders_a_placeholder_and_no_controls()
    {
        Services.AddSingleton(new AppState(LoadEngine()));

        var cut = RenderComponent<Home>();

        Assert.Contains("not wired up yet", cut.Find(".placeholder").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("button"));
        Assert.Empty(cut.FindAll("input"));
    }
}
```

If `EngineContext.Load` or `ConfigLocator.FindSchema` need a different namespace or the Registrar.App.Tests project cannot see the schema folder from its output directory, look at how the deleted `EngagementComponentTests` or `HomeReopenTests` obtained an `EngineContext` (read them BEFORE deleting in Step 3) and copy that exact approach. Do not change production code to make the test pass.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Registrar.App.Tests --configuration Release --filter "FullyQualifiedName~HomePlaceholderTests"`
Expected: FAIL. The current `Home.razor` renders toolbar inputs and buttons, so the assertion on `.placeholder` fails.

- [ ] **Step 3: Delete the retired code and its tests**

```bash
cd /c/GitRepos/The-Registrar
for f in Editor EditorHeader NewArtifact Engagement Import IdentityPane SectionPanel ProseBlock RadioBlock ClauseBlock ReadOnlyBlock ValidationRail; do
  git rm src/Registrar.App/Components/$f.razor
done
git rm src/Registrar.App/DraftSettler.cs src/Registrar.App/BuildRunner.cs src/Registrar.App/wwwroot/editor.js
for f in BlockEditorComponentTests BuildRunnerTests DraftSettlerTests EditorShellComponentTests EngagementComponentTests EngagementOpenTests HomeReopenTests ImportComponentTests NewArtifactComponentTests ValidationRailComponentTests; do
  git rm tests/Registrar.App.Tests/$f.cs
done
ls tests/Registrar.App.Tests/*.cs
```
Expected listing: `HomePlaceholderTests.cs` and `ScreenErrorTests.cs` only.

- [ ] **Step 4: Replace Home**

Overwrite `src/Registrar.App/Components/Home.razor`:

```razor
@page "/"

@* A stand-in until the simple window page lands (SW-3). It offers no controls on purpose: a
   button that does nothing is the kind of confusion this window is being rebuilt to remove.
   The CLI (Registrar.cmd, or registrar.exe) does everything this page will do. *@

<section class="placeholder">
    <h1>The Registrar</h1>
    <p>The window is not wired up yet. Use the command line (Registrar.cmd) to validate and build.</p>
</section>
```

- [ ] **Step 5: Reduce AppState**

Overwrite `src/Registrar.App/AppState.cs`:

```csharp
using Registrar.Validation;

namespace Registrar.App;

// What the open window needs to remember. A bag of values with no rules: every decision
// about validity and output belongs to Registrar.Core and Registrar.Pipeline, and a
// condition that lived here would be one the fast suite cannot reach.
public sealed class AppState
{
    public AppState(EngineContext engine) => Engine = engine;

    public EngineContext Engine { get; }

    // The working folder the operator chose, if any.
    public string? Folder { get; set; }

    // The ID Registry export the operator chose, if any. Null means no registry, which
    // the registry layer already reports as a skipped check.
    public string? RegistryCsvPath { get; set; }
}
```

- [ ] **Step 6: Reword ScreenError and fix Routes comments**

In `src/Registrar.App/Components/ScreenError.razor` change the advice paragraph and button text so nothing mentions a draft or an engagement list:

```razor
    <p class="advice">
        Nothing further was written by the step that failed. Anything already saved is still
        on disk. Go back and try again; if it happens again, send this message along with the
        file you were working on.
    </p>

    <button class="recover" @onclick="OnRecover">Back to the start</button>
```

Leave the `ScreenError` explanatory comment block and parameters as they are. In `src/Registrar.App/Components/Routes.razor`, edit only comments that name deleted things: in the `ErrorBoundary` comment, replace the sentence naming "GUI-8 closed the Build call site and the Import handlers close the other adversarial one; those local catches stay, ..." with "Handlers that can fail on the operator's input should still catch and report beside the control the operator just used; this is the floor under all of them.", and in the `Recover` comment replace "The engagement list rebuilds from disk, so it is the one screen that cannot depend on whatever went wrong." with "The start page holds no state of its own, so it is the one screen that cannot depend on whatever went wrong." Behavior stays identical.

- [ ] **Step 7: Build and run the tests**

Run: `dotnet build Registrar.sln --configuration Release`
Expected: 0 errors, 0 warnings. If the compiler names a remaining reference to a deleted type, remove that reference only if it is dead glue in `src/Registrar.App`; if it points into Core, Cli or Pipeline, STOP and report BLOCKED (SW-2 must not touch those).

Run: `dotnet test Registrar.sln --configuration Release`
Expected: Registrar.Tests unchanged from the Step 0 baseline; Registrar.App.Tests equals the ScreenErrorTests count plus 1 (the Home test now passes). Report both numbers.

- [ ] **Step 8: Commit**

```bash
git add -A src/Registrar.App tests/Registrar.App.Tests
git status --short
git commit -m "refactor: retire the GUI-6 through GUI-8 authoring screens

Removes the editor, new artifact, engagement and import screens, the draft settler
and build runner glue, and their tests. The window keeps its shell, the error net
and a placeholder Home page; SW-3 builds the real page on it. Core authoring code is
untouched and is dead code for a follow-up PR.

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```
Before committing, `git status --short` must show only changes under `src/Registrar.App` and `tests/Registrar.App.Tests`, plus the untracked `fixtures/ORG-GOV-AUP-004.agrdraft` which must NOT be staged.

---

### Task 2: Remove dead page assets and prove the window still starts

**Files:**
- Modify: `src/Registrar.App/wwwroot/index.html`
- Modify: `src/Registrar.App/wwwroot/app.css`
- Modify: `docs/tech-debt.md`

**Interfaces:**
- Consumes: the shell from Task 1.
- Produces: an `index.html` that loads only `org-tokens.css`, `app.css` and `blazor.webview.js`; an `app.css` with no rule that targets a deleted component; tech-debt notes that mark items about deleted code obsolete.

- [ ] **Step 1: Drop the editor script tag**

In `src/Registrar.App/wwwroot/index.html` delete the line `<script type="module" src="editor.js"></script>`.

- [ ] **Step 2: Remove dead CSS**

For every selector in `src/Registrar.App/wwwroot/app.css`, grep `src/Registrar.App` (razor and html) for its class names. Remove the rules whose classes no remaining markup uses. Expected to go: `.new-artifact*`, `.editor*`, `.editor-header*`, `.rail*`, `.identity-pane*`, `.section-panel*`, `.block*` and `.block-row*`, `.option-count*`, `.brief-block*`, `.paste-block*`, `.report*`, `.out-of-scope`, `.build-result*`, `.folder-state*`, and any toolbar rule only the old Home used. Keep: the body and heading base rules, `.screen-error*`, `.error`, `.notice`, `.placeholder` styling (add a short `.placeholder { max-width: 40rem; margin: 2rem auto; }` rule if none exists), and anything still referenced. Keep the CSS comments that explain a kept rule; remove comments about removed rules. Every color must remain a `var(--org-*)` token (no literal colors).

Report the list of removed selectors and the list kept, with the grep evidence for each kept one.

- [ ] **Step 3: Mark obsolete tech-debt items**

`docs/tech-debt.md` has items that describe code this unit deleted (search for `Editor.razor`, `DraftSettler`, `BuildRunner`, `NewArtifact`, `ValidationRail`, `SectionPanel`, `IdentityPane`, `Engagement.razor`, `Import.razor`, `ProseBlock`, `RadioBlock`, `ClauseBlock`, `ReadOnlyBlock`, `EditorHeader`, `editor.js`). Do not delete any item. For each item whose subject no longer exists, append one line at its end: `Obsolete (2026-10-08, SW-2): the code this item describes was removed when the authoring screens were retired.` Items that concern Core code stay untouched. Items already struck through stay as they are.

- [ ] **Step 4: Search for stragglers outside src and tests**

Run: `grep -rn "editor\.js\|DraftSettler\|BuildRunner" --include=*.ps1 --include=*.cmd --include=*.yml --include=*.json --include=*.csproj . 2>/dev/null | grep -v "/bin/\|/obj/\|node_modules\|\.superpowers\|docs/"`
Expected: no output. A hit in a script means that script would break; report it, and fix it only if it is a one-line removal inside `scripts/` or `.github/`.

- [ ] **Step 5: Build, test, and smoke-launch the window**

Run: `dotnet build Registrar.sln --configuration Release` then `dotnet test Registrar.sln --configuration Release`
Expected: 0 warnings; same counts as Task 1 Step 7.

Smoke launch (PowerShell). Record the PID you start and stop only that PID:

```powershell
$exe = Get-ChildItem -Recurse -Path src/Registrar.App/bin/Release -Filter Registrar.App.exe | Select-Object -First 1
$p = Start-Process -FilePath $exe.FullName -PassThru
Start-Sleep -Seconds 8
$alive = -not $p.HasExited
$title = $p.MainWindowTitle
if ($alive) { Stop-Process -Id $p.Id }
"alive=$alive title='$title' pid=$($p.Id)"
```
Expected: `alive=True`, and a non-empty title. If the process exited, report its exit code and any crash text and STOP: do not paper over it. This only proves the window opens and survives eight seconds; it cannot see the page, so the owner's by-hand run in SW-3 remains the real acceptance.

- [ ] **Step 6: Commit**

```bash
git add -A src/Registrar.App docs/tech-debt.md
git commit -m "chore: drop the dead page assets and mark obsolete tech-debt items

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```
Stage by explicit path; `fixtures/ORG-GOV-AUP-004.agrdraft` must remain untracked.

---

## Self-review

**Spec coverage.** The spec's Retirement list: Editor, EditorHeader, NewArtifact, Engagement, Import, IdentityPane, SectionPanel, ProseBlock, RadioBlock, ClauseBlock, ReadOnlyBlock, ValidationRail, Home (replaced), DraftSettler, BuildRunner, AppState (reduced), `wwwroot/editor.js`, and their tests: all in Task 1 Step 3 to 5 or Task 2 Step 1. "Not removed here" (Core authoring, `.agrdraft`): honored by the Global Constraints. "Kept" (design tokens, doctor WebView2 check): `org-tokens.css` link and the WebView2 setup are untouched. SW-4's doc updates are out of scope; only the obsolete notes in tech-debt, which SW-2 itself causes, are done here.

**Placeholders.** None. The one conditional (how the Home test gets an `EngineContext`) names where to look and forbids production-code changes.

**Type consistency.** `AppState(EngineContext)` constructor and the three properties match between Task 1's test, `AppState.cs`, and `MainWindow.xaml.cs` (which registers `AppState` as a singleton constructed from the `EngineContext` singleton, unchanged). `ScreenError` parameters and CSS hooks match `ScreenErrorTests`.
