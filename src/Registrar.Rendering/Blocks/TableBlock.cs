using Registrar.Core.Config;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;

namespace Registrar.Rendering;

public static class TableBlock
{
    public static Table Render(IReadOnlyList<string>? headers,
        IReadOnlyList<IReadOnlyList<string>> rows, StyleRules style)
    {
        var table = new Table(TableProps(style));

        var colCount = headers?.Count ?? (rows.Count > 0 ? rows[0].Count : 1);
        table.Append(OpenXmlHelpers.Grid(Enumerable.Repeat(1000, colCount).ToArray()));

        if (headers is not null)
            table.Append(Row(headers, style, isHeader: true, alt: false, style.Colors["headingNavy"]));

        for (var i = 0; i < rows.Count; i++)
            table.Append(Row(rows[i], style, isHeader: false, alt: i % 2 == 1,
                style.Colors["tableRowAltFill"]));

        return table;
    }

    // CT_TblPr order: tblW, tblBorders, tblCellMar. CT_TblBorders is a strict schema-ordered
    // sequence of the six named border elements (no generic Border type spans all of them in
    // the SDK). Horizontal rules only: outer box and vertical interior lines are off by config.
    private static TableProperties TableProps(StyleRules style)
    {
        var b = style.TableBorders;
        var color = OpenXmlHelpers.Hex(b.Color);
        var size = OpenXmlHelpers.BorderSizeEighths(b.WidthHalfPoints);
        BorderValues V(bool on) => on ? BorderValues.Single : BorderValues.None;
        var m = style.TableCellMargin;

        return new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new OpenXmlTableBorders(
                new TopBorder { Val = V(b.Outer), Size = size, Color = color },
                new LeftBorder { Val = V(b.Outer), Size = size, Color = color },
                new BottomBorder { Val = V(b.Outer), Size = size, Color = color },
                new RightBorder { Val = V(b.Outer), Size = size, Color = color },
                new InsideHorizontalBorder { Val = V(b.InsideHorizontal), Size = size, Color = color },
                new InsideVerticalBorder { Val = V(b.InsideVertical), Size = size, Color = color }),
            new TableCellMarginDefault(
                new TopMargin { Width = m.TopTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = (short)m.LeftTwips, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = m.BottomTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = (short)m.RightTwips, Type = TableWidthValues.Dxa }));
    }

    private static TableRow Row(IReadOnlyList<string> cells, StyleRules style,
        bool isHeader, bool alt, string fillColor)
    {
        var row = new TableRow();
        foreach (var cell in cells)
        {
            var color = isHeader ? "#FFFFFF" : "#000000";
            var run = OpenXmlHelpers.StyledRun(cell, style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial",
                style.TypeScale.BodyPt, color, bold: isHeader);
            var props = new TableCellProperties();
            if (isHeader || alt)
                props.Append(new Shading { Val = ShadingPatternValues.Clear,
                    Fill = OpenXmlHelpers.Hex(fillColor) });
            row.Append(new TableCell(props, new Paragraph(run)));
        }
        return row;
    }
}
