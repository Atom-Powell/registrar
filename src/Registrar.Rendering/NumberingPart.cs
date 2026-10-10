using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

// The bullet glyph lives in the numbering definition, not in a document run, so the
// QA-SF-08 run scan never sees it. Body run text stays within the allowed character set.
public static class NumberingPart
{
    public const int BulletNumId = 1;
    private const int AbstractNumId = 0;

    public static void Build(MainDocumentPart main)
    {
        // Explicit relationship ID: the SDK otherwise mints a random one per render, which
        // makes the package bytes (and therefore artifactSha256) unreproducible.
        var numberingPart = main.AddNewPart<NumberingDefinitionsPart>(RelationshipIds.Numbering);
        var abstractNum = new AbstractNum(
            new Level(
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = "\u2022" },
                new PreviousParagraphProperties(new Indentation { Left = "720", Hanging = "360" }),
                new NumberingSymbolRunProperties(new RunFonts { Ascii = "Symbol", HighAnsi = "Symbol" }))
            { LevelIndex = 0 })
        { AbstractNumberId = AbstractNumId };
        var num = new NumberingInstance(new AbstractNumId { Val = AbstractNumId }) { NumberID = BulletNumId };
        numberingPart.Numbering = new Numbering(abstractNum, num);
    }
}
