using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

// The serialized shape of one clause insertion in generation-log.json. Kept
// distinct from the renderer-side ClauseInsertionAudit so the wire contract does
// not depend on the internal record's positional layout.
public sealed record ClauseInsertion
{
    [JsonPropertyName("clauseId")]       public required string ClauseId { get; init; }
    [JsonPropertyName("expectedSha")]    public required string ExpectedSha { get; init; }
    [JsonPropertyName("actualSha")]      public required string ActualSha { get; init; }
    [JsonPropertyName("verified")]       public required bool Verified { get; init; }
    [JsonPropertyName("tokensResolved")] public required int TokensResolved { get; init; }
}
