using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using Registrar.Cli;
using Registrar.Core;
using Xunit;

namespace Registrar.Tests.Rendering;

// Acceptance milestone: the Placeholder Dictionary artifact type renders end to end.
// Every canonical token in the loaded dictionary must appear in the rendered aggregation
// table, and the build must be byte-deterministic like every other artifact type.
public class PdxEndToEndTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
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

    [Theory]
    [InlineData("fixtures/agr-sample-pdx-draft.json", "draft", false)]
    [InlineData("fixtures/agr-sample-pdx-promotion.json", "promotion", true)]
    public void Pdx_builds_and_every_token_is_present(string fixture, string mode, bool useRegistry)
    {
        var agrPath = CopyToTemp(fixture);
        var registryPath = useRegistry ? Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv") : null;

        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, mode, registryPath);
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
        foreach (var token in config.CanonicalTokens)
            Assert.Contains(token.Token, text);
    }

    [Fact]
    public void Pdx_build_is_byte_deterministic()
    {
        var a = CopyToTemp("fixtures/agr-sample-pdx-draft.json");
        Assert.Equal(0, BuildCommand.Run(a, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var docxA = File.ReadAllBytes(Directory.GetFiles(Path.GetDirectoryName(a)!, "*.docx").Single());

        var b = CopyToTemp("fixtures/agr-sample-pdx-draft.json");
        Assert.Equal(0, BuildCommand.Run(b, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var docxB = File.ReadAllBytes(Directory.GetFiles(Path.GetDirectoryName(b)!, "*.docx").Single());

        Assert.Equal(docxA, docxB);
    }
}
