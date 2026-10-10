using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class RegistryLayerTests
{
    [Fact]
    public void QA_SF_03_passes_when_id_present_and_metadata_agrees()
    {
        var checks = new RegistryLayer().Validate(
            SemanticTestSupport.ContextWithRegistry("agr-sample-ds-draft.json", Mode.Draft, SemanticTestSupport.SampleRegistryCsv));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_03_fails_when_id_absent()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("ORG-GOV-DS-016", "ORG-GOV-DS-999");
        var context = new SemanticContext(AgrParser.Parse(text), SemanticTestSupport.Config(),
            Mode.Draft, CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv));

        var check = Assert.Single(new RegistryLayer().Validate(context));
        Assert.Equal("QA-SF-03", check.CheckId);
        Assert.Equal(Layer.Release, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.RegistrySyncError, check.ErrorCategory);
    }

    [Fact]
    public void QA_SF_03_fails_on_classification_drift()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"classification\": \"Client-Facing\"", "\"classification\": \"Controlled Signoff\"");
        var context = new SemanticContext(AgrParser.Parse(text), SemanticTestSupport.Config(),
            Mode.Draft, CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv));

        var checks = new RegistryLayer().Validate(context);
        Assert.Contains(checks, c => c.Result == CheckResultValue.Fail
            && c.JsonPath == "/artifactIdentity/classification");
    }

    [Fact]
    public void QA_SF_03_blocks_promotion_when_registry_absent()
    {
        var check = Assert.Single(new RegistryLayer().Validate(
            SemanticTestSupport.ContextNoRegistry("agr-sample-ds-draft.json", Mode.Promotion)));
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.RegistrySyncError, check.ErrorCategory);
    }

    [Fact]
    public void QA_SF_03_skips_in_draft_when_registry_absent()
    {
        var check = Assert.Single(new RegistryLayer().Validate(
            SemanticTestSupport.ContextNoRegistry("agr-sample-ds-draft.json", Mode.Draft)));
        Assert.Equal(CheckResultValue.Skipped, check.Result);
    }
}
