using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public static class OverallResultPolicy
{
    public static string Compute(IReadOnlyList<Check> checks, int? warningThreshold)
    {
        var hasBlockingFail = checks.Any(c =>
            c.Result == CheckResultValue.Fail && c.SeverityLevel == Severity.Blocking);
        if (hasBlockingFail) return OverallResult.Fail;

        var warnCount = checks.Count(c => c.Result == CheckResultValue.Warn);
        if (warningThreshold is not null && warnCount > warningThreshold.Value)
            return OverallResult.Fail;

        return warnCount > 0 ? OverallResult.PassWithWarnings : OverallResult.Pass;
    }
}
