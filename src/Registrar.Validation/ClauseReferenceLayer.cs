using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class ClauseReferenceLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-04";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 7";
    private const string ClauseInsertion = "clauseInsertion";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var inserted = new HashSet<string>();
        var sections = context.Agr.DataInputs.Sections;

        for (var s = 0; s < sections.Count; s++)
        {
            var blocks = sections[s].Blocks;
            for (var b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                if (block.BlockType != ClauseInsertion) continue;
                if (block.ClauseId is null)
                {
                    checks.Add(Fail(
                        "clauseInsertion block has no clauseId.",
                        $"/dataInputs/sections/{s}/blocks/{b}/clauseId",
                        "a clauseId present in clause-library.json", "null",
                        "Set clauseId on the clauseInsertion block to a clause defined in clause-library.json."));
                    continue;
                }
                inserted.Add(block.ClauseId);
                if (!context.Config.ClauseIds.Contains(block.ClauseId))
                    checks.Add(Fail(
                        $"Clause '{block.ClauseId}' does not exist in the clause library.",
                        $"/dataInputs/sections/{s}/blocks/{b}/clauseId",
                        "a clauseId present in clause-library.json", block.ClauseId,
                        $"Reference an existing clause from config/clause-library.json; '{block.ClauseId}' is not defined."));
            }
        }

        if (context.Config.ArtifactTypesByCode.TryGetValue(context.Agr.Identity.ArtifactType, out var type)
            && type.RequiredClauses is not null)
        {
            foreach (var clauseId in type.RequiredClauses)
                if (!inserted.Contains(clauseId))
                    checks.Add(Fail(
                        $"Required clause '{clauseId}' is not inserted anywhere in the artifact.",
                        "/dataInputs/sections",
                        $"a clauseInsertion block referencing '{clauseId}'", "absent",
                        $"Add a clauseInsertion block with clauseId '{clauseId}' to the appropriate section."));
        }

        return checks;
    }

    private static Check Fail(string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Content,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ClauseResolutionError,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
