using System.Text.Json;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;
using Xunit;

namespace Registrar.Tests;

public class AggregationSourceValidationTests
{
    [Fact]
    public void AgrBlock_deserializes_aggregationSource()
    {
        const string json = """
        { "blockType": "aggregationTable", "aggregationSource": "clauseLibrary" }
        """;
        var block = JsonSerializer.Deserialize<AgrBlock>(json);
        Assert.NotNull(block);
        Assert.Equal("aggregationTable", block!.BlockType);
        Assert.Equal("clauseLibrary", block.AggregationSource);
    }

    // Wraps a single block into a one-section AGR (based on the DS draft fixture) and a
    // SemanticContext with real loaded config, mirroring BlockTypeSupportLayerTests.
    // ContextWithBlock. TokenResidueLayer needs Config.ContentLint.TokenResidue.Pattern
    // from real config, hence "AndConfig" - Mode.Draft is used unconditionally since
    // neither check under test here branches on mode.
    private static SemanticContext ContextWithBlockAndConfig(AgrBlock block)
    {
        var agr = SemanticTestSupport.Agr("agr-sample-ds-draft.json");
        var section = agr.DataInputs.Sections[0] with { Blocks = new[] { block } };
        var withBlock = agr with
        {
            DataInputs = agr.DataInputs with { Sections = new[] { section } }
        };
        return new SemanticContext(withBlock, SemanticTestSupport.Config(), Mode.Draft, Registry: null);
    }

    // QA-ENG-07 (TokenResidueLayer) pin: proves the token-residue scan's existing
    // allowlist already exempts aggregationTable (config-sourced, no AGR body text) while
    // still catching a residual token left in authored prose. See the ScanFields comment
    // in TokenResidueLayer.cs for the rationale this guards.
    [Fact]
    public void QA_ENG_07_does_not_flag_aggregationTable_block()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "clauseLibrary"
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-07");
    }

    // PDX rows ARE bracketed tokens by nature; the residue scan must never flag the
    // placeholderDictionary aggregation, exactly as it exempts clauseLibrary. This pins
    // that a future scan-switch edit cannot start failing the document whose purpose is
    // to display the tokens.
    [Fact]
    public void QA_ENG_07_does_not_flag_placeholderDictionary_aggregation()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "placeholderDictionary"
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-07");
    }

    [Fact]
    public void QA_ENG_07_still_flags_token_in_authored_paragraph()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "paragraph",
            Text = "This references [TOOL / CONNECTOR NAME] in authored prose."
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.Contains(checks, c => c.CheckId == "QA-ENG-07" && c.Result == CheckResultValue.Fail);
    }
}
