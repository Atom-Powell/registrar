using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class BulletListBlock
{
    public static IEnumerable<Paragraph> Render(IReadOnlyList<string> items, StyleRules style)
    {
        foreach (var item in items)
        {
            var ppr = ParagraphFormatting.Build(new ParaFormat
            {
                WidowControl = style.Pagination.WidowControl,
                Numbering = new NumberingProperties(
                    new NumberingLevelReference { Val = 0 },
                    new NumberingId { Val = NumberingPart.BulletNumId }),
                SpaceAfterTwips = style.Spacing.BulletAfterPt * 20,
                LineSpacing240 = (int)System.Math.Round(style.Spacing.LineSpacingPct * 240.0 / 100)
            });
            yield return new Paragraph(ppr, OpenXmlHelpers.StyledRun(
                item, style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial",
                style.TypeScale.BodyPt, "#000000", bold: false));
        }
    }
}
