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
