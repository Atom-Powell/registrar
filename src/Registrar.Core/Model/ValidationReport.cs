using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record ValidationReport
{
    [JsonPropertyName("reportVersion")]  public required string ReportVersion { get; init; }
    [JsonPropertyName("executionId")]    public required string ExecutionId { get; init; }
    [JsonPropertyName("documentId")]     public required string DocumentId { get; init; }
    [JsonPropertyName("requestedMode")]  public required string RequestedMode { get; init; }
    [JsonPropertyName("overallResult")]  public required string OverallResult { get; init; }
    [JsonPropertyName("engineVersion")]  public required string EngineVersion { get; init; }
    [JsonPropertyName("configVersions")] public required IReadOnlyDictionary<string, string> ConfigVersions { get; init; }
    [JsonPropertyName("timestampUtc")]   public required string TimestampUtc { get; init; }
    [JsonPropertyName("artifactSha256")] public string? ArtifactSha256 { get; init; }
    [JsonPropertyName("checks")]         public required IReadOnlyList<Check> Checks { get; init; }
}
