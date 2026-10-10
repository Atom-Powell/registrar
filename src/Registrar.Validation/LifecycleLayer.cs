using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class LifecycleLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-04";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 4";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        if (context.Registry is null)
            return new[] { Skipped("No ID Registry supplied; lifecycle check skipped.") };

        var record = context.Registry.Lookup(context.Agr.Identity.DocumentId);
        if (record is null)
            return new[] { Skipped("Document ID is not in the registry; lifecycle not evaluated (see QA-SF-03).") };

        var current = record.ApprovalStatus;
        var target = context.Agr.Identity.ApprovalStatus;

        if (!context.Config.ApprovalStatuses.Contains(current))
            return new[] { Fail(
                $"Registry approval status '{current}' is not a controlled status value.",
                $"a controlled status in {Join(context.Config.ApprovalStatuses)}", current,
                "Correct the registry entry's approval status to a controlled value.") };

        context.Config.Transitions.TryGetValue(current, out var allowed);
        allowed ??= Array.Empty<string>();

        if (allowed.Count == 0)
            return new[] { Fail(
                $"Registry status '{current}' is terminal; the artifact cannot be regenerated.",
                "a non-terminal registry status", current,
                "Issue a new Document ID or version for a superseded or retired artifact; terminal artifacts are historical records.") };

        if (target == current)
            return Array.Empty<Check>();     // same-state regeneration, a non-transition

        if (allowed.Contains(target))
            return Array.Empty<Check>();

        return new[] { Fail(
            $"Transition from '{current}' to '{target}' is not a legal approval-status transition.",
            $"a target in {Join(allowed)}", target,
            $"Set artifactIdentity.approvalStatus to a legal next state from '{current}', or leave it unchanged.") };
    }

    private static string Join(IEnumerable<string> items) => "[" + string.Join(", ", items) + "]";

    private static Check Fail(string message, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.LifecycleTransitionError,
        Message = message,
        JsonPath = "/artifactIdentity/approvalStatus",
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
