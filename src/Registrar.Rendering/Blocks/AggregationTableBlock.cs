using Registrar.Core.Config;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

// Config-sourced aggregation. The AGR block carries only aggregationSource; the whole
// body is projected from config here, so the document cannot diverge from config.
// clauseLibrary and placeholderDictionary are the two live sources. Canonical text and
// tokens are rendered verbatim, because these reference documents exist to display them.
public static class AggregationTableBlock
{
    private static readonly IReadOnlyList<string> ClauseHeaders =
        new[] { "Clause ID", "Name", "Canonical Text", "SHA-256" };

    private static readonly IReadOnlyList<string> PlaceholderHeaders =
        new[] { "Token", "Drift Aliases", "Usage" };

    public static BlockRenderResult Render(string? aggregationSource, RenderContext ctx) => aggregationSource switch
    {
        "clauseLibrary" => Project(ClauseHeaders, ctx.ClauseLibrary
            .Select(c => (IReadOnlyList<string>)new[]
                { c.ClauseId, c.Name, c.CanonicalText, c.CanonicalTextSha256 }).ToList(), ctx.Style),
        "placeholderDictionary" => Project(PlaceholderHeaders, ctx.PlaceholderDictionary
            .Select(t => (IReadOnlyList<string>)new[]
                { t.Token, string.Join(", ", t.KnownDriftAliases), t.Usage ?? string.Empty }).ToList(), ctx.Style),
        // BlockTypeSupportLayer (QA-ENG-06) fails closed on a non-live source before
        // rendering, so reaching here with one is an engine bug, not Governor input.
        _ => throw new InvalidOperationException(
            $"aggregationTable reached the renderer with source '{aggregationSource ?? "null"}'; "
            + "QA-ENG-06 should have blocked it. Live sources are clauseLibrary and placeholderDictionary."),
    };

    private static BlockRenderResult Project(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, StyleRules style)
    {
        var table = TableBlock.Render(headers, rows, style);
        var scan = string.Join(" ", headers.Concat(rows.SelectMany(r => r)));
        return new BlockRenderResult(new OpenXmlElement[] { table }, scan, null, null);
    }
}
