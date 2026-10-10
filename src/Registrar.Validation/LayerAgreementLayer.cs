using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

// Validates artifactIdentity.layer against the artifact type's configured framework
// layer. Before this check existed, layer was the one required identity field the
// engine accepted on trust: it flows from the AGR into artifact-manifest.json via
// ArtifactManifestBuilder and nothing else reads it, so a wrong value was recorded
// silently and shipped in the manifest.
public sealed class LayerAgreementLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-08";
    private const string RuleSourceValue = "ADR-002 AI Governance Program Structure; per-type layer assignment ratified 2026-08-01";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var artifactType = context.Agr.Identity.ArtifactType;
        var actual = context.Agr.Identity.Layer;

        // Deliberately fail-open on an unknown type or a type with no configured layer.
        // QA-ENG-02 (SectionSchemaLayer) already refuses a type absent from
        // artifact-types.json or without a defined section schema, so this layer does
        // not double-report it. Only generable types carry a layer in config.
        if (!context.Config.ArtifactTypesByCode.TryGetValue(artifactType, out var type)
            || string.IsNullOrEmpty(type.Layer))
            return Array.Empty<Check>();

        if (string.Equals(type.Layer, actual, StringComparison.Ordinal))
            return Array.Empty<Check>();

        return new[]
        {
            new Check
            {
                CheckId = CheckId,
                LayerName = Layer.Input,
                Result = CheckResultValue.Fail,
                SeverityLevel = Severity.Blocking,
                ErrorCategory = ErrorCategory.MetadataViolation,
                Message = $"Framework layer '{actual}' does not match the layer configured "
                    + $"for artifact type '{artifactType}'.",
                JsonPath = "/artifactIdentity/layer",
                Expected = type.Layer,
                Actual = actual,
                Remediation = $"Set artifactIdentity.layer to '{type.Layer}'.",
                RuleSource = RuleSourceValue
            }
        };
    }
}
