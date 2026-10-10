using Registrar.Tests;

namespace Registrar.Tests.Config;

public class WarnCalloutConfigTests
{
    [Fact]
    public void WarnCallout_label_binds_from_style_rules()
    {
        var config = SemanticTestSupport.Config();
        Assert.Equal("Warning", config.WarnCallout.Label);
    }
}
