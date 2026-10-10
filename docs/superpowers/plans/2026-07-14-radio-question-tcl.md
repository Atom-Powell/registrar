# radioQuestion Rendering and TCL End-to-End Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make TCL (Training Checklist) generate end to end, by building the `radioQuestion` block renderer and the QA-SF-10 check that governs it, and stamp the validation report's `artifactSha256` (today hardcoded null).

**Architecture:** Config-first. `radioOptionPattern` in `style-rules.json` is currently dead data (`ConfigLoader` binds only the `visualIdentity` node), so Task 1 binds it. The model gains the `question` object the AGR schema already defines. A pre-render `ISemanticLayer` enforces catalogued QA-SF-10 (blocking in both modes); the renderer then emits the radio pattern from config values, referencing no numbering definition, which makes the "no bullets, no numbered list" half of QA-SF-10 true by construction.

**Tech Stack:** .NET 8, C#, Open XML SDK (DocumentFormat.OpenXml), xUnit, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-07-14-radio-question-tcl-design.md`

**Branch:** `feat/radio-question-tcl` (already created; the spec is committed on it).

## Global Constraints

- **Fail closed** (engine rule 1). QA-SF-10 is blocking in BOTH draft and promotion. Do not use warn severity; the lenient null-`clauseId` precedent in `ClauseReferenceLayer` is recorded as a mistake (tech-debt item 12) and must not be copied.
- **Configuration over code** (engine rule 3). No artifact constants in core logic. `minimumOptions` is read from config and NEVER appears as a literal `4` in C#. The glyph is read from config and never typed as a literal in C#.
- **Do not invent structure** (engine rule 6). No AGR schema change. No new section schemas. The other three deferred block types (`warnCallout`, `aggregationTable`, `copyReadyMessage`) stay deferred and keep failing closed.
- **Zero LLM calls at runtime** (engine rule 2).
- **Output hygiene** (engine rule 8). US English; no emojis; no em/en dashes or decorative unicode in generated documents, config strings, or source. The ONE approved non-ASCII glyph is U+25CB, already on `approvedGlyphAllowlist`. **Never paste it as a literal into any `.cs` file, test files included:** `SourceHygieneTests` scans both `src/**/*.cs` AND `tests/**/*.cs` and enforces an escapes-only rule, so a literal glyph in a test fails the suite. Write `"\u25cb"`. In JSON config and fixtures the glyph is fine (it lives in `style-rules.json` today).
- **Descriptor placeholder syntax is `{number}` / `{requirement}`, NOT `[NUMBER]`.** The QA-ENG-07 token-residue scan matches the bracketed upper-case token shape and would flag a bracketed token as unresolved placeholder residue in rendered prose.
- **Every QA checkId gets at least one passing and one failing test** named after the checkId (repo test rule, CLAUDE.md).
- Target OS Windows 11; dev shell PowerShell 7.x. Run tests with `dotnet test`.

---

## File Structure

**Create:**
- `src/Registrar.Core/Config/RadioOptionPattern.cs` - config record for the radio pattern
- `src/Registrar.Validation/RadioQuestionLayer.cs` - QA-SF-10 pre-render enforcement
- `src/Registrar.Rendering/Blocks/RadioQuestionBlock.cs` - the renderer
- `tests/Registrar.Tests/RadioQuestionLayerTests.cs`
- `tests/Registrar.Tests/Rendering/RadioQuestionBlockTests.cs`
- `fixtures/agr-sample-tcl-draft.json`
- `fixtures/agr-sample-tcl-promotion.json`
- `fixtures/agr-sample-tcl-radio-missing-question.json` - QA-SF-10 failure case (schema-valid; see Task 3)

**Modify:**
- `config/style-rules.json` - three new keys under `radioOptionPattern`
- `config/qa-checklist.json` - QA-ENG-06 rule text (it currently names `radioQuestion` as not built)
- `src/Registrar.Core/Config/ConfigLoader.cs:28-30` - bind `radioOptionPattern`
- `src/Registrar.Core/Config/ConfigBundle.cs` - expose `RadioPattern`
- `src/Registrar.Core/Model/AgrDocument.cs:55-64` - `AgrBlock.Question` + `AgrQuestion`
- `src/Registrar.Core/Hashing.cs` - `byte[]` overload
- `src/Registrar.Validation/ValidationService.cs:62-74` - register the layer; `:117` - stamp hash
- `src/Registrar.Validation/BlockTypeSupportLayer.cs:16-19,48` - supported set + Expected string
- `src/Registrar.Rendering/ParagraphFormatting.cs` - `IndentLeftTwips` on `ParaFormat`
- `src/Registrar.Rendering/OpenXmlHelpers.cs:22-32` - italic on `StyledRun`
- `src/Registrar.Rendering/RenderContext.cs` - carry `RadioOptionPattern`
- `src/Registrar.Rendering/ArtifactRenderer.cs:19` - pass it
- `src/Registrar.Rendering/Blocks/BlockRenderer.cs:15-58` - `radioQuestion` case
- `src/Registrar.Cli/BuildCommand.cs:64-84` - reorder so eligibility precedes WriteOutputs
- `fixtures/registry-sample.csv` - add `ORG-TRN-TCL-001`
- `tests/Registrar.Tests/Rendering/BlockRendererTests.cs:123-158` - QA-ENG-06 drift guard

---

## Task 1: Bind the radio config (it is dead data today)

**Files:**
- Create: `src/Registrar.Core/Config/RadioOptionPattern.cs`
- Modify: `config/style-rules.json` (add 3 keys to the existing `radioOptionPattern` object)
- Modify: `src/Registrar.Core/Config/ConfigLoader.cs` (line 30 area)
- Modify: `src/Registrar.Core/Config/ConfigBundle.cs`
- Test: `tests/Registrar.Tests/ConfigLoaderTests.cs` (add to existing file if present; else create)

**Interfaces:**
- Consumes: nothing.
- Produces: `Registrar.Core.Config.RadioOptionPattern` with properties `Glyph` (string), `GlyphColor` (string), `OptionsIndented` (bool), `MinimumOptions` (int), `StandardOptionSet` (IReadOnlyList<string>), `DescriptorLineFormat` (string), `RequiredLabel` (string), `OptionalLabel` (string). Reachable as `ConfigBundle.RadioPattern`.

- [ ] **Step 1: Add the three new config keys**

In `config/style-rules.json`, the `radioOptionPattern` object already exists (top level, sibling of `visualIdentity`). Add exactly three keys to it, leaving every existing key untouched:

```json
    "descriptorLineFormat": "Question {number}. Select one answer. {requirement}",
    "requiredLabel": "Required",
    "optionalLabel": "Optional",
    "optionIndentTwips": 360,
```

That is FOUR new keys. `optionIndentTwips` is the indent magnitude for each option line (360 twips = 0.25in); config previously carried only the boolean `optionsIndented` with no magnitude, which would have forced a code constant and violated rule 3.

Place them immediately after the existing `"descriptorLine"` key. Do NOT remove `descriptorLine` (prose, documentary) or `prohibitedRenderings` (documentary); neither is read at runtime.

- [ ] **Step 2: Write the failing test**

