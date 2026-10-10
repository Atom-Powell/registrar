using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using OpenXmlTableBorders = DocumentFormat.OpenXml.Wordprocessing.TableBorders;

namespace Registrar.Rendering;

internal static class HeaderTableRenderer
{
    private const string PictureUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";

    public static Table Render(MainDocumentPart main, AgrDocument agr, ArtifactTypeConfig type, StyleRules style, byte[] logoBytes)
    {
        var logoCell = new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1500" }),
            LogoParagraph(main, style, logoBytes));

        var titleCell = new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "3500" }));
        foreach (var line in TitleStack(agr, type, style)) titleCell.Append(line);

        var contentWidth = OpenXmlHelpers.Twips(style.Page.WidthIn - (2 * style.Page.MarginsIn));
        var logoWidth = (int)Math.Round(contentWidth * 0.30);
        var titleWidth = contentWidth - logoWidth;

        return new Table(Borderless(), OpenXmlHelpers.Grid(logoWidth, titleWidth), new TableRow(logoCell, titleCell));
    }

    private static TableProperties Borderless() => new(
        new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
        new OpenXmlTableBorders(
            new TopBorder { Val = BorderValues.None },
            new LeftBorder { Val = BorderValues.None },
            new BottomBorder { Val = BorderValues.None },
            new RightBorder { Val = BorderValues.None },
            new InsideHorizontalBorder { Val = BorderValues.None },
            new InsideVerticalBorder { Val = BorderValues.None }));

    private static Paragraph LogoParagraph(MainDocumentPart main, StyleRules style, byte[] logoBytes)
    {
        var imagePart = main.AddImagePart(ImagePartType.Png, RelationshipIds.Logo);
        using (var s = new MemoryStream(logoBytes)) imagePart.FeedData(s);
        var relId = main.GetIdOfPart(imagePart);

        var (pw, ph) = PngSize(logoBytes);
        long cy = OpenXmlHelpers.Emu(style.Wordmark.HeightIn);
        long cx = cy * pw / ph;

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = 1U, Name = "ORG Logo" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = "org-logo.png" },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(
                            new A.Blip { Embed = relId },
                            new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(
                                new A.Offset { X = 0L, Y = 0L },
                                new A.Extents { Cx = cx, Cy = cy }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = PictureUri }))
            { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });

        return new Paragraph(new Run(drawing));
    }

    private static Paragraph[] TitleStack(AgrDocument agr, ArtifactTypeConfig type, StyleRules style)
    {
        var navy = style.Colors["headingNavy"];
        var gray = style.Colors["secondaryGray"];
        var family = style.Fonts.Heading.Family ?? style.Fonts.Heading.Fallback ?? "Arial";
        var clientOrEngagement = agr.Context.Client ?? agr.Context.Engagement ?? string.Empty;

        return new[]
        {
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun(type.Name ?? type.Code, family, style.TypeScale.TitlePt, navy, true)),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun(clientOrEngagement, family, style.TypeScale.SubtitlePt, gray, false)),
            OpenXmlHelpers.Line(OpenXmlHelpers.StyledRun("ORG AI Governance Program", family, style.TypeScale.CaptionPt, gray, false))
        };
    }

    // PNG width/height live big-endian in the IHDR chunk at byte offsets 16 and 20.
    private static (int Width, int Height) PngSize(byte[] png)
    {
        if (png.Length < 24 || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47)
            throw new InvalidOperationException("Logo asset is not a valid PNG.");

        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }
}
