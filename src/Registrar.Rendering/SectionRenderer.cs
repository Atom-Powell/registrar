using Registrar.Core.Model;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public sealed record ScanUnit(string Text, string Locator);

public sealed record SectionRenderResult(
    IReadOnlyList<OpenXmlElement> Elements,
    IReadOnlyList<Check> ClauseChecks,
    IReadOnlyList<ClauseInsertionAudit> Audits,
    IReadOnlyList<ScanUnit> ScanUnits);

public static class SectionRenderer
{
    public static SectionRenderResult Render(AgrSection section, int sectionIndex, RenderContext ctx)
    {
        var elements = new List<OpenXmlElement>();
        var checks = new List<Check>();
        var audits = new List<ClauseInsertionAudit>();
        var scanUnits = new List<ScanUnit>();
        var sectionLocator = $"section '{section.Title}'";
        var style = ctx.Style;

        var headingText = $"{sectionIndex + 1}. {section.Title}";
        var headingFamily = style.Fonts.Heading.Family ?? style.Fonts.Heading.Fallback ?? "Arial";
        var numberRun = OpenXmlHelpers.StyledRun(
            $"{sectionIndex + 1}. ", headingFamily, style.HeadingScale.H1Pt,
            style.SectionNumber.Color, bold: true);
        var titleRun = OpenXmlHelpers.StyledRun(
            section.Title, headingFamily, style.HeadingScale.H1Pt,
            style.Colors["headingNavy"], bold: true);
        var headingPpr = ParagraphFormatting.Build(new ParaFormat
        {
            KeepNext = style.Pagination.KeepHeadingWithNext,
            LeftBar = (
                (int)OpenXmlHelpers.BorderSizeEighths(style.SectionAccent.WidthHalfPoints),
                style.SectionAccent.Color,
                style.SectionAccent.GapPoints),
            SpaceBeforeTwips = style.Spacing.SectionTitleBeforePt * 20,
            SpaceAfterTwips = style.Spacing.SectionTitleAfterPt * 20
        });
        elements.Add(new Paragraph(headingPpr, numberRun, titleRun));
        scanUnits.Add(new ScanUnit(headingText, sectionLocator));

        if (section.DescriptorLine is not null)
        {
            var rpr = new RunProperties(new RunFonts
            {
                Ascii = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial"
            });
            rpr.Append(new Italic());
            rpr.Append(new Color { Val = OpenXmlHelpers.Hex(style.Colors["secondaryGray"]) });
            rpr.Append(new FontSize { Val = (style.TypeScale.CaptionPt * 2).ToString() });
            elements.Add(new Paragraph(new Run(rpr,
                new Text(section.DescriptorLine) { Space = SpaceProcessingModeValues.Preserve })));
            scanUnits.Add(new ScanUnit(section.DescriptorLine, sectionLocator));
        }

        for (var b = 0; b < section.Blocks.Count; b++)
        {
            var block = section.Blocks[b];
            var result = BlockRenderer.Render(block, sectionIndex, b, ctx);
            elements.AddRange(result.Elements);
            if (result.ClauseCheck is not null) checks.Add(result.ClauseCheck);
            if (result.ClauseAudit is not null) audits.Add(result.ClauseAudit);
            var locator = block.BlockType == "clauseInsertion" && block.ClauseId is not null
                ? $"clause {block.ClauseId}"
                : sectionLocator;
            scanUnits.Add(new ScanUnit(result.ScanText, locator));
        }

        return new SectionRenderResult(elements, checks, audits, scanUnits);
    }
}
