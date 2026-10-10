using System.Text.Json.Nodes;
using Json.Schema;
using Registrar.Core;
using Registrar.Core.Config;

namespace Registrar.Validation;

// Config and schema, loaded once and reused. Lives in Validation rather than Pipeline
// because it holds the compiled schema, which is a Validation concern, and
// ValidationService consumes it.
public sealed record EngineContext(
    ConfigBundle Config,
    JsonSchema Schema,
    JsonNode SchemaRoot,
    string SchemaHash)
{
    public static EngineContext Load(string repoRoot)
    {
        var schemaPath = ConfigLocator.FindSchema(repoRoot, "agr.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        return new EngineContext(
            ConfigLoader.Load(repoRoot),
            IsolatedSchema.FromText(schemaText),
            JsonNode.Parse(schemaText)!,
            Hashing.Sha256Hex(schemaText));
    }
}
