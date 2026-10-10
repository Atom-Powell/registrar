using Registrar.Rendering;
using Registrar.Tests;
using Registrar.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class BlockRendererTests
{
    private static Registrar.Core.Config.StyleRules Style() => SemanticTestSupport.Config().Style;

    [Fact]
    public void Paragraph_renders_body_text()
    {
        var p = ParagraphBlock.Render("Hello body.", Style());
        Assert.Equal("Hello body.", p.InnerText);
    }

    [Fact]
    public void Heading_h2_uses_navy_and_h3_uses_accent()
    {
        var style = Style();
        var h2 = HeadingBlock.Render("Head two", 2, style);
        var h3 = HeadingBlock.Render("Head three", 3, style);
        var navy = OpenXmlHelpers.Hex(style.Colors["headingNavy"]);
        var accent = OpenXmlHelpers.Hex(style.Colors["accentBlue"]);
        Assert.Equal(navy, h2.Descendants<Color>().First().Val!.Value, ignoreCase: true);
        Assert.Equal(accent, h3.Descendants<Color>().First().Val!.Value, ignoreCase: true);
    }

    [Fact]
    public void Heading_h2_and_h3_have_distinct_sizes_and_keep_next()
    {
        var style = Style();
        var h2 = HeadingBlock.Render("Two", 2, style);
        var h3 = HeadingBlock.Render("Three", 3, style);
        var h2Sz = h2.Descendants<DocumentFormat.OpenXml.Wordprocessing.FontSize>().First().Val!.Value;
        var h3Sz = h3.Descendants<DocumentFormat.OpenXml.Wordprocessing.FontSize>().First().Val!.Value;
        Assert.Equal("26", h2Sz);   // 13pt * 2 half-points
        Assert.Equal("22", h3Sz);   // 11pt * 2 half-points
        Assert.NotNull(h2.ParagraphProperties!.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.KeepNext>());
    }

    [Fact]
    public void BulletList_uses_numbering_and_no_bullet_glyph_in_runs()
    {
        var paras = BulletListBlock.Render(new[] { "first", "second" }, Style()).ToList();
        Assert.Equal(2, paras.Count);
        Assert.Equal("first", paras[0].InnerText);
        // Each list paragraph references the bullet numbering definition.
        Assert.All(paras, p => Assert.NotNull(p.ParagraphProperties!.NumberingProperties));
        // No non-ASCII character (hence no literal bullet glyph) leaks into run text.
        Assert.All(paras, p => Assert.All(p.InnerText, ch => Assert.True(ch < (char)0x80)));
    }

    [Fact]
    public void Table_renders_headers_and_rows()
    {
        var t = TableBlock.Render(new[] { "Name", "Role" },
            new IReadOnlyList<string>[] { new[] { "J. Talewind", "Owner" } }, Style());
        var rows = t.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().ToList();
        Assert.Equal(2, rows.Count); // header + one body row
        Assert.Contains("Name", rows[0].InnerText);
        Assert.Contains("J. Talewind", rows[1].InnerText);
    }

    private static RenderContext Ctx()
    {
        var config = SemanticTestSupport.Config();
        return new RenderContext(config.Style, config.ClausesById,
            new Dictionary<string, string?> { ["[TOOL / CONNECTOR NAME]"] = "Microsoft Copilot Chat" },
            config.RadioPattern, config.WarnCallout, config.Clauses, config.CopyReadyMessage,
            config.CanonicalTokens);
    }

    [Fact]
    public void Dispatches_paragraph_and_reports_scan_text()
    {
        var block = new Registrar.Core.Model.AgrBlock { BlockType = "paragraph", Text = "Hi." };
        var r = BlockRenderer.Render(block, 0, 0, Ctx());
        Assert.Single(r.Elements);
        Assert.Equal("Hi.", r.ScanText);
        Assert.Null(r.ClauseCheck);
    }

    [Fact]
    public void Dispatches_clause_insertion_with_audit()
    {
        var block = new Registrar.Core.Model.AgrBlock { BlockType = "clauseInsertion", ClauseId = "CL-006" };
        var r = BlockRenderer.Render(block, 4, 1, Ctx());
        Assert.Null(r.ClauseCheck);
        Assert.NotNull(r.ClauseAudit);
        Assert.True(r.ClauseAudit!.Verified);
        Assert.Single(r.Elements);
    }

    [Fact]
    public void Rejects_out_of_scope_block_type()
    {
        // No schema block type is deferred any more; the renderer's default branch still
        // guards against an unknown/future block type.
        var block = new Registrar.Core.Model.AgrBlock { BlockType = "notARenderableBlock", Text = "note" };
        Assert.Throws<NotSupportedException>(() => BlockRenderer.Render(block, 0, 0, Ctx()));
    }

    [Fact]
    public void Renders_a_warnCallout_block_and_returns_its_text_as_scan_text()
    {
        var block = new Registrar.Core.Model.AgrBlock { BlockType = "warnCallout", Text = "Do not deploy without sign-off." };

        var result = BlockRenderer.Render(block, 0, 0, Ctx());

        Assert.Single(result.Elements);
        Assert.Equal("Do not deploy without sign-off.", result.ScanText);
        Assert.Null(result.ClauseCheck);
    }

    [Fact]
    public void Paragraph_has_body_spacing_and_widow_control()
    {
        var p = ParagraphBlock.Render("Body.", Style());
        var ppr = p.ParagraphProperties!;
        Assert.NotNull(ppr.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.WidowControl>());
        var spacing = ppr.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.SpacingBetweenLines>()!;
        Assert.Equal("120", spacing.After!.Value);   // 6pt * 20
        Assert.Equal("317", spacing.Line!.Value);     // 132% * 240 / 100 (integer)
    }

    [Fact]
    public void Bullet_keeps_numbering_and_gains_spacing()
    {
        var paras = BulletListBlock.Render(new[] { "one" }, Style()).ToList();
        var ppr = paras[0].ParagraphProperties!;
        Assert.NotNull(ppr.NumberingProperties);
        var spacing = ppr.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.SpacingBetweenLines>()!;
        Assert.Equal("60", spacing.After!.Value);      // 3pt * 20
    }

    [Fact]
    public void QA_ENG_06_supported_set_matches_renderer_capability()
    {
        var samples = new Dictionary<string, Registrar.Core.Model.AgrBlock>(StringComparer.Ordinal)
        {
            ["paragraph"] = new() { BlockType = "paragraph", Text = "x" },
            ["h2"] = new() { BlockType = "h2", Text = "x" },
            ["h3"] = new() { BlockType = "h3", Text = "x" },
            ["bulletList"] = new() { BlockType = "bulletList", Items = new[] { "x" } },
            ["table"] = new() { BlockType = "table", ColumnHeaders = new[] { "H" },
                Rows = new IReadOnlyList<string>[] { new[] { "c" } } },
            ["clauseInsertion"] = new() { BlockType = "clauseInsertion", ClauseId = "CL-006" },
            ["radioQuestion"] = new() { BlockType = "radioQuestion", Question = new Registrar.Core.Model.AgrQuestion
            {
                Number = 1,
                Prompt = "Was the walkthrough completed?",
                Required = true,
                Options = new[] { "Yes", "No", "Partial", "Not applicable" }
            } },
            ["warnCallout"] = new() { BlockType = "warnCallout", Text = "x" },
            ["aggregationTable"] = new() { BlockType = "aggregationTable", AggregationSource = "clauseLibrary" },
            ["copyReadyMessage"] = new() { BlockType = "copyReadyMessage", Text = "x" },
        };

        // The layer's declared set is exactly the set the test covers.
        Assert.Equal(
            BlockTypeSupportLayer.SupportedBlockTypes.OrderBy(x => x, StringComparer.Ordinal),
            samples.Keys.OrderBy(x => x, StringComparer.Ordinal));

        // Every declared-supported type renders without a NotSupportedException.
        foreach (var (type, block) in samples)
        {
            var ex = Record.Exception(() => BlockRenderer.Render(block, 4, 1, Ctx()));
            Assert.False(ex is NotSupportedException,
                $"'{type}' is declared supported but the renderer rejected it.");
        }

        // No schema block type is deferred. The renderer still rejects an unknown type.
        foreach (var deferred in new[] { "notARenderableBlock" })
        {
            Assert.DoesNotContain(deferred, BlockTypeSupportLayer.SupportedBlockTypes);
            Assert.Throws<NotSupportedException>(() =>
                BlockRenderer.Render(
                    new Registrar.Core.Model.AgrBlock { BlockType = deferred, Text = "x" }, 0, 0, Ctx()));
        }
    }

    [Fact]
    public void Table_has_horizontal_rules_only_and_cell_margins()
    {
        var t = TableBlock.Render(new[] { "H" },
            new IReadOnlyList<string>[] { new[] { "c" } }, Style());
        var props = t.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableProperties>()!;
        var borders = props.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableBorders>()!;

        // Horizontal insides present; verticals and outer box off.
        Assert.Equal(DocumentFormat.OpenXml.Wordprocessing.BorderValues.Single,
            borders.InsideHorizontalBorder!.Val!.Value);
        Assert.Equal(DocumentFormat.OpenXml.Wordprocessing.BorderValues.None,
            borders.InsideVerticalBorder!.Val!.Value);
        Assert.Equal(DocumentFormat.OpenXml.Wordprocessing.BorderValues.None,
            borders.TopBorder!.Val!.Value);

        var margin = props.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.TableCellMarginDefault>();
        Assert.NotNull(margin);
    }
}
