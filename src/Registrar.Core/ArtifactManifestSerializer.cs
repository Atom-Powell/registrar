using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core;

public static class ArtifactManifestSerializer
{
    public static string ToJson(ArtifactManifest manifest)
        => JsonSerializer.Serialize(manifest, ReportSerializer.Options);

    public static ArtifactManifest FromJson(string json)
        => JsonSerializer.Deserialize<ArtifactManifest>(json, ReportSerializer.Options)
           ?? throw new JsonException("artifact-manifest.json deserialized to null.");
}
