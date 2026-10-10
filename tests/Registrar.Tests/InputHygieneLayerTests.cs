using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class InputHygieneLayerTests
{
    [Fact]
    public void QA_ENG_05_passes_for_clean_chrome_and_placeholders()
    {
        var checks = new InputHygieneLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_ENG_05_fails_closed_on_prohibited_char_in_client(string mode)
    {
        var checks = new InputHygieneLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-prohibited-char.json", mode));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-05", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("/context/client", check.JsonPath);
        Assert.Contains("U+2019", check.Actual);
    }

    [Fact]
    public void QA_ENG_05_reports_offense_in_a_placeholder_value()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace(
                "\"[APPROVED PURPOSE]\": \"Drafting and summarization of non-restricted business content\"",
                "\"[APPROVED PURPOSE]\": \"Drafting \\u2014 summarization\"");
        var agr = AgrParser.Parse(text);

        var checks = new InputHygieneLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        var check = Assert.Single(checks);
        Assert.Equal("/dataInputs/placeholderMap/[APPROVED PURPOSE]", check.JsonPath);
        Assert.Contains("em dash", check.Actual);
    }

    [Fact]
    public void QA_ENG_05_emits_one_check_per_offending_field()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"owner\": \"Registry Owner\"", "\"owner\": \"Tech\\u2013Success\"")
            .Replace("\"packageName\": \"Governed AI Rollout\"", "\"packageName\": \"Governed\\u2014Rollout\"");
        var agr = AgrParser.Parse(text);

        var checks = new InputHygieneLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        Assert.Equal(2, checks.Count);
        Assert.Contains(checks, c => c.JsonPath == "/context/owner");
        Assert.Contains(checks, c => c.JsonPath == "/renderingOptions/packageName");
    }
}
