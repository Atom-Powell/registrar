using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class MetadataLayerTests
{
    [Fact]
    public void QA_ENG_03_passes_when_document_id_matches_type()
    {
        var checks = new MetadataLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_03_fails_when_document_id_code_mismatches_artifact_type()
    {
        var checks = new MetadataLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-id-code-mismatch.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-03", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("AUP", check.Actual);
    }
}
