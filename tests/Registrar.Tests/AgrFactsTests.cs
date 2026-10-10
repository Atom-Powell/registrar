using Registrar.Validation;

namespace Registrar.Tests;

public class AgrFactsTests
{
    [Fact]
    public void Extract_on_valid_json_array_degrades_to_fallback_without_throwing()
    {
        var (executionId, documentId, mode) = AgrFacts.Extract("[]");

        Assert.Equal("unknown", executionId);
        Assert.Equal("unknown", documentId);
        Assert.Equal("draft", mode);
    }

    [Fact]
    public void Extract_on_valid_json_scalar_degrades_to_fallback_without_throwing()
    {
        var (executionId, documentId, mode) = AgrFacts.Extract("42");

        Assert.Equal("unknown", executionId);
        Assert.Equal("unknown", documentId);
        Assert.Equal("draft", mode);
    }

    [Fact]
    public void Extract_on_non_string_field_value_degrades_to_fallback_without_throwing()
    {
        // A present field with a non-string type (documentId: 42) must degrade to
        // the fallback tuple, not throw. Guards against a future narrowing of the
        // catch clause silently regressing this.
        var agr = """
            {
              "artifactIdentity": { "documentId": 42 },
              "sourceWorkflowContext": { "executionId": "GOV-2026-07-03-001" },
              "validationPolicy": { "mode": "promotion" }
            }
            """;

        var (executionId, documentId, mode) = AgrFacts.Extract(agr);

        Assert.Equal("unknown", executionId);
        Assert.Equal("unknown", documentId);
        Assert.Equal("draft", mode);
    }
}
