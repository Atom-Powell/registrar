using System.Collections.Generic;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class PromotionLayerTests
{
    private static SemanticContext PromotionContext(ConfigBundle config)
        => new(SemanticTestSupport.Agr("agr-sample-ds-draft.json"), config, Mode.Promotion,
               CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv));

    [Fact]
    public void QA_SPEC_18_skips_in_draft()
    {
        var checks = new PromotionLayer().Validate(
            new SemanticContext(SemanticTestSupport.Agr("agr-sample-ds-draft.json"),
                SemanticTestSupport.Config(), Mode.Draft,
                CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv)));

        Assert.Equal(CheckResultValue.Skipped, Assert.Single(checks).Result);
    }

    [Fact]
    public void QA_SPEC_18_passes_with_attestation_at_promotion()
    {
        var check = Assert.Single(new PromotionLayer().Validate(PromotionContext(SemanticTestSupport.Config())));

        Assert.Equal("QA-SPEC-18", check.CheckId);
        Assert.Equal(Layer.Release, check.LayerName);
        Assert.Equal(CheckResultValue.Pass, check.Result);
        Assert.Equal(Severity.Info, check.SeverityLevel);
        Assert.NotNull(check.Remediation);
    }

    [Fact]
    public void QA_SPEC_18_fails_when_no_repository_target_for_category()
    {
        var config = SemanticTestSupport.Config() with
        {
            RepositoryTargetsByCategory = new Dictionary<string, string>()
        };

        var check = Assert.Single(new PromotionLayer().Validate(PromotionContext(config)));
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.RegistrySyncError, check.ErrorCategory);
    }

    [Fact]
    public void QA_SPEC_18_skips_at_promotion_when_registry_absent()
    {
        var context = new SemanticContext(SemanticTestSupport.Agr("agr-sample-ds-draft.json"),
            SemanticTestSupport.Config(), Mode.Promotion, null);

        Assert.Equal(CheckResultValue.Skipped, Assert.Single(new PromotionLayer().Validate(context)).Result);
    }
}
