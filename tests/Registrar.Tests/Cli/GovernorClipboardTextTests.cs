using Registrar.Cli;
using Registrar.Core.Model;

namespace Registrar.Tests.Cli;

public class GovernorClipboardTextTests
{
    [Fact]
    public void Build_lists_failing_checks_with_header_and_excludes_passes()
    {
        var report = new ValidationReport
        {
            ReportVersion = "1.0", ExecutionId = "e", DocumentId = "ORG-GOV-DS-016",
            RequestedMode = "promotion", OverallResult = "fail", EngineVersion = "v",
            ConfigVersions = new Dictionary<string, string>(), TimestampUtc = "t",
            Checks = new[]
            {
                new Check { CheckId = "QA-ENG-05", LayerName = "input", Result = "fail",
                    SeverityLevel = "blocking", Message = "bad char", JsonPath = "/context/owner",
                    Remediation = "remove the em dash", RuleSource = "ORG-MST-SPEC-001 Section 4.2" },
                new Check { CheckId = "QA-PASS", LayerName = "input", Result = "pass",
                    SeverityLevel = "info", Message = "fine" }
            }
        };
        var text = GovernorClipboardText.Build(report);
        Assert.Contains("ORG-GOV-DS-016", text);
        Assert.Contains("QA-ENG-05", text);
        Assert.Contains("/context/owner", text);
        Assert.Contains("remove the em dash", text);
        Assert.Contains("ORG-MST-SPEC-001 Section 4.2", text);
        Assert.DoesNotContain("QA-PASS", text);
    }
}
