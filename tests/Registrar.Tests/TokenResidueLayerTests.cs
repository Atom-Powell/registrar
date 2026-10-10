using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class TokenResidueLayerTests
{
    [Fact]
    public void QA_ENG_07_passes_on_clean_body_prose()
    {
        var checks = new TokenResidueLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));
        Assert.Empty(checks);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_ENG_07_fails_on_token_in_paragraph(string mode)
    {
        var checks = new TokenResidueLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-token-residue.json", mode));
        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-07", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/0/text", check.JsonPath);
        Assert.Equal("[CLIENT ORGANIZATION NAME]", check.Actual);
    }

    [Fact]
    public void QA_ENG_07_does_not_false_positive_on_clause_blocks()
    {
        // The Audit section (index 4) is paragraph + clauseInsertion. clauseInsertion
        // carries no AGR-level text (clause text is resolved from the library at render
        // and governed by ClauseResolver / QA-SF-11), and ScanFields omits it, so a
        // document containing clause blocks yields no QA-ENG-07 finding.
        var checks = new TokenResidueLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Promotion));
        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_07_fails_closed_when_pattern_is_not_configured()
    {
        var config = SemanticTestSupport.Config() with
        {
            ContentLint = new ContentLint
            {
                ApprovedGlyphAllowlist = SemanticTestSupport.Config().ContentLint.ApprovedGlyphAllowlist,
                TokenResidue = null
            }
        };
        var context = new SemanticContext(
            SemanticTestSupport.Agr("agr-sample-ds-draft.json"), config, Mode.Draft, Registry: null);

        var check = Assert.Single(new TokenResidueLayer().Validate(context));
        Assert.Equal("QA-ENG-07", check.CheckId);
        Assert.Equal(ErrorCategory.ConfigurationError, check.ErrorCategory);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
    }

    [Fact]
    public void QA_ENG_07_fails_closed_when_pattern_times_out()
    {
        var config = SemanticTestSupport.Config() with
        {
            ContentLint = new ContentLint
            {
                ApprovedGlyphAllowlist = SemanticTestSupport.Config().ContentLint.ApprovedGlyphAllowlist,
                TokenResidue = new TokenResidueConfig { Pattern = "(a+)+$" }
            }
        };
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace(
                "This summary documents the approved pilot deployment posture for Microsoft Copilot Chat.",
                new string('a', 60) + "!");
        var agr = AgrParser.Parse(text);
        var context = new SemanticContext(agr, config, Mode.Draft, Registry: null);

        var check = Assert.Single(new TokenResidueLayer().Validate(context));
        Assert.Equal("QA-ENG-07", check.CheckId);
        Assert.Equal(ErrorCategory.ConfigurationError, check.ErrorCategory);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
    }

    [Fact]
    public void QA_ENG_07_flags_token_residue_in_a_warnCallout()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("{ \"blockType\": \"clauseInsertion\", \"clauseId\": \"CL-006\" }",
                     "{ \"blockType\": \"warnCallout\", \"text\": \"Residual [CLIENT ORGANIZATION NAME] token.\" }");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var checks = new TokenResidueLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        Assert.Contains(checks, c => c.CheckId == "QA-ENG-07" && c.Result == CheckResultValue.Fail);
    }
}
