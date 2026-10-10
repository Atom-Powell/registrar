using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Pipeline;

public enum LineStyle { Normal, Pass, Warn, Fail }

public sealed record PresenterLine(string Text, LineStyle Style);

public static class ReportPresenter
{
    public static IReadOnlyList<PresenterLine> Present(ValidationReport report)
    {
        var lines = new List<PresenterLine>();
        var failing = report.Checks.Where(c => c.Result == CheckResultValue.Fail).ToList();
        var warning = report.Checks.Where(c => c.Result == CheckResultValue.Warn).ToList();

        var (banner, style) = report.OverallResult switch
        {
            OverallResult.Pass => ("PASS", LineStyle.Pass),
            OverallResult.PassWithWarnings => ("PASS WITH WARNINGS", LineStyle.Warn),
            _ => ("FAIL", LineStyle.Fail)
        };
        lines.Add(new PresenterLine(
            $"{banner}  -  {report.DocumentId}  ({report.RequestedMode})", style));
        lines.Add(new PresenterLine(
            $"{report.Checks.Count} check(s): {failing.Count} failing, {warning.Count} warning.", LineStyle.Normal));

        foreach (var c in failing.Concat(warning))
        {
            var s = c.Result == CheckResultValue.Fail ? LineStyle.Fail : LineStyle.Warn;
            lines.Add(new PresenterLine("", LineStyle.Normal));
            lines.Add(new PresenterLine($"[{c.Result}] {c.CheckId} ({c.LayerName}/{c.SeverityLevel})", s));
            lines.Add(new PresenterLine($"  {c.Message}", LineStyle.Normal));
            if (c.JsonPath is not null) lines.Add(new PresenterLine($"  at: {c.JsonPath}", LineStyle.Normal));
            if (c.Expected is not null || c.Actual is not null)
                lines.Add(new PresenterLine($"  expected: {c.Expected}   actual: {c.Actual}", LineStyle.Normal));
            if (c.Remediation is not null) lines.Add(new PresenterLine($"  fix: {c.Remediation}", LineStyle.Normal));
            if (c.RuleSource is not null) lines.Add(new PresenterLine($"  rule: {c.RuleSource}", LineStyle.Normal));
        }
        return lines;
    }
}
