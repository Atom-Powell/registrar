using System.Text;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Pipeline;

public static class GovernorClipboardText
{
    public static string Build(ValidationReport report)
    {
        var failing = report.Checks.Where(c => c.Result == CheckResultValue.Fail).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Validation failures for {report.DocumentId} ({report.RequestedMode}). "
            + "Regenerate the AGR addressing each item:");
        sb.AppendLine();
        var n = 0;
        foreach (var c in failing)
        {
            n++;
            sb.AppendLine($"{n}. {c.CheckId} at {c.JsonPath ?? "(document)"}");
            if (c.Expected is not null || c.Actual is not null)
                sb.AppendLine($"   expected: {c.Expected}; actual: {c.Actual}");
            if (c.Remediation is not null) sb.AppendLine($"   fix: {c.Remediation}");
            if (c.RuleSource is not null) sb.AppendLine($"   rule source: {c.RuleSource}");
        }
        return sb.ToString();
    }
}
