using System.Text.Encodings.Web;
using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core;

public static class ReportSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToJson(ValidationReport report)
        => JsonSerializer.Serialize(report, Options);

    public static ValidationReport FromJson(string json)
        => JsonSerializer.Deserialize<ValidationReport>(json, Options)
           ?? throw new JsonException("validation-report.json deserialized to null.");
}
