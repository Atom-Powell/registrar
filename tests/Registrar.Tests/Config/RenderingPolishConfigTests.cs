using Registrar.Tests;

namespace Registrar.Tests.Config;

public class RenderingPolishConfigTests
{
    private static Registrar.Core.Config.StyleRules Style() => SemanticTestSupport.Config().Style;

    [Fact]
    public void Heading_scale_gives_h2_and_h3_distinct_sizes()
    {
        var s = Style();
        Assert.Equal(16, s.HeadingScale.H1Pt);
        Assert.Equal(13, s.HeadingScale.H2Pt);
        Assert.Equal(11, s.HeadingScale.H3Pt);
    }

    [Fact]
    public void Spacing_puts_more_space_before_headings_than_after()
    {
        var s = Style();
        Assert.True(s.Spacing.SectionTitleBeforePt > s.Spacing.SectionTitleAfterPt);
        Assert.True(s.Spacing.H2BeforePt > s.Spacing.H2AfterPt);
        Assert.True(s.Spacing.H3BeforePt > s.Spacing.H3AfterPt);
        Assert.Equal(6, s.Spacing.BodyAfterPt);
        Assert.Equal(132, s.Spacing.LineSpacingPct);
    }

    [Fact]
    public void Accent_and_number_use_brand_blue_and_bars_are_horizontal_only()
    {
        var s = Style();
        Assert.Equal("#005CB9", s.SectionNumber.Color);
        Assert.Equal("#005CB9", s.SectionAccent.Color);
        Assert.False(s.TableBorders.Outer);
        Assert.True(s.TableBorders.InsideHorizontal);
        Assert.False(s.TableBorders.InsideVertical);
    }

    [Fact]
    public void Pagination_dch_label_and_logo_size_are_configured()
    {
        var s = Style();
        Assert.True(s.Pagination.KeepHeadingWithNext);
        Assert.True(s.Pagination.WidowControl);
        Assert.Equal("#5A6470", s.DchLabel.Color);
        Assert.True(s.DchLabel.Uppercase);
        Assert.Equal(0.55, s.Wordmark.HeightIn, 3);
    }
}
