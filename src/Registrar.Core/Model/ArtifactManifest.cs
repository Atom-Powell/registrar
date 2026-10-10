using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record ArtifactManifest
{
    [JsonPropertyName("manifestVersion")]  public required string ManifestVersion { get; init; }
    [JsonPropertyName("executionId")]      public required string ExecutionId { get; init; }
    [JsonPropertyName("documentId")]       public required string DocumentId { get; init; }
    [JsonPropertyName("engineVersion")]    public required string EngineVersion { get; init; }
    [JsonPropertyName("timestampUtc")]     public required string TimestampUtc { get; init; }
    [JsonPropertyName("identity")]         public required ManifestIdentity Identity { get; init; }
    [JsonPropertyName("context")]          public required ManifestContext Context { get; init; }
    [JsonPropertyName("sections")]         public required IReadOnlyList<ManifestSection> Sections { get; init; }
    [JsonPropertyName("dependencies")]     public required IReadOnlyList<ManifestDependency> Dependencies { get; init; }
    [JsonPropertyName("registryReference")] public ManifestRegistryReference? RegistryReference { get; init; }
    [JsonPropertyName("artifactSha256")]   public required string ArtifactSha256 { get; init; }
}

public sealed record ManifestIdentity
{
    [JsonPropertyName("artifactType")]   public required string ArtifactType { get; init; }
    [JsonPropertyName("documentId")]     public required string DocumentId { get; init; }
    [JsonPropertyName("version")]        public required string Version { get; init; }
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("approvalStatus")] public required string ApprovalStatus { get; init; }
    [JsonPropertyName("layer")]          public required string Layer { get; init; }
}

public sealed record ManifestContext
{
    [JsonPropertyName("owner")]      public required string Owner { get; init; }
    [JsonPropertyName("audience")]   public required string Audience { get; init; }
    [JsonPropertyName("preparedBy")] public required string PreparedBy { get; init; }
    [JsonPropertyName("client")]     public string? Client { get; init; }
    [JsonPropertyName("engagement")] public string? Engagement { get; init; }
    [JsonPropertyName("reviewDate")] public string? ReviewDate { get; init; }
}

public sealed record ManifestSection
{
    [JsonPropertyName("number")]     public required int Number { get; init; }
    [JsonPropertyName("title")]      public required string Title { get; init; }
    [JsonPropertyName("blockCount")] public required int BlockCount { get; init; }
    [JsonPropertyName("clauseIds")]  public required IReadOnlyList<string> ClauseIds { get; init; }
}

public sealed record ManifestDependency
{
    [JsonPropertyName("dependencyId")] public required string DependencyId { get; init; }
    [JsonPropertyName("kind")]         public required string Kind { get; init; }
    [JsonPropertyName("blocking")]     public required bool Blocking { get; init; }
}

public sealed record ManifestRegistryReference
{
    [JsonPropertyName("documentId")] public required string DocumentId { get; init; }
    [JsonPropertyName("fromStatus")] public required string FromStatus { get; init; }
    [JsonPropertyName("toStatus")]   public required string ToStatus { get; init; }
    [JsonPropertyName("version")]    public required string Version { get; init; }
}
