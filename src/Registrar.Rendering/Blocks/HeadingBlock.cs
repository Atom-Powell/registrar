using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class HeadingBlock
{
    public static Paragraph Render(string text, int level, StyleRules style)
    {
        var isH3 = level >= 3;
        var color = isH3 ? style.Colors["accentBlue"] : style.Colors["headingNavy"];
        var sizePt = isH3 ? style.HeadingScale.H3Pt : style.HeadingScale.H2Pt;
        var run = OpenXmlHelpers.StyledRun(
            text, style.Fonts.Heading.Family ?? style.Fonts.Heading.Fallback ?? "Arial",
            sizePt, color, bold: true);
        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            KeepNext = style.Pagination.KeepHeadingWithNext,
            SpaceBeforeTwips = (isH3 ? style.Spacing.H3BeforePt : style.Spacing.H2BeforePt) * 20,
            SpaceAfterTwips = (isH3 ? style.Spacing.H3AfterPt : style.Spacing.H2AfterPt) * 20
        });
        return new Paragraph(ppr, run);
    }
}