Add to `tests/Registrar.Tests/ConfigLoaderTests.cs` (create the file with this content if it does not exist, using namespace `Registrar.Tests` and `using Registrar.Core.Config;`):

```csharp
[Fact]
public void Loads_radio_option_pattern_from_style_rules()
{
    var config = SemanticTestSupport.Config();

    Assert.Equal("\u25cb", config.RadioPattern.Glyph); // U+25CB, escapes-only in source
    Assert.Equal("#005CB9", config.RadioPattern.GlyphColor);
    Assert.True(config.RadioPattern.OptionsIndented);
    Assert.Equal(360, config.RadioPattern.OptionIndentTwips);
    Assert.Equal(4, config.RadioPattern.MinimumOptions);
    Assert.Equal("Question {number}. Select one answer. {requirement}",
        config.RadioPattern.DescriptorLineFormat);
    Assert.Equal("Required", config.RadioPattern.RequiredLabel);
    Assert.Equal("Optional", config.RadioPattern.OptionalLabel);
    Assert.Equal(4, config.RadioPattern.StandardOptionSet.Count);
}
```

- [ ] **Step 3: Run it to make sure it fails**

Run: `dotnet test --filter "FullyQualifiedName~Loads_radio_option_pattern"`
Expected: FAIL to compile - `ConfigBundle` has no `RadioPattern`.

- [ ] **Step 4: Create the config record**

Create `src/Registrar.Core/Config/RadioOptionPattern.cs`:

```csharp
namespace Registrar.Core.Config;

// The radio-option pattern from ORG-MST-WFE-001, bound from the top-level
// "radioOptionPattern" node of style-rules.json (a sibling of "visualIdentity",
// which is what StyleRules maps to). Kept off StyleRules deliberately so that
// record continues to correspond 1:1 with the visualIdentity node.
//
// Two keys in that node are documentary and are NOT bound here: "descriptorLine"
// (prose describing the line, superseded by DescriptorLineFormat) and
// "prohibitedRenderings" (checkboxes/bullets/numbered lists, which the renderer
// makes impossible by construction rather than checking at runtime).
public sealed record RadioOptionPattern
{
    public required string Glyph { get; init; }
    public required string GlyphColor { get; init; }
    public required bool OptionsIndented { get; init; }
    public required int OptionIndentTwips { get; init; }
    public required int MinimumOptions { get; init; }
    public required IReadOnlyList<string> StandardOptionSet { get; init; }

    // Composed by RadioQuestionBlock. Uses {number}/{requirement} rather than
    // bracketed tokens: the QA-ENG-07 residue scan matches the bracketed
    // upper-case token shape and would flag [NUMBER] as unresolved residue.
    public required string DescriptorLineFormat { get; init; }
    public required string RequiredLabel { get; init; }
    public required string OptionalLabel { get; init; }
}
```

- [ ] **Step 5: Bind it in ConfigLoader**

In `src/Registrar.Core/Config/ConfigLoader.cs`, after line 30 (`var contentLint = ...`), add:

```csharp
        var radioPattern = styleRoot["radioOptionPattern"]!.Deserialize<RadioOptionPattern>(Options)!;
```

Then in the returned `ConfigBundle` initializer, after `ContentLint = contentLint,`, add:

```csharp
            RadioPattern = radioPattern,
```

- [ ] **Step 6: Expose it on ConfigBundle**

In `src/Registrar.Core/Config/ConfigBundle.cs`, after the `ContentLint` property, add:

```csharp
    public required RadioOptionPattern RadioPattern { get; init; }
```

- [ ] **Step 7: Run the test**

Run: `dotnet test --filter "FullyQualifiedName~Loads_radio_option_pattern"`
Expected: PASS.

- [ ] **Step 8: Run the full suite (nothing should break)**

Run: `dotnet test`
Expected: all existing tests still pass (270 baseline + 1 new).

- [ ] **Step 9: Commit**

```bash
git add config/style-rules.json src/Registrar.Core/Config/RadioOptionPattern.cs src/Registrar.Core/Config/ConfigLoader.cs src/Registrar.Core/Config/ConfigBundle.cs tests/Registrar.Tests/ConfigLoaderTests.cs
git commit -m "feat: bind radioOptionPattern config and add descriptor-line format

radioOptionPattern was a top-level key in style-rules.json that no code could
read: ConfigLoader deserializes only the visualIdentity node. Bind it, and add
the descriptor-line format string the renderer needs (rule 3: the wording is
versioned config, not a code constant)."
```

---

## Task 2: Model the question object

**Files:**
- Modify: `src/Registrar.Core/Model/AgrDocument.cs` (lines 55-64)
- Test: `tests/Registrar.Tests/AgrParserTests.cs` (add to existing file)

**Interfaces:**
- Consumes: nothing.
- Produces: `Registrar.Core.Model.AgrQuestion` record with `int Number`, `string Prompt`, `bool Required`, `IReadOnlyList<string> Options`. `AgrBlock.Question` is `AgrQuestion?`.

The AGR schema at `schemas/agr.schema.json` `$defs.contentBlock` ALREADY defines this object (`number`, `prompt`, `required`, `options` with `minItems: 4`). The C# model simply never had a counterpart, so the field was silently dropped on deserialization. **Do not change the schema.**

- [ ] **Step 1: Write the failing test**

Add to `tests/Registrar.Tests/AgrParserTests.cs`:

```csharp
[Fact]
public void Parses_radio_question_block()
{
    const string json = """
    {
      "blockType": "radioQuestion",
      "question": {
        "number": 3,
        "prompt": "Did the user complete the prompting walkthrough?",
        "required": true,
        "options": ["Yes, reviewed and acknowledged",
                    "Yes, partial review, follow-up scheduled",
                    "No, blocked by access or timing",
                    "Other or Not applicable"]
      }
    }
    """;

    var block = System.Text.Json.JsonSerializer.Deserialize<Registrar.Core.Model.AgrBlock>(
        json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    Assert.Equal("radioQuestion", block.BlockType);
    Assert.NotNull(block.Question);
    Assert.Equal(3, block.Question!.Number);
    Assert.True(block.Question.Required);
    Assert.Equal(4, block.Question.Options.Count);
    Assert.Equal("Did the user complete the prompting walkthrough?", block.Question.Prompt);
}
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test --filter "FullyQualifiedName~Parses_radio_question_block"`
Expected: FAIL to compile - `AgrBlock` has no `Question`.

- [ ] **Step 3: Add the model**

In `src/Registrar.Core/Model/AgrDocument.cs`, add the `question` property to `AgrBlock` (after the `rows` line, keeping the existing aligned style):

```csharp
    [JsonPropertyName("question")]      public AgrQuestion? Question { get; init; }
```

Then add the new record immediately after the `AgrBlock` record closes:

```csharp
// Mirrors $defs.contentBlock.question in agr.schema.json. Required when
// blockType=radioQuestion; the schema cannot enforce that (contentBlock is one
// flat object with no per-blockType if/then), so RadioQuestionLayer does.
public sealed record AgrQuestion
{
    [JsonPropertyName("number")]   public required int Number { get; init; }
    [JsonPropertyName("prompt")]   public required string Prompt { get; init; }
    [JsonPropertyName("required")] public required bool Required { get; init; }
    [JsonPropertyName("options")]  public required IReadOnlyList<string> Options { get; init; }
}
```

