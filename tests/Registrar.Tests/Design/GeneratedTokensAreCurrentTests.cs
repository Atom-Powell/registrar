using Registrar.Cli;
using Xunit;

namespace Registrar.Tests.Design;

// The stylesheet is committed so the application can consume it and a reviewer can see
// a palette change in a PR. That only stays honest if a config change without a
// regeneration fails the build. This is that gate, and it mirrors
// GeneratedKnowledgeIsCurrentTests exactly.
public class GeneratedTokensAreCurrentTests
{
    [Fact]
    public void The_committed_stylesheet_matches_a_fresh_render()
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var path = Path.Combine(repoRoot, EmitTokensCommand.DefaultRelativePath);

        Assert.True(File.Exists(path),
            $"{EmitTokensCommand.DefaultRelativePath} is missing. Run: registrar emit-tokens");

        var committed = File.ReadAllText(path);
        var fresh = EmitTokensCommand.RenderCurrent(repoRoot);

        Assert.True(committed == fresh,
            $"{EmitTokensCommand.DefaultRelativePath} is out of date with config. "
            + "Run: dotnet run --project src/Registrar.Cli -- emit-tokens");
    }

    // The failure this guards against is invisible locally and only appears on a fresh
    // clone, because core.autocrlf rewrites the working copy on checkout. It has already
    // cost this repo a CI run once.
    [Fact]
    public void The_committed_stylesheet_holds_no_carriage_returns()
    {
        var path = Path.Combine(SemanticTestSupport.RepoRoot(), EmitTokensCommand.DefaultRelativePath);

        Assert.DoesNotContain("\r", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
