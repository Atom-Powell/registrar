using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class MetadataLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-03";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 5.2";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var checks = new List<Check>();
        var id = context.Agr.Identity.DocumentId;
        var artifactType = context.Agr.Identity.ArtifactType;

        // The schema pattern guarantees the ORG-CATEGORY-CODE-NNN shape.
        var parts = id.Split('-');
        if (parts.Length < 4) return checks;
        var idCategory = parts[1];
        var idCode = parts[2];

        if (idCode != artifactType)
            checks.Add(Fail(
                $"Document ID code '{idCode}' does not match artifactType '{artifactType}'.",
                $"a document ID whose code segment is '{artifactType}'", idCode,
                $"Reconcile artifactIdentity.documentId and artifactIdentity.artifactType so the code segment equals '{artifactType}'."));

        if (context.Config.ArtifactTypesByCode.TryGetValue(artifactType, out var type)
            && idCategory != type.Category)
            checks.Add(Fail(
                $"Document ID category '{idCategory}' does not match the '{artifactType}' type category '{type.Category}'.",
                $"category segment '{type.Category}'", idCategory,
                $"Correct artifactIdentity.documentId so its category segment is '{type.Category}'."));

        return checks;
    }

    private static Check Fail(string message, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = message,
        JsonPath = "/artifactIdentity/documentId",
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
