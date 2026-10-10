using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;

namespace Registrar.Rendering;

// A warn callout: a full-width box, warn-red border and light-red fill, with a bold
// config-driven label line above the Governor-supplied text. Structure mirrors
// ConfigCalloutRenderer. Colors are the existing visualIdentity keys warnRedBorder /
// warnRedFill; the label comes from config (WarnCalloutStyle.Label). Pure: no clock,
// no ambient state, so the rendered bytes stay reproducible.
public static class WarnCalloutBlock
{
    public static Table Render(string text, WarnCalloutStyle warn, StyleRules style)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";
        var borderColor = OpenXmlHelpers.Hex(style.Colors["warnRedBorder"]);
        var borderSize = OpenXmlHelpers.BorderSizeEighths(style.Borders.WidthHalfPoints);
        var labelColor = style.Colors["warnRedBorder"];

        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = OpenXmlHelpers.Hex(style.Colors["warnRedFill"]) }),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun(warn.Label, family, style.TypeScale.SubtitlePt, labelColor, true)),
            new Paragraph(OpenXmlHelpers.StyledRun(text, family, style.TypeScale.BodyPt, "#000000", false)));

        var contentWidth = OpenXmlHelpers.Twips(style.Page.WidthIn - (2 * style.Page.MarginsIn));

        return new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new OpenXmlTableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = borderSize, Color = borderColor },
                    new LeftBorder { Val = BorderValues.Single, Size = borderSize, Color = borderColor },
                    new BottomBorder { Val = BorderValues.Single, Size = borderSize, Color = borderColor },
                    new RightBorder { Val = BorderValues.Single, Size = borderSize, Color = borderColor },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None })),
            OpenXmlHelpers.Grid(contentWidth),
            new TableRow(cell));
    }
}
