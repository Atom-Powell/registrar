using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using Registrar.Cli;
using Registrar.Core;
using Xunit;

namespace Registrar.Tests.Rendering;

// Task 5: the DCN "Suggested Delivery Message" section renders a copyReadyMessage box
// instead of a plain paragraph. This is an end-to-end proof (mirrors SclEndToEndTests):
// both draft and promotion builds must pass and the rendered DOCX body must contain the
// config-driven copyReadyMessage label.
public class DcnCopyReadyMessageTests
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

    [Theory]
    [InlineData("fixtures/agr-sample-dcn-draft.json", "draft", false)]
    [InlineData("fixtures/agr-sample-dcn-promotion.json", "promotion", true)]
    public void Dcn_builds_with_copy_ready_message_box(string fixture, string mode, bool useRegistry)
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
        Assert.Contains(config.CopyReadyMessage.Label, text);
    }
}
