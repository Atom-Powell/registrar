using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class BlockTypeSupportLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-06";
    private const string RuleSourceValue =
        "ORG-MST-SPEC-001 Section 6; Arti-Generator Unsafe Block Exclusion Rule";

    // Mirrors the case labels handled by Registrar.Rendering.Blocks.BlockRenderer.Render.
    // Any blockType outside this set falls to that method's default branch and throws
    // NotSupportedException; this layer catches the same condition before render so the
    // failure is reported (checkId/remediation/ruleSource) instead of crashing the CLI.
    public static readonly IReadOnlySet<string> SupportedBlockTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "paragraph", "h2", "h3", "bulletList", "table", "clauseInsertion", "radioQuestion", "warnCallout", "aggregationTable", "copyReadyMessage"
    };

    // Aggregation sources this engine version projects. Both are config-sourced reference
    // projections (the clause library and the placeholder dictionary).
    public static readonly IReadOnlySet<string> LiveAggregationSources = new HashSet<string>(StringComparer.Ordinal)
    {
        "clauseLibrary",
        "placeholderDictionary"
    };

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var sections = context.Agr.DataInputs.Sections;
        for (var s = 0; s < sections.Count; s++)
        {
            var section = sections[s];
            for (var b = 0; b < section.Blocks.Count; b++)
            {
                var block = section.Blocks[b];
                var blockType = block.BlockType;
                if (!SupportedBlockTypes.Contains(blockType))
                {
                    checks.Add(Fail(section.Title, blockType, s, b));
                    continue;
                }
                if (blockType == "aggregationTable" && !LiveAggregationSources.Contains(block.AggregationSource ?? ""))
                    checks.Add(FailSource(section.Title, block.AggregationSource, s, b));
            }
        }
        return checks;
    }

    private static Check Fail(string sectionTitle, string blockType, int sectionIndex, int blockIndex) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ConfigurationError,
        Message = $"Section '{sectionTitle}' contains a block type this engine version cannot render: "
            + $"'{blockType}'.",
        JsonPath = $"/dataInputs/sections/{sectionIndex}/blocks/{blockIndex}/blockType",
        Expected = "a block type this engine version renders: paragraph, h2, h3, bulletList, table, clauseInsertion, radioQuestion, warnCallout, aggregationTable, copyReadyMessage",
        Actual = blockType,
        Remediation = $"Replace the '{blockType}' block in section '{sectionTitle}' with a supported "
            + $"block type; '{blockType}' is not yet rendered by this engine version.",
        RuleSource = RuleSourceValue
    };

    private static Check FailSource(string sectionTitle, string? source, int sectionIndex, int blockIndex) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ConfigurationError,
        Message = $"Section '{sectionTitle}' has an aggregationTable with an aggregationSource this engine "
            + $"version does not project: '{source ?? "null"}'.",
        JsonPath = $"/dataInputs/sections/{sectionIndex}/blocks/{blockIndex}/aggregationSource",
        Expected = "a live aggregation source: clauseLibrary or placeholderDictionary",
        Actual = source ?? "null",
        Remediation = $"Set aggregationSource to a live source (clauseLibrary or placeholderDictionary). "
            + $"'{source ?? "null"}' is not projected by this engine version.",
        RuleSource = RuleSourceValue
    };
}
