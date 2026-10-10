using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Governor;

namespace Registrar.Cli;

public static class EmitKnowledgeCommand
{
    // The single list of projections. Adding one here is all it takes for both the CLI
    // and the staleness guard to pick it up.
    public static IReadOnlyList<IKnowledgeProjection> Projections { get; } = new IKnowledgeProjection[]
    {
        new IntakeProjection(),
        new DrafterProjection(),
        new PayloadProjection(),
        new CorrectionProjection()
    };

    public static KnowledgeSource LoadSource(string repoRoot)
    {
        var schemaPath = ConfigLocator.FindSchema(repoRoot, "agr.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        var schema = JsonNode.Parse(schemaText)!;

        var config = ConfigLoader.Load(repoRoot);

        // The knowledge generator's own hash set, distinct from ConfigBundle.Hashes: that
        // map deliberately excludes qa-checklist.json and never contained agr.schema.json
        // (see the comments in ConfigLoader.cs and KnowledgeSource.cs), but correction.md
        // is generated entirely from qa-checklist.json, so its provenance table needs it.
        var qaChecklistPath = ConfigLocator.FindConfig(repoRoot, "qa-checklist.json");
        var qaChecklistText = File.ReadAllText(qaChecklistPath);

        var sourceHashes = new Dictionary<string, string>(config.Hashes)
        {
            ["qa-checklist.json"] = Hashing.Sha256Hex(qaChecklistText),
            ["agr.schema.json"] = Hashing.Sha256Hex(schemaText)
        };

        return new KnowledgeSource(config, schema, sourceHashes);
    }

    public static int Run(string? outDir, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var repoRoot = Directory.GetParent(
                Path.GetDirectoryName(ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;
            var target = outDir ?? Path.Combine(repoRoot, "docs", "governor");
            Directory.CreateDirectory(target);

            var source = LoadSource(repoRoot);
            foreach (var projection in Projections)
            {
                var path = Path.Combine(target, projection.FileName);
                File.WriteAllText(path, projection.Render(source));
                stdout.WriteLine($"wrote {projection.FileName}");
            }
            stdout.WriteLine($"{Projections.Count} knowledge file(s) written to {target}");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }
}
