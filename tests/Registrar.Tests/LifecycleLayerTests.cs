using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class LifecycleLayerTests
{
    private static SemanticContext Context(string agrText, string mode)
        => new(AgrParser.Parse(agrText), SemanticTestSupport.Config(), mode,
               CsvIdRegistry.FromCsvText(SemanticTestSupport.SampleRegistryCsv));

    [Fact]
    public void QA_SF_04_passes_on_same_state_regeneration()
    {
        // Golden AGR approvalStatus is Draft; registry ORG-GOV-DS-016 is Draft.
        var checks = new LifecycleLayer().Validate(
            Context(SemanticTestSupport.FixtureText("agr-sample-ds-draft.json"), Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_04_passes_on_legal_transition()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"approvalStatus\": \"Draft\"", "\"approvalStatus\": \"In Review\"");

        Assert.Empty(new LifecycleLayer().Validate(Context(text, Mode.Draft)));
    }

    [Fact]
    public void QA_SF_04_fails_on_illegal_transition()
    {
        // Draft -> Approved is not in the transition map.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"approvalStatus\": \"Draft\"", "\"approvalStatus\": \"Approved\"");

        var check = Assert.Single(new LifecycleLayer().Validate(Context(text, Mode.Draft)));
        Assert.Equal("QA-SF-04", check.CheckId);
        Assert.Equal(Layer.Release, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.LifecycleTransitionError, check.ErrorCategory);
    }

    [Fact]
    public void QA_SF_04_blocks_regeneration_of_terminal_registry_state()
    {
        // ORG-GOV-DS-017 is Retired in the registry.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("ORG-GOV-DS-016", "ORG-GOV-DS-017");

        var check = Assert.Single(new LifecycleLayer().Validate(Context(text, Mode.Draft)));
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Contains("terminal", check.Message);
    }

    [Fact]
    public void QA_SF_04_skips_when_registry_absent()
    {
        var context = new SemanticContext(
            SemanticTestSupport.Agr("agr-sample-ds-draft.json"), SemanticTestSupport.Config(), Mode.Draft, null);

        var check = Assert.Single(new LifecycleLayer().Validate(context));
        Assert.Equal(CheckResultValue.Skipped, check.Result);
    }
}
