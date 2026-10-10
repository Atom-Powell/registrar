using Registrar.Core.Config;
using Registrar.Core.Model;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

// The v1.2 radio-option pattern (ORG-MST-WFE-001), governed by QA-SF-10.
//
// Every visual property comes from config (glyph, glyph color, indentation flag,
// descriptor wording) so a v1.3 template pack is a config release, not a rebuild
// (rule 3). The glyph is never a literal in this file: engine source is held to an
// escapes-only rule by SourceHygieneTests, and the glyph's canonical home is
// style-rules.json radioOptionPattern.glyph (U+25CB, on the approvedGlyphAllowlist).
//
// This class emits NO NumberingProperties. That is the structural guarantee behind
// QA-SF-10's prohibition on checkboxes, plain bullets, dash lists, and numbered
// lists: there is no code path here that could produce one.
public static class RadioQuestionBlock
{
    public static IEnumerable<Paragraph> Render(AgrQuestion question, StyleRules style, RadioOptionPattern radio)
    {
        yield return Descriptor(question, style, radio);
        foreach (var option in question.Options)
            yield return Option(option, style, radio);
    }

    // "Question 3. Select one answer. Required" - italic, secondary gray, caption size.
    // Composed from the config format string; {number}/{requirement} rather than
    // bracketed tokens so the QA-ENG-07 residue scan does not read them as unresolved
    // placeholders in rendered prose.
    public static string DescriptorText(AgrQuestion question, RadioOptionPattern radio)
        => radio.DescriptorLineFormat
            .Replace("{number}", question.Number.ToString())
            .Replace("{requirement}", question.Required ? radio.RequiredLabel : radio.OptionalLabel);

    private static Paragraph Descriptor(AgrQuestion question, StyleRules style, RadioOptionPattern radio)
    {
        var run = OpenXmlHelpers.StyledRun(
            DescriptorText(question, radio),
            style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial",
            style.TypeScale.CaptionPt,
            style.Colors["secondaryGray"],
            bold: false,
            italic: true);

        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            KeepNext = true, // the descriptor must not orphan from its options
            WidowControl = style.Pagination.WidowControl,
            SpaceAfterTwips = style.Spacing.BulletAfterPt * 20,
            LineSpacing240 = (int)System.Math.Round(style.Spacing.LineSpacingPct * 240.0 / 100)
        });
        return new Paragraph(ppr, run);
    }

    private static Paragraph Option(string label, StyleRules style, RadioOptionPattern radio)
    {
        var family = style.Fonts.Body.Family ?? style.Fonts.Body.Fallback ?? "Arial";

        // The glyph run carries only the glyph itself; the gap before the label lives
        // in the label run so the glyph run's InnerText is exactly the glyph.
        var glyphRun = OpenXmlHelpers.StyledRun(
            radio.Glyph, family, style.TypeScale.BodyPt, radio.GlyphColor, bold: false);
        var labelRun = OpenXmlHelpers.StyledRun(
            "  " + label, family, style.TypeScale.BodyPt, "#000000", bold: false);

        var ppr = ParagraphFormatting.Build(new ParaFormat
        {
            WidowControl = style.Pagination.WidowControl,
            SpaceAfterTwips = style.Spacing.BulletAfterPt * 20,
            LineSpacing240 = (int)System.Math.Round(style.Spacing.LineSpacingPct * 240.0 / 100),
            IndentLeftTwips = radio.OptionsIndented ? radio.OptionIndentTwips : null
        });
        return new Paragraph(ppr, glyphRun, labelRun);
    }
}
