using Registrar.Tests;
using Xunit;

namespace Registrar.Tests.Config;

public class CopyReadyMessageConfigTests
{
    [Fact]
    public void Config_binds_copyReadyMessage_label()
    {
        var config = SemanticTestSupport.Config();
        Assert.False(string.IsNullOrWhiteSpace(config.CopyReadyMessage.Label));
    }
}
