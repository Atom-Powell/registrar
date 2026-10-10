using Registrar.Rendering;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Validation;

namespace Registrar.Tests.Rendering;

public class ParagraphFormattingTests
{
    [Fact]
    public void Build_emits_children_in_schema_order_and_validates()
    {
        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            KeepNext = true,
            WidowControl = true,
            Numbering = new NumberingProperties(
                new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }),
            LeftBar = (32, "#005CB9", 8),
            SpaceBeforeTwips = 320,
            SpaceAfterTwips = 120,
            LineSpacing240 = 317
        });

        var names = ppr.ChildElements.Select(c => c.LocalName).ToList();
        Assert.Equal(new[] { "keepNext", "widowControl", "numPr", "pBdr", "spacing" }, names);

        // The paragraph must be schema-valid inside a real document.
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(ppr.CloneNode(true), new Run(new Text("x")))));
        }
        ms.Position = 0;
        using var check = WordprocessingDocument.Open(ms, false);
        Assert.Empty(new OpenXmlValidator().Validate(check));
    }

    [Fact]
    public void StyledRun_caps_and_spacing_order_is_rFonts_b_caps_color_spacing_sz()
    {
        var run = OpenXmlHelpers.StyledRun("Doc ID", "Montserrat", 8, "#5A6470",
            bold: false, caps: true, characterSpacingTwips: 6);
        var names = run.RunProperties!.ChildElements.Select(c => c.LocalName).ToList();
        Assert.Equal(new[] { "rFonts", "caps", "color", "spacing", "sz" }, names);
    }
}
