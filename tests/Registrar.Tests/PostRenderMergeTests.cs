using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class PostRenderMergeTests
{
    private static ValidationReport Report(string overall, params Check[] checks) => new()
    {
        ReportVersion = "1.0", ExecutionId = "e", DocumentId = "ORG-GOV-DS-016",
        RequestedMode = "draft", OverallResult = overall, EngineVersion = "v",
        ConfigVersions = new Dictionary<string, string>(), TimestampUtc = "2026-07-05T00:00:00Z",
        Checks = checks
    };

    private static Check Blocking() => new()
    {
        CheckId = "QA-SF-08", LayerName = "content", Result = "fail",
        SeverityLevel = "blocking", Message = "bad"
    };

    [Fact]
    public void Blocking_post_render_check_flips_overall_to_fail_and_drops_payload()
    {
        var outcome = new ValidationOutcome(Report("pass"), null);
        var merged = PostRenderMerge.Apply(outcome, new[] { Blocking() }, null);
        Assert.Equal(OverallResult.Fail, merged.Report.OverallResult);
        Assert.Null(merged.Payload);
        Assert.Contains(merged.Report.Checks, c => c.CheckId == "QA-SF-08");
    }

    [Fact]
    public void No_post_render_checks_preserves_outcome()
    {
        var outcome = new ValidationOutcome(Report("pass"), null);
        var merged = PostRenderMerge.Apply(outcome, Array.Empty<Check>(), null);
        Assert.Equal(OverallResult.Pass, merged.Report.OverallResult);
    }
}
