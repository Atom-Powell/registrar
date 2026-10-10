# Slice 6: AGR Input Validation Layer (Free-Text Hygiene) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Block generation of any artifact whose Governor-supplied free text (six chrome fields plus every `placeholderMap` value) contains a prohibited character, failing closed in both draft and promotion, driven from `style-rules.json` `contentLint`.

**Architecture:** A reusable pure scanner (`CharacterLint` over a `CharacterPolicy`) with no AGR knowledge, plus a thin `InputHygieneLayer : ISemanticLayer` that enumerates the target AGR fields and emits one `Check` per offending field. The policy's allowlist is loaded from config into a new `ContentLint` record; the allowed ranges are a code constant. The layer is registered first in `ValidationService`'s pipeline.

**Tech Stack:** .NET 8, C#, System.Text.Json, System.Text.Rune, xUnit.

## Global Constraints

- **Fail closed, both modes.** A prohibited codepoint is always `result=fail`, `severity=blocking`, in draft and promotion alike. Never mode-gate this check.
- **Block, never normalize.** The engine reports offending characters; it never rewrites Governor content.
- **Allowed set (verbatim):** printable Basic Latin `U+0020`-`U+007E`; printable Latin-1 Supplement `U+00A1`-`U+00FF` **excluding** soft hyphen `U+00AD`; plus config allowlist glyphs (currently `U+25CB`). Everything else is prohibited, including all C0/C1 controls, NBSP `U+00A0`, and every codepoint `>= U+0100` not in the allowlist.
- **Scan by `System.Text.Rune`,** never by `char`, so an astral-plane codepoint (surrogate pair) is one offense at one position.
- **checkId:** `QA-ENG-05`, `layer: input`, `errorCategory: MetadataViolation`, `ruleSource: ORG-MST-SPEC-001 Section 4.2`.
- **No emojis, em/en dashes, or decorative unicode** in any source, comment, or commit message (repo rule 8 / user global). Where a test or fixture must carry a prohibited character, express it as a `\uXXXX` escape (C# string escape in test source, JSON `\uXXXX` escape in fixtures) so no literal glyph is ever committed.
- **US English** throughout.

## Running tests on this machine

The .NET 8 runtime is not installed on this laptop; roll forward to the installed major. Prefix test/build commands:

- PowerShell: `$env:DOTNET_ROLL_FORWARD='LatestMajor'; dotnet test --filter "FullyQualifiedName~<ClassName>"`
- Bash: `DOTNET_ROLL_FORWARD=LatestMajor dotnet test --filter "FullyQualifiedName~<ClassName>"`

Run commands below omit the prefix for brevity; apply it on this machine.

## File Structure

- **Create** `src/Registrar.Core/Config/ContentLint.cs` - config record carrying the glyph allowlist.
- **Modify** `src/Registrar.Core/Config/ConfigBundle.cs` - add `ContentLint` property.
- **Modify** `src/Registrar.Core/Config/ConfigLoader.cs` - deserialize the `contentLint` node.
- **Create** `src/Registrar.Validation/CharacterPolicy.cs` - allowed-codepoint predicate.
- **Create** `src/Registrar.Validation/CharacterLint.cs` - pure scanner: `Offense`, `Scan`, `Describe`, `FormatActual`.
- **Create** `src/Registrar.Validation/InputHygieneLayer.cs` - `ISemanticLayer` enumerating the AGR fields.
- **Modify** `src/Registrar.Validation/ValidationService.cs` - register the layer first.
- **Modify** `config/qa-checklist.json` - add the `QA-ENG-05` catalog entry.
- **Create** `fixtures/agr-sample-ds-prohibited-char.json` - DS AGR with a smart quote in the client name.
- **Create** `tests/Registrar.Tests/CharacterLintTests.cs`
- **Create** `tests/Registrar.Tests/InputHygieneLayerTests.cs`
- **Modify** `tests/Registrar.Tests/Config/ConfigLoaderTests.cs` - assert the allowlist loads.
- **Modify** `tests/Registrar.Tests/QaChecklistCatalogTests.cs` - assert `QA-ENG-05` is catalogued.
- **Modify** `tests/Registrar.Tests/BuildCommandTests.cs` - end-to-end fail-closed test.

---

### Task 1: Load `contentLint` allowlist into config

**Files:**
- Create: `src/Registrar.Core/Config/ContentLint.cs`
- Modify: `src/Registrar.Core/Config/ConfigBundle.cs`
- Modify: `src/Registrar.Core/Config/ConfigLoader.cs:27` (shared parse) and `:47` (bundle init)
- Test: `tests/Registrar.Tests/Config/ConfigLoaderTests.cs`

**Interfaces:**
- Produces: `Registrar.Core.Config.ContentLint` record with `IReadOnlyList<string> ApprovedGlyphAllowlist`. Exposed as `ConfigBundle.ContentLint`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/Config/ConfigLoaderTests.cs`. The allowlist entry is the open-circle glyph U+25CB; assert with the escape, not a literal glyph:

```csharp
    [Fact]
    public void Load_parses_content_lint_allowlist()
    {
        var bundle = ConfigLoader.Load(RepoRoot());

        Assert.Contains("\u25cb", bundle.ContentLint.ApprovedGlyphAllowlist);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ConfigLoaderTests.Load_parses_content_lint_allowlist"`
Expected: FAIL to compile - `ConfigBundle` has no `ContentLint` member.

- [ ] **Step 3: Create the `ContentLint` record**

Create `src/Registrar.Core/Config/ContentLint.cs`:

```csharp
namespace Registrar.Core.Config;

public sealed record ContentLint
{
    public required IReadOnlyList<string> ApprovedGlyphAllowlist { get; init; }
}
```

- [ ] **Step 4: Add the property to `ConfigBundle`**

In `src/Registrar.Core/Config/ConfigBundle.cs`, add after the `Style` property:

```csharp
    public required ContentLint ContentLint { get; init; }
```

- [ ] **Step 5: Deserialize the node in `ConfigLoader`**

In `src/Registrar.Core/Config/ConfigLoader.cs`, replace line 27:

```csharp
        var style = JsonNode.Parse(styleRulesText)!["visualIdentity"]!.Deserialize<StyleRules>(Options)!;
```

with a single shared parse of both sub-objects:

```csharp
        var styleRoot = JsonNode.Parse(styleRulesText)!;
        var style = styleRoot["visualIdentity"]!.Deserialize<StyleRules>(Options)!;
        var contentLint = styleRoot["contentLint"]!.Deserialize<ContentLint>(Options)!;
```

Then in the `new ConfigBundle { ... }` initializer, add after `Style = style`:

```csharp
            ContentLint = contentLint,
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: PASS (all `ConfigLoaderTests`, including the existing `Load_..._hashes` which still asserts `Hashes.Count == 6` - no new file, so unchanged).

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/Config/ContentLint.cs src/Registrar.Core/Config/ConfigBundle.cs src/Registrar.Core/Config/ConfigLoader.cs tests/Registrar.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat(core): load contentLint glyph allowlist into config"
```

---

### Task 2: Reusable character scanner and policy

**Files:**
- Create: `src/Registrar.Validation/CharacterPolicy.cs`
- Create: `src/Registrar.Validation/CharacterLint.cs`
- Test: `tests/Registrar.Tests/CharacterLintTests.cs`

**Interfaces:**
- Consumes: `Registrar.Core.Config.ContentLint` (Task 1).
- Produces:
  - `CharacterPolicy` with `CharacterPolicy(IReadOnlyList<string> approvedGlyphs)`, static `CharacterPolicy FromConfig(ContentLint contentLint)`, and `bool IsAllowed(System.Text.Rune rune)`.
  - `Offense` record: `Offense(System.Text.Rune Codepoint, int Position)` (1-based rune position).
  - `CharacterLint` static: `IReadOnlyList<Offense> Scan(string text, CharacterPolicy policy)`, `string Describe(System.Text.Rune rune)`, `string FormatActual(IReadOnlyList<Offense> offenses)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/CharacterLintTests.cs`. Every prohibited character is written as a `\uXXXX` escape so no literal glyph is committed:

```csharp
using System.Text;
using Registrar.Core.Config;
using Registrar.Validation;

namespace Registrar.Tests;

public class CharacterLintTests
{
    private static CharacterPolicy Policy()
        => CharacterPolicy.FromConfig(new ContentLint { ApprovedGlyphAllowlist = new[] { "\u25cb" } });

    [Theory]
    [InlineData("Talewind CPA")]
    [InlineData("Pe\u00f1a")]   // n-tilde U+00F1, in the allowed Latin-1 range
    [InlineData("\u25cb")]      // approved radio glyph
    [InlineData("")]            // empty
    public void Scan_returns_no_offenses_for_allowed_text(string text)
    {
        Assert.Empty(CharacterLint.Scan(text, Policy()));
    }

    [Theory]
    [InlineData("A\u2014B", 0x2014, 2)]   // em dash at rune position 2
    [InlineData("\u2013", 0x2013, 1)]     // en dash
    [InlineData("\u2026", 0x2026, 1)]     // ellipsis
    [InlineData("\u2019", 0x2019, 1)]     // right single quote (smart quote)
    [InlineData("\u00a0", 0x00a0, 1)]     // NBSP (in Latin-1 but carved out)
    [InlineData("\u00ad", 0x00ad, 1)]     // soft hyphen (carved out)
    [InlineData("A\nB", 0x000a, 2)]       // newline control char
    [InlineData("A\tB", 0x0009, 2)]       // tab control char
    public void Scan_flags_single_prohibited_codepoint(string text, int codepoint, int position)
    {
        var offense = Assert.Single(CharacterLint.Scan(text, Policy()));
        Assert.Equal(codepoint, offense.Codepoint.Value);
        Assert.Equal(position, offense.Position);
    }

    [Fact]
    public void Scan_treats_astral_emoji_as_one_offense()
    {
        var offenses = CharacterLint.Scan("A\U0001F600", Policy()); // grinning face U+1F600
        var offense = Assert.Single(offenses);
        Assert.Equal(0x1F600, offense.Codepoint.Value);
        Assert.Equal(2, offense.Position);
    }

    [Fact]
    public void Describe_labels_known_codepoints()
    {
        Assert.Equal("U+2014 (em dash)", CharacterLint.Describe(new Rune(0x2014)));
        Assert.Equal("U+2660", CharacterLint.Describe(new Rune(0x2660))); // unlabeled falls back
    }

    [Fact]
    public void FormatActual_caps_at_five_with_overflow_suffix()
    {
        var offenses = Enumerable.Range(1, 6)
            .Select(i => new Offense(new Rune(0x2014), i)).ToList();

        var actual = CharacterLint.FormatActual(offenses);

        Assert.Contains("at position 5", actual);
        Assert.Contains("and 1 more", actual);
        Assert.DoesNotContain("at position 6", actual);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~CharacterLintTests"`
Expected: FAIL to compile - `CharacterPolicy`, `CharacterLint`, `Offense` not defined.

- [ ] **Step 3: Implement `CharacterPolicy`**

Create `src/Registrar.Validation/CharacterPolicy.cs`:

```csharp
using System.Text;
using Registrar.Core.Config;

namespace Registrar.Validation;

// The allowed ranges are fixed by ORG-MST-SPEC-001 Section 4.2 and live in code;
// only the glyph allowlist is config-driven. Changing the ranges is an ADR-level
// decision, not a config release.
public sealed class CharacterPolicy
{
    private readonly HashSet<int> _allowedGlyphs;

    public CharacterPolicy(IReadOnlyList<string> approvedGlyphs)
    {
        _allowedGlyphs = new HashSet<int>();
        foreach (var glyph in approvedGlyphs)
            foreach (var rune in glyph.EnumerateRunes())
                _allowedGlyphs.Add(rune.Value);
    }

    public static CharacterPolicy FromConfig(ContentLint contentLint)
        => new(contentLint.ApprovedGlyphAllowlist);

    public bool IsAllowed(Rune rune)
    {
        int v = rune.Value;
        if (v >= 0x20 && v <= 0x7E) return true;                    // printable Basic Latin
        if (v >= 0x00A1 && v <= 0x00FF && v != 0x00AD) return true; // Latin-1 minus NBSP/soft hyphen
        return _allowedGlyphs.Contains(v);
    }
}
```

- [ ] **Step 4: Implement `CharacterLint` and `Offense`**

Create `src/Registrar.Validation/CharacterLint.cs`:

```csharp
using System.Text;

namespace Registrar.Validation;

public sealed record Offense(Rune Codepoint, int Position);

public static class CharacterLint
{
    private const int MaxShown = 5;

    // Friendly labels for the codepoints an operator is most likely to hit. This
    // map is engine design (message ergonomics), not framework policy; unlabeled
    // codepoints degrade to the bare U+XXXX form.
    private static readonly IReadOnlyDictionary<int, string> Labels = new Dictionary<int, string>
    {
        [0x2014] = "em dash",
        [0x2013] = "en dash",
        [0x2026] = "ellipsis",
        [0x00A0] = "no-break space",
        [0x00AD] = "soft hyphen",
        [0x2018] = "left single quote",
        [0x2019] = "right single quote",
        [0x201C] = "left double quote",
        [0x201D] = "right double quote"
    };

    public static IReadOnlyList<Offense> Scan(string text, CharacterPolicy policy)
    {
        var offenses = new List<Offense>();
        int position = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            position++;
            if (!policy.IsAllowed(rune)) offenses.Add(new Offense(rune, position));
        }
        return offenses;
    }

    public static string Describe(Rune rune)
    {
        var code = $"U+{rune.Value:X4}";
        return Labels.TryGetValue(rune.Value, out var label) ? $"{code} ({label})" : code;
    }

    public static string FormatActual(IReadOnlyList<Offense> offenses)
    {
        var shown = offenses.Take(MaxShown)
            .Select(o => $"{Describe(o.Codepoint)} at position {o.Position}");
        var joined = string.Join("; ", shown);
        return offenses.Count > MaxShown
            ? $"{joined}; and {offenses.Count - MaxShown} more"
            : joined;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~CharacterLintTests"`
Expected: PASS (all cases).

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Validation/CharacterPolicy.cs src/Registrar.Validation/CharacterLint.cs tests/Registrar.Tests/CharacterLintTests.cs
git commit -m "feat(validation): add reusable character lint scanner and policy"
```

---

### Task 3: `InputHygieneLayer`, catalog entry, and fixture

**Files:**
- Modify: `config/qa-checklist.json` (add `QA-ENG-05` after the last entry, `QA-ENG-04`)
- Create: `src/Registrar.Validation/InputHygieneLayer.cs`
- Create: `fixtures/agr-sample-ds-prohibited-char.json`
- Test: `tests/Registrar.Tests/InputHygieneLayerTests.cs`
- Test: `tests/Registrar.Tests/QaChecklistCatalogTests.cs`

**Interfaces:**
- Consumes: `CharacterPolicy`, `CharacterLint`, `Offense` (Task 2); `ContentLint` via `SemanticContext.Config.ContentLint`.
- Produces: `InputHygieneLayer : ISemanticLayer` with `public const string CheckId = "QA-ENG-05"`.

- [ ] **Step 1: Add the catalog entry**

In `config/qa-checklist.json`, add a comma after the `QA-ENG-04` object (the last entry in the `checks` array) and insert:

```json
    { "checkId": "QA-ENG-05", "layer": "input", "automation": "full", "appliesTo": "all", "rule": "Chrome free-text fields (context.owner/audience/preparedBy/client/engagement, renderingOptions.packageName) and every dataInputs.placeholderMap value contain only allowed characters: printable Basic Latin, printable Latin-1 Supplement excluding NBSP and soft hyphen, plus approved glyphs. Blocks in draft and promotion.", "errorCategory": "MetadataViolation", "ruleSource": "ORG-MST-SPEC-001 Section 4.2", "alsoSatisfies": ["ORG-MST-WFE-001 4.5 item 8", "ORG-MST-SPEC-001 Section 9 item 10", "ORG-MST-UG Section 4"] }
```

- [ ] **Step 2: Create the failing fixture**

Create `fixtures/agr-sample-ds-prohibited-char.json` as a copy of `fixtures/agr-sample-ds-draft.json` with exactly one change: the `context.client` value carries a right single quote written as the JSON escape `\u2019` (do not paste a literal glyph). Full file:

```json
{
  "schemaVersion": "1.0.0",
  "artifactIdentity": {
    "artifactType": "DS",
    "documentId": "ORG-GOV-DS-016",
    "version": "v1.0",
    "classification": "Client-Facing",
    "approvalStatus": "Draft",
    "layer": "Governance"
  },
  "context": {
    "client": "Talewind\u2019s CPA",
    "engagement": "Copilot Chat Pilot 2026",
    "owner": "Registry Owner",
    "audience": "Talewind CPA leadership, tenant admin, ORG delivery lead",
    "preparedBy": "ORG Consulting, LLC",
    "reviewDate": null,
    "reviewDateOverride": null
  },
  "sourceWorkflowContext": {
    "workflowName": "Copilot Readiness Assessment",
    "phase": "Reporting",
    "executionId": "GOV-2026-07-03-001"
  },
  "dataInputs": {
    "placeholderMap": {
      "[CLIENT ORGANIZATION NAME]": "Talewind CPA",
      "[CLIENT TENANT NAME / TENANT ID]": "talewindcpa.onmicrosoft.com",
      "[APPROVED USER GROUP OR PILOT GROUP]": "Copilot Pilot Group",
      "[TOOL / CONNECTOR NAME]": "Microsoft Copilot Chat",
      "[APPROVED PURPOSE]": "Drafting and summarization of non-restricted business content",
      "[OUT OF SCOPE USES]": "Processing regulated client financial data",
      "[DESIGNATED SECURITY OWNER]": "J. Talewind",
      "[ORG SUPPORT CONTACT]": "support@orgconsulting.example",
      "[DATE]": "2026-07-03",
      "[VERSION]": "v1.0"
    },
    "sections": [
      { "title": "Purpose", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "This summary documents the approved pilot deployment posture for Microsoft Copilot Chat." }
      ] },
      { "title": "Deployment Scope", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Scope covers the approved pilot group only." }
      ] },
      { "title": "Access Model", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Access is limited to approved users through approved accounts." }
      ] },
      { "title": "Control Framework", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Controls follow the approved governance baseline." }
      ] },
      { "title": "Audit, Logging, and Discoverability Validation", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Audit posture is subject to tenant validation." },
        { "blockType": "clauseInsertion", "clauseId": "CL-006" }
      ] },
      { "title": "Roles and Responsibilities", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Roles are assigned to the security owner and delivery lead." }
      ] },
      { "title": "Client Acknowledgment", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "The client acknowledges the deployment posture described above." }
      ] },
      { "title": "Version and Change Notes", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "v1.0 initial issue." }
      ] }
    ],
    "normalizedFindings": null,
    "normalizedRisks": null,
    "normalizedInventoryEntities": null
  },
  "evidenceReferences": null,
  "dependencies": [
    { "dependencyId": "ORG-MST-SCL", "kind": "clause-library", "blocking": true }
  ],
  "renderingOptions": {
    "packageName": "Governed AI Rollout",
    "outputFileName": "ORG - Deployment Summary - Talewind CPA - v1.0.docx",
    "optionalSectionToggles": null,
    "stylingProfile": "org-v1.2"
  },
  "validationPolicy": {
    "mode": "draft",
    "additionalRequiredCheckIds": null,
    "warningThreshold": null
  }
}
```

- [ ] **Step 3: Write the failing tests**

Create `tests/Registrar.Tests/InputHygieneLayerTests.cs`. The `.Replace` cases inject the offending characters as JSON `\uXXXX` escapes into the fixture text, which `AgrParser` decodes:

```csharp
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class InputHygieneLayerTests
{
    [Fact]
    public void QA_ENG_05_passes_for_clean_chrome_and_placeholders()
    {
        var checks = new InputHygieneLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_ENG_05_fails_closed_on_prohibited_char_in_client(string mode)
    {
        var checks = new InputHygieneLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-prohibited-char.json", mode));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-05", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("/context/client", check.JsonPath);
        Assert.Contains("U+2019", check.Actual);
    }

    [Fact]
    public void QA_ENG_05_reports_offense_in_a_placeholder_value()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace(
                "\"[APPROVED PURPOSE]\": \"Drafting and summarization of non-restricted business content\"",
                "\"[APPROVED PURPOSE]\": \"Drafting \\u2014 summarization\"");
        var agr = AgrParser.Parse(text);

        var checks = new InputHygieneLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        var check = Assert.Single(checks);
        Assert.Equal("/dataInputs/placeholderMap/[APPROVED PURPOSE]", check.JsonPath);
        Assert.Contains("em dash", check.Actual);
    }

    [Fact]
    public void QA_ENG_05_emits_one_check_per_offending_field()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"owner\": \"Registry Owner\"", "\"owner\": \"Tech\\u2013Success\"")
            .Replace("\"packageName\": \"Governed AI Rollout\"", "\"packageName\": \"Governed\\u2014Rollout\"");
        var agr = AgrParser.Parse(text);

        var checks = new InputHygieneLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        Assert.Equal(2, checks.Count);
        Assert.Contains(checks, c => c.JsonPath == "/context/owner");
        Assert.Contains(checks, c => c.JsonPath == "/renderingOptions/packageName");
    }
}
```

Add to `tests/Registrar.Tests/QaChecklistCatalogTests.cs`:

```csharp
    [Fact]
    public void InputHygieneLayer_checkId_exists_in_qa_checklist()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        var checklist = JsonNode.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "config", "qa-checklist.json")))!;
        var ids = checklist["checks"]!.AsArray().Select(c => (string?)c!["checkId"]).ToHashSet();

        Assert.Contains(InputHygieneLayer.CheckId, ids);
    }
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~InputHygieneLayerTests|FullyQualifiedName~QaChecklistCatalogTests.InputHygieneLayer"`
Expected: FAIL to compile - `InputHygieneLayer` not defined.

