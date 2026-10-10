using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record GenerationLog
{
    [JsonPropertyName("logVersion")]       public required string LogVersion { get; init; }
    [JsonPropertyName("executionId")]      public required string ExecutionId { get; init; }
    [JsonPropertyName("documentId")]       public required string DocumentId { get; init; }
    [JsonPropertyName("engineVersion")]    public required string EngineVersion { get; init; }
    [JsonPropertyName("requestedMode")]    public required string RequestedMode { get; init; }
    [JsonPropertyName("outcome")]          public required string Outcome { get; init; }
    [JsonPropertyName("timestampUtc")]     public required string TimestampUtc { get; init; }
    [JsonPropertyName("configVersions")]   public required IReadOnlyDictionary<string, string> ConfigVersions { get; init; }
    [JsonPropertyName("rulesApplied")]     public required IReadOnlyList<string> RulesApplied { get; init; }
    [JsonPropertyName("clauseInsertions")] public required IReadOnlyList<ClauseInsertion> ClauseInsertions { get; init; }
}
