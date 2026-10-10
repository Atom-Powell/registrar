using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

// QA-SF-07: heading structure of section bodies. A section is an implicit H1;
// h2/h3 blocks carry explicit sub-levels. Two rules: every H1 has body content
// (no-empty-h1) and no H1 -> H3 level skip (no-heading-level-skips). The
// section-schema-driven "subsection or table where required" clause of QA-SF-07
// is owned by QA-ENG-02 (SectionSchemaLayer) and is out of scope here.
public sealed class HeadingStructureLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-07";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 7";

    private static readonly HashSet<string> HeadingTypes = new(StringComparer.Ordinal) { "h2", "h3" };

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var sections = context.Agr.DataInputs.Sections;
        for (var s = 0; s < sections.Count; s++)
        {
            var section = sections[s];

            var hasContent = section.Blocks.Any(b => !HeadingTypes.Contains(b.BlockType));
            if (!hasContent) checks.Add(EmptyH1(section.Title, s));

            var sawH2 = false;
            for (var b = 0; b < section.Blocks.Count; b++)
            {
                var type = section.Blocks[b].BlockType;
                if (type == "h2") sawH2 = true;
                else if (type == "h3" && !sawH2) checks.Add(HeadingSkip(section.Title, s, b));
            }
        }
        return checks;
    }

    private static Check EmptyH1(string title, int s) => Build(
        $"Section '{title}' (an H1) has no body content.",
        $"/dataInputs/sections/{s}",
        "at least one content block (paragraph, bulletList, table, or clauseInsertion)",
        "heading-only or empty section",
        $"Add body content to section '{title}', or remove the section.");

    private static Check HeadingSkip(string title, int s, int b) => Build(
        $"Section '{title}' has an h3 with no preceding h2 (an H1 to H3 level skip).",
        $"/dataInputs/sections/{s}/blocks/{b}",
        "an h2 heading before this h3",
        "h3 without a preceding h2",
        $"Insert an h2 before the h3 in section '{title}', or promote the h3 to h2.");

    private static Check Build(string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
