using Registrar.Cli;
using Registrar.Core.Model;

namespace Registrar.Tests.Cli;

public class ReportPresenterTests
{
    private static ValidationReport Report(string overall, params Check[] checks) => new()
    {
        ReportVersion = "1.0", ExecutionId = "e", DocumentId = "ORG-GOV-DS-016",
        RequestedMode = "draft", OverallResult = overall, EngineVersion = "v",
        ConfigVersions = new Dictionary<string, string>(), TimestampUtc = "t", Checks = checks
    };

    [Fact]
    public void Fail_report_shows_banner_and_each_failing_check()
    {
        var report = Report("fail", new Check
        {
            CheckId = "QA-ENG-05", LayerName = "input", Result = "fail", SeverityLevel = "blocking",
            Message = "prohibited character", Remediation = "remove the em dash", JsonPath = "/context/owner"
        });
        var lines = ReportPresenter.Present(report);
        var text = string.Join("\n", lines.Select(l => l.Text));
        Assert.Contains(lines, l => l.Style == LineStyle.Fail && l.Text.Contains("FAIL"));
        Assert.Contains("ORG-GOV-DS-016", text);
        Assert.Contains("QA-ENG-05", text);
        Assert.Contains("remove the em dash", text);
        Assert.Contains("/context/owner", text);
    }

    [Fact]
    public void Pass_report_shows_pass_and_no_failure_blocks()
    {
        var lines = ReportPresenter.Present(Report("pass"));
        Assert.Contains(lines, l => l.Style == LineStyle.Pass && l.Text.Contains("PASS"));
        Assert.DoesNotContain(lines, l => l.Style == LineStyle.Fail);
    }
}
