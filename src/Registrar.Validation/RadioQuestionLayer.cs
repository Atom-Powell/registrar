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
