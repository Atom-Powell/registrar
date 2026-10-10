using System.Text.Json.Nodes;
using Registrar.Core.Model;
using Json.Schema;

namespace Registrar.Validation;

// AgrText is re-parsed independently by the layer (never trusts an already-parsed
// instance) so malformed JSON is caught inside Validate rather than by the caller.
// Schema is the compiled agr.schema.json used for structural evaluation.
// SchemaRoot is the same schema document as a JsonNode, used only for constraint
// lookup (SchemaConstraint.Lookup) via JSON Pointer navigation, since
// JsonSchema.Evaluate does not expose a way to walk back into the schema text.
public sealed record SchemaValidationInput(string AgrText, JsonSchema Schema, JsonNode SchemaRoot);

public interface IValidationLayer
{
    IReadOnlyList<Check> Validate(SchemaValidationInput input);
}
