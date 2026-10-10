namespace Registrar.Core.Config;

public sealed record StyleRules
{
    public required FontSet Fonts { get; init; }
    public required IReadOnlyDictionary<string, string> Colors { get; init; }
    public required BorderSpec Borders { get; init; }
    public required PageSpec Page { get; init; }
    public required string FooterTemplate { get; init; }
    public required Wordmark Wordmark { get; init; }
    public required TypeScale TypeScale { get; init; }
    public required Spacing Spacing { get; init; }
    public required HeadingScale HeadingScale { get; init; }
    public required SectionNumber SectionNumber { get; init; }
    public required SectionAccent SectionAccent { get; init; }
    public required TableCellMargin TableCellMargin { get; init; }
    public required TableBorders TableBorders { get; init; }
    public required Pagination Pagination { get; init; }
    public required DchLabel DchLabel { get; init; }
}

public sealed record FontSet
{
    public required FontSpec Heading { get; init; }
    public required FontSpec Body { get; init; }
    public required FontSpec DocumentId { get; init; }
}

public sealed record FontSpec
{
    public string? Family { get; init; }
    public string? Fallback { get; init; }
    public int? SizePt { get; init; }
    public bool? Bold { get; init; }
    public string? Color { get; init; }
}

public sealed record BorderSpec
{
    public int WidthHalfPoints { get; init; }
    public required string Color { get; init; }
}

public sealed record PageSpec
{
    public double WidthIn { get; init; }
    public double HeightIn { get; init; }
    public double MarginsIn { get; init; }
}

public sealed record Wordmark
{
    public required string Asset { get; init; }
    public double HeightIn { get; init; }
}

public sealed record TypeScale
{
    public int TitlePt { get; init; }
    public int SubtitlePt { get; init; }
    public int BodyPt { get; init; }
    public int CaptionPt { get; init; }
    public int LabelPt { get; init; }
}

public sealed record Spacing
{
    public int BodyAfterPt { get; init; }
    public int LineSpacingPct { get; init; }
    public int SectionTitleBeforePt { get; init; }
    public int SectionTitleAfterPt { get; init; }
    public int H2BeforePt { get; init; }
    public int H2AfterPt { get; init; }
    public int H3BeforePt { get; init; }
    public int H3AfterPt { get; init; }
    public int BulletAfterPt { get; init; }
    public int CalloutBeforePt { get; init; }
}

public sealed record HeadingScale
{
    public int H1Pt { get; init; }
    public int H2Pt { get; init; }
    public int H3Pt { get; init; }
}

public sealed record SectionNumber
{
    public required string Color { get; init; }
}

public sealed record SectionAccent
{
    public int WidthHalfPoints { get; init; }
    public required string Color { get; init; }
    public uint GapPoints { get; init; }
}

public sealed record TableCellMargin
{
    public int TopTwips { get; init; }
    public int BottomTwips { get; init; }
    public int LeftTwips { get; init; }
    public int RightTwips { get; init; }
}

public sealed record TableBorders
{
    public bool Outer { get; init; }
    public bool InsideHorizontal { get; init; }
    public bool InsideVertical { get; init; }
    public int WidthHalfPoints { get; init; }
    public required string Color { get; init; }
}

public sealed record Pagination
{
    public bool KeepHeadingWithNext { get; init; }
    public bool WidowControl { get; init; }
}

public sealed record DchLabel
{
    public required string Color { get; init; }
    public bool Uppercase { get; init; }
    public int CharacterSpacingTwips { get; init; }
}
