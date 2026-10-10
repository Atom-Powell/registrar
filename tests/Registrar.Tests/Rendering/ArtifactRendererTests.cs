using System.Linq;
using Registrar.Core;
using Registrar.Rendering;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests.Rendering;

public class ArtifactRendererTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    internal static byte[] LogoBytes()
        => File.ReadAllBytes(ConfigLocator.FindAsset(AppContext.BaseDirectory, "org-logo.png"));

    internal static Registrar.Rendering.RenderResult RenderResultGolden()
        => new ArtifactRenderer().Render(
            SemanticTestSupport.Agr("agr-sample-ds-draft.json"),
            SemanticTestSupport.Config(),
            LogoBytes());

    internal static byte[] RenderGolden() => RenderResultGolden().Bytes;

    [Fact]
    public void Render_produces_valid_docx_with_letter_page_size()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);

        var pageSize = doc.MainDocumentPart!.Document!.Body!
            .GetFirstChild<SectionProperties>()!.GetFirstChild<PageSize>()!;
        Assert.Equal(12240U, (uint)pageSize.Width!);   // 8.5in * 1440
        Assert.Equal(15840U, (uint)pageSize.Height!);  // 11in * 1440
        Assert.Equal("ORG-GOV-DS-016", doc.PackageProperties.Title);
    }

    [Fact]
    public void Render_embeds_logo_and_title_stack()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);

        Assert.NotEmpty(doc.MainDocumentPart!.ImageParts);           // logo embedded
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Deployment Summary", text);                 // artifact name
        Assert.Contains("Talewind CPA", text);                      // client
    }

    [Fact]
    public void Render_includes_dch_with_metadata()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;

        Assert.Contains("DOCUMENT CONTROL HEADER", text);
        Assert.Contains("ORG-GOV-DS-016", text);
        Assert.Contains("v1.0", text);
        Assert.Contains("Client-Facing", text);
        Assert.Contains("Draft", text);
        Assert.Contains("Registry Owner", text);
        Assert.Contains("To be set on Approval", text);   // golden reviewDate is null
    }

    [Fact]
    public void Render_includes_callout_and_footer()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);
        var bodyText = doc.MainDocumentPart!.Document!.Body!.InnerText;

        Assert.Contains("Configuration", bodyText);
        Assert.Contains("ORG-GOV-DS-NNN", bodyText);           // artifact id pattern in the callout

        Assert.NotEmpty(doc.MainDocumentPart.FooterParts);
        var footerText = doc.MainDocumentPart!.FooterParts.First().Footer!.InnerText;
        Assert.Contains("Governed AI Rollout", footerText);    // package name
        Assert.Contains("ORG Consulting, LLC", footerText);
        Assert.Contains("Page", footerText);
    }

    [Fact]
    public void Render_produces_schema_valid_docx()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(ms, false);
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Render_includes_numbered_body_sections_and_clause_text()
    {
        var result = RenderResultGolden();
        using var ms = new MemoryStream(result.Bytes);
        using var doc = WordprocessingDocument.Open(ms, false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;

        Assert.Contains("1. Purpose", text);
        Assert.Contains("8. Version and Change Notes", text);
        Assert.Contains("audit", text, StringComparison.OrdinalIgnoreCase); // resolved CL-006 text
        Assert.Empty(result.PostRenderChecks);                              // clean render
        Assert.Contains(result.ClauseAudits, a => a.ClauseId == "CL-006" && a.Verified);
    }

    [Fact]
    public void Render_uses_the_configured_larger_logo_height()
    {
        using var ms = new MemoryStream(RenderGolden());
        using var doc = WordprocessingDocument.Open(ms, false);
        var extent = doc.MainDocumentPart!.Document!.Body!
            .Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().First();
        // 0.55in * 914400 EMU/in = 502920.
        Assert.Equal(502920L, extent.Cy!.Value);
    }
}
