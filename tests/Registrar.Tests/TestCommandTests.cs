using Registrar.Cli;

namespace Registrar.Tests;

public class TestCommandTests
{
    [Fact]
    public void All_committed_fixtures_match_their_goldens()
    {
        var sw = new StringWriter();
        var code = TestCommand.Run(fixtureName: null, all: true, update: false, sw, sw);
        Assert.True(code == 0, sw.ToString());
    }

    [Fact]
    public void Single_fixture_matches_its_golden()
    {
        var sw = new StringWriter();
        var code = TestCommand.Run("agr-sample-ds-draft", all: false, update: false, sw, sw);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Missing_selector_is_a_usage_error()
    {
        var sw = new StringWriter();
        var code = TestCommand.Run(fixtureName: null, all: false, update: false, sw, sw);
        Assert.Equal(2, code);
    }
}
