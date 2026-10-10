using Registrar.Rendering;
using Registrar.Tests;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class DchRendererTests
{
    private static (DocumentFormat.OpenXml.OpenXmlElement[] els, Registrar.Core.Config.StyleRules style) Render()
    {
        var c = SemanticTestSupport.Config();
        var agr = SemanticTestSupport.Agr("agr-sample-ds-draft.json");
        return (DchRenderer.Render(agr, c.Style).ToArray(), c.Style);
    }

    [Fact]
    public void Dch_table_is_borderless()
    {
        var (els, _) = Render();
        var table = els.OfType<Table>().First();
        var borders = table.GetFirstChild<TableProperties>()!.GetFirstChild<TableBorders>()!;
        Assert.Equal(BorderValues.None, borders.TopBorder!.Val!.Value);
        Assert.Equal(BorderValues.None, borders.InsideHorizontalBorder!.Val!.Value);
        Assert.Equal(BorderValues.None, borders.InsideVerticalBorder!.Val!.Value);
    }

    [Fact]
    public void Dch_field_labels_use_gray_caps_and_character_spacing()
    {
        var (els, style) = Render();
        var table = els.OfType<Table>().First();
        // First cell's first paragraph is the "Document ID" label.
        var labelRun = table.Descendants<Run>().First();
        var rpr = labelRun.RunProperties!;
        Assert.NotNull(rpr.GetFirstChild<Caps>());
        Assert.Equal(OpenXmlHelpers.Hex(style.DchLabel.Color),
            rpr.GetFirstChild<Color>()!.Val!.Value, ignoreCase: true);
        Assert.NotNull(rpr.GetFirstChild<Spacing>());   // character spacing
    }

    [Fact]
    public void Dch_banner_caption_is_retained()
    {
        var (els, _) = Render();
        Assert.Contains(els, e => e.InnerText.Contains("DOCUMENT CONTROL HEADER"));
    }
}
