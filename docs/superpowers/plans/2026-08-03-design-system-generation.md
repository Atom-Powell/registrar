# Design System Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate the operator GUI's color tokens from `config/style-rules.json`, so the application and the DOCX deliverables cannot drift apart.

**Architecture:** A `screen` block joins `visualIdentity` in `style-rules.json`, carrying the two colors that are correct in print and fail WCAG on screen. `TokenProjection` in `Registrar.Core` derives CSS custom properties from both blocks; `registrar emit-tokens` writes them to `config/generated/org-tokens.css`; a byte-compare test fails the build if config and the committed stylesheet disagree. This mirrors `registrar emit-knowledge` exactly, which is the proven pattern in this repo.

**Tech Stack:** .NET 8, C#, `System.Text.Json`, `System.CommandLine`, xUnit.

## Global Constraints

These bind every task. Copied from `CLAUDE.md` and the design spec.

- **US English, no emojis, no em dashes or en dashes, no decorative unicode** in any source, config, comment, test name, generated output, or commit message. Use a plain hyphen, straight quotes, and three periods. `SourceHygieneTests` enforces this on engine source: never write a non-ASCII character as a literal; build it from a code point.
- **Zero LLM calls.** Nothing here contacts a model.
- **Configuration over code.** Color values live in `config/style-rules.json` only. No hex literal for a brand color may appear in `src/`. The two hex values in the *contrast test's* role table are test data describing intent, which is allowed and is the point of the test.
- **The document palette is not touched.** `visualIdentity` keeps every existing value. The renderer must never read `screen`.
- **Fail closed.** A duplicate token name is an error, not a last-one-wins.
- **Generated output is committed and guarded.** A config change without a regeneration must fail the build, with a message naming the command to run.
- **Line endings.** Any byte-compared generated file must be pinned to LF in `.gitattributes`. This is not theoretical: a byte-compare guard on a generated file already failed CI on this repo, because `core.autocrlf` rewrites the working copy on a fresh Windows clone and the guard passes locally while failing there.
- Run the full suite with `dotnet test Registrar.sln` from the repo root. It must be green with no new warnings.

---

## File Structure

**Create:**
- `src/Registrar.Core/Config/ScreenTokens.cs` - the bound `screen` block. Model only.
- `src/Registrar.Core/Contrast.cs` - WCAG 2.1 relative luminance and contrast ratio. Engine code, not test code, so the check and any future tooling share one implementation.
- `src/Registrar.Core/Design/TokenProjection.cs` - derives CSS custom properties from config. No file IO.
- `src/Registrar.Cli/EmitTokensCommand.cs` - loads config, renders, writes. Mirrors `EmitKnowledgeCommand`.
- `config/generated/org-tokens.css` - generated and committed.
- `tests/Registrar.Tests/Design/ContrastTests.cs`
- `tests/Registrar.Tests/Design/TokenProjectionTests.cs`
- `tests/Registrar.Tests/Design/GeneratedTokensAreCurrentTests.cs`

**Modify:**
- `config/style-rules.json` - add the `screen` block.
- `src/Registrar.Core/Config/ConfigBundle.cs` - add `Screen`.
- `src/Registrar.Core/Config/ConfigLoader.cs` - bind it.
- `src/Registrar.Cli/Program.cs` - register `emit-tokens`.
- `.gitattributes` - pin the generated stylesheet to LF.

---

### Task 1: The screen block

**Files:**
- Modify: `config/style-rules.json`
- Create: `src/Registrar.Core/Config/ScreenTokens.cs`
- Modify: `src/Registrar.Core/Config/ConfigBundle.cs`
- Modify: `src/Registrar.Core/Config/ConfigLoader.cs`
- Test: `tests/Registrar.Tests/ConfigLoaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ConfigBundle.Screen` of type `ScreenTokens`, with `IReadOnlyDictionary<string, string> Colors`. Tasks 2, 3, and 4 all consume it.