- [ ] **Step 4: Run the test**

Run: `dotnet test --filter "FullyQualifiedName~Parses_radio_question_block"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Model/AgrDocument.cs tests/Registrar.Tests/AgrParserTests.cs
git commit -m "feat: model the AGR question object for radioQuestion blocks

The schema has defined this object all along; the C# model had no counterpart,
so the field was silently dropped on deserialization. No schema change."
```

---

## Task 3: QA-SF-10 enforcement (RadioQuestionLayer)

**Files:**
- Create: `src/Registrar.Validation/RadioQuestionLayer.cs`
- Create: `fixtures/agr-sample-tcl-radio-missing-question.json`
- Modify: `src/Registrar.Validation/ValidationService.cs` (layer list, lines 62-74)
- Test: `tests/Registrar.Tests/RadioQuestionLayerTests.cs`

**Interfaces:**
- Consumes: `ConfigBundle.RadioPattern.MinimumOptions` (Task 1); `AgrBlock.Question` (Task 2).
- Produces: `Registrar.Validation.RadioQuestionLayer : ISemanticLayer` with `public const string CheckId = "QA-SF-10"`.

QA-SF-10 is ALREADY catalogued in `config/qa-checklist.json` (`layer: content`, `automation: full`, `appliesTo: usesRadioOptionPattern`, ruleSource `ORG-MST-WFE-001 4.5 item 10`). Do not invent a new check id. `ruleSource` and `errorCategory` are stamped from the catalog by `QaCatalog.StampAll`, but set `RuleSource` on the check anyway, matching how sibling layers do it.

**Note on task ordering:** `radioQuestion` is NOT yet in `BlockTypeSupportLayer.SupportedBlockTypes` until Task 4. That is fine and intentional: the tests below construct the layer and call `Validate` directly, so they do not go through the full pipeline. Do not add the block type to the supported set in this task.

- [ ] **Step 1: Create the failure fixture**

**Why this fixture is a MISSING question, not a short one.** `ValidationService.cs:44` short-circuits on a blocking schema failure: `if (schemaBlocked) { checks = schemaChecks; }` and the semantic layers never run. The AGR schema enforces `options: minItems 4` and `prompt: minLength 1` inside the `question` object, so a short-options or empty-prompt AGR is schema-INVALID and dies before QA-SF-10 could ever fire. A fixture like that could never produce a QA-SF-10 check end to end.

