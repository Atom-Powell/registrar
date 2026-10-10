using System.Text.Json;

namespace Registrar.Core.Model;

public static class AgrParser
{
    // Call only after schema validation has passed: the schema guarantees every
    // required member is present, so deserialization of required members cannot
    // throw for missing fields. Unmapped AGR fields are ignored by design.
    public static AgrDocument Parse(string agrText)
        => JsonSerializer.Deserialize<AgrDocument>(agrText)!;
}
