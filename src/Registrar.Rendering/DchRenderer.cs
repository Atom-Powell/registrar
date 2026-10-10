using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;

namespace Registrar.Rendering;

internal static class DchRenderer
{
    public static IEnumerable<OpenXmlElement> Render(AgrDocument agr, StyleRules style)
    {
        yield return Banner(style);
        yield return Table(agr, style);
    }

    private static Paragraph Banner(StyleRules style)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";
        return OpenXmlHelpers.Line(
            OpenXmlHelpers.StyledRun("DOCUMENT CONTROL HEADER", family, style.TypeScale.CaptionPt, style.Colors["secondaryGray"], true));
    }

    private static Table Table(AgrDocument agr, StyleRules style)
    {
        var id = agr.Identity;
        var ctx = agr.Context;
        var reviewDate = string.IsNullOrEmpty(ctx.ReviewDate) ? "To be set on Approval" : ctx.ReviewDate!;

        var row1 = new TableRow(
            Cell("Document ID", id.DocumentId, style, monospace: true),
            Cell("Version", id.Version, style),
            Cell("Classification", id.Classification, style),
            Cell("Approval Status", id.ApprovalStatus, style));
        var row2 = new TableRow(
            Cell("Owner", ctx.Owner, style),
            Cell("Review Date", reviewDate, style),
            Cell("Audience", ctx.Audience, style),
            Cell("Prepared By", ctx.PreparedBy, style));

        var contentWidth = OpenXmlHelpers.Twips(style.Page.WidthIn - (2 * style.Page.MarginsIn));
        var colWidth = contentWidth / 4;
        var lastColWidth = contentWidth - (colWidth * 3);

        return new Table(TableProps(style), OpenXmlHelpers.Grid(colWidth, colWidth, colWidth, lastColWidth), row1, row2);
    }

    private static TableProperties TableProps(StyleRules style) => new(
        new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
        new OpenXmlTableBorders(
            new TopBorder { Val = BorderValues.None },
            new LeftBorder { Val = BorderValues.None },
            new BottomBorder { Val = BorderValues.None },
            new RightBorder { Val = BorderValues.None },
            new InsideHorizontalBorder { Val = BorderValues.None },
            new InsideVerticalBorder { Val = BorderValues.None }));

    private static TableCell Cell(string label, string value, StyleRules style, bool monospace = false)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";
        var labelPara = OpenXmlHelpers.Line(
            OpenXmlHelpers.StyledRun(label, family, style.TypeScale.LabelPt, style.DchLabel.Color,
                bold: false, caps: style.DchLabel.Uppercase,
                characterSpacingTwips: style.DchLabel.CharacterSpacingTwips));

        var valueRun = monospace
            ? OpenXmlHelpers.StyledRun(value, style.Fonts.DocumentId.Family ?? "Consolas",
                  style.Fonts.DocumentId.SizePt ?? 10, style.Fonts.DocumentId.Color ?? "#000000",
                  style.Fonts.DocumentId.Bold ?? true)
            : OpenXmlHelpers.StyledRun(value, family, style.TypeScale.BodyPt, "#000000", false);

        return new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1250" },
                new Shading { Val = ShadingPatternValues.Clear, Fill = OpenXmlHelpers.Hex(style.Colors["dchFill"]) }),
            labelPara,
            OpenXmlHelpers.Line(valueRun));
    }
}