The one QA-SF-10 violation that IS schema-valid is a `radioQuestion` block carrying **no `question` object at all**. The `question` field is optional at the `contentBlock` level, because `contentBlock` is one flat object with no per-`blockType` `if`/`then`. That is precisely the hole the spec identified, and it is the only one reachable through the full pipeline. The short-options and empty-prompt cases remain unit-level only (Task 3's layer tests construct `AgrBlock` directly and bypass schema validation), which is the right level for them.

Create `fixtures/agr-sample-tcl-radio-missing-question.json`. Copy `fixtures/agr-sample-tak-draft.json` as the starting shape and change: `artifactType` to `TCL`, `documentId` to `ORG-TRN-TCL-001`, `outputFileName` to `"ORG - Training Checklist - Talewind CPA - v1.0.docx"`, and replace `sections` with the two below. TCL requires clauses CL-003 and CL-004 (`config/artifact-types.json`), so both must be present or `ClauseReferenceLayer` will fail for an unrelated reason and muddy the test.

```json
    "sections": [
      { "title": "Pre-Training Preparation", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Complete these steps before the training session begins." },
        { "blockType": "clauseInsertion", "clauseId": "CL-003" },
        { "blockType": "clauseInsertion", "clauseId": "CL-004" }
      ] },
      { "title": "Required Training Coverage", "descriptorLine": null, "blocks": [
        { "blockType": "radioQuestion" }
      ] }
    ],
```

This fixture IS schema-valid. It reaches the semantic layers, and QA-SF-10 fails it closed. Verify that with `dotnet run --project src/Registrar.Cli -- validate fixtures/agr-sample-tcl-radio-missing-question.json` and confirm the report names QA-SF-10 and NOT a SchemaValidationError. If you see a schema error instead, the fixture is malformed somewhere other than the question.

Note: only the two sections above are needed for this fixture; `SectionSchemaLayer` will also flag the missing sections, which is fine and does not interfere with asserting QA-SF-10 is present.

- [ ] **Step 2: Write the failing tests**

Create `tests/Registrar.Tests/RadioQuestionLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;
using Xunit;

namespace Registrar.Tests;

public class RadioQuestionLayerTests
{
    private static SemanticContext Ctx(AgrDocument agr, string mode)
        => new(agr, SemanticTestSupport.Config(), mode, Registry: null);

    private static AgrDocument WithBlock(AgrBlock block)
    {
        var agr = SemanticTestSupport.Agr("agr-sample-tcl-draft.json");
        var section = agr.DataInputs.Sections[0] with { Blocks = new[] { block } };
        return agr with
        {
            DataInputs = agr.DataInputs with { Sections = new[] { section } }
        };
    }

    private static AgrQuestion GoodQuestion() => new()
    {
        Number = 1,
        Prompt = "Was the approved-use walkthrough completed?",
        Required = true,
        Options = new[]
        {
            "Yes, reviewed and acknowledged",
            "Yes, partial review, follow-up scheduled",
            "No, blocked by access or timing",
            "Other or Not applicable"
        }
    };

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_10_passes_for_a_well_formed_radio_question(string mode)
    {
        var agr = WithBlock(new AgrBlock { BlockType = "radioQuestion", Question = GoodQuestion() });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, mode));

        Assert.Empty(checks);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_10_fails_closed_when_question_is_missing(string mode)
    {
        var agr = WithBlock(new AgrBlock { BlockType = "radioQuestion", Question = null });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, mode));

        var check = Assert.Single(checks);
        Assert.Equal(RadioQuestionLayer.CheckId, check.CheckId);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/0/question", check.JsonPath);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_10_fails_closed_below_the_configured_minimum_options(string mode)
    {
        var q = GoodQuestion() with { Options = new[] { "Yes", "No" } };
        var agr = WithBlock(new AgrBlock { BlockType = "radioQuestion", Question = q });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, mode));

        var check = Assert.Single(checks);
        Assert.Equal(RadioQuestionLayer.CheckId, check.CheckId);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/0/question/options", check.JsonPath);
        Assert.Contains("2", check.Actual);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_10_fails_closed_on_an_empty_prompt(string mode)
    {
        var q = GoodQuestion() with { Prompt = "   " };
        var agr = WithBlock(new AgrBlock { BlockType = "radioQuestion", Question = q });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, mode));

        var check = Assert.Single(checks);
        Assert.Equal(RadioQuestionLayer.CheckId, check.CheckId);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/0/question/prompt", check.JsonPath);
    }

    [Fact]
    public void QA_SF_10_ignores_non_radio_blocks()
    {
        var agr = WithBlock(new AgrBlock { BlockType = "paragraph", Text = "Plain body text." });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, Mode.Draft));

        Assert.Empty(checks);
    }
}
```

- [ ] **Step 3: Run to make sure they fail**

Run: `dotnet test --filter "FullyQualifiedName~RadioQuestionLayerTests"`
Expected: FAIL to compile - `RadioQuestionLayer` does not exist. (`agr-sample-tcl-draft.json` is created in Task 5; if it does not exist yet, create it now per Task 5 Step 1 and move on. The layer tests need a TCL AGR to mutate.)

- [ ] **Step 4: Implement the layer**

Create `src/Registrar.Validation/RadioQuestionLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

// QA-SF-10 (catalogued in config/qa-checklist.json, appliesTo usesRadioOptionPattern):
// every selection item uses the U+25CB glyph, consistent indentation, a descriptor
// line, and a four-option minimum; no checkboxes, plain bullets, or numbered lists.
//
// This layer enforces the half that depends on the Governor's input: that a
// radioQuestion block actually carries a well-formed question. The rendering half
// (glyph, indentation, descriptor line, and the absence of bullets/numbering) is
// guaranteed by construction in Registrar.Rendering.RadioQuestionBlock, which is the
// only code path that can emit a radio question, and is asserted by its unit tests.
//
// The AGR schema cannot enforce this: $defs.contentBlock is one flat object with
// every field optional and no per-blockType if/then, so a radioQuestion block with
// no question object at all is schema-valid. Blocking in BOTH modes (rule 1); the
// lenient warn on a null clauseId in ClauseReferenceLayer is recorded as too lenient
// in docs/tech-debt.md item 12 and is not repeated here.
public sealed class RadioQuestionLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-10";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 10";
    private const string RadioQuestion = "radioQuestion";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var minimum = context.Config.RadioPattern.MinimumOptions;
        var sections = context.Agr.DataInputs.Sections;

        for (var s = 0; s < sections.Count; s++)
        {
            var blocks = sections[s].Blocks;
            for (var b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                if (block.BlockType != RadioQuestion) continue;
                var path = $"/dataInputs/sections/{s}/blocks/{b}";

                if (block.Question is null)
                {
                    checks.Add(Fail(
                        $"Section '{sections[s].Title}' has a radioQuestion block with no question object.",
                        $"{path}/question",
                        "a question object with number, prompt, required, and options",
                        "absent",
                        "Add a question object to the radioQuestion block, with number, prompt, "
                            + "required, and at least the minimum number of options."));
                    continue;
                }

                var q = block.Question;

                if (string.IsNullOrWhiteSpace(q.Prompt))
                    checks.Add(Fail(
                        $"Radio question {q.Number} has an empty prompt.",
                        $"{path}/question/prompt",
                        "a non-empty question prompt",
                        "empty",
                        $"Supply the prompt text for radio question {q.Number}."));

                if (q.Options.Count < minimum)
                    checks.Add(Fail(
                        $"Radio question {q.Number} has {q.Options.Count} options; the pattern requires at least {minimum}.",
                        $"{path}/question/options",
                        $"at least {minimum} options",
                        $"{q.Options.Count} options",
                        $"Add options to radio question {q.Number} until it has at least {minimum}. "
                            + "The standard option set in style-rules.json radioOptionPattern is a valid basis."));
            }
        }

        return checks;
    }

    private static Check Fail(string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ConfigurationError,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
```

- [ ] **Step 5: Register the layer**

In `src/Registrar.Validation/ValidationService.cs`, in the `layers` list (lines 62-74), add after `new ClauseReferenceLayer(),`:

```csharp
                new RadioQuestionLayer(),
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~RadioQuestionLayerTests"`
Expected: PASS (9 test cases: 2+2+2+2 theory cases plus the non-radio fact).

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Validation/RadioQuestionLayer.cs src/Registrar.Validation/ValidationService.cs tests/Registrar.Tests/RadioQuestionLayerTests.cs fixtures/agr-sample-tcl-radio-missing-question.json
git commit -m "feat: implement QA-SF-10 radio-question validation, blocking in both modes

The AGR schema is flat (one contentBlock object, no per-blockType if/then), so a
radioQuestion block with no question object is schema-valid and would null-deref
in the renderer. This layer fails closed on it. Blocking in draft as well as
promotion: the lenient null-clauseId warn is recorded as a mistake (tech-debt 12)
and is not repeated."
```

---

## Task 4: Render the radio question

**Files:**
- Create: `src/Registrar.Rendering/Blocks/RadioQuestionBlock.cs`
- Modify: `src/Registrar.Rendering/ParagraphFormatting.cs` (add `IndentLeftTwips`)
- Modify: `src/Registrar.Rendering/OpenXmlHelpers.cs` (add `italic` to `StyledRun`)
- Modify: `src/Registrar.Rendering/RenderContext.cs`
- Modify: `src/Registrar.Rendering/ArtifactRenderer.cs` (line 19)
- Modify: `src/Registrar.Rendering/Blocks/BlockRenderer.cs` (add case)
- Modify: `src/Registrar.Validation/BlockTypeSupportLayer.cs` (lines 16-19 and 48)
- Modify: `config/qa-checklist.json` (QA-ENG-06 rule text)
- Test: `tests/Registrar.Tests/Rendering/RadioQuestionBlockTests.cs`
- Test: `tests/Registrar.Tests/Rendering/BlockRendererTests.cs` (drift guard, lines 123-158)

**Interfaces:**
- Consumes: `RadioOptionPattern` (Task 1), `AgrQuestion` (Task 2).
- Produces: `RadioQuestionBlock.Render(AgrQuestion question, StyleRules style, RadioOptionPattern radio)` returning `IEnumerable<Paragraph>`; `RenderContext` gains a fourth positional member `RadioOptionPattern Radio`.

**Critical constraints for this task:**
- **Never write the U+25CB character literally into a `.cs` file.** `SourceHygieneTests` enforces an escapes-only rule on engine source. The glyph comes from config at runtime (`radio.Glyph`); in TEST code, if you need to assert it, write it as the escape `"\u25cb"`.
- **Emit no `NumberingProperties`.** This is what makes "no plain bullets, no numbered list" true by construction and is directly asserted by a test below.
- `ParaFormat.Build` appends children in CT_PPr schema order (keepNext, widowControl, numPr, pBdr, spacing, ind). Indentation MUST be appended last.
- `StyledRun` builds CT_RPr in schema order (rFonts, b, i, caps, color, spacing, sz). Italic MUST be appended after Bold and before Caps.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Rendering/RadioQuestionBlockTests.cs`:

```csharp
using DocumentFormat.OpenXml.Wordprocessing;
using Registrar.Core.Model;
using Registrar.Rendering;
using Xunit;

namespace Registrar.Tests.Rendering;

public class RadioQuestionBlockTests
{
    private static AgrQuestion Question(bool required = true) => new()
    {
        Number = 3,
        Prompt = "Was the prompting walkthrough completed?",
        Required = required,
        Options = new[]
        {
            "Yes, reviewed and acknowledged",
            "Yes, partial review, follow-up scheduled",
            "No, blocked by access or timing",
            "Other or Not applicable"
        }
    };

    private static List<Paragraph> Render(AgrQuestion q)
    {
        var config = SemanticTestSupport.Config();
        return RadioQuestionBlock.Render(q, config.Style, config.RadioPattern).ToList();
    }

    [Fact]
    public void Renders_a_descriptor_line_then_one_paragraph_per_option()
    {
        var paras = Render(Question());

        Assert.Equal(5, paras.Count); // 1 descriptor + 4 options
        Assert.Equal("Question 3. Select one answer. Required", paras[0].InnerText);
    }

    [Fact]
    public void Descriptor_line_uses_the_optional_label_when_not_required()
    {
        var paras = Render(Question(required: false));

        Assert.Equal("Question 3. Select one answer. Optional", paras[0].InnerText);
    }

    [Fact]
    public void Descriptor_line_is_italic_and_secondary_gray()
    {
        var paras = Render(Question());

        var rpr = paras[0].Descendants<RunProperties>().First();
        Assert.NotNull(rpr.Italic);
        Assert.Equal("5A6470", rpr.Color!.Val!.Value);
    }

    [Fact]
    public void Each_option_leads_with_the_radio_glyph_in_the_configured_color()
    {
        var paras = Render(Question());

        var option = paras[1];
        var runs = option.Descendants<Run>().ToList();
        Assert.Equal("\u25cb", runs[0].InnerText); // U+25CB
        Assert.Equal("005CB9", runs[0].RunProperties!.Color!.Val!.Value);
        Assert.Contains("Yes, reviewed and acknowledged", option.InnerText);
    }

    [Fact]
    public void Options_are_indented()
    {
        var paras = Render(Question());

        var ind = paras[1].ParagraphProperties!.Indentation;
        Assert.NotNull(ind);
        Assert.True(int.Parse(ind!.Left!.Value!) > 0);
    }

    // QA-SF-10 forbids checkboxes, plain bullets, dash lists, and numbered lists for
    // selections. RadioQuestionBlock is the only path that emits a radio question, so
    // the guarantee is structural: it references no numbering definition at all.
    [Fact]
    public void QA_SF_10_emits_no_numbering_reference()
    {
        var paras = Render(Question());

        Assert.All(paras, p => Assert.Empty(p.Descendants<NumberingProperties>()));
    }
}
```

- [ ] **Step 2: Run to make sure they fail**

Run: `dotnet test --filter "FullyQualifiedName~RadioQuestionBlockTests"`
Expected: FAIL to compile - `RadioQuestionBlock` does not exist.

- [ ] **Step 3: Add italic support to StyledRun**

In `src/Registrar.Rendering/OpenXmlHelpers.cs`, change the signature and body (CT_RPr order is rFonts, b, i, caps, color, spacing, sz - italic goes between bold and caps):

```csharp
    public static Run StyledRun(string text, string family, int sizePt, string colorHex, bool bold,
        bool caps = false, int characterSpacingTwips = 0, bool italic = false)
    {
        var rpr = new RunProperties(new RunFonts { Ascii = family, HighAnsi = family });
        if (bold) rpr.Append(new Bold());
        if (italic) rpr.Append(new Italic());
        if (caps) rpr.Append(new Caps());
        rpr.Append(new Color { Val = Hex(colorHex) });
        if (characterSpacingTwips != 0) rpr.Append(new Spacing { Val = characterSpacingTwips });
        rpr.Append(new FontSize { Val = (sizePt * 2).ToString() });
        return new Run(rpr, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }
```

`italic` is added as a trailing optional parameter so no existing call site changes.

- [ ] **Step 4: Add indentation support to ParaFormat**

In `src/Registrar.Rendering/ParagraphFormatting.cs`, add to the `ParaFormat` record:

```csharp
    public int? IndentLeftTwips { get; init; }
```

And in `ParagraphFormatting.Build`, append AFTER the spacing block (ind is last in CT_PPr order), just before `return ppr;`:

```csharp
        if (f.IndentLeftTwips is int ind)
            ppr.Append(new Indentation { Left = ind.ToString() });
```

- [ ] **Step 5: Write the renderer**

Create `src/Registrar.Rendering/Blocks/RadioQuestionBlock.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

// The v1.2 radio-option pattern (ORG-MST-WFE-001), governed by QA-SF-10.
//
// Every visual property comes from config (glyph, glyph color, indentation flag,
// descriptor wording) so a v1.3 template pack is a config release, not a rebuild
// (rule 3). The glyph is never a literal in this file: engine source is held to an
// escapes-only rule by SourceHygieneTests, and the glyph's canonical home is
// style-rules.json radioOptionPattern.glyph (U+25CB, on the approvedGlyphAllowlist).
//
// This class emits NO NumberingProperties. That is the structural guarantee behind
// QA-SF-10's prohibition on checkboxes, plain bullets, dash lists, and numbered
// lists: there is no code path here that could produce one.
public static class RadioQuestionBlock
{
    public static IEnumerable<Paragraph> Render(AgrQuestion question, StyleRules style, RadioOptionPattern radio)
    {
        yield return Descriptor(question, style, radio);
        foreach (var option in question.Options)
            yield return Option(option, style, radio);
    }

    // "Question 3. Select one answer. Required" - italic, secondary gray, caption size.
    // Composed from the config format string; {number}/{requirement} rather than
    // bracketed tokens so the QA-ENG-07 residue scan does not read them as unresolved
    // placeholders in rendered prose.
    public static string DescriptorText(AgrQuestion question, RadioOptionPattern radio)
        => radio.DescriptorLineFormat
            .Replace("{number}", question.Number.ToString())
            .Replace("{requirement}", question.Required ? radio.RequiredLabel : radio.OptionalLabel);

    private static Paragraph Descriptor(AgrQuestion question, StyleRules style, RadioOptionPattern radio)
    {
        var run = OpenXmlHelpers.StyledRun(
            DescriptorText(question, radio),
            style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial",
            style.TypeScale.CaptionPt,
            style.Colors["secondaryGray"],
            bold: false,
            italic: true);

        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            KeepNext = true, // the descriptor must not orphan from its options
            WidowControl = style.Pagination.WidowControl,
            SpaceAfterTwips = style.Spacing.BulletAfterPt * 20,
            LineSpacing240 = (int)System.Math.Round(style.Spacing.LineSpacingPct * 240.0 / 100)
        });
        return new Paragraph(ppr, run);
    }

    private static Paragraph Option(string label, StyleRules style, RadioOptionPattern radio)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";

        var glyphRun = OpenXmlHelpers.StyledRun(
            radio.Glyph + "  ", family, style.TypeScale.BodyPt, radio.GlyphColor, bold: false);
        var labelRun = OpenXmlHelpers.StyledRun(
            label, family, style.TypeScale.BodyPt, "#000000", bold: false);

        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            WidowControl = style.Pagination.WidowControl,
            SpaceAfterTwips = style.Spacing.BulletAfterPt * 20,
            LineSpacing240 = (int)System.Math.Round(style.Spacing.LineSpacingPct * 240.0 / 100),
            IndentLeftTwips = radio.OptionsIndented ? radio.OptionIndentTwips : null
        });
        return new Paragraph(ppr, glyphRun, labelRun);
    }
}
```

- [ ] **Step 6: Carry the radio config into the render context**

In `src/Registrar.Rendering/RenderContext.cs`:

```csharp
using Registrar.Core.Config;

namespace Registrar.Rendering;

public sealed record RenderContext(
    StyleRules Style,
    IReadOnlyDictionary<string, ClauseEntry> Clauses,
    IReadOnlyDictionary<string, string?> Placeholders,
    RadioOptionPattern Radio);
```

In `src/Registrar.Rendering/ArtifactRenderer.cs` line 19:

```csharp
        var ctx = new RenderContext(style, config.ClausesById, agr.DataInputs.PlaceholderMap, config.RadioPattern);
```

Any other `new RenderContext(...)` call sites (notably the `Ctx()` helper in `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`) must pass `SemanticTestSupport.Config().RadioPattern` as the fourth argument. Fix them until the solution compiles.

- [ ] **Step 7: Dispatch it in BlockRenderer**

In `src/Registrar.Rendering/Blocks/BlockRenderer.cs`, add a case before `default:`:

```csharp
            case "radioQuestion":
            {
                // RadioQuestionLayer (QA-SF-10) has already failed closed on a null
                // question, so reaching here with one is an engine bug, not Governor
                // input. Guard rather than null-deref.
                if (block.Question is null)
                    throw new InvalidOperationException(
                        "radioQuestion block reached the renderer with no question; QA-SF-10 should have blocked it.");

                var paras = RadioQuestionBlock.Render(block.Question, ctx.Style, ctx.Radio)
                    .Cast<OpenXmlElement>().ToList();
                var scan = string.Join(" ",
                    new[] { RadioQuestionBlock.DescriptorText(block.Question, ctx.Radio), block.Question.Prompt }
                        .Concat(block.Question.Options));
                return new BlockRenderResult(paras, scan, null, null);
            }
```

The scan text feeds the QA-SF-08 post-render character scan and the QA-ENG-07 token-residue scan, so rendered radio prose is linted like any other body text.

**Note:** the prompt is included in the scan text but is NOT rendered as its own paragraph by `RadioQuestionBlock`. TCL fixtures carry the prompt as a preceding `paragraph` or `h3` block, which is how the existing artifact types introduce content. Keep it that way; do not have the renderer invent a prompt paragraph.

- [ ] **Step 8: Add radioQuestion to the supported set**

In `src/Registrar.Validation/BlockTypeSupportLayer.cs`, line 16-19:

```csharp
    public static readonly IReadOnlySet<string> SupportedBlockTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "paragraph", "h2", "h3", "bulletList", "table", "clauseInsertion", "radioQuestion"
    };
```

And line 48, the `Expected` string:

```csharp
        Expected = "a block type this engine version renders: paragraph, h2, h3, bulletList, table, clauseInsertion, radioQuestion",
```

- [ ] **Step 9: Update the QA-ENG-06 catalog entry**

In `config/qa-checklist.json`, the QA-ENG-06 entry's `rule` currently names `radioQuestion` among the not-yet-built types. Replace its `rule` value with:

```
"Every dataInputs.sections[].blocks[].blockType is one this engine version renders: paragraph, h2, h3, bulletList, table, clauseInsertion, radioQuestion. Block types defined in the AGR schema but not yet built (warnCallout, aggregationTable, copyReadyMessage) fail closed with a reported check before rendering is attempted. Blocks in draft and promotion."
```

Leaving this stale would make the catalog contradict the engine.

- [ ] **Step 10: Update the QA-ENG-06 drift guard**

In `tests/Registrar.Tests/Rendering/BlockRendererTests.cs`, the test `QA_ENG_06_supported_set_matches_renderer_capability` (lines 123-158) asserts the supported set exactly and loops the four deferred types. Update it so `radioQuestion` moves from the deferred list into the supported set, and add a sample `radioQuestion` block (with a valid four-option `AgrQuestion`) to the per-type sample dictionary so the "renders without throwing" half covers it. The deferred loop now covers exactly three types: `warnCallout`, `aggregationTable`, `copyReadyMessage`.

- [ ] **Step 11: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~RadioQuestionBlockTests|FullyQualifiedName~BlockRendererTests|FullyQualifiedName~BlockTypeSupportLayerTests"`
Expected: PASS.

- [ ] **Step 12: Run the full suite**

Run: `dotnet test`
Expected: all green.

- [ ] **Step 13: Commit**

```bash
git add src/Registrar.Rendering config/qa-checklist.json src/Registrar.Validation/BlockTypeSupportLayer.cs tests/Registrar.Tests/Rendering
git commit -m "feat: render radioQuestion blocks (U+25CB pattern) and support the block type

First of the four deferred block types to land. Every visual property comes from
config; the glyph is never a literal in source. The renderer emits no numbering
properties at all, which is the structural guarantee behind QA-SF-10's ban on
checkboxes, bullets, and numbered lists for selections, and is asserted directly.

QA-ENG-06's catalog rule text is updated in the same commit so the checklist does
not contradict the engine."
```

---

## Task 5: TCL end to end (draft and promotion)

**Files:**
- Create: `fixtures/agr-sample-tcl-draft.json`
- Create: `fixtures/agr-sample-tcl-promotion.json`
- Modify: `fixtures/registry-sample.csv`
- Test: `tests/Registrar.Tests/BuildCommandTests.cs` (add TCL cases)

**Interfaces:**
- Consumes: everything from Tasks 1-4.
- Produces: nothing new in code; proves the feature.

TCL config (`config/artifact-types.json`): `idPattern` `ORG-TRN-TCL-NNN`, classification `Client-Facing`, `requiredClauses` `["CL-003", "CL-004"]`, and seven ordered sections: Pre-Training Preparation, Access Validation, Required Training Coverage, Demonstration Requirements, Training Completion Record, Open Follow-Up Items, Record Storage Location. `SectionSchemaLayer` checks section titles against that ordered list, so **all seven must be present, in that order.**

- [ ] **Step 1: Create the draft fixture**

Create `fixtures/agr-sample-tcl-draft.json`, modeled on `fixtures/agr-sample-tak-draft.json`. Use `artifactType: "TCL"`, `documentId: "ORG-TRN-TCL-001"`, `version: "v1.0"`, `classification: "Client-Facing"`, `approvalStatus: "Draft"`, `layer: "Operational"`, and `outputFileName: "ORG - Training Checklist - Talewind CPA - v1.0.docx"`. Keep the same `context`, `sourceWorkflowContext`, `placeholderMap`, `dependencies`, and `renderingOptions` shape as the TAK fixture. Set `validationPolicy.mode` to `"draft"`.

All seven sections, in order, with CL-003 and CL-004 inserted and at least one `radioQuestion` carrying the full four-option standard set:

```json
    "sections": [
      { "title": "Pre-Training Preparation", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Complete these steps before the training session begins." },
        { "blockType": "clauseInsertion", "clauseId": "CL-003" }
      ] },
      { "title": "Access Validation", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Confirm each participant can sign in and reach the approved tool." },
        { "blockType": "clauseInsertion", "clauseId": "CL-004" }
      ] },
      { "title": "Required Training Coverage", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Record coverage for each required topic." },
        { "blockType": "radioQuestion", "question": {
          "number": 1,
          "prompt": "Was the approved-use walkthrough completed?",
          "required": true,
          "options": [
            "Yes, reviewed and acknowledged",
            "Yes, partial review, follow-up scheduled",
            "No, blocked by access or timing",
            "Other or Not applicable"
          ]
        } },
        { "blockType": "radioQuestion", "question": {
          "number": 2,
          "prompt": "Was the data-handling boundary reviewed?",
          "required": true,
          "options": [
            "Yes, reviewed and acknowledged",
            "Yes, partial review, follow-up scheduled",
            "No, blocked by access or timing",
            "Other or Not applicable"
          ]
        } }
      ] },
      { "title": "Demonstration Requirements", "descriptorLine": null, "blocks": [
        { "blockType": "radioQuestion", "question": {
          "number": 3,
          "prompt": "Did the participant demonstrate a governed prompt end to end?",
          "required": false,
          "options": [
            "Yes, reviewed and acknowledged",
            "Yes, partial review, follow-up scheduled",
            "No, blocked by access or timing",
            "Other or Not applicable"
          ]
        } }
      ] },
      { "title": "Training Completion Record", "descriptorLine": null, "blocks": [
        { "blockType": "table",
          "columnHeaders": ["Participant", "Role", "Completed"],
          "rows": [["Named participant", "Approved user", "Yes"]] }
      ] },
      { "title": "Open Follow-Up Items", "descriptorLine": null, "blocks": [
        { "blockType": "bulletList", "items": [
          "Schedule a follow-up for any partial review recorded above.",
          "Record any access blocker with the responsible owner."
        ] }
      ] },
      { "title": "Record Storage Location", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "The completed checklist is stored in the engagement record library." }
      ] }
    ],
```

Note question 3 uses `"required": false`, which exercises the `optionalLabel` path end to end.

- [ ] **Step 2: Create the promotion fixture**

Copy the draft fixture to `fixtures/agr-sample-tcl-promotion.json` and change only `validationPolicy.mode` to `"promotion"`. (This mirrors how the DCN/TAK/RAF draft and promotion fixtures relate.)

- [ ] **Step 3: Register the document ID**

In `fixtures/registry-sample.csv`, add a row (promotion cannot pass `RegistryLayer` without it). Match the existing column order and the classification in `artifact-types.json` (`Client-Facing`):

```
ORG-TRN-TCL-001,TCL,Client-Facing,Draft,v1.0,"Talewind CPA, LLC"
```

- [ ] **Step 4: Write the failing end-to-end tests**

Add to `tests/Registrar.Tests/BuildCommandTests.cs`. It already has `CopyToTemp`, `RepoRoot`, `FixedClock`, and a `PromotionBuildPayload(fixtureRelativePath)` helper that runs a promotion build, asserts a clean exit and a schema-valid DOCX, and returns the payload. Reuse them; do not invent a new harness. TCL is category `TRN`, so its repository target is `Controlled Documents/Training` (same as TAK).

```csharp
    [Fact]
    public void Tcl_draft_build_renders_radio_questions_and_writes_a_docx()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-tcl-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docxPath = Path.Combine(dir, "ORG - Training Checklist - Talewind CPA - v1.0.docx");
        Assert.True(File.Exists(docxPath));

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var text = body.InnerText;

        // All seven TCL sections rendered, in schema order.
        Assert.Contains("1. Pre-Training Preparation", text);
        Assert.Contains("7. Record Storage Location", text);

        // Descriptor lines: composed from config, both the required and optional labels.
        Assert.Contains("Question 1. Select one answer. Required", text);
        Assert.Contains("Question 3. Select one answer. Optional", text);

        // The radio glyph is present, and no option is a numbered or bulleted list item.
        Assert.Contains("\u25cb", text); // U+25CB radio glyph
        Assert.Contains("Yes, reviewed and acknowledged", text);

        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Tcl_promotion_build_renders_docx_and_emits_payload()
    {
        var payload = PromotionBuildPayload("fixtures/agr-sample-tcl-promotion.json");
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("Controlled Documents/Training", (string?)payload["repositoryTarget"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void QA_SF_10_missing_question_build_writes_report_no_docx_and_exits_1()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-tcl-radio-missing-question.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));

        var report = File.ReadAllText(Path.Combine(dir, "validation-report.json"));
        Assert.Contains("QA-SF-10", report);

        // The fixture must reach the semantic layers, not die at schema validation:
        // ValidationService short-circuits on a blocking schema failure and would never
        // run QA-SF-10. If this assertion fires, the fixture is schema-invalid.
        Assert.DoesNotContain("SchemaValidationError", report);
    }
```

The third test is the fail-closed end-to-end proof for QA-SF-10: a schema-valid AGR whose `radioQuestion` block carries no `question` object produces a report naming the check and no document at all. Its `DoesNotContain("SchemaValidationError")` assertion is load-bearing, not decoration: it is what proves the check was actually reached rather than pre-empted by the schema layer.

- [ ] **Step 5: Run them**

Run: `dotnet test --filter "FullyQualifiedName~BuildCommandTests"`
Expected: PASS. If `SectionSchemaLayer` fails, the section titles do not match `artifact-types.json` exactly, in content or order.

- [ ] **Step 6: Eyeball the document**

Run the CLI against the draft fixture and open the DOCX. Confirm the descriptor line reads "Question 1. Select one answer. Required", question 3 reads "Optional", the glyph is a hollow circle in ORG blue, and the options are indented and are NOT a bulleted or numbered list.

```powershell
dotnet run --project src/Registrar.Cli -- build fixtures/agr-sample-tcl-draft.json --mode draft
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add fixtures tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "test: render TCL end to end in draft and promotion

TCL has been a declared generation target (generationTarget: true,
usesRadioOptionPattern: true, section schema defined) that could not produce a
document, because every question it needs is a radioQuestion. It renders now.
Adds ORG-TRN-TCL-001 to the registry fixture so promotion can pass RegistryLayer."
```

---

## Task 6: Stamp artifactSha256

**Files:**
- Modify: `src/Registrar.Core/Hashing.cs`
- Modify: `src/Registrar.Cli/BuildCommand.cs` (lines 64-90)
- Test: `tests/Registrar.Tests/BuildCommandTests.cs`

**Interfaces:**
- Consumes: `RenderResult.Bytes`.
- Produces: `Hashing.Sha256Hex(byte[] bytes)` returning lowercase hex.

`ValidationReport.ArtifactSha256` exists in the model and the schema but `ValidationService.cs:117` hardcodes it to `null` and nothing ever sets it. The report therefore advertises an artifact hash and always emits nothing.

**The ordering problem.** In `BuildCommand`, `CommandSupport.WriteOutputs` (which serializes `validation-report.json`) is called at line 67, but the DOCX is not written until line 84, and the `docxEligible` decision sits between them at line 76. So today the report is written before the engine knows whether a document will exist. The hash must only ever describe a document that was actually produced; stamping it on a run that emitted no DOCX would be a false attestation. So compute eligibility FIRST, stamp the hash only if eligible, and only then write the outputs.

Leave `ValidationService.cs:117` as `ArtifactSha256 = null`. That is correct: at validation time no document exists. The stamp happens in `BuildCommand`, which is the only place that knows a DOCX will be written.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Registrar.Tests/BuildCommandTests.cs`:

```csharp
    [Fact]
    public void Stamps_artifact_sha256_of_the_document_actually_produced()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docxPath = Directory.GetFiles(dir, "*.docx").Single();
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "validation-report.json")))!.AsObject();

        var stamped = (string?)report["artifactSha256"];
        Assert.NotNull(stamped);
        Assert.Equal(64, stamped!.Length);
        Assert.Equal(stamped, stamped.ToLowerInvariant());

        // The hash must be OF THE BYTES ON DISK, not of some other artifact.
        Assert.Equal(Hashing.Sha256Hex(File.ReadAllBytes(docxPath)), stamped);
    }

    [Fact]
    public void Leaves_artifact_sha256_null_when_no_document_is_produced()
    {
        // Fails closed pre-render (QA-ENG-05 prohibited character), so no DOCX exists.
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-prohibited-char.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(1, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));

        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "validation-report.json")))!.AsObject();
        Assert.Null((string?)report["artifactSha256"]);
    }
