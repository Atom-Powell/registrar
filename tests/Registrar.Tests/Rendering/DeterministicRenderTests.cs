using System.IO.Compression;
using System.Security.Cryptography;
using Registrar.Core;
using Registrar.Rendering;

namespace Registrar.Tests.Rendering;

/// <summary>
/// The DOCX must be a pure function of (AGR + config). artifactSha256 is offered to the
/// operator as proof of the artifact, and a hash that changes on every render proves
/// nothing. These tests pin the three sources of nondeterminism that previously made it
/// unreproducible: auto-generated relationship IDs, the random core-properties part GUID,
/// and wall-clock zip entry timestamps.
/// </summary>
public class DeterministicRenderTests
{
    private static byte[] Render(string fixture = "agr-sample-ds-draft.json") =>
        new ArtifactRenderer().Render(
            SemanticTestSupport.Agr(fixture),
            SemanticTestSupport.Config(),
            ArtifactRendererTests.LogoBytes()).Bytes;

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Theory]
    [InlineData("agr-sample-ds-draft.json")]
    [InlineData("agr-sample-aup-draft.json")]
    [InlineData("agr-sample-tcl-draft.json")]
    public void Same_agr_renders_byte_identical(string fixture)
    {
        var first = Render(fixture);
        var second = Render(fixture);

        Assert.Equal(Sha(first), Sha(second));
    }

    [Fact]
    public void Relationship_ids_are_stable_across_renders()
    {
        static string Rels(byte[] docx)
        {
            using var ms = new MemoryStream(docx);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.GetEntry("word/_rels/document.xml.rels")!;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }

        Assert.Equal(Rels(Render()), Rels(Render()));
    }

    [Fact]
    public void Zip_entry_timestamps_are_fixed_not_wall_clock()
    {
        using var ms = new MemoryStream(Render());
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        Assert.All(zip.Entries, e =>
            Assert.Equal(new DateTime(1980, 1, 1, 0, 0, 0), e.LastWriteTime.DateTime));
    }

    [Fact]
    public void Core_properties_land_in_docProps_core_xml_where_word_reads_them()
    {
        using var ms = new MemoryStream(Render());
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        // The OPC .psmdcp form that PackageProperties emits is not read by Word or
        // SharePoint as document properties, and its part name is a fresh GUID per render.
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".psmdcp"));

        var core = zip.GetEntry("docProps/core.xml");
        Assert.NotNull(core);

        using var reader = new StreamReader(core!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("ORG-GOV-DS-016", xml);   // dc:title carries the Document ID
        Assert.Contains("registrar", xml);        // dc:creator
        Assert.DoesNotContain("dcterms:created", xml);
        Assert.DoesNotContain("dcterms:modified", xml);
    }
}
