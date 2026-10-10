using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core;

public static class GenerationLogSerializer
{
    // Reuse the report serializer's options: indented, ignore-null, relaxed
    // escaping. One options instance keeps every output-package file byte-consistent.
    public static string ToJson(GenerationLog log)
        => JsonSerializer.Serialize(log, ReportSerializer.Options);

    public static GenerationLog FromJson(string json)
        => JsonSerializer.Deserialize<GenerationLog>(json, ReportSerializer.Options)
           ?? throw new JsonException("generation-log.json deserialized to null.");
}
