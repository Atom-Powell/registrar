using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Rendering;

public static class PostRenderScan
{
    public const string CheckId = "QA-SF-08";
    private const string RuleSourceValue = "ORG-MST-WFE-001 Section 4.5 item 8";

    public static IReadOnlyList<Check> Scan(IReadOnlyList<ScanUnit> units, CharacterPolicy policy)
    {
        var checks = new List<Check>();
        foreach (var unit in units)
        {
            if (string.IsNullOrEmpty(unit.Text)) continue;
            var offenses = CharacterLint.Scan(unit.Text, policy);
            if (offenses.Count == 0) continue;
            var first = offenses[0];
            checks.Add(new Check
            {
                CheckId = CheckId,
                LayerName = Layer.Content,
                Result = CheckResultValue.Fail,
                SeverityLevel = Severity.Blocking,
                ErrorCategory = ErrorCategory.MetadataViolation,
                Message = $"Rendered text in {unit.Locator} contains a prohibited character: "
                    + $"{CharacterLint.Describe(first.Codepoint)} at position {first.Position}.",
                JsonPath = null,
                Expected = "only printable Basic Latin, printable Latin-1 Supplement "
                    + "excluding NBSP and soft hyphen, or an approved glyph",
                Actual = CharacterLint.FormatActual(offenses),
                Remediation = $"A prohibited character reached the rendered document in {unit.Locator}. "
                    + "It originates in clause text or a field not covered by input hygiene; "
                    + "reconcile the source (clause-library.json or the offending field).",
                RuleSource = RuleSourceValue
            });
        }
        return checks;
    }
}
