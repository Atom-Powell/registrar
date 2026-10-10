using Registrar.Core.Model;
using Registrar.Rendering;
using Registrar.Tests;

namespace Registrar.Tests.Rendering;

public class SectionRendererTests
{
    private static RenderContext Ctx()
    {
        var c = SemanticTestSupport.Config();
        return new RenderContext(c.Style, c.ClausesById, new Dictionary<string, string?>(), c.RadioPattern, c.WarnCallout, c.Clauses, c.CopyReadyMessage, c.CanonicalTokens);
    }

    [Fact]
    public void Renders_numbered_heading_descriptor_and_blocks()
    {
        var section = new AgrSection { Title = "Purpose", DescriptorLine = "Why this exists.",
            Blocks = new[] { new AgrBlock { BlockType = "paragraph", Text = "Body." } } };
        var r = SectionRenderer.Render(section, 0, Ctx());
        var text = string.Concat(r.Elements.Select(e => e.InnerText));
        Assert.Contains("1. Purpose", text);
        Assert.Contains("Why this exists.", text);
        Assert.Contains("Body.", text);
        Assert.Contains(r.ScanUnits, u => u.Locator.Contains("Purpose"));
    }

    [Fact]
    public void Section_title_has_blue_number_navy_title_and_left_accent_bar()
    {
        var section = new AgrSection { Title = "Purpose", DescriptorLine = null,
            Blocks = new[] { new AgrBlock { BlockType = "paragraph", Text = "Body." } } };
        var r = SectionRenderer.Render(section, 0, Ctx());
        var titlePara = (DocumentFormat.OpenXml.Wordprocessing.Paragraph)r.Elements[0];

        // InnerText is unchanged so the QA-SF-08 scan unit and prior tests hold.
        Assert.Equal("1. Purpose", titlePara.InnerText);

        // Two runs: number in brand blue, title in navy.
        var colors = titlePara.Descendants<DocumentFormat.OpenXml.Wordprocessing.Color>()
            .Select(c => c.Val!.Value).ToList();
        Assert.Equal("005CB9", colors[0], ignoreCase: true);
        Assert.Equal("002060", colors[1], ignoreCase: true);

        // Left accent bar, no bottom/horizontal rule.
        var borders = titlePara.ParagraphProperties!
            .GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.ParagraphBorders>()!;
        Assert.NotNull(borders.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.LeftBorder>());
        Assert.Null(borders.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.BottomBorder>());
        Assert.NotNull(titlePara.ParagraphProperties!
            .GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.KeepNext>());
    }

    [Fact]
    public void Collects_clause_audit_and_locator()
    {
        var section = new AgrSection { Title = "Audit", DescriptorLine = null,
            Blocks = new[] { new AgrBlock { BlockType = "clauseInsertion", ClauseId = "CL-006" } } };
        var r = SectionRenderer.Render(section, 4, Ctx());
        Assert.Single(r.Audits);
        Assert.Empty(r.ClauseChecks);
        Assert.Contains(r.ScanUnits, u => u.Locator.Contains("CL-006"));
    }
}
