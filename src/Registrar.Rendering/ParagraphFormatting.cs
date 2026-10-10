using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public sealed record ParaFormat
{
    public bool KeepNext { get; init; }
    public bool WidowControl { get; init; }
    public NumberingProperties? Numbering { get; init; }
    public (int WidthEighths, string ColorHex, uint GapPoints)? LeftBar { get; init; }
    public int? SpaceBeforeTwips { get; init; }
    public int? SpaceAfterTwips { get; init; }
    public int? LineSpacing240 { get; init; }
    public int? IndentLeftTwips { get; init; }
}

// Builds a CT_PPr with children in the schema-required order:
// keepNext, widowControl, numPr, pBdr, spacing, ind.
public static class ParagraphFormatting
{
    public static ParagraphProperties Build(ParaFormat f)
    {
        var ppr = new ParagraphProperties();
        if (f.KeepNext) ppr.Append(new KeepNext());
        if (f.WidowControl) ppr.Append(new WidowControl());
        if (f.Numbering is not null) ppr.Append(f.Numbering);
        if (f.LeftBar is { } bar)
            ppr.Append(new ParagraphBorders(
                new LeftBorder
                {
                    Val = BorderValues.Single,
                    Size = (uint)bar.WidthEighths,
                    Color = OpenXmlHelpers.Hex(bar.ColorHex),
                    Space = bar.GapPoints
                }));

        var spacing = new SpacingBetweenLines();
        var hasSpacing = false;
        if (f.SpaceBeforeTwips is int sb) { spacing.Before = sb.ToString(); hasSpacing = true; }
        if (f.SpaceAfterTwips is int sa) { spacing.After = sa.ToString(); hasSpacing = true; }
        if (f.LineSpacing240 is int ln)
        {
            spacing.Line = ln.ToString();
            spacing.LineRule = LineSpacingRuleValues.Auto;
            hasSpacing = true;
        }
        if (hasSpacing) ppr.Append(spacing);

        if (f.IndentLeftTwips is int ind)
            ppr.Append(new Indentation { Left = ind.ToString() });

        return ppr;
    }
}
