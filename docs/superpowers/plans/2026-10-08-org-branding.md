# ORG branding of the operator window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the ORG visual design to the one-page operator window: brand lockup, button hierarchy, status banners with distinct icon shapes, a solid-fill "Copy failures for the Governor" button, and the notice and error treatments. A skin, not a redesign: no copy changes, no reordering, no new behavior.

**Architecture:** Colors stay config-driven. `config/style-rules.json` feeds `registrar emit-tokens`, which writes `config/generated/org-tokens.css`; `app.css` consumes those `var(--org-*)` tokens and declares no color of its own. The design needs two colors the token set lacks (white and a darker danger for hover), so two screen tokens are added to config with measured contrast. Markup changes are small: a logo image, five inline SVG icons, and a few CSS hooks. Everything else is `app.css`.

**Tech Stack:** .NET 8, Blazor in WPF, bUnit, xUnit, CSS. The design handoff is `C:\Users\ajpow\Downloads\ORG branded GUI design.zip` (a static 8-state HTML board and a README); it is a visual reference and is NOT committed or ported. Its facts are restated in the "Design spec" below.

## Global Constraints

- The window's structure, copy and one-page order do not change. Every existing label, button name, class hook and test selector keeps working: the 25 `Registrar.App.Tests` and 733 `Registrar.Tests` stay green, with existing tests edited only where this plan says so.
- `app.css` declares zero hex literals, zero `rgb()`/`hsl()`, and no named colors (`white`, `black`, `transparent`, and so on). Colors come only from `var(--org-*)`. A new color goes into `config/style-rules.json` `screen.colors` and is regenerated, never typed into CSS or markup. Inline SVG uses `currentColor`, never a hex fill.
- Token rules from `app.css`'s header still hold: control boundaries use `--org-border`, not `--org-border-neutral`; text uses `--org-heading-navy`, `--org-primary-blue`, `--org-accent-blue`, `--org-secondary-gray`, or `--org-danger`; warn red (`--org-warn-red-border`) is never used for on-screen warnings. `--org-warn-red-fill` may be used as the background behind danger text (that pairing is already measured).
- Status must not rely on color alone: each result state keeps its text word (PASS, PASS WITH WARNINGS, FAIL) and gets a distinct icon shape; the Copy failures button stays visibly different in fill and shape from the ghost buttons.
- No emojis, no em or en dashes, US English in code, config strings and comments (rule 8).
- Layering unchanged: App references Core, Validation and Pipeline only; no engine logic in App. `Registrar.App` edits stay presentational.
- Build and test with `--configuration Release`. Zero warnings. Do not terminate processes this session did not start; a smoke launch records its own PID and stops only that.
- Ship as a PR to `main`. Do not edit `docs/ROADMAP.md` or `docs/DELIVERED.md` in this PR (PR #81 is editing them); a follow-up doc commit records this work after #81 merges.

## Design spec (from the handoff, all values are existing tokens unless marked NEW)

Type: `"Montserrat", Arial, sans-serif` for everything except paths and report/health output, which stay `Consolas, "Courier New", monospace`. Montserrat is the document font in `style-rules.json` (`visualIdentity.fonts`); it is NOT bundled in the app, so a machine without it falls back to Arial, exactly as the documents do.

| Element | Spec |
|---|---|
| Brand lockup | ORG cloud mark (26px tall image) + 1px vertical rule `--org-border-neutral` + "The Registrar" H1 (22px, 700, `--org-heading-navy`), inline at the top left. Subtle, not a header band. |
| Step headings | `--org-heading-navy`, 600 |
| Paths, hints | `--org-secondary-gray`; paths monospace |
| Fieldset | 1px `--org-border`, radius 6px |
| Radio rows | native radio, `accent-color: var(--org-primary-blue)`, 15px, each `label.file` min-height 34px, padding 4px 6px |
| Ghost buttons (Choose folder, Choose registry file, Clear, Add a file..., Health check, Open report, Open folder) | NEW `--org-surface` fill (white), 1px `--org-border`, radius 6px, text `--org-heading-navy`, 13px/600, padding 8px 16px, `white-space: nowrap`; hover `--org-table-row-alt-fill`; focus-visible 3px `--org-accent-blue` outline, offset 2px |
| Primary buttons (Check, Build, Open document) | fill `--org-dch-fill`, 2px `--org-primary-blue` border, text `--org-primary-blue`, 13px/700, radius 6px; hover `--org-header-right-cell-fill`; focus offset 3px. Check and Build look identical. |
| Copy failures button | solid `--org-danger` fill, 2px `--org-danger` border, text NEW `--org-surface`, 13px/700, clipboard icon before the label; hover NEW `--org-danger-hover` fill; focus offset 3px |
| Disabled | `opacity: 0.45`, `cursor: not-allowed` (buttons and radios) |
| Banner (the first report line, `.banner`) | 6px left border, 22px icon, 19px/700 text. PASS: `--org-primary-blue` border and text. WARN: `--org-heading-navy` border and text. FAIL: `--org-danger` border and text on `--org-warn-red-fill`. |
| Report and health lines | monospace 12.5px, line-height 1.7. pass `--org-primary-blue`; warn `--org-heading-navy` bold; fail `--org-danger` |
| `.notice` | `--org-primary-blue` text on `--org-dch-fill`, 4px `--org-accent-blue` left border |
| `.error` / `.problem` | `--org-danger` text on `--org-warn-red-fill`, 4px `--org-danger` left border, plus a warning-triangle icon before the text |
| Health check section | top rule 1px `--org-border` above the button |
| Icons | PASS: check in a circle. WARN: triangle with an exclamation mark. FAIL: X in a square. Error: warning triangle (reuse the WARN triangle shape is acceptable if it reads differently from the FAIL square). Clipboard for Copy failures. All are inline SVG with `aria-hidden="true"`, `stroke`/`fill` via `currentColor`. |

The mock's title strip, window card, shadow and page background (`#EDEFF2`) are presentational framing for the board and are NOT implemented: the real window has its own chrome and no page background.

## Decisions recorded for the owner (defaults applied)

1. **Two new screen tokens**, following the convention the `screen` block's comment already records for `danger` and `border`: `surface` = `#FFFFFF` (button fill and text on danger) and `dangerHover` = `#8A2124` (hover of the solid Copy failures button). `dangerHover` is a new brand value; the owner approves it by merging this PR. Contrast is measured and pinned in `ContrastTests` (white on `#A3282B` is about 7.3:1; white on `#8A2124` is higher).
2. **Logo is the existing raster** `assets/org-logo.png` (52x54), shown at 26px. It is crisp at 200 percent display scaling and slightly soft at 150 percent. The design's vector `OrgCloudMark` was not in the handoff; swap in an SVG later if ORG supplies one.
3. **Montserrat is not bundled.** The window uses it when installed and falls back to Arial, the same as the DOCX deliverables.

## File Structure

| Path | Action |
|---|---|
| `config/style-rules.json` | add `screen.colors.surface` and `screen.colors.dangerHover`; extend the `screen.$comment` |
| `config/generated/org-tokens.css` | regenerated by `registrar emit-tokens` (never hand-edited) |
| `tests/Registrar.Tests/Design/ContrastTests.cs` | add rows for the two new tokens |
| `tests/Registrar.Tests/Design/AppCssTests.cs` | create: the no-literal-colors guard and the token-existence guard |
| `src/Registrar.App/Registrar.App.csproj` | link `assets/org-logo.png` into `wwwroot` |
| `src/Registrar.App/Components/Icon.razor` | create: the five inline SVG icons |
| `src/Registrar.App/Components/Home.razor`, `ResultPanel.razor`, `HealthPanel.razor` | brand lockup, icons, class hooks; no copy or behavior change |
| `src/Registrar.App/wwwroot/app.css` | restyle per the spec |
| `tests/Registrar.App.Tests/HomePageTests.cs` | add branding hook tests |

---

### Task 1: Tokens and the CSS guards

**Files:**
- Modify: `config/style-rules.json`, `config/generated/org-tokens.css` (generated)
- Modify: `tests/Registrar.Tests/Design/ContrastTests.cs`
- Create: `tests/Registrar.Tests/Design/AppCssTests.cs`

**Interfaces:**
- Consumes: `registrar emit-tokens` (`src/Registrar.Cli/EmitTokensCommand.cs`), `TokenProjection` (derives `--org-surface` and `--org-danger-hover` from the camelCase config names), `Contrast.Ratio(string, string) : double`.
- Produces: tokens `--org-surface: #FFFFFF` and `--org-danger-hover: #8A2124` in the screen group of `org-tokens.css`; a test that fails if `app.css` contains a literal color or references a token that does not exist.

- [ ] **Step 1: Write the failing tests.**

Read `tests/Registrar.Tests/Design/ContrastTests.cs` in full first and add rows to its existing role table in the same style: `surface` on `danger` as text-on-fill meeting `TextMinimum`, and `surface` on `dangerHover` meeting `TextMinimum`. Then create `tests/Registrar.Tests/Design/AppCssTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Registrar.Core;
using Xunit;

namespace Registrar.Tests.Design;

// app.css states in its own header that it declares no color, because a hand-maintained
// color is the first crack in "the screen is on brand because it reads the brand". These
// tests make that sentence enforceable instead of aspirational.
public class AppCssTests
{
    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static string CssWithoutComments()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Registrar.App", "wwwroot", "app.css"));
        return Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
    }

    [Fact]
    public void app_css_declares_no_hex_or_functional_colors()
    {
        var css = CssWithoutComments();

        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", css);
        Assert.DoesNotMatch(@"\b(rgb|rgba|hsl|hsla)\(", css);
    }

    [Fact]
    public void app_css_uses_no_named_colors()
    {
        // Property values only, so "white-space" and class names are not matched.
        var css = CssWithoutComments();
        var offenders = Regex.Matches(css, @"(?<=:\s*)(?:[^;{}]*\s)?(white|black|transparent|red|blue|gray|grey|green|yellow|orange)\b(?=[^-])")
            .Select(m => m.Value.Trim()).ToList();

        Assert.True(offenders.Count == 0, "Named colors in app.css: " + string.Join(" | ", offenders));
    }

    [Fact]
    public void every_token_app_css_references_exists_in_the_generated_tokens()
    {
        var tokens = File.ReadAllText(Path.Combine(RepoRoot(), "config", "generated", "org-tokens.css"));
        var declared = Regex.Matches(tokens, @"(--org-[a-z0-9-]+)\s*:").Select(m => m.Groups[1].Value).ToHashSet();
        var used = Regex.Matches(CssWithoutComments(), @"var\((--org-[a-z0-9-]+)\)").Select(m => m.Groups[1].Value).ToHashSet();

        var missing = used.Where(u => !declared.Contains(u)).ToList();

        Assert.True(missing.Count == 0, "app.css uses undeclared tokens: " + string.Join(", ", missing));
    }
}
```

If the named-color regex proves unreliable against the real file (false positives or negatives), simplify it to a clear, testable rule (for example scan each declaration value after the colon for whole-word color keywords) and say so in your report; the intent is that no named color survives in `app.css`. Do not weaken the hex or functional-color test.

- [ ] **Step 2: Run to verify they fail or pass as expected.**

Run: `dotnet test tests/Registrar.Tests --configuration Release --filter "FullyQualifiedName~AppCssTests|FullyQualifiedName~ContrastTests"`
Expected: the contrast rows FAIL (tokens missing) and the `AppCssTests` PASS against the current `app.css` (it is already token-only). A pre-existing violation found by the guard is a finding to fix in Task 3, not to hide: report it.

- [ ] **Step 3: Add the tokens.**

In `config/style-rules.json`, in `screen.colors`, add `"surface": "#FFFFFF"` and `"dangerHover": "#8A2124"`. Extend the `screen.$comment` with two sentences recording: surface is the plain white used as button fill and as text on danger, introduced for the ORG branding of the window on 2026-10-08; dangerHover is a new brand value for the hover of the solid danger button, approved by the repo owner by merging that change, measuring (state the real measured ratio from your test) against white. No dashes in the comment.

Then regenerate: `dotnet run --project src/Registrar.Cli --configuration Release -- emit-tokens` (read `EmitTokensCommand.cs` first for its arguments and output path; the output must land at `config/generated/org-tokens.css`). Run `dotnet test tests/Registrar.Tests --configuration Release` to see which tests the config edit staleness affects (`GeneratedTokensAreCurrentTests`, config-hash goldens). Per the repo's recorded lesson, a `config/*.json` content edit stales every fixture golden's `configVersions` hash; regenerate them with the repo's own command (`registrar test --all --update`, see `FixtureTestRunner`/`TestCommand`) and report exactly which files changed and why. Do not hand-edit goldens.

- [ ] **Step 4: Run the full suite.**

Run: `dotnet test Registrar.sln --configuration Release`
Expected: all pass, 0 warnings, including the new contrast rows and the `AppCssTests`. Report the counts (expect Registrar.Tests above 733 by the number of tests you added).

- [ ] **Step 5: Commit.**

```bash
git add config/style-rules.json config/generated/org-tokens.css tests/Registrar.Tests/Design
git status --short
git commit -m "feat: add surface and dangerHover screen tokens; guard app.css against literal colors

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```
Stage any regenerated golden files too (by explicit path) and list them in the commit body. `fixtures/ORG-GOV-AUP-004.agrdraft` must never be staged.

---

### Task 2: Markup: logo, icons, class hooks

**Files:**
- Modify: `src/Registrar.App/Registrar.App.csproj`
- Create: `src/Registrar.App/Components/Icon.razor`
- Modify: `src/Registrar.App/Components/Home.razor`, `ResultPanel.razor`, `HealthPanel.razor`
- Test: `tests/Registrar.App.Tests/HomePageTests.cs`

**Interfaces:**
- Consumes: `assets/org-logo.png`; the existing component hooks (`.banner`, `.report .line`, `.problem`, `.copy-failures`, `.open-document`, and so on).
- Produces: DOM hooks the CSS (Task 3) and tests rely on: `header.brand` containing `img.brand-mark` (alt "ORG") and `h1`; `svg.icon[data-icon="pass|warn|fail|alert|clipboard"]` with `aria-hidden="true"`; `.banner` gets a `data-status="pass|warn|fail"` attribute; `button.copy-failures` contains the clipboard icon and the existing label text; every `.problem`/`.error` paragraph starts with the alert icon.

- [ ] **Step 1: Write the failing tests** (append to `HomePageTests.cs`; use the existing `Render`, `FolderWith`, `host` and `fake` members; add `using System.IO;` only if the file lacks it):

```csharp
    [Fact]
    public void The_page_shows_the_ORG_mark_beside_the_title()
    {
        var cut = Render(TempDir.Create());

        var mark = cut.Find("header.brand img.brand-mark");
        Assert.Equal("ORG", mark.GetAttribute("alt"));
        Assert.Contains("org-logo.png", mark.GetAttribute("src"), StringComparison.Ordinal);
        Assert.Equal("The Registrar", cut.Find("header.brand h1").TextContent.Trim());
    }

    [Fact]
    public void A_pass_banner_carries_a_pass_icon_and_the_word()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft.json"));

        cut.Find("button.check").Click();
        cut.WaitForAssertion(() => Assert.Contains("PASS", cut.Find(".banner").TextContent, StringComparison.Ordinal));

        Assert.Equal("pass", cut.Find(".banner").GetAttribute("data-status"));
        Assert.NotNull(cut.Find(".banner svg.icon[data-icon='pass']"));
        Assert.Equal("true", cut.Find(".banner svg.icon").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void A_warning_result_uses_a_different_icon_shape_than_pass_and_fail()
    {
        var cut = Render(FolderWith("agr-sample-ds-draft-warn.json"));

        cut.Find("button.check").Click();
        cut.WaitForAssertion(() => Assert.Contains("WARNINGS", cut.Find(".banner").TextContent, StringComparison.Ordinal));

        Assert.Equal("warn", cut.Find(".banner").GetAttribute("data-status"));
        Assert.NotNull(cut.Find(".banner svg.icon[data-icon='warn']"));
        Assert.Empty(cut.FindAll("button.copy-failures"));
    }

    [Fact]
    public void A_fail_banner_carries_a_fail_icon_and_the_copy_button_has_a_clipboard_icon()
    {
        var cut = Render(FolderWith("agr-sample-ds-invalid-schema.json"));

        cut.Find("button.check").Click();
        cut.WaitForAssertion(() => Assert.Contains("FAIL", cut.Find(".banner").TextContent, StringComparison.Ordinal));

        Assert.Equal("fail", cut.Find(".banner").GetAttribute("data-status"));
        Assert.NotNull(cut.Find(".banner svg.icon[data-icon='fail']"));
        Assert.NotNull(cut.Find("button.copy-failures svg.icon[data-icon='clipboard']"));
        Assert.Contains("Copy failures for the Governor", cut.Find("button.copy-failures").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors_start_with_an_alert_icon()
    {
        var folder = FolderWith("agr-sample-ds-draft.json");
        fake.Canned = FakeAgrRunner.UsageError("error: registry file not found: x.csv", folder);
        var cut = Render(folder, useFake: true);

        cut.Find("button.check").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".result .error svg.icon[data-icon='alert']")));
    }
```

- [ ] **Step 2: Run to verify they fail.**

Run: `dotnet test tests/Registrar.App.Tests --configuration Release --filter "FullyQualifiedName~HomePageTests"`
Expected: the five new tests FAIL (`ElementNotFoundException`); all pre-existing tests still PASS.

- [ ] **Step 3: Implement.**

`src/Registrar.App/Registrar.App.csproj`: next to the existing `org-tokens.css` `Content` link add `<Content Include="..\..\assets\org-logo.png" Link="wwwroot\org-logo.png" CopyToOutputDirectory="PreserveNewest" />` (read the existing entry and mirror it exactly).

`src/Registrar.App/Components/Icon.razor`: one component, one parameter. Passing `Kind="pass"` as a quoted LITERAL is correct here (the trap only bites when you mean an expression):

```razor
@* Inline icons. Each is drawn with currentColor so the stylesheet decides the color from a
   token, and each is aria-hidden because the adjacent text already says the same thing: the
   icons exist so status never depends on color alone, not to carry information of their own. *@

<svg class="icon" data-icon="@Kind" viewBox="0 0 24 24" width="@Size" height="@Size"
     fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"
     aria-hidden="true" focusable="false">
    @switch (Kind)
    {
        case "pass":
            <circle cx="12" cy="12" r="10" />
            <path d="M7.5 12.5l3 3 6-6.5" />
            break;
        case "warn":
            <path d="M12 3.5L2.5 20h19L12 3.5z" />
            <path d="M12 10v4.5" />
            <path d="M12 17.2v.1" />
            break;
        case "fail":
            <rect x="3" y="3" width="18" height="18" rx="2" />
            <path d="M8.5 8.5l7 7M15.5 8.5l-7 7" />
            break;
        case "alert":
            <path d="M12 3.5L2.5 20h19L12 3.5z" />
            <path d="M12 10v4.5" />
            <path d="M12 17.2v.1" />
            break;
        case "clipboard":
            <rect x="6" y="4" width="12" height="17" rx="2" />
            <path d="M9 4h6v3H9z" />
            <path d="M9 12h6M9 16h4" />
            break;
    }
</svg>

@code {
    [Parameter, EditorRequired] public string Kind { get; set; } = "";
    [Parameter] public int Size { get; set; } = 18;
}
```

The `warn` and `alert` shapes share a triangle by design (both mean "attention"); the FAIL square is the distinct one. If the two should differ more, make `alert` a triangle with a filled bang or use a different weight and say so; do not change `pass`, `warn` or `fail`.

`Home.razor`: wrap the title as `<header class="brand"><img class="brand-mark" src="org-logo.png" alt="ORG" width="26" height="27" /><span class="brand-rule" aria-hidden="true"></span><h1>The Registrar</h1></header>`. Put `<Icon Kind="alert" />` at the start of each `.problem` paragraph. Put no icon anywhere else in Home. Do not change any label text.

`ResultPanel.razor`: the banner element gets `data-status="@StatusOf(line)"` for the first line only (`pass`, `warn`, `fail` from the existing `LineStyle` mapping) and `<Icon Kind="@StatusOf(line)" Size="22" />` as its first child, inside the same `div.line.banner`; `StatusOf` maps `LineStyle.Pass` to "pass", `LineStyle.Warn` to "warn", `LineStyle.Fail` to "fail". Add `<Icon Kind="alert" />` at the start of the `.error` paragraph and of the `.problem` paragraph. Put `<Icon Kind="clipboard" />` before the label text inside `button.copy-failures`. Pass `Kind` expressions with `@`, never as a bare quoted name where an expression is meant.

`HealthPanel.razor`: add `<Icon Kind="alert" />` at the start of its `.problem` paragraph. Nothing else.

- [ ] **Step 4: Run the tests.**

Run: `dotnet test tests/Registrar.App.Tests --configuration Release` then `dotnet test Registrar.sln --configuration Release`
Expected: all pass (existing 25 plus the five new), 0 warnings. If an existing test asserts exact `.banner` text and an icon breaks it, the icon must not add text (`svg` has none); fix the markup, not the test.

- [ ] **Step 5: Commit.**

```bash
git add src/Registrar.App tests/Registrar.App.Tests
git status --short
git commit -m "feat: ORG brand lockup, status icons and styling hooks for the window

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The stylesheet, and verify

**Files:**
- Modify: `src/Registrar.App/wwwroot/app.css`

**Interfaces:**
- Consumes: the hooks from Task 2 and the tokens from Task 1 (`--org-surface`, `--org-danger-hover` included).
- Produces: the finished look; the `AppCssTests` guards stay green.

- [ ] **Step 1: Read `app.css` in full, then restyle it per the "Design spec" table.** Keep the file's explanatory header and update it to mention the two new tokens. Required: the font stack; `header.brand` laid out inline with `.brand-mark` 26px tall, `.brand-rule` a 1px vertical `--org-border-neutral` rule, the `h1` 22px/700; ghost, primary and copy-failures button rules exactly as specified (including hover and `:focus-visible` offsets 2px ghost, 3px primary and danger); disabled state; fieldset border and radius; radio rows; the banner variants driven by `data-status`; report line colors; `.notice` and `.error`/`.problem` including the icon alignment (`display: flex`, a gap, the icon not shrinking); the health check rule. Use `svg.icon { flex: none; }`. Every color is `var(--org-*)`; every existing class the page uses keeps working. If `.error` and `.problem` currently share one rule, keep it shared.
- [ ] **Step 2: Run the guards and the suite.**

Run: `dotnet test Registrar.sln --configuration Release`
Expected: all pass, 0 warnings, including `AppCssTests` (no literal colors, no undeclared tokens).

- [ ] **Step 3: Prove it renders.** Build, then run the window as a smoke test with a recorded PID and stop only that PID:

```powershell
dotnet build src/Registrar.App --configuration Release
$exe = Get-ChildItem -Recurse -Path src/Registrar.App/bin/Release -Filter Registrar.App.exe | Where-Object { $_.FullName -notmatch 'win-x64' } | Select-Object -First 1
$p = Start-Process -FilePath $exe.FullName -PassThru
Start-Sleep -Seconds 10
"alive=$(-not $p.HasExited) title='$($p.MainWindowTitle)' pid=$($p.Id)"
if (-not $p.HasExited) { Stop-Process -Id $p.Id }
Get-ChildItem (Split-Path $exe.FullName) -Recurse -Filter org-logo.png | Select-Object -First 1 FullName
```
Expected: `alive=True`, and `wwwroot\org-logo.png` present beside the executable. This proves the window opens and the logo ships; it cannot prove the look. The owner's by-hand look at the real window is the acceptance.

- [ ] **Step 4: Commit.**

```bash
git add src/Registrar.App/wwwroot/app.css
git commit -m "feat: restyle the window with the ORG design

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Owner acceptance (by hand, before merge)

Run the built window beside the design board: all eight states (empty, folder and files chosen, running, PASS, PASS WITH WARNINGS, FAIL, Health check output, error banner) at 100 and 150 percent display scaling. Check: the FAIL banner and the Copy failures button look different from the others without relying on color; focus rings are visible when you press Tab through every control; the logo is not blurry at your scaling; Montserrat appears if installed on the ORG machine.

## Self-review

**Spec coverage.** Every element in the handoff's Components list has a row in the Design spec and a place in Tasks 2 or 3; the eight states are all produced by existing component states (no new state); the three decisions the handoff left open (new colors, logo source, font bundling) are recorded with defaults. Not implemented, by design: the mock's window strip, card, shadow and page background.

**Placeholders.** None. The two spots that depend on reading the real file (the contrast test rows, the emit-tokens arguments) say what to read and what the result must be.

**Consistency.** Hook names (`header.brand`, `img.brand-mark`, `svg.icon[data-icon]`, `.banner[data-status]`, `button.copy-failures`) are the same in Task 2's tests and implementation and in Task 3's CSS. Token names (`--org-surface`, `--org-danger-hover`) follow `TokenProjection`'s camelCase to kebab-case rule from the config names `surface` and `dangerHover`.
