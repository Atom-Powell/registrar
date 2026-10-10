using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class SectionSchemaLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-02";
    private const string RuleSourceValue = "ORG-MST-SPEC-001 Section 6; Arti-Generator Section 14.2";
    private const string DefinedStatus = "defined";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var artifactType = context.Agr.Identity.ArtifactType;

        if (!context.Config.ArtifactTypesByCode.TryGetValue(artifactType, out var type))
            return new[] { Fail(
                $"Artifact type '{artifactType}' is not defined in artifact-types.json.",
                "/artifactIdentity/artifactType",
                "a code present in artifact-types.json", artifactType,
                "Add the artifact type to config/artifact-types.json or correct artifactIdentity.artifactType.") };

        var schema = type.SectionSchema;
        if (schema is null || schema.Status != DefinedStatus || schema.OrderedSections is null)
            return new[] { Fail(
                $"Artifact type '{artifactType}' has no defined section schema; generation is refused.",
                "/dataInputs/sections",
                "an artifact type whose sectionSchema.status is 'defined'",
                $"sectionSchema.status = {schema?.Status ?? "absent"}",
                "Author and approve a section schema for this artifact type before generating it.") };

        var expected = schema.OrderedSections;
        var actual = context.Agr.DataInputs.Sections.Select(s => s.Title).ToList();
        if (expected.SequenceEqual(actual))
            return Array.Empty<Check>();

        return new[] { Fail(
            "Section titles do not match the artifact type's section schema exactly and in order.",
            "/dataInputs/sections",
            string.Join(" | ", expected), string.Join(" | ", actual),
            $"Emit exactly these sections, in order, with no additions, omissions, or reordering: {string.Join(" | ", expected)}.") };
    }

    private static Check Fail(string message, string jsonPath, string expected, string actual, string remediation) => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.ConfigurationError,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