**Context you need.** `style-rules.json` is SHA-256 pinned into every fixture golden and into all four generated Governor knowledge documents. Adding a key re-pins roughly 42 files. That is expected. Regenerate them in this same commit so the churn is paid once, and confirm every regenerated file changed only its hash line before committing.

The commands to regenerate are:
```
dotnet run --project src/Registrar.Cli -- test --all --update
dotnet run --project src/Registrar.Cli -- emit-knowledge
```

`screen` is deliberately a sibling of `visualIdentity`, not nested inside it, so a reviewer can see at a glance that the renderer does not read it.

- [ ] **Step 1: Write the failing test**

Append to the existing class in `tests/Registrar.Tests/ConfigLoaderTests.cs`:

```csharp
    [Fact]
    public void The_screen_block_binds_its_colors()
    {
        var screen = SemanticTestSupport.Config().Screen;

        Assert.Equal("#A3282B", screen.Colors["danger"]);
        Assert.Equal("#7E8B9C", screen.Colors["border"]);
    }

    [Fact]
    public void The_screen_block_carries_only_the_two_colors_it_needs()
    {
        var screen = SemanticTestSupport.Config().Screen;

        // Two new brand values were approved, and no more. A third arriving without a
        // ratified decision is the drift this assertion exists to catch.
        Assert.Equal(2, screen.Colors.Count);
    }

    [Fact]
    public void The_document_palette_is_untouched_by_the_screen_block()
    {
        var config = SemanticTestSupport.Config();

        // The renderer reads visualIdentity. If a screen token ever leaks into it, a
        // document starts rendering a color that was chosen for a monitor.
        Assert.DoesNotContain("danger", config.Style.Colors.Keys);
        Assert.DoesNotContain("border", config.Style.Colors.Keys);

        // The count matters as much as the two names. Asserting only known keys would
        // let any other leak through, and "border" in particular is a name the document
        // palette could plausibly grow on its own.
        Assert.Equal(10, config.Style.Colors.Count);

        Assert.Equal("#C0504D", config.Style.Colors["warnRedBorder"]);
        Assert.Equal("#B8C1CC", config.Style.Colors["borderNeutral"]);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: FAIL to compile, "'ConfigBundle' does not contain a definition for 'Screen'".

- [ ] **Step 3: Add the config block**

In `config/style-rules.json`, add `screen` as a top-level sibling of `visualIdentity`:

```json
  "screen": {
    "$comment": "Screen-only tokens. Documents must never reference these, and the renderer never reads this block: visualIdentity is the document palette. These exist because three visualIdentity colors are correct in print and fail WCAG as interactive UI. danger #A3282B is a new brand value approved by the repo owner on 2026-08-03, needed because warn red is reserved by colorUsage and QA-SF-09 to mean remove-before-client; it measures 7.25:1 on white and 6.40:1 on warnRedFill. border #7E8B9C replaces borderNeutral, which measures 1.82:1 on white against a 3:1 control-boundary threshold.",
    "colors": {
      "danger": "#A3282B",
      "border": "#7E8B9C"
    }
  }
```

- [ ] **Step 4: Add the model and the binding**

Create `src/Registrar.Core/Config/ScreenTokens.cs`:

```csharp
namespace Registrar.Core.Config;

// The screen palette, deliberately separate from StyleRules. The renderer binds
// visualIdentity and must never see this block: these colors were chosen against WCAG
// thresholds for a monitor, and some of them would be wrong in print.
public sealed record ScreenTokens
{
    public required IReadOnlyDictionary<string, string> Colors { get; init; }
}
```

In `src/Registrar.Core/Config/ConfigBundle.cs`, add beside `Style`:

```csharp
    public required ScreenTokens Screen { get; init; }
```

In `src/Registrar.Core/Config/ConfigLoader.cs`, beside the existing `contentLint` line, add:

```csharp
        var screen = styleRoot["screen"]!.Deserialize<ScreenTokens>(Options)!;
```

and in the returned object initializer, beside `Style = style`:

```csharp
            Screen = screen,
