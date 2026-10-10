using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record PromotionPayload
{
    [JsonPropertyName("payloadVersion")]  public required string PayloadVersion { get; init; }
    [JsonPropertyName("executionId")]     public required string ExecutionId { get; init; }
    [JsonPropertyName("documentId")]      public required string DocumentId { get; init; }
    [JsonPropertyName("artifactType")]    public required string ArtifactType { get; init; }
    [JsonPropertyName("version")]         public required string Version { get; init; }
    [JsonPropertyName("generatedUtc")]    public required string GeneratedUtc { get; init; }
    [JsonPropertyName("registryUpdate")]  public required RegistryUpdate RegistryUpdate { get; init; }
    [JsonPropertyName("repositoryTarget")] public required string RepositoryTarget { get; init; }
    [JsonPropertyName("attestation")]     public required Attestation Attestation { get; init; }
}

public sealed record RegistryUpdate
{
    [JsonPropertyName("documentId")] public required string DocumentId { get; init; }
    [JsonPropertyName("fromStatus")] public required string FromStatus { get; init; }
    [JsonPropertyName("toStatus")]   public required string ToStatus { get; init; }
    [JsonPropertyName("version")]    public required string Version { get; init; }
}

public sealed record Attestation
{
    [JsonPropertyName("checkId")]   public required string CheckId { get; init; }
    [JsonPropertyName("statement")] public required string Statement { get; init; }
    [JsonPropertyName("requiresOperatorSignoff")] public required bool RequiresOperatorSignoff { get; init; }
}
