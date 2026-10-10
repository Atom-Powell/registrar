using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class OverallResultPolicyTests
{
    private static Check Make(string result, string severity) => new()
    {
        CheckId = "X", LayerName = Layer.Content, Result = result, SeverityLevel = severity,
        Message = "m"
    };

    [Fact]
    public void All_pass_yields_pass()
    {
        var checks = new[] { Make(CheckResultValue.Pass, Severity.Info) };
        Assert.Equal(OverallResult.Pass, OverallResultPolicy.Compute(checks, null));
    }

    [Fact]
    public void A_warn_yields_pass_with_warnings()
    {
        var checks = new[] { Make(CheckResultValue.Warn, Severity.Warning) };
        Assert.Equal(OverallResult.PassWithWarnings, OverallResultPolicy.Compute(checks, null));
    }

    [Fact]
    public void A_blocking_fail_yields_fail()
    {
        var checks = new[] { Make(CheckResultValue.Fail, Severity.Blocking) };
        Assert.Equal(OverallResult.Fail, OverallResultPolicy.Compute(checks, null));
    }

    [Fact]
    public void Warn_count_over_threshold_yields_fail()
    {
        var checks = new[]
        {
            Make(CheckResultValue.Warn, Severity.Warning),
            Make(CheckResultValue.Warn, Severity.Warning)
        };
        Assert.Equal(OverallResult.Fail, OverallResultPolicy.Compute(checks, 1));
    }

    [Fact]
    public void Warn_count_equal_to_threshold_yields_pass_with_warnings()
    {
        var checks = new[]
        {
            Make(CheckResultValue.Warn, Severity.Warning),
            Make(CheckResultValue.Warn, Severity.Warning)
        };
        Assert.Equal(OverallResult.PassWithWarnings, OverallResultPolicy.Compute(checks, 2));
    }

    [Fact]
    public void Blocking_fail_beats_over_threshold_warns()
    {
        var checks = new[]
        {
            Make(CheckResultValue.Fail, Severity.Blocking),
            Make(CheckResultValue.Warn, Severity.Warning),
            Make(CheckResultValue.Warn, Severity.Warning),
            Make(CheckResultValue.Warn, Severity.Warning)
        };
        Assert.Equal(OverallResult.Fail, OverallResultPolicy.Compute(checks, 1));
    }
}
