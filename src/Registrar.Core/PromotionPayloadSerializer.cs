using System.Text.Json;
using Registrar.Core.Model;

namespace Registrar.Core;

public static class PromotionPayloadSerializer
{
    // Reuses the report serializer's options (indented, relaxed escaping,
    // omit nulls) so both engine outputs render identically.
    public static string ToJson(PromotionPayload payload)
        => JsonSerializer.Serialize(payload, ReportSerializer.Options);
}
