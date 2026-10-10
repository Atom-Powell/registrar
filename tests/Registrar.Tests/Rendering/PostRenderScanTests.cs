using Registrar.Core;
using Registrar.Rendering;
using Registrar.Tests;

namespace Registrar.Tests.Rendering;

public class PostRenderScanTests
{
    private static CharacterPolicy Policy()
        => CharacterPolicy.FromConfig(SemanticTestSupport.Config().ContentLint);

    [Fact]
    public void QA_SF_08_passes_on_clean_runs()
    {
        var units = new[] { new ScanUnit("Clean ASCII text.", "section 'Purpose'") };
        Assert.Empty(PostRenderScan.Scan(units, Policy()));
    }

    [Fact]
    public void QA_SF_08_fails_on_prohibited_character()
    {
        var units = new[] { new ScanUnit("Bad \u2014 dash.", "clause CL-006") };
        var checks = PostRenderScan.Scan(units, Policy());
        var c = Assert.Single(checks);
        Assert.Equal("QA-SF-08", c.CheckId);
        Assert.Equal(Severity.Blocking, c.SeverityLevel);
        Assert.Null(c.JsonPath);
        Assert.Contains("CL-006", c.Message);
    }
}
