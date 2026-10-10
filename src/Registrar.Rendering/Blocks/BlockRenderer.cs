using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public sealed record BlockRenderResult(
    IReadOnlyList<OpenXmlElement> Elements, string ScanText,
    Check? ClauseCheck, ClauseInsertionAudit? ClauseAudit);

public static class BlockRenderer
{
    public static BlockRenderResult Render(AgrBlock block, int sectionIndex, int blockIndex, RenderContext ctx)
    {
        switch (block.BlockType)
        {
            case "paragraph":
            {
                var el = ParagraphBlock.Render(block.Text ?? string.Empty, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, block.Text ?? string.Empty, null, null);
            }
            case "h2":
            case "h3":
            {
                var level = block.BlockType == "h3" ? 3 : 2;
                var el = HeadingBlock.Render(block.Text ?? string.Empty, level, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, block.Text ?? string.Empty, null, null);
            }
            case "bulletList":
            {
                var items = block.Items ?? Array.Empty<string>();
                var paras = BulletListBlock.Render(items, ctx.Style).Cast<OpenXmlElement>().ToList();
                return new BlockRenderResult(paras, string.Join(" ", items), null, null);
            }
            case "table":
            {
                var rows = block.Rows ?? Array.Empty<IReadOnlyList<string>>();
                var el = TableBlock.Render(block.ColumnHeaders, rows, ctx.Style);
                var scan = string.Join(" ", (block.ColumnHeaders ?? Array.Empty<string>())
                    .Concat(rows.SelectMany(r => r)));
                return new BlockRenderResult(new OpenXmlElement[] { el }, scan, null, null);
            }
            case "clauseInsertion":
            {
                var res = ClauseResolver.Resolve(block.ClauseId, sectionIndex, blockIndex,
                    ctx.Clauses, ctx.Placeholders);
                if (res.Failure is not null)
                    return new BlockRenderResult(Array.Empty<OpenXmlElement>(), string.Empty,
                        res.Failure, res.Audit);
                var el = ParagraphBlock.Render(res.Text!, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, res.Text!, null, res.Audit);
            }
            case "radioQuestion":
            {
                // RadioQuestionLayer (QA-SF-10) has already failed closed on a null
                // question, so reaching here with one is an engine bug, not Governor
                // input. Guard rather than null-deref.
                if (block.Question is null)
                    throw new InvalidOperationException(
                        "radioQuestion block reached the renderer with no question; QA-SF-10 should have blocked it.");

                var paras = RadioQuestionBlock.Render(block.Question, ctx.Style, ctx.Radio)
                    .Cast<OpenXmlElement>().ToList();
                var scan = string.Join(" ",
                    new[] { RadioQuestionBlock.DescriptorText(block.Question, ctx.Radio), block.Question.Prompt }
                        .Concat(block.Question.Options));
                return new BlockRenderResult(paras, scan, null, null);
            }
            case "warnCallout":
            {
                var el = WarnCalloutBlock.Render(block.Text ?? string.Empty, ctx.Warn, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, block.Text ?? string.Empty, null, null);
            }
            case "aggregationTable":
            {
                return AggregationTableBlock.Render(block.AggregationSource, ctx);
            }
            case "copyReadyMessage":
            {
                var el = CopyReadyMessageBlock.Render(block.Text ?? string.Empty, ctx.CopyReadyMessage, ctx.Style);
                return new BlockRenderResult(new OpenXmlElement[] { el }, block.Text ?? string.Empty, null, null);
            }
            default:
                // No schema block type is deferred any more; this guards against an
                // unknown or future block type reaching the renderer.
                throw new NotSupportedException(
                    $"Block type '{block.BlockType}' is not supported in Slice 7 rendering.");
        }
    }
}
