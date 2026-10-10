using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class SectionSchemaLayerTests
{
    [Fact]
    public void QA_ENG_02_passes_for_matching_sections()
    {
        var checks = new SectionSchemaLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_02_accepts_dcn_now_that_its_schema_is_defined()
    {
        var checks = new SectionSchemaLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-dcn-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_02_accepts_tak_now_that_its_schema_is_defined()
    {
        var checks = new SectionSchemaLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-tak-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_02_accepts_raf_now_that_its_schema_is_defined()
    {
        var checks = new SectionSchemaLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-raf-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_02_fails_for_mismatched_sections()
    {
        var checks = new SectionSchemaLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-section-mismatch.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-02", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.ConfigurationError, check.ErrorCategory);
        Assert.Contains("Purpose", check.Expected);
        Assert.Contains("Overview", check.Actual);
    }
}
