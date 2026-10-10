using Registrar.Tests;

namespace Registrar.Tests.Config;

public class Slice8ContentLintConfigTests
{
    [Fact]
    public void DS_artifact_type_carries_its_configured_classification()
    {
        var config = SemanticTestSupport.Config();
        Assert.True(config.ArtifactTypesByCode.TryGetValue("DS", out var ds));
        Assert.Equal("Client-Facing", ds!.Classification);
    }

    [Fact]
    public void ContentLint_exposes_a_non_empty_token_residue_pattern()
    {
        var config = SemanticTestSupport.Config();
        Assert.NotNull(config.ContentLint.TokenResidue);
        Assert.False(string.IsNullOrWhiteSpace(config.ContentLint.TokenResidue!.Pattern));
    }
}
