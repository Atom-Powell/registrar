using System.Linq;
using Registrar.Tests;
using Xunit;

namespace Registrar.Tests.Config;

public class PlaceholderDictionaryConfigTests
{
    [Fact]
    public void CanonicalToken_binds_usage_from_config()
    {
        var config = SemanticTestSupport.Config();
        var package = config.CanonicalTokens.Single(t => t.Token == "[PACKAGE]");
        Assert.Equal("footer", package.Usage);
    }

    [Fact]
    public void CanonicalToken_usage_is_null_when_absent()
    {
        var config = SemanticTestSupport.Config();
        var clientOrg = config.CanonicalTokens.Single(t => t.Token == "[CLIENT ORGANIZATION NAME]");
        Assert.Null(clientOrg.Usage);
    }

    [Fact]
    public void Dictionary_is_declared_canonical_not_a_subset()
    {
        var path = System.IO.Path.Combine(SemanticTestSupport.RepoRoot(), "config", "placeholder-dictionary.json");
        var text = System.IO.File.ReadAllText(path);
        Assert.DoesNotContain("extracted-subset", text);
        Assert.DoesNotContain("reconciledAgainstPdxXlsx", text);
    }

    [Fact]
    public void Pdx_is_a_defined_generation_target()
    {
        var config = SemanticTestSupport.Config();
        var pdx = config.ArtifactTypesByCode["PDX"];
        Assert.True(pdx.GenerationTarget);
        Assert.Equal("defined", pdx.SectionSchema!.Status);
        Assert.Equal(
            new[] { "Purpose", "How Placeholders Work", "Placeholder Dictionary", "Version and Change Notes" },
            pdx.SectionSchema.OrderedSections);
    }
}
