using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class LayerAgreementLayerTests
{
    [Fact]
    public void QA_ENG_08_passes_when_layer_matches_the_configured_layer()
    {
        var checks = new LayerAgreementLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_08_fails_when_layer_disagrees_with_config()
    {
        var checks = new LayerAgreementLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-layer-mismatch.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-08", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("/artifactIdentity/layer", check.JsonPath);
        Assert.Equal("Governance", check.Expected);
        Assert.Equal("Delivery", check.Actual);
    }

    [Fact]
    public void QA_ENG_08_blocks_in_promotion_as_well_as_draft()
    {
        var checks = new LayerAgreementLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-layer-mismatch.json", Mode.Promotion));

        Assert.Single(checks);
    }

    // A type with no defined section schema carries no layer in config, so there is
    // nothing to compare. QA-ENG-02 already refuses it; this layer must not double-report.
    [Fact]
    public void QA_ENG_08_skips_a_type_that_declares_no_layer()
    {
        var context = SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft);
        var tvr = context.Agr with
        {
            Identity = context.Agr.Identity with { ArtifactType = "TVR" }
        };

        var checks = new LayerAgreementLayer().Validate(context with { Agr = tvr });

        Assert.Empty(checks);
    }

    // QA-ENG-08 stays quiet for a type with no configured layer (above) because
    // QA-ENG-02 already refuses that type before layer agreement would matter.
    // This test makes that premise executable instead of asserted only in prose.
    [Fact]
    public void QA_ENG_02_refuses_the_type_QA_ENG_08_skips()
    {
        var context = SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft);
        var tvr = context.Agr with
        {
            Identity = context.Agr.Identity with { ArtifactType = "TVR" }
        };

        var checks = new SectionSchemaLayer().Validate(context with { Agr = tvr });

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-02", check.CheckId);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
    }
}
