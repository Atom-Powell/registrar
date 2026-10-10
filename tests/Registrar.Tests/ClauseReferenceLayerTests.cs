using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class ClauseReferenceLayerTests
{
    [Fact]
    public void QA_ENG_04_passes_when_required_clause_present_and_valid()
    {
        var checks = new ClauseReferenceLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_04_fails_when_required_clause_missing()
    {
        var checks = new ClauseReferenceLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-missing-clause.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-04", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.ClauseResolutionError, check.ErrorCategory);
        Assert.Contains("CL-006", check.Message);
    }

    [Fact]
    public void QA_ENG_04_fails_when_clause_id_unknown()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"CL-006\"", "\"CL-999\"");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var checks = new ClauseReferenceLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        Assert.Contains(checks, c => c.Actual == "CL-999");
    }

    [Fact]
    public void QA_ENG_04_blocks_when_clause_insertion_has_no_clause_id()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("{ \"blockType\": \"clauseInsertion\", \"clauseId\": \"CL-006\" }",
                     "{ \"blockType\": \"clauseInsertion\", \"clauseId\": null }");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var checks = new ClauseReferenceLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        var check = Assert.Single(checks, c => c.CheckId == "QA-ENG-04"
            && c.JsonPath is not null && c.JsonPath.EndsWith("/clauseId"));
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.ClauseResolutionError, check.ErrorCategory);
    }
}
