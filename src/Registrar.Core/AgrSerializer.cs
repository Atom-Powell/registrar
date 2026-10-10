using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Registrar.Core.Model;

namespace Registrar.Core;

// Writes an AgrDocument back to JSON. AgrParser reads; nothing wrote one until the
// GUI needed to validate a document it authored in memory, because schema validation
// operates on text rather than on a parsed object.
//
// Options mirror ReportSerializer exactly so every JSON artifact this engine emits
// looks the same.
public static class AgrSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToJson(AgrDocument agr) => JsonSerializer.Serialize(agr, Options);
}