```

`Hashing` lives in `Registrar.Core`, which `BuildCommandTests.cs` already imports (`using Registrar.Core;` at line 4), and `JsonNode` is already imported at line 2. No new usings needed.

- [ ] **Step 2: Run to make sure they fail**

Run: `dotnet test --filter "FullyQualifiedName~artifact_sha256"`
Expected: FAIL - the passing-build test finds `ArtifactSha256` null.

- [ ] **Step 3: Add the byte[] overload**

In `src/Registrar.Core/Hashing.cs`:

```csharp
    public static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
```

- [ ] **Step 4: Reorder BuildCommand and stamp the hash**

In `src/Registrar.Cli/BuildCommand.cs`, replace lines 64-68 (the `PostRenderMerge.Apply` through the first `WriteOutputs`) so eligibility is decided before the report is written:

```csharp
        var merged = PostRenderMerge.Apply(outcome, rendered.PostRenderChecks, agr.ValidationPolicy.WarningThreshold, config.Qa);

        // The artifact hash must describe a document that was actually produced, so
        // eligibility is decided BEFORE the report is written. Stamping a hash on a run
        // that emitted no DOCX would be a false attestation.
        var docxEligible = merged.Report.OverallResult == OverallResult.Pass
            || (merged.Report.OverallResult == OverallResult.PassWithWarnings
                && merged.Report.RequestedMode == Mode.Draft);

        if (docxEligible)
            merged = merged with
            {
                Report = merged.Report with { ArtifactSha256 = Hashing.Sha256Hex(rendered.Bytes) }
            };

        var mergedReport = merged.Report;

        var writeMerged = CommandSupport.WriteOutputs(merged, dir, stderr);
        if (writeMerged is not null) return writeMerged.Value;