```

- [ ] **Step 5: Regenerate the pinned files**

```bash
dotnet run --project src/Registrar.Cli -- test --all --update
dotnet run --project src/Registrar.Cli -- emit-knowledge
```

Then confirm nothing but hashes moved:

```bash
git diff --stat
git diff -- fixtures/ docs/governor/ | grep -E "^[+-]" | grep -vE "^(\+\+\+|---)" | grep -viE "style-rules|[0-9a-f]{64}"
```

The second command must print nothing. If it prints anything, a regeneration changed real content and you must stop and report it rather than commit.

- [ ] **Step 6: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add config/style-rules.json src/Registrar.Core/Config/ScreenTokens.cs src/Registrar.Core/Config/ConfigBundle.cs src/Registrar.Core/Config/ConfigLoader.cs tests/Registrar.Tests/ConfigLoaderTests.cs fixtures/ docs/governor/
git commit -m "feat: add the screen token block to style-rules"
```

---

### Task 2: Contrast measurement and the accessibility assertions

**Files:**
- Create: `src/Registrar.Core/Contrast.cs`
- Test: `tests/Registrar.Tests/Design/ContrastTests.cs`

**Interfaces:**
- Consumes: `ConfigBundle.Screen` and `ConfigBundle.Style` from Task 1.
- Produces: `Contrast.Ratio(string hexA, string hexB) -> double` and `Contrast.RelativeLuminance(string hex) -> double`. Nothing later in this plan consumes them; they exist for this test and for future tooling.

**Context you need, and why this task matters most.** The three contrast failures this unit fixes were found by a human reading a spec, once. Nothing stops the next palette edit from shipping an illegible token. This test is the standing guard, and it asserts a property of config rather than of code, in the same spirit as `Every_repaired_code_point_is_actually_prohibited` in `ConfigLoaderTests`, which caught real dead config.

The WCAG 2.1 formulas, which you must implement exactly:

Relative luminance, for each of R, G, B expressed as a 0-to-1 fraction:
- if the channel is at most `0.03928`, use `channel / 12.92`
- otherwise use `((channel + 0.055) / 1.055)` raised to the power `2.4`
- then `L = 0.2126 * R + 0.7152 * G + 0.0722 * B`

Contrast ratio: `(Lbrighter + 0.05) / (Ldarker + 0.05)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Design/ContrastTests.cs`:

```csharp
using Registrar.Core;
using Xunit;

namespace Registrar.Tests.Design;

public class ContrastTests
{
    private const string White = "#FFFFFF";

    // WCAG 2.1 minimums. Text needs 4.5:1; a control boundary that carries no text needs
    // 3:1. Named rather than inlined so a failure message can say which rule was missed.
    private const double TextMinimum = 4.5;
    private const double BoundaryMinimum = 3.0;

    [Theory]
    [InlineData("#FFFFFF", "#000000", 21.0)]
    [InlineData("#FFFFFF", "#FFFFFF", 1.0)]
    [InlineData("#000000", "#000000", 1.0)]
    public void The_ratio_matches_the_WCAG_reference_values(string a, string b, double expected)
    {
        Assert.Equal(expected, Contrast.Ratio(a, b), 2);
    }

    [Fact]
    public void The_ratio_is_symmetric()
    {
        Assert.Equal(Contrast.Ratio("#005CB9", White), Contrast.Ratio(White, "#005CB9"), 10);
    }

    // The blanket rule. Every screen color must be at least visible as a boundary, so a
    // token added later without a role entry below is still caught by something.
    [Fact]
    public void Every_screen_color_is_at_least_visible_on_white()
    {
        foreach (var (name, hex) in SemanticTestSupport.Config().Screen.Colors)
        {
            var ratio = Contrast.Ratio(hex, White);
            Assert.True(ratio >= BoundaryMinimum,
                $"screen.colors.{name} ({hex}) measures {ratio:F2}:1 on white, "
                + $"below the {BoundaryMinimum}:1 control-boundary minimum.");
        }
    }

    // The role table. Each pair is a place the GUI actually puts one color on another.
    [Fact]
    public void Every_screen_color_meets_the_threshold_for_the_role_it_serves()
    {
        var config = SemanticTestSupport.Config();
        var screen = config.Screen.Colors;
        var document = config.Style.Colors;

        var roles = new (string Label, string Foreground, string Background, double Minimum)[]
        {
            ("danger text on white", screen["danger"], White, TextMinimum),
            ("danger text on the warn fill", screen["danger"], document["warnRedFill"], TextMinimum),
            ("control boundary on white", screen["border"], White, BoundaryMinimum),
            ("primary blue text on white", document["primaryBlue"], White, TextMinimum),
            ("heading navy text on white", document["headingNavy"], White, TextMinimum),
            ("accent blue text on white", document["accentBlue"], White, TextMinimum),
            ("secondary gray text on white", document["secondaryGray"], White, TextMinimum)
        };

        foreach (var (label, fg, bg, minimum) in roles)
        {
            var ratio = Contrast.Ratio(fg, bg);
            Assert.True(ratio >= minimum,
                $"{label}: {fg} on {bg} measures {ratio:F2}:1, below the required {minimum}:1.");
        }
    }

    // The reason screen.danger exists at all. If warn red ever passed on its own fill,
    // the new brand value would be unnecessary and this unit's premise would be wrong.
    [Fact]
    public void The_document_warn_red_still_fails_on_its_own_fill_which_is_why_danger_exists()
    {
        var document = SemanticTestSupport.Config().Style.Colors;

        Assert.True(Contrast.Ratio(document["warnRedBorder"], document["warnRedFill"]) < TextMinimum);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~ContrastTests"`
