using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class HeadingStructureLayerTests
{
    [Fact]
    public void QA_SF_07_passes_for_well_formed_body()
    {
        var checks = new HeadingStructureLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));
        Assert.Empty(checks);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_07_fails_on_empty_h1(string mode)
    {
        var checks = new HeadingStructureLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-empty-h1.json", mode));
        var check = Assert.Single(checks);
        Assert.Equal("QA-SF-07", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0", check.JsonPath);
    }

    [Fact]
    public void QA_SF_07_fails_on_heading_level_skip()
    {
        var checks = new HeadingStructureLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-heading-skip.json", Mode.Draft));
        var check = Assert.Single(checks);
        Assert.Equal("QA-SF-07", check.CheckId);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/1", check.JsonPath);
    }
}
