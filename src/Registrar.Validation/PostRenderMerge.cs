using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Validation;

public static class PostRenderMerge
{
    public static ValidationOutcome Apply(
        ValidationOutcome outcome, IReadOnlyList<Check> postRenderChecks, int? warningThreshold,
        QaCatalog? qa = null)
    {
        if (postRenderChecks.Count == 0) return outcome;

        // Post-render checks are the only unstamped checks here; the report's existing
        // checks were already stamped in ValidationService.
        var stamped = qa is null ? postRenderChecks : qa.StampAll(postRenderChecks);
        var combined = outcome.Report.Checks.Concat(stamped).ToList();
        var overall = OverallResultPolicy.Compute(combined, warningThreshold);
        var report = outcome.Report with { Checks = combined, OverallResult = overall };
        var payload = overall == OverallResult.Fail ? null : outcome.Payload;
        return new ValidationOutcome(report, payload);
    }
}
