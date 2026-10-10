using DocumentFormat.OpenXml.Wordprocessing;
using Registrar.Rendering;
using Registrar.Tests;
using Xunit;

namespace Registrar.Tests.Rendering;

public class AggregationTableBlockTests
{
    private static RenderContext Ctx()
    {
        var config = SemanticTestSupport.Config();
        return new RenderContext(config.Style, config.ClausesById, new Dictionary<string, string?>(),
            config.RadioPattern, config.WarnCallout, config.Clauses, config.CopyReadyMessage,
            config.CanonicalTokens);
    }

    [Fact]
    public void Render_projects_every_clause_in_order_with_recorded_sha()
    {
        var ctx = Ctx();
        var result = AggregationTableBlock.Render("clauseLibrary", ctx);
        var table = Assert.IsType<Table>(Assert.Single(result.Elements));
        var text = table.InnerText;

        foreach (var clause in ctx.ClauseLibrary)
        {
            Assert.Contains(clause.ClauseId, text);
            Assert.Contains(clause.CanonicalTextSha256, text);
        }
        // First data row is the first library clause: order is preserved.
        var firstDataRow = table.Elements<TableRow>().ElementAt(1).InnerText;
        Assert.Contains(ctx.ClauseLibrary[0].ClauseId, firstDataRow);
    }

    [Fact]
    public void Render_shows_canonical_text_verbatim_including_tokens()
    {
        var ctx = Ctx();
        var result = AggregationTableBlock.Render("clauseLibrary", ctx);
        // A clause library documents the token slots; canonical text is NOT substituted.
        Assert.Contains("[TOOL / CONNECTOR NAME]", result.ScanText);
    }

    [Fact]
    public void Render_throws_on_non_live_source()
    {
        var ctx = Ctx();
        Assert.Throws<InvalidOperationException>(() => AggregationTableBlock.Render("unknownSource", ctx));
    }

    [Fact]
    public void Render_projects_every_token_with_aliases_and_usage()
    {
        var ctx = Ctx();
        var result = AggregationTableBlock.Render("placeholderDictionary", ctx);
        var table = Assert.IsType<Table>(Assert.Single(result.Elements));
        var text = table.InnerText;

        foreach (var token in ctx.PlaceholderDictionary)
            Assert.Contains(token.Token, text);

        // A known alias renders in the aliases column.
        Assert.Contains("[CUSTOMER]", result.ScanText);
        // A usage tag renders in the usage column.
        Assert.Contains("footer", result.ScanText);
        // Header row present.
        Assert.Contains("Token", text);
        Assert.Contains("Drift Aliases", text);
        Assert.Contains("Usage", text);
    }
}
