using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class RegistryLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-03";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 3";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var id = context.Agr.Identity.DocumentId;

        if (context.Registry is null)
        {
            if (context.Mode == Mode.Promotion)
                return new[] { Fail(
                    "The ID Registry export is required to promote.",
                    "/artifactIdentity/documentId",
                    "a --registry CSV export of the PMC Registry sheet", "no registry supplied",
                    "Supply the registry export with --registry <path> before promotion.") };
            return new[] { Skipped("No ID Registry supplied; registry checks are skipped in draft mode.") };
        }

        var record = context.Registry.Lookup(id);
        if (record is null)
            return new[] { Fail(
                $"Document ID '{id}' is not present in the ID Registry.",
                "/artifactIdentity/documentId",
                "a Document ID issued in the registry", id,
                "Have the Registrar issue this Document ID in the registry, or correct artifactIdentity.documentId.") };

        var checks = new List<Check>();
        if (record.ArtifactType != context.Agr.Identity.ArtifactType)
            checks.Add(Fail(
                $"Artifact type '{context.Agr.Identity.ArtifactType}' disagrees with the registry ('{record.ArtifactType}').",
                "/artifactIdentity/artifactType",
                $"registry artifactType '{record.ArtifactType}'", context.Agr.Identity.ArtifactType,
                "Reconcile artifactIdentity.artifactType with the registry, or correct the registry entry."));

        if (record.Classification != context.Agr.Identity.Classification)
            checks.Add(Fail(
                $"Classification '{context.Agr.Identity.Classification}' disagrees with the registry ('{record.Classification}').",
                "/artifactIdentity/classification",
                $"registry classification '{record.Classification}'", context.Agr.Identity.Classification,
                "Reconcile artifactIdentity.classification with the registry, or correct the registry entry."));

        return checks;
    }

    private static Check Fail(string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.RegistrySyncError,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };

    private static Check Skipped(string message) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Release,
        Result = CheckResultValue.Skipped,
        SeverityLevel = Severity.Info,
        Message = message,
        RuleSource = RuleSourceValue
    };
}