- [ ] **Step 5: Implement `InputHygieneLayer`**

Create `src/Registrar.Validation/InputHygieneLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class InputHygieneLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-05";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 4.2";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var policy = CharacterPolicy.FromConfig(context.Config.ContentLint);
        var ctx = context.Agr.Context;

        var fields = new List<(string JsonPath, string Label, string? Value)>
        {
            ("/context/owner", "context.owner", ctx.Owner),
            ("/context/audience", "context.audience", ctx.Audience),
            ("/context/preparedBy", "context.preparedBy", ctx.PreparedBy),
            ("/context/client", "context.client", ctx.Client),
            ("/context/engagement", "context.engagement", ctx.Engagement),
            ("/renderingOptions/packageName", "renderingOptions.packageName",
                context.Agr.RenderingOptions.PackageName)
        };

        foreach (var (key, value) in context.Agr.DataInputs.PlaceholderMap)
            fields.Add(("/dataInputs/placeholderMap/" + EscapeToken(key),
                $"dataInputs.placeholderMap['{key}']", value));

        var checks = new List<Check>();
        foreach (var (jsonPath, label, value) in fields)
        {
            if (value is null) continue; // optional fields absent from the AGR are not linted
            var offenses = CharacterLint.Scan(value, policy);
            if (offenses.Count > 0) checks.Add(Build(jsonPath, label, offenses));
        }
        return checks;
    }

    // Placeholder tokens can contain '/', which must be escaped in a JSON Pointer
    // (RFC 6901). Mirrors PlaceholderLayer.PointerFor.
    private static string EscapeToken(string token)
        => token.Replace("~", "~0").Replace("/", "~1");

    private static Check Build(string jsonPath, string label, IReadOnlyList<Offense> offenses)
    {
        var first = offenses[0];
        return new Check
        {
            CheckId = CheckId,
            LayerName = Layer.Input,
            Result = CheckResultValue.Fail,
            SeverityLevel = Severity.Blocking,
            ErrorCategory = ErrorCategory.MetadataViolation,
            Message = $"{label} contains a prohibited character: "
                + $"{CharacterLint.Describe(first.Codepoint)} at position {first.Position}.",
            JsonPath = jsonPath,
            Expected = "only printable Basic Latin, printable Latin-1 Supplement "
                + "excluding NBSP and soft hyphen, or an approved glyph",
            Actual = CharacterLint.FormatActual(offenses),
            Remediation = $"Remove or replace the prohibited character(s) in {label}. "
                + "Use only standard Latin characters, for example a standard hyphen instead of an em dash.",
            RuleSource = RuleSourceValue
        };
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~InputHygieneLayerTests|FullyQualifiedName~QaChecklistCatalogTests"`
Expected: PASS (clean fixture yields no checks; the prohibited-char fixture fails blocking in both modes; placeholder and multi-field cases resolve to the right pointers).

