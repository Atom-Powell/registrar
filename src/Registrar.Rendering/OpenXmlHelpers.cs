using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

public static class OpenXmlHelpers
{
    private const int TwipsPerInch = 1440;
    private const long EmuPerInch = 914400;

    public static int Twips(double inches) => (int)Math.Round(inches * TwipsPerInch);
    public static long Emu(double inches) => (long)Math.Round(inches * EmuPerInch);

    public static string Hex(string color) => color.TrimStart('#');

    // Table/cell border w:sz is in eighths of a point; the config expresses border
    // width in half-points, so convert (1 half-point = 4 eighths).
    public static uint BorderSizeEighths(int halfPoints) => (uint)(halfPoints * 4);

    // FontSize.Val is in half-points, so pass point size and it doubles.
    // CT_RPr is a strict schema-ordered sequence: rFonts, b, ..., caps, ..., color, spacing, ..., sz.
    public static Run StyledRun(string text, string family, int sizePt, string colorHex, bool bold,
        bool caps = false, int characterSpacingTwips = 0, bool italic = false)
    {
        var rpr = new RunProperties(new RunFonts { Ascii = family, HighAnsi = family });
        if (bold) rpr.Append(new Bold());
        if (italic) rpr.Append(new Italic());
        if (caps) rpr.Append(new Caps());
        rpr.Append(new Color { Val = Hex(colorHex) });
        if (characterSpacingTwips != 0) rpr.Append(new Spacing { Val = characterSpacingTwips });
        rpr.Append(new FontSize { Val = (sizePt * 2).ToString() });
        return new Run(rpr, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    public static Paragraph Line(params Run[] runs) => new(runs);

    // A zero-content paragraph of an exact height, used to open vertical space
    // above a boxed element so its own top border reads as a distinct edge.
    // w:spacing/@w:line is in twentieths of a point; Exact pins the line height.
    public static Paragraph Spacer(int points)
        => new(new ParagraphProperties(
            new SpacingBetweenLines
            {
                Before = "0",
                After = "0",
                Line = (points * 20).ToString(),
                LineRule = LineSpacingRuleValues.Exact
            }));

    // CT_TblGrid requires at least one gridCol; widths are in twips. Word recomputes
    // actual column widths from tcW/tblW when cells use percentage widths, but the
    // element must still be present and non-empty to validate.
    public static TableGrid Grid(params int[] widthsTwips)
    {
        var grid = new TableGrid();
        foreach (var w in widthsTwips) grid.Append(new GridColumn { Width = w.ToString() });
        return grid;
    }
}
