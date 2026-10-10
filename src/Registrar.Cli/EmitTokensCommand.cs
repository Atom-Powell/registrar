using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Design;

namespace Registrar.Cli;

// Mirrors EmitKnowledgeCommand: load config, render, write. The generated stylesheet is
// committed and guarded, so a palette change that skips regeneration fails the build.
public static class EmitTokensCommand
{
    public const string DefaultRelativePath = "config/generated/org-tokens.css";

    // The single render path, shared with the staleness guard. If the test rebuilt the
    // arguments itself the two could diverge, and the guard would stop proving anything.
    public static string RenderCurrent(string repoRoot)
    {
        var config = ConfigLoader.Load(repoRoot);
        return TokenProjection.Render(config, config.Hashes["style-rules.json"]);
    }

    public static int Run(string? outPath, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var repoRoot = Directory.GetParent(
                Path.GetDirectoryName(ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

            var target = outPath ?? Path.Combine(repoRoot, DefaultRelativePath);

            // A bare filename (no directory component) resolves against the current
            // working directory, which is what a user typing one plainly expects.
            // Path.GetDirectoryName returns "" in that case, and
            // Directory.CreateDirectory("") throws, so only create a directory when
            // there is one to create.
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(target, RenderCurrent(repoRoot));
            stdout.WriteLine($"wrote {target}");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }
}
