using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;

namespace Registrar.Validation;

public static class AgrFacts
{
    public static (string executionId, string documentId, string mode) Extract(string agrText)
    {
        try
        {
            // Use "as JsonObject" (not .AsObject()) so a syntactically valid but
            // non-object payload (a bare array or scalar) degrades to null here
            // instead of throwing InvalidOperationException.
            var root = JsonNode.Parse(agrText) as JsonObject;
            var executionId = (string?)root?["sourceWorkflowContext"]?["executionId"] ?? "unknown";
            var documentId = (string?)root?["artifactIdentity"]?["documentId"] ?? "unknown";
            var mode = (string?)root?["validationPolicy"]?["mode"] ?? Mode.Draft;
            return (executionId, documentId, mode);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // The engine never trusts the Governor: any parse failure, unexpected
            // JSON shape, or a present field with a non-string type must degrade
            // to the fallback tuple, never crash ValidationService.Validate.
            return ("unknown", "unknown", Mode.Draft);
        }
    }
}