Expected: FAIL to compile, "The name 'Contrast' does not exist".

- [ ] **Step 3: Implement**

Create `src/Registrar.Core/Contrast.cs`:

```csharp
using System.Globalization;

namespace Registrar.Core;

// WCAG 2.1 contrast measurement. This lives in the engine rather than in a test so the
// accessibility guard and any future tooling share one implementation: two copies of a
// formula are two chances to get it subtly wrong and never notice.
public static class Contrast
{
    public static double Ratio(string hexA, string hexB)
    {
        var a = RelativeLuminance(hexA);
        var b = RelativeLuminance(hexB);
        var brighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (brighter + 0.05) / (darker + 0.05);
    }

    public static double RelativeLuminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Channel(double value)
        => value <= 0.03928
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static (double R, double G, double B) Parse(string hex)
    {
        var text = hex.StartsWith('#') ? hex[1..] : hex;

        if (text.Length != 6)
            throw new ArgumentException($"'{hex}' is not a six-digit hex color.", nameof(hex));

        return (Component(text, 0), Component(text, 2), Component(text, 4));
    }

    private static double Component(string text, int offset)
        => int.Parse(text.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS. If `Every_screen_color_meets_the_threshold_for_the_role_it_serves` fails, a color value is wrong; fix the config, not the threshold.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Contrast.cs tests/Registrar.Tests/Design/ContrastTests.cs
git commit -m "feat: measure WCAG contrast and assert the screen palette meets it"
```

---

### Task 3: TokenProjection

**Files:**
- Create: `src/Registrar.Core/Design/TokenProjection.cs`
- Test: `tests/Registrar.Tests/Design/TokenProjectionTests.cs`

**Interfaces:**
- Consumes: `ConfigBundle.Style.Colors` and `ConfigBundle.Screen.Colors` from Task 1.
- Produces: `TokenProjection.Render(ConfigBundle config, string styleRulesSha256) -> string`, and `TokenProjection.TokenName(string configKey) -> string`. Task 4 consumes `Render`.

**Context you need.** Names are *derived*, never mapped. A lookup table would fall out of step with config the first time someone adds a color. The rule is: camelCase becomes kebab-case, prefixed `--org-`. So `primaryBlue` becomes `--org-primary-blue` and `headerRightCellFill` becomes `--org-header-right-cell-fill`.

Both color dictionaries feed one flat namespace, so a key present in both would silently emit a duplicate custom property where the last one wins. That must fail closed instead.

