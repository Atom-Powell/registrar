using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class PromotionLayer : ISemanticLayer
{
    public const string CheckId = "QA-SPEC-18";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 9 item 18";
    private const string AttestationRemediation =
        "Apply the emitted promotion-payload.json registry update to the PMC Registry sheet and confirm post-update state before final delivery.";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        if (context.Mode != Mode.Promotion)
            return new[] { Skipped("Registry-update payload applies only at promotion.") };

        if (context.Registry is null)
            return new[] { Skipped("No ID Registry supplied; registry-update payload not evaluated.") };

        var record = context.Registry.Lookup(context.Agr.Identity.DocumentId);
        if (record is null)
            return new[] { Skipped("Document ID not in registry; registry-update payload not evaluated (see QA-SF-03).") };

        var category = context.Agr.Identity.DocumentId.Split('-')[1];
        if (!context.Config.RepositoryTargetsByCategory.ContainsKey(category))
            return new[] { Fail(
                $"No repository target is configured for category '{category}'.",
                $"a target for '{category}' in repository-targets.json", category,
                "Add the category to config/repository-targets.json so the promotion payload can name a repository target.") };

        return new[] { Pass(
            "Registry update required; promotion-payload.json will be emitted for operator application.") };
    }

    private static Check Pass(string message) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Pass,
        SeverityLevel = Severity.Info,
        Message = message,
        Remediation = AttestationRemediation,
        RuleSource = RuleSourceValue
    };

    private static Check Fail(string message, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.RegistrySyncError,
        Message = message,
        JsonPath = "/artifactIdentity/documentId",
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };

    private static Check Skipped(string message) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Skipped,
        SeverityLevel = Severity.Info,
        Message = message,
        RuleSource = RuleSourceValue
    };
}
