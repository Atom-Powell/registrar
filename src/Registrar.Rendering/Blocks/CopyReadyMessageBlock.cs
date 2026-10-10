using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;

namespace Registrar.Rendering;

// A copy-ready message: a full-width box with a neutral border and light fill, a bold
// config-driven label line above the operator-facing message text. Structure mirrors
// WarnCalloutBlock; colors are existing visualIdentity keys borderNeutral / dchFill and
// the headingNavy label. Pure: no clock, no ambient state.
public static class CopyReadyMessageBlock
{
    public static Table Render(string text, CopyReadyMessageStyle copyReady, StyleRules style)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";
        var borderColor = OpenXmlHelpers.Hex(style.Colors["borderNeutral"]);
        var borderSize = OpenXmlHelpers.BorderSizeEighths(style.Borders.WidthHalfPoints);
        var labelColor = style.Colors["headingNavy"];

        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = OpenXmlHelpers.Hex(style.Colors["dchFill"]) }),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun(copyReady.Label, family, style.TypeScale.SubtitlePt, labelColor, true)),
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
