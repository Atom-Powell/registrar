using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class BlockTypeSupportLayerTests
{
    // Wraps a single block into a one-section AGR (based on the DS draft fixture) and
    // a SemanticContext, mirroring RadioQuestionLayerTests.WithBlock. QA-ENG-06 does not
    // branch on mode, so Mode.Draft is used unconditionally.
    private static SemanticContext ContextWithBlock(AgrBlock block)
    {
        var agr = SemanticTestSupport.Agr("agr-sample-ds-draft.json");
        var section = agr.DataInputs.Sections[0] with { Blocks = new[] { block } };
        var withBlock = agr with
        {
            DataInputs = agr.DataInputs with { Sections = new[] { section } }
        };
        return new SemanticContext(withBlock, SemanticTestSupport.Config(), Mode.Draft, Registry: null);
    }

    [Fact]
    public void QA_ENG_06_passes_for_all_supported_block_types()
    {
        var checks = new BlockTypeSupportLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Theory]
    [InlineData(Mode.Draft)]
    [InlineData(Mode.Promotion)]
    public void QA_ENG_06_fails_closed_on_unsupported_block_type(string mode)
    {
        // agr-sample-ds-unsupported-block.json carries a synthetic unrenderable block type
        // (notARenderableBlock) in its second Purpose block. All ten schema-defined block
        // types are now supported (copyReadyMessage landed in this task), so this fixture
        // no longer retargets to a real deferred type; it exercises the generic fail-closed
        // path against a block type that will never appear in the AGR schema.
        var checks = new BlockTypeSupportLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-unsupported-block.json", mode));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-06", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.ConfigurationError, check.ErrorCategory);
        Assert.Equal("/dataInputs/sections/0/blocks/1/blockType", check.JsonPath);
        Assert.Equal("notARenderableBlock", check.Actual);
    }

    [Fact]
    public void QA_ENG_06_accepts_warnCallout()
    {
        Assert.Contains("warnCallout", BlockTypeSupportLayer.SupportedBlockTypes);
    }

    [Fact]
    public void QA_ENG_06_passes_copyReadyMessage()
    {
        var ctx = ContextWithBlock(new AgrBlock { BlockType = "copyReadyMessage", Text = "Delivery message." });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-06");
    }

    [Fact]
    public void QA_ENG_06_passes_aggregationTable_with_clauseLibrary_source()
    {
        var ctx = ContextWithBlock(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "clauseLibrary"
        });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-06");
    }

    [Fact]
    public void QA_ENG_06_passes_aggregationTable_with_placeholderDictionary_source()
    {
        var ctx = ContextWithBlock(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "placeholderDictionary"
        });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-06");
    }

    [Fact]
    public void QA_ENG_06_live_aggregation_sources_are_exactly_clauseLibrary_and_placeholderDictionary()
    {
        Assert.Equal(
            new[] { "clauseLibrary", "placeholderDictionary" }.OrderBy(x => x),
            BlockTypeSupportLayer.LiveAggregationSources.OrderBy(x => x));
    }

    [Fact]
    public void QA_ENG_06_fails_aggregationTable_with_missing_source()
    {
        var ctx = ContextWithBlock(new AgrBlock { BlockType = "aggregationTable" });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.Contains(checks, c => c.CheckId == "QA-ENG-06" && c.Result == CheckResultValue.Fail);
    }

    [Fact]
    public void QA_ENG_06_fails_aggregationTable_with_unknown_source()
    {
        var ctx = ContextWithBlock(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "unknownSource"
        });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.Contains(checks, c => c.CheckId == "QA-ENG-06" && c.Result == CheckResultValue.Fail);
    }
}