- [ ] **Step 7: Commit**

```bash
git add config/qa-checklist.json src/Registrar.Validation/InputHygieneLayer.cs fixtures/agr-sample-ds-prohibited-char.json tests/Registrar.Tests/InputHygieneLayerTests.cs tests/Registrar.Tests/QaChecklistCatalogTests.cs
git commit -m "feat(validation): add InputHygieneLayer blocking prohibited chars (QA-ENG-05)"
```

---

### Task 4: Wire into the pipeline and prove fail-closed end to end

**Files:**
- Modify: `src/Registrar.Validation/ValidationService.cs:62-69` (layer list)
- Test: `tests/Registrar.Tests/BuildCommandTests.cs`

**Interfaces:**
- Consumes: `InputHygieneLayer` (Task 3), `BuildCommand.Run` (existing).

- [ ] **Step 1: Write the failing end-to-end test**

Add to `tests/Registrar.Tests/BuildCommandTests.cs`:

```csharp
    [Fact]
    public void Prohibited_char_build_writes_no_docx_and_reports_QA_ENG_05()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-prohibited-char.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));

        var reportPath = Path.Combine(dir, "validation-report.json");
        Assert.True(File.Exists(reportPath));
        Assert.Contains("QA-ENG-05", File.ReadAllText(reportPath));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~BuildCommandTests.Prohibited_char_build"`
