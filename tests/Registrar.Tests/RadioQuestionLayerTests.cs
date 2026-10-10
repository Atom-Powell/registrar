using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;
using Xunit;

namespace Registrar.Tests;

public class RadioQuestionLayerTests
{
    private static SemanticContext Ctx(AgrDocument agr, string mode)
        => new(agr, SemanticTestSupport.Config(), mode, Registry: null);

    private static AgrDocument WithBlock(AgrBlock block)
    {
        var agr = SemanticTestSupport.Agr("agr-sample-tcl-draft.json");
        var section = agr.DataInputs.Sections[0] with { Blocks = new[] { block } };
        return agr with
        {
            DataInputs = agr.DataInputs with { Sections = new[] { section } }
        };
    }

    private static AgrQuestion GoodQuestion() => new()
    {
        Number = 1,
        Prompt = "Was the approved-use walkthrough completed?",
        Required = true,
        Options = new[]
        {
            "Yes, reviewed and acknowledged",
            "Yes, partial review, follow-up scheduled",
            "No, blocked by access or timing",
            "Other or Not applicable"
        }
    };

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_10_passes_for_a_well_formed_radio_question(string mode)
    {
        var agr = WithBlock(new AgrBlock { BlockType = "radioQuestion", Question = GoodQuestion() });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, mode));

        Assert.Empty(checks);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_10_fails_closed_when_question_is_missing(string mode)
    {
        var agr = WithBlock(new AgrBlock { BlockType = "radioQuestion", Question = null });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, mode));

        var check = Assert.Single(checks);
        Assert.Equal(RadioQuestionLayer.CheckId, check.CheckId);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/0/question", check.JsonPath);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_10_fails_closed_below_the_configured_minimum_options(string mode)
    {
        var q = GoodQuestion() with { Options = new[] { "Yes", "No" } };
        var agr = WithBlock(new AgrBlock { BlockType = "radioQuestion", Question = q });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, mode));

        var check = Assert.Single(checks);
        Assert.Equal(RadioQuestionLayer.CheckId, check.CheckId);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/0/question/options", check.JsonPath);
        Assert.Contains("2", check.Actual);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_SF_10_fails_closed_on_an_empty_prompt(string mode)
    {
        var q = GoodQuestion() with { Prompt = "   " };
        var agr = WithBlock(new AgrBlock { BlockType = "radioQuestion", Question = q });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, mode));

        var check = Assert.Single(checks);
        Assert.Equal(RadioQuestionLayer.CheckId, check.CheckId);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/0/question/prompt", check.JsonPath);
    }

    [Fact]
    public void QA_SF_10_ignores_non_radio_blocks()
    {
        var agr = WithBlock(new AgrBlock { BlockType = "paragraph", Text = "Plain body text." });

        var checks = new RadioQuestionLayer().Validate(Ctx(agr, Mode.Draft));

        Assert.Empty(checks);
    }
}
