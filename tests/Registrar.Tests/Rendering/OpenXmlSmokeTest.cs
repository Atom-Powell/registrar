using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class OpenXmlSmokeTest
{
    [Fact]
    public void Can_create_and_reopen_a_wordprocessing_document()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("smoke")))));
        }

        stream.Position = 0;
        using var reopened = WordprocessingDocument.Open(stream, false);
        var text = reopened.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Equal("smoke", text);
    }
}