Expected: FAIL - the layer is not yet registered, so validation passes, a DOCX is written, and exit is 0.

- [ ] **Step 3: Register the layer first in the pipeline**

In `src/Registrar.Validation/ValidationService.cs`, change the layer list initializer (currently starting at line 62) so `InputHygieneLayer` is first:

```csharp
            var layers = new List<ISemanticLayer>
            {
                new InputHygieneLayer(),
                new SectionSchemaLayer(),
                new PlaceholderLayer(),
                new ReviewDateLayer(_clock),
                new MetadataLayer(),
                new ClauseReferenceLayer()
            };
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~BuildCommandTests"`
Expected: PASS - all `BuildCommandTests`, including the existing `Valid_draft_build_writes_a_docx` (the clean draft fixture has no prohibited characters, so the new layer is silent) and the new fail-closed test.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS - the whole solution. Confirm no other layer test regressed from the new pipeline entry.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Validation/ValidationService.cs tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "feat(validation): wire input hygiene into the pipeline; fail closed on render path"
```

---

## Self-Review

**1. Spec coverage:**
- Coverage = chrome fields + placeholder values -> Task 3 enumeration (six fields + `PlaceholderMap` loop). Covered.
- Fail closed both modes -> Task 3 `[Theory]` over Draft and Promotion; `Severity.Blocking` unconditional. Covered.
- New input-layer checkId `QA-ENG-05`, `alsoSatisfies` citations -> Task 3 Step 1 catalog entry; Task 3 catalog test. Covered.
- Policy from config, field list in code -> Task 1 (`ContentLint`) + Task 2 (`CharacterPolicy` ranges in code) + Task 3 (field list in code). Covered.
- Character policy (ranges, NBSP/soft-hyphen/control carve-outs, by-Rune) -> Task 2 `CharacterPolicy.IsAllowed` + `CharacterLintTests`. Covered.
- Reporting shape (one check per field, jsonPath, expected/actual/remediation, cap at five) -> Task 2 `FormatActual` + Task 3 `Build`. Covered.
- `MetadataViolation`, no schema change -> Task 3 `Build`; report schema untouched. Covered.
- Config load defense-in-depth (missing `contentLint`) -> `styleRoot["contentLint"]!` throws in `ConfigLoader` if absent, surfaced as a config-load failure; the committed `style-rules.json` always carries the node. Covered by construction (no separate task needed).
- End-to-end fail-closed, no DOCX, report carries the check -> Task 4. Covered.
- Reuse-first architecture (scanner primitive independent of AGR) -> Task 2 has no AGR dependency; Task 3 consumes it. Covered.

**2. Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to". Every code step shows complete code. Clear.

**3. Type consistency:** `CharacterPolicy.FromConfig(ContentLint)`, `CharacterPolicy.IsAllowed(Rune)`, `Offense(Rune, int)`, `CharacterLint.Scan/Describe/FormatActual`, `InputHygieneLayer.CheckId = "QA-ENG-05"`, `Layer.Input`, `Severity.Blocking`, `CheckResultValue.Fail`, `ErrorCategory.MetadataViolation`, `Check` property names (`LayerName`, `SeverityLevel`, `JsonPath`, `Actual`, `Remediation`, `RuleSource`) all match the definitions in `Check.cs`, `Vocabulary.cs`, `AgrDocument.cs`, and `SemanticContext`. Consistent.

**Note on a small, deliberate spec deviation:** the spec said `ContentLint` carries "the named prohibited exemplars"; this plan keeps `ContentLint` minimal (allowlist only) and puts the friendly-label map in `CharacterLint` code, because the config's exemplar block is loosely structured and the label map must also cover characters the config never names (smart quotes, NBSP). This lands on the spec's own sourced-vs-engine-design line: labels are engine ergonomics, not framework policy.
