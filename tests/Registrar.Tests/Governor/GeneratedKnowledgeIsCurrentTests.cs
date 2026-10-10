using Registrar.Cli;

namespace Registrar.Tests.Governor;

// The generated knowledge files are committed so they can be attached to Copilot Studio
// agents and reviewed in a PR. That only stays honest if a config change without a
// regeneration fails the build. This is that gate.
public class GeneratedKnowledgeIsCurrentTests
{
    [Fact]
    public void Committed_knowledge_files_match_a_fresh_render()
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var source = EmitKnowledgeCommand.LoadSource(repoRoot);

        foreach (var projection in EmitKnowledgeCommand.Projections)
        {
            var path = Path.Combine(repoRoot, "docs", "governor", projection.FileName);
            Assert.True(File.Exists(path), $"{projection.FileName} is missing. Run: registrar emit-knowledge");

            var committed = File.ReadAllText(path);
            var fresh = projection.Render(source);

            Assert.True(committed == fresh,
                $"docs/governor/{projection.FileName} is out of date with config. "
                + "Run: dotnet run --project src/Registrar.Cli -- emit-knowledge");
        }
    }
}
