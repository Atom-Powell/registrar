using System.Text.Json.Nodes;
using Registrar.Validation;

namespace Registrar.Tests;

public class SchemaConstraintTests
{
    private static JsonNode Schema(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void Lookup_returns_keyword_constraint_at_location()
    {
        var root = Schema("""{ "properties": { "documentId": { "pattern": "^ORG-" } } }""");
        var location = new Uri("https://x/schema#/properties/documentId");

        var result = SchemaConstraint.Lookup(root, location, "pattern");

        Assert.Equal("\"^ORG-\"", result);
    }

    [Fact]
    public void Lookup_unescapes_percent_encoded_fragment_segments()
    {
        // A property name containing a space is percent-encoded to %20 in Uri.Fragment;
        // Lookup must decode it before evaluating the JSON Pointer.
        var root = Schema("""{ "properties": { "doc id": { "pattern": "^ORG-" } } }""");
        var location = new Uri("https://x/schema#/properties/doc%20id");

        var result = SchemaConstraint.Lookup(root, location, "pattern");

        Assert.Equal("\"^ORG-\"", result);
    }

    [Fact]
    public void Lookup_returns_null_for_empty_fragment()
    {
        var root = Schema("{}");
        Assert.Null(SchemaConstraint.Lookup(root, new Uri("https://x/schema"), "pattern"));
    }

    [Fact]
    public void Lookup_returns_null_for_unparsable_pointer()
    {
        // A fragment that is not a valid JSON Pointer (does not start with '/').
        var root = Schema("{}");
        Assert.Null(SchemaConstraint.Lookup(root, new Uri("https://x/schema#not-a-pointer"), "pattern"));
    }

    [Fact]
    public void Lookup_returns_null_when_keyword_absent()
    {
        var root = Schema("""{ "properties": { "documentId": { "pattern": "^ORG-" } } }""");
        var location = new Uri("https://x/schema#/properties/documentId");

        Assert.Null(SchemaConstraint.Lookup(root, location, "enum"));
    }
}
