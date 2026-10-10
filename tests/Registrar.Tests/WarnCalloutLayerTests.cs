using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class WarnCalloutLayerTests
{
    [Fact]
    public void QA_SF_09_fails_on_warn_callout_in_client_facing_promotion()
    {
        var context = new SemanticContext(
            AgrParser.Parse(WithWarnCalloutInPurpose()), SemanticTestSupport.Config(), Mode.Promotion, Registry: null);

        var check = Assert.Single(new WarnCalloutLayer().Validate(context));
        Assert.Equal("QA-SF-09", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal("/dataInputs/sections/0/blocks/1", check.JsonPath);
    }

    [Fact]
    public void QA_SF_09_passes_in_draft_even_with_warn_callout()
    {
        var context = new SemanticContext(
            AgrParser.Parse(WithWarnCalloutInPurpose()), SemanticTestSupport.Config(), Mode.Draft, Registry: null);

        Assert.Empty(new WarnCalloutLayer().Validate(context));
    }

    [Fact]
    public void QA_SF_09_passes_when_no_warn_callout_at_promotion()
    {
        var checks = new WarnCalloutLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Promotion));
        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_09_passes_for_non_client_facing_type_at_promotion()
    {
        // PIG (Package Inventory and Executive Guide) is classified Internal Working Document.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-unsupported-block.json")
            .Replace("\"artifactType\": \"DS\"", "\"artifactType\": \"PIG\"");
        var context = new SemanticContext(
            AgrParser.Parse(text), SemanticTestSupport.Config(), Mode.Promotion, Registry: null);

        Assert.Empty(new WarnCalloutLayer().Validate(context));
    }

    [Fact]
    public void QA_SF_09_fails_on_internal_only_block_in_client_facing_promotion()
    {
        var context = new SemanticContext(
            AgrParser.Parse(WithInternalNoteInPurpose()), SemanticTestSupport.Config(), Mode.Promotion, Registry: null);

        var check = Assert.Single(new WarnCalloutLayer().Validate(context));
        Assert.Equal("QA-SF-09", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("/dataInputs/sections/0/blocks/1", check.JsonPath);
        Assert.Contains("internal-only", check.Message);
    }

    [Fact]
    public void QA_SF_09_passes_in_draft_even_with_internal_only_block()
    {
        var context = new SemanticContext(
            AgrParser.Parse(WithInternalNoteInPurpose()), SemanticTestSupport.Config(), Mode.Draft, Registry: null);

        Assert.Empty(new WarnCalloutLayer().Validate(context));
    }

    // Injects an internalOnly paragraph as the second block of the Purpose section
    // (blocks index 1) of the client-facing DS draft fixture.
    private static string WithInternalNoteInPurpose()
        => SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace(
                "{ \"blockType\": \"paragraph\", \"text\": \"This summary documents the approved pilot deployment posture for Microsoft Copilot Chat.\" }",
                "{ \"blockType\": \"paragraph\", \"text\": \"This summary documents the approved pilot deployment posture for Microsoft Copilot Chat.\" }, { \"blockType\": \"paragraph\", \"text\": \"Internal note: confirm the pilot group before delivery.\", \"internalOnly\": true }");

    // Injects a warnCallout as the second block of the Purpose section (blocks index 1)
    // of the client-facing DS draft fixture. Used instead of the QA-ENG-06 fixture
    // (agr-sample-ds-unsupported-block.json), which now carries a synthetic
    // never-in-schema block type (notARenderableBlock) since all ten schema-defined
    // block types are supported as of this task.
    private static string WithWarnCalloutInPurpose()
        => SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace(
                "{ \"blockType\": \"paragraph\", \"text\": \"This summary documents the approved pilot deployment posture for Microsoft Copilot Chat.\" }",
                "{ \"blockType\": \"paragraph\", \"text\": \"This summary documents the approved pilot deployment posture for Microsoft Copilot Chat.\" }, { \"blockType\": \"warnCallout\", \"text\": \"Do not deploy without sign-off.\" }");
}
