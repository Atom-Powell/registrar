using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record Check
{
    [JsonPropertyName("checkId")]    public required string CheckId { get; init; }
    [JsonPropertyName("layer")]      public required string LayerName { get; init; }
    [JsonPropertyName("result")]     public required string Result { get; init; }
    [JsonPropertyName("severity")]   public required string SeverityLevel { get; init; }
    [JsonPropertyName("errorCategory")] public string? ErrorCategory { get; init; }
    [JsonPropertyName("message")]    public required string Message { get; init; }
    [JsonPropertyName("jsonPath")]   public string? JsonPath { get; init; }
    [JsonPropertyName("expected")]   public string? Expected { get; init; }
    [JsonPropertyName("actual")]     public string? Actual { get; init; }
    [JsonPropertyName("remediation")] public string? Remediation { get; init; }
    [JsonPropertyName("ruleSource")] public string? RuleSource { get; init; }
}
