using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;

namespace Registrar.Rendering;

internal static class ConfigCalloutRenderer
{
    public static Table Render(AgrDocument agr, ArtifactTypeConfig type, StyleRules style)
    {
        var navy = style.Colors["headingNavy"];
        var blue = OpenXmlHelpers.Hex(style.Colors["primaryBlue"]);
        var borderSize = OpenXmlHelpers.BorderSizeEighths(style.Borders.WidthHalfPoints);
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";

        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = OpenXmlHelpers.Hex(style.Colors["headerRightCellFill"]) }),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun("Configuration", family, style.TypeScale.SubtitlePt, navy, true)),
            LabelValue("Artifact", type.Name ?? type.Code, family, navy, style.TypeScale.BodyPt),
            LabelValue("Description", type.Description ?? string.Empty, family, navy, style.TypeScale.BodyPt),
            LabelValue("Audience", agr.Context.Audience, family, navy, style.TypeScale.BodyPt),
            LabelValue("ID Pattern", type.IdPattern ?? string.Empty, family, navy, style.TypeScale.BodyPt));

        var contentWidth = OpenXmlHelpers.Twips(style.Page.WidthIn - (2 * style.Page.MarginsIn));

        return new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new OpenXmlTableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = borderSize, Color = blue },
                    new LeftBorder { Val = BorderValues.Single, Size = borderSize, Color = blue },
                    new BottomBorder { Val = BorderValues.Single, Size = borderSize, Color = blue },
                    new RightBorder { Val = BorderValues.Single, Size = borderSize, Color = blue },
                    new InsideHorizontalBorder { Val = BorderValues.None },
                    new InsideVerticalBorder { Val = BorderValues.None })),
            OpenXmlHelpers.Grid(contentWidth),
            new TableRow(cell));
    }

    private static Paragraph LabelValue(string label, string value, string family, string labelColor, int sizePt)
        => new(
            OpenXmlHelpers.StyledRun(label + ": ", family, sizePt, labelColor, true),
            OpenXmlHelpers.StyledRun(value, family, sizePt, "#000000", false));
}
