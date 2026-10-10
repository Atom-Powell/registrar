using System.Text.Json.Nodes;
using Json.Pointer;

namespace Registrar.Validation;

// Resolves the constraint value for a failing keyword by navigating the schema
// document at the error's schema location. Returns e.g. the pattern regex for a
// "pattern" failure, or the allowed list for an "enum" failure.
public static class SchemaConstraint
{
    public static string? Lookup(JsonNode schemaRoot, Uri schemaLocation, string keyword)
    {
        // schemaLocation fragment is a JSON Pointer into the schema, e.g.
        // "#/properties/artifactIdentity/properties/documentId".
        var fragment = schemaLocation.Fragment;
        if (string.IsNullOrEmpty(fragment)) return null;
        var pointerText = fragment.StartsWith('#') ? fragment[1..] : fragment;

        // Uri.Fragment is percent-encoded; decode so property names carrying
        // reserved characters (spaces, slashes) resolve to the right subschema.
        pointerText = Uri.UnescapeDataString(pointerText);

        if (!JsonPointer.TryParse(pointerText, out var pointer)) return null;
        if (!pointer.TryEvaluate(schemaRoot, out var subSchema)) return null;

        var constraint = subSchema?[keyword];
        return constraint?.ToJsonString();
    }
}
