using System.Text.Json;
using Registrar.Core;
using Registrar.Core.Model;
using Json.Pointer;
using Json.Schema;

namespace Registrar.Validation;

public sealed class SchemaValidationLayer : IValidationLayer
{
    public const string CheckId = "QA-SCHEMA-01";
    private const string RuleSourceValue = "agr.schema.json";

    public IReadOnlyList<Check> Validate(SchemaValidationInput input)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(input.AgrText);
        }
        catch (JsonException ex)
        {
            return new[] { MalformedJsonCheck($"AGR is not valid JSON: {ex.Message}") };
        }

        // Keep the JsonDocument alive for the whole evaluation and for reading
        // instance values below; JsonSchema.Net only evaluates JsonElement, which
        // is a view over this document's buffer.
        using (doc)
        {
            var results = input.Schema.Evaluate(
                doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

            if (results.IsValid) return Array.Empty<Check>();

            var checks = new List<Check>();
            if (results.Details is not null)
            {
                foreach (var node in results.Details)
                {
                    if (node.Errors is null || node.Errors.Count == 0) continue;
                    foreach (var (keyword, message) in node.Errors)
                        checks.Add(BuildCheck(input, node, keyword, message, doc.RootElement));
                }
            }

            // Fallback: schema said invalid but produced no per-node keyword errors
            // (e.g. a future JsonSchema.Net version restructures Details). Fail
            // closed rather than silently returning zero checks for an invalid AGR.
            if (checks.Count == 0)
                checks.Add(MalformedJsonCheck("AGR failed schema validation (no per-field detail available)."));

            return checks;
        }
    }

    private static Check BuildCheck(
        SchemaValidationInput input, EvaluationResults node,
        string keyword, string message, JsonElement rootInstance)
    {
        var jsonPath = node.InstanceLocation.ToString();
        var constraint = SchemaConstraint.Lookup(input.SchemaRoot, node.SchemaLocation, keyword);
        var expected = constraint is null ? keyword : $"{keyword} {constraint}";
        var actual = ValueAt(rootInstance, node.InstanceLocation);
        var pathForMessage = string.IsNullOrEmpty(jsonPath) ? "the document root" : jsonPath;

        return new Check
        {
            CheckId = CheckId,
            LayerName = Layer.Input,
            Result = CheckResultValue.Fail,
            SeverityLevel = Severity.Blocking,
            ErrorCategory = ErrorCategory.SchemaValidationError,
            Message = message,
            JsonPath = string.IsNullOrEmpty(jsonPath) ? "/" : jsonPath,
            Expected = expected,
            Actual = actual,
            Remediation = $"Correct the value at {pathForMessage} in the AGR so it satisfies {expected} (see agr.schema.json).",
            RuleSource = RuleSourceValue
        };
    }

    private static string? ValueAt(JsonElement root, JsonPointer location)
    {
        var element = location.Evaluate(root);
        if (element is null) return null;

        var value = element.Value;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    private static Check MalformedJsonCheck(string message) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.SchemaValidationError,
        Message = message,
        JsonPath = null,
        Expected = "well-formed JSON conforming to agr.schema.json",
        Actual = null,
        Remediation = "Emit a syntactically valid AGR JSON document.",
        RuleSource = RuleSourceValue
    };
}