```

Then delete the now-duplicate `docxEligible` declaration further down (the old lines 76-77), keeping the `if (mergedReport.OverallResult == OverallResult.Fail)` early return above it and the `if (docxEligible) { ... }` block that writes the DOCX unchanged.

`ValidationOutcome` is a record, so `with` works. Add `using Registrar.Core;` to the file if `Hashing` is not already in scope.

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~artifact_sha256"`
Expected: PASS.

- [ ] **Step 6: Run the full suite**

This step matters more than usual: the reorder touches the shared build path that DS and AUP also use.

Run: `dotnet test`
Expected: all green, including every existing DS and AUP build test.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/Hashing.cs src/Registrar.Cli/BuildCommand.cs tests/Registrar.Tests/BuildCommandTests.cs
git commit -m "fix: stamp artifactSha256 for documents that were actually produced

The field existed in the schema and the model but was hardcoded null and never
computed, so the validation report advertised an artifact hash and always emitted
nothing. Compute it from the rendered bytes.

Requires deciding DOCX eligibility before the report is written: previously the
report was serialized before the engine knew whether a document would exist.
The hash is stamped only when a DOCX is actually produced, so it can never
attest to a document that was withheld."
```

---

## Task 7: Close the loop on docs

**Files:**
- Modify: `docs/ROADMAP.md`
- Modify: `docs/tech-debt.md`
- Modify: `README.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update the deferred-block-type records**

