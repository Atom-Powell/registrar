using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;
using Xunit;

namespace Registrar.Tests;

// QA-ENG-07 (TokenResidueLayer) pin: copyReadyMessage carries operator-facing authored
// text that is copied verbatim to the client (unlike aggregationTable/clauseInsertion,
// which are config-sourced and exempt). An unresolved bracketed token left in a
// copy-ready message would ship broken to the client, so it must be scanned and must
// fail closed. See the ScanFields comment in TokenResidueLayer.cs for the rationale.
public class CopyReadyMessageValidationTests
{
    // Mirrors AggregationSourceValidationTests.ContextWithBlockAndConfig: wraps a single
    // block into a one-section AGR (based on the DS draft fixture) and a SemanticContext
    // with real loaded config, since TokenResidueLayer needs
    // Config.ContentLint.TokenResidue.Pattern from real config. Mode.Draft is used
    // unconditionally since QA-ENG-07 does not branch on mode.
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

    [Fact]
    public void QA_ENG_07_flags_token_in_copyReadyMessage()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "copyReadyMessage",
            Text = "Dear [CLIENT ORGANIZATION NAME], your package is attached."
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.Contains(checks, c => c.CheckId == "QA-ENG-07" && c.Result == CheckResultValue.Fail);
    }

    [Fact]
    public void QA_ENG_07_passes_clean_copyReadyMessage()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "copyReadyMessage",
            Text = "Dear team, your governed package is attached. Please review and confirm."
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-07");
    }
}
