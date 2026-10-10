using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using Registrar.Cli;
using Registrar.Core;
using Xunit;

namespace Registrar.Tests.Rendering;

// Task 7 acceptance milestone: the Shared Clause Library artifact type renders end to
// end. Every clause in the loaded clause library must appear (by ClauseId and its
// recorded CanonicalTextSha256) in the rendered aggregation table, and the build must
// be byte-deterministic like every other artifact type.
public class SclEndToEndTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    private static string CopyToTemp(string repoRelative)
    {
        var src = Path.Combine(RepoRoot(), repoRelative);
        var dir = TempDir.Create();
        var dest = Path.Combine(dir, Path.GetFileName(src));
        File.Copy(src, dest);
        return dest;
    }

    private static string DocxBodyText(string docxPath)
    {
        using var doc = WordprocessingDocument.Open(docxPath, false);
        return doc.MainDocumentPart!.Document!.Body!.InnerText;
    }

    // Draft and promotion are separate AGR fixtures (validationPolicy.mode is fixed per
    // file, exactly like every other artifact type's draft/promotion pair), so this
    // covers both modes of the SCL build end to end. Promotion additionally exercises the
    // registry: the fixture row starts "Draft" and the AGR requests "In Review".
    [Theory]
    [InlineData("fixtures/agr-sample-scl-draft.json", "draft", false)]
    [InlineData("fixtures/agr-sample-scl-promotion.json", "promotion", true)]
    public void Scl_builds_and_every_clause_is_present(string fixture, string mode, bool useRegistry)
    {
        var agrPath = CopyToTemp(fixture);
        var registryPath = useRegistry ? Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv") : null;

        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            mode, registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();

        using (var doc = WordprocessingDocument.Open(docx, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
            Assert.Empty(errors);
        }

        var text = DocxBodyText(docx);
        var config = SemanticTestSupport.Config();
        foreach (var clause in config.Clauses)
        {
            Assert.Contains(clause.ClauseId, text);
            Assert.Contains(clause.CanonicalTextSha256, text);
        }
    }

    [Fact]
    public void Scl_build_is_byte_deterministic()
    {
        var agrPathA = CopyToTemp("fixtures/agr-sample-scl-draft.json");
        Assert.Equal(0, BuildCommand.Run(agrPathA, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var docxA = File.ReadAllBytes(Directory.GetFiles(Path.GetDirectoryName(agrPathA)!, "*.docx").Single());

        var agrPathB = CopyToTemp("fixtures/agr-sample-scl-draft.json");
        Assert.Equal(0, BuildCommand.Run(agrPathB, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var docxB = File.ReadAllBytes(Directory.GetFiles(Path.GetDirectoryName(agrPathB)!, "*.docx").Single());

        Assert.Equal(docxA, docxB);
    }
}