- `docs/tech-debt.md` item 8 ("Deferred block types"): note that `radioQuestion` is **resolved** (rendered, QA-SF-10 enforced, TCL renders end to end), leaving three: `warnCallout`, `aggregationTable`, `copyReadyMessage`.
- `docs/ROADMAP.md`: add a delivered row for this slice, and update the "Remaining block types" backlog bullet to list only the remaining three.
- `README.md` "Known gaps": the four-deferred-block-types bullet becomes three.
- `CLAUDE.md` "Known gaps": same correction, in the Slice 8 paragraph that lists the deferred block types.

- [ ] **Step 2: Record what this slice did NOT do**

Add to `docs/tech-debt.md`:

- `style-rules.json` `radioOptionPattern.descriptorLine` (prose) and `prohibitedRenderings` remain documentary and are read by no code.
- The radio question's `prompt` is carried in the render scan text but is not rendered as its own paragraph; TCL fixtures introduce each question with a preceding paragraph block. If the documentation owner wants the prompt rendered by the block itself, that is a follow-up.

- [ ] **Step 3: Commit**

```bash
git add docs/ROADMAP.md docs/tech-debt.md README.md CLAUDE.md
git commit -m "docs: record radioQuestion delivered; three deferred block types remain"
```

- [ ] **Step 4: Open the PR**

```bash
git push -u origin feat/radio-question-tcl
gh pr create --base main --title "feat: radioQuestion rendering and TCL end-to-end" --body "..."
```

The PR body should lead with the outcome: TCL was a declared generation target that could not produce a document; it renders now, in draft and promotion. Then note QA-SF-10 is implemented (blocking in both modes), that `radioOptionPattern` was dead config until this slice bound it, and that `artifactSha256` is now stamped (with the BuildCommand reorder that required, since it touches the shared DS/AUP path).

---

## Self-Review

**Spec coverage:** Config binding (Task 1), model (Task 2), QA-SF-10 (Task 3), renderer + supported set + QA-ENG-06 catalog text (Task 4), TCL fixtures/registry/end-to-end (Task 5), artifactSha256 + BuildCommand reorder (Task 6), docs (Task 7). Every spec section maps to a task.

**Risks carried from the spec:**
- Task 6 modifies the shared build path used by DS and AUP. Its Step 6 exists specifically to catch a regression there.
- Task 4 changes the `RenderContext` constructor, which is a breaking change to every call site including tests. Step 6 of that task says so explicitly.
- Task 3's fixture is deliberately schema-invalid on `options`; the layer test bypasses schema validation by calling `Validate` directly. Do not "fix" the fixture.
