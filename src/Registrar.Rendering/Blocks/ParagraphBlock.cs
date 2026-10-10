using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class ParagraphBlock
{
    public static Paragraph Render(string text, StyleRules style)
    {
        var run = OpenXmlHelpers.StyledRun(text,
            style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial",
            style.TypeScale.BodyPt, "#000000", bold: false);
        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            WidowControl = style.Pagination.WidowControl,
            SpaceAfterTwips = style.Spacing.BodyAfterPt * 20,
            LineSpacing240 = (int)System.Math.Round(style.Spacing.LineSpacingPct * 240.0 / 100)
        });
        return new Paragraph(ppr, run);
    }
}
