using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Rendering;

public sealed record ClauseResolution(string? Text, ClauseInsertionAudit? Audit, Check? Failure);

public static class ClauseResolver
{
    public const string CheckId = "QA-ENG-04";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 7";

    public static ClauseResolution Resolve(
        string? clauseId, int sectionIndex, int blockIndex,
        IReadOnlyDictionary<string, ClauseEntry> clauses,
        IReadOnlyDictionary<string, string?> placeholders)
    {
        var pointer = $"/dataInputs/sections/{sectionIndex}/blocks/{blockIndex}/clauseId";

        if (clauseId is null)
            return new ClauseResolution(null, null, Fail(
                "clauseInsertion block reached rendering with no clauseId.", pointer,
                "a clauseId referencing an approved clause", "null",
                "Set clauseId on the clauseInsertion block to a clause defined in clause-library.json."));

        if (!clauses.TryGetValue(clauseId, out var clause))
            return new ClauseResolution(null, null, Fail(
                $"Clause '{clauseId}' is not defined in the clause library.", pointer,
                "a clauseId present in clause-library.json", clauseId,
                $"Reference an existing clause from config/clause-library.json; '{clauseId}' is not defined."));

        // Clause-integrity failure (canonical text no longer matches its recorded SHA-256).
        // Classified as ClauseResolutionError, not a separate ClauseIntegrityError: the spec
        // (qa-checklist.json QA-SF-11) treats clause paraphrase/integrity as ClauseResolutionError.
        var actualSha = Hashing.Sha256Hex(clause.CanonicalText);
        var verified = string.Equals(actualSha, clause.CanonicalTextSha256, StringComparison.OrdinalIgnoreCase);
        if (!verified)
            return new ClauseResolution(null,
                new ClauseInsertionAudit(clauseId, clause.CanonicalTextSha256, actualSha, false, 0),
                Fail($"Clause '{clauseId}' failed SHA-256 integrity verification.", pointer,
                    clause.CanonicalTextSha256, actualSha,
                    "Reconcile clause-library.json: the canonical text no longer matches its recorded hash."));

        var text = clause.CanonicalText;
        var resolved = 0;
        foreach (var (token, value) in placeholders)
        {
            if (value is null) continue;
            if (text.Contains(token, StringComparison.Ordinal))
            {
                text = text.Replace(token, value);
                resolved++;
            }
        }

        return new ClauseResolution(text,
            new ClauseInsertionAudit(clauseId, clause.CanonicalTextSha256, actualSha, true, resolved), null);
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