Every newline must be a literal `"\n"`, never `Environment.NewLine`. The output is byte-compared on a Windows dev machine and on a Windows CI runner from a fresh clone; `KnowledgeDocument` already does this for the same reason.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Design/TokenProjectionTests.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Design;
using Xunit;

namespace Registrar.Tests.Design;

public class TokenProjectionTests
{
    private static string Render()
        => TokenProjection.Render(SemanticTestSupport.Config(), "abc123");

    [Theory]
    [InlineData("danger", "--org-danger")]
    [InlineData("primaryBlue", "--org-primary-blue")]
    [InlineData("headerRightCellFill", "--org-header-right-cell-fill")]
    public void A_token_name_is_derived_from_its_config_key(string key, string expected)
    {
        Assert.Equal(expected, TokenProjection.TokenName(key));
    }

    [Fact]
    public void Every_document_color_becomes_a_token()
    {
        var config = SemanticTestSupport.Config();
        var css = Render();

        foreach (var (key, value) in config.Style.Colors)
            Assert.Contains($"{TokenProjection.TokenName(key)}: {value};", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_screen_color_becomes_a_token()
    {
        var config = SemanticTestSupport.Config();
        var css = Render();

        foreach (var (key, value) in config.Screen.Colors)
            Assert.Contains($"{TokenProjection.TokenName(key)}: {value};", css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_output_records_the_config_it_came_from()
    {
        var css = Render();

        Assert.Contains("registrar emit-tokens", css, StringComparison.Ordinal);
        Assert.Contains("abc123", css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_output_uses_line_feeds_only()
    {
        Assert.DoesNotContain("\r", Render(), StringComparison.Ordinal);
    }

    // Both dictionaries feed one flat namespace. A key in both would emit the property
    // twice and the last one would quietly win, which is a palette change nobody made.
    [Fact]
    public void A_token_name_claimed_by_both_blocks_fails_closed()
    {
        var config = SemanticTestSupport.Config();
        var colliding = config with
        {
            Screen = new ScreenTokens
            {
                Colors = new Dictionary<string, string> { ["primaryBlue"] = "#123456" }
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => TokenProjection.Render(colliding, "abc123"));

        Assert.Contains("--org-primary-blue", ex.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~TokenProjectionTests"`
Expected: FAIL to compile, "The type or namespace name 'Design' does not exist".

- [ ] **Step 3: Implement**

Create `src/Registrar.Core/Design/TokenProjection.cs`:

```csharp
using System.Text;
using Registrar.Core.Config;

namespace Registrar.Core.Design;

// Derives the operator GUI's color tokens from the same config the renderer uses, so
// the application and the documents it produces cannot drift onto different palettes.
//
// Names are derived rather than mapped. A lookup table would fall out of step with
// config the first time someone added a color, and the drift would be invisible until
// somebody put a screen beside a deliverable.
public static class TokenProjection
{
    public static string Render(ConfigBundle config, string styleRulesSha256)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        Collect(tokens, config.Style.Colors, "visualIdentity.colors");
        Collect(tokens, config.Screen.Colors, "screen.colors");

        var sb = new StringBuilder();
        sb.Append("/* Generated by registrar emit-tokens. Do not edit by hand: edits are\n");
        sb.Append("   overwritten on the next run, and a committed copy that disagrees with\n");
        sb.Append("   config fails the build.\n");
        sb.Append("\n");
        sb.Append("   Source: config/style-rules.json\n");
        sb.Append("   SHA-256: ").Append(styleRulesSha256).Append("\n");
        sb.Append("\n");
        sb.Append("   visualIdentity is the document palette, shared with the renderer.\n");
        sb.Append("   screen carries the tokens that exist only because some document\n");
        sb.Append("   colors fail WCAG as interactive UI. */\n");
        sb.Append(":root {\n");

        foreach (var (name, value) in tokens.OrderBy(t => t.Key, StringComparer.Ordinal))
            sb.Append("  ").Append(name).Append(": ").Append(value).Append(";\n");

        sb.Append("}\n");
        return sb.ToString();
    }

    // camelCase to kebab-case, prefixed. Derived so a new config color appears in the
    // stylesheet automatically and no table can fall out of step.
    public static string TokenName(string configKey)
    {
        var sb = new StringBuilder("--org-");

        foreach (var ch in configKey)
        {
            if (char.IsUpper(ch))
            {
                sb.Append('-').Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    // Fails closed on a collision rather than letting the second write win. Two blocks
    // feed one flat namespace, so a duplicate would be a palette change nobody decided.
    private static void Collect(
        Dictionary<string, string> tokens,
        IReadOnlyDictionary<string, string> colors,
        string source)
    {
        foreach (var (key, value) in colors)
        {
            var name = TokenName(key);

            if (tokens.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"Token {name} is claimed by more than one config block; {source} would "
                    + "overwrite an earlier definition. Rename one of the color keys.");
            }

            tokens[name] = value;
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Design/TokenProjection.cs tests/Registrar.Tests/Design/TokenProjectionTests.cs
git commit -m "feat: derive CSS color tokens from config"
```

---

### Task 4: emit-tokens and the regeneration guard

**Files:**
- Create: `src/Registrar.Cli/EmitTokensCommand.cs`
- Modify: `src/Registrar.Cli/Program.cs`
- Create: `config/generated/org-tokens.css`
- Modify: `.gitattributes`
- Test: `tests/Registrar.Tests/Design/GeneratedTokensAreCurrentTests.cs`

**Interfaces:**
- Consumes: `TokenProjection.Render(ConfigBundle, string)` from Task 3.
- Produces: `EmitTokensCommand.Run(string? outDir, TextWriter stdout, TextWriter stderr) -> int`, `EmitTokensCommand.DefaultRelativePath` (`"config/generated/org-tokens.css"`), and `EmitTokensCommand.RenderCurrent(string repoRoot) -> string`. The guard test consumes the last two.

**Context you need.** `src/Registrar.Cli/EmitKnowledgeCommand.cs` is the file to mirror: read it first. It resolves the repo root by walking up from `AppContext.BaseDirectory` to find the schema, then loads config from there. Use the same approach so both commands behave identically when run from a published binary.

The staleness guard must call the same render path the command does, or the two can diverge and the guard stops proving anything. That is why `RenderCurrent` exists as a shared entry point rather than the test rebuilding the arguments itself.

`config/style-rules.json`'s SHA-256 is already computed during config load and available as `ConfigBundle.Hashes["style-rules.json"]`. Use it rather than re-hashing the file.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Design/GeneratedTokensAreCurrentTests.cs`:

```csharp
using Registrar.Cli;
using Xunit;

namespace Registrar.Tests.Design;

// The stylesheet is committed so the application can consume it and a reviewer can see
// a palette change in a PR. That only stays honest if a config change without a
// regeneration fails the build. This is that gate, and it mirrors
// GeneratedKnowledgeIsCurrentTests exactly.
public class GeneratedTokensAreCurrentTests
{
    [Fact]
    public void The_committed_stylesheet_matches_a_fresh_render()
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var path = Path.Combine(repoRoot, EmitTokensCommand.DefaultRelativePath);

        Assert.True(File.Exists(path),
            $"{EmitTokensCommand.DefaultRelativePath} is missing. Run: registrar emit-tokens");

        var committed = File.ReadAllText(path);
        var fresh = EmitTokensCommand.RenderCurrent(repoRoot);

        Assert.True(committed == fresh,
            $"{EmitTokensCommand.DefaultRelativePath} is out of date with config. "
            + "Run: dotnet run --project src/Registrar.Cli -- emit-tokens");
    }

    // The failure this guards against is invisible locally and only appears on a fresh
    // clone, because core.autocrlf rewrites the working copy on checkout. It has already
    // cost this repo a CI run once.
    [Fact]
    public void The_committed_stylesheet_holds_no_carriage_returns()
    {
        var path = Path.Combine(SemanticTestSupport.RepoRoot(), EmitTokensCommand.DefaultRelativePath);

        Assert.DoesNotContain("\r", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~GeneratedTokensAreCurrentTests"`
Expected: FAIL to compile, "The name 'EmitTokensCommand' does not exist".

- [ ] **Step 3: Implement the command**

Create `src/Registrar.Cli/EmitTokensCommand.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Design;

namespace Registrar.Cli;

// Mirrors EmitKnowledgeCommand: load config, render, write. The generated stylesheet is
// committed and guarded, so a palette change that skips regeneration fails the build.
public static class EmitTokensCommand
{
    public const string DefaultRelativePath = "config/generated/org-tokens.css";

    // The single render path, shared with the staleness guard. If the test rebuilt the
    // arguments itself the two could diverge, and the guard would stop proving anything.
    public static string RenderCurrent(string repoRoot)
    {
        var config = ConfigLoader.Load(repoRoot);
        return TokenProjection.Render(config, config.Hashes["style-rules.json"]);
    }

    public static int Run(string? outPath, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var repoRoot = Directory.GetParent(
                Path.GetDirectoryName(ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

            var target = outPath ?? Path.Combine(repoRoot, DefaultRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            File.WriteAllText(target, RenderCurrent(repoRoot));
            stdout.WriteLine($"wrote {target}");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }
}
```

- [ ] **Step 4: Register the command**

In `src/Registrar.Cli/Program.cs`, immediately after the `emitKnowledge` block and before `var root = new RootCommand(...)`, add:

```csharp
var emitTokensOutOption = new Option<string?>("--out")
{
    Description = "File to write the stylesheet to. Defaults to config/generated/org-tokens.css."
};

var emitTokens = new Command("emit-tokens",
    "Generate the operator GUI's color tokens from config.");
emitTokens.Options.Add(emitTokensOutOption);
emitTokens.SetAction(parseResult =>
    EmitTokensCommand.Run(parseResult.GetValue(emitTokensOutOption), Console.Out, Console.Error));
```

Then add `emitTokens` to the root command's list, so it reads:

```csharp
var root = new RootCommand("The Registrar - governed artifact compiler")
    { validate, build, menu, test, doctor, emitKnowledge, emitTokens };
```

- [ ] **Step 5: Pin the generated file to LF**

Append to `.gitattributes`:

```
# The generated stylesheet is byte-compared against a fresh render by
# GeneratedTokensAreCurrentTests, and TokenProjection emits "\n" literals deliberately.
# Without this, core.autocrlf rewrites it to CRLF on checkout and the guard fails on a
# clean clone with no config change, exactly as it did for the generated knowledge files.
config/generated/*.css text eol=lf
```

- [ ] **Step 6: Generate the stylesheet**

```bash
dotnet run --project src/Registrar.Cli -- emit-tokens
```

Then confirm it is LF on disk and in the index:

```bash
git add config/generated/org-tokens.css
git ls-files --eol config/generated/org-tokens.css
```

Expected: `i/lf` and `w/lf`. If the working copy reports `w/crlf`, the attribute did not take effect; renormalize before continuing.

- [ ] **Step 7: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS.

- [ ] **Step 8: Prove the guard actually fails when it should**

This guard is worthless if it cannot fail. Temporarily change a color value in `config/style-rules.json` without regenerating, run the test, and confirm it fails with the message naming `emit-tokens`. Then revert the change and confirm the suite is green again. Report both outcomes; do not commit the temporary change.

- [ ] **Step 9: Commit**

```bash
git add src/Registrar.Cli/EmitTokensCommand.cs src/Registrar.Cli/Program.cs .gitattributes config/generated/org-tokens.css tests/Registrar.Tests/Design/GeneratedTokensAreCurrentTests.cs
git commit -m "feat: registrar emit-tokens and its staleness guard"
```

---

## Definition of done

- `dotnet test Registrar.sln` green, no new warnings.
- `registrar emit-tokens` twice in a row leaves the working tree clean.
- `git ls-files --eol config/generated/org-tokens.css` reports `i/lf w/lf`.
- No brand hex literal appears in `src/`.
- `visualIdentity` is unchanged; `git diff` on it shows only the added `screen` sibling.
- The staleness guard was demonstrated failing, not merely assumed to work.
