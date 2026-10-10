using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

internal static class FooterRenderer
{
    public static string Create(MainDocumentPart main, AgrDocument agr, ArtifactTypeConfig type, StyleRules style)
    {
        var footerPart = main.AddNewPart<FooterPart>(RelationshipIds.Footer);
        footerPart.Footer = new Footer(FooterParagraph(agr, type, style));
        return main.GetIdOfPart(footerPart);
    }

    private static Paragraph FooterParagraph(AgrDocument agr, ArtifactTypeConfig type, StyleRules style)
    {
        var gray = style.Colors["secondaryGray"];
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";
        var sizePt = style.TypeScale.LabelPt;

        var resolved = style.FooterTemplate
            .Replace("[PACKAGE]", agr.RenderingOptions.PackageName)
            .Replace("[ARTIFACT]", type.Name ?? type.Code)
            .Replace("[VERSION]", agr.Identity.Version.TrimStart('v'));

        // The template carries the literal "Page X of Y"; replace it with live fields.
        var parts = resolved.Split("Page X of Y");
        var before = parts[0] + "Page ";
        var after = parts.Length > 1 ? " " + parts[1].TrimStart() : string.Empty;

        return new Paragraph(
            OpenXmlHelpers.StyledRun(before, family, sizePt, gray, false),
            Field("PAGE", family, gray, sizePt),
            OpenXmlHelpers.StyledRun(" of ", family, sizePt, gray, false),
            Field("NUMPAGES", family, gray, sizePt),
            OpenXmlHelpers.StyledRun(after, family, sizePt, gray, false));
    }

    private static SimpleField Field(string instruction, string family, string colorHex, int sizePt)
        => new(OpenXmlHelpers.StyledRun("1", family, sizePt, colorHex, false)) { Instruction = instruction };
}
