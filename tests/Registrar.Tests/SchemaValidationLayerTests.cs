using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class SchemaValidationLayerTests
{
    private static SchemaValidationInput InputFor(string agrText)
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");

        // See IsolatedSchema for why this must never build against the
        // default/global SchemaRegistry.
        var schema = IsolatedSchema.FromFile(schemaPath);
        var schemaRoot = JsonNode.Parse(File.ReadAllText(schemaPath))!;
        return new SchemaValidationInput(agrText, schema, schemaRoot);
    }

    private static string ValidAgr()
    {
        var fixturePath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(fixturePath)!)!.FullName;
        return File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));
    }

    [Fact]
    public void Valid_agr_produces_no_checks()
    {
        var layer = new SchemaValidationLayer();
        var checks = layer.Validate(InputFor(ValidAgr()));
        Assert.Empty(checks);
    }

    [Fact]
    public void Bad_document_id_produces_qa_schema_01_fail_with_path_and_values()
    {
        // Break the documentId pattern (trailing 2-digit sequence instead of 3).
        var broken = ValidAgr().Replace("ORG-GOV-DS-016", "ORG-GOV-DS-16");
        var layer = new SchemaValidationLayer();

        var checks = layer.Validate(InputFor(broken));

        var check = Assert.Single(checks, c => c.JsonPath == "/artifactIdentity/documentId");
        Assert.Equal("QA-SCHEMA-01", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.SchemaValidationError, check.ErrorCategory);
        Assert.Equal("agr.schema.json", check.RuleSource);
        Assert.Contains("ORG-", check.Expected); // pattern constraint text
        Assert.Equal("ORG-GOV-DS-16", check.Actual);
        Assert.NotNull(check.Remediation);
    }

    [Fact]
    public void Malformed_json_produces_single_fail_with_null_path()
    {
        var layer = new SchemaValidationLayer();
        var checks = layer.Validate(InputFor("{ not valid json "));

        var check = Assert.Single(checks);
        Assert.Equal("QA-SCHEMA-01", check.CheckId);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Null(check.JsonPath);
        Assert.Equal(ErrorCategory.SchemaValidationError, check.ErrorCategory);
    }
}
