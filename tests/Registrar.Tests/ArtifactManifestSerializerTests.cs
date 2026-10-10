using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class ArtifactManifestSerializerTests
{
    internal static ArtifactManifest Sample() => new()
    {
        ManifestVersion = EngineInfo.ManifestVersion,
        ExecutionId = "GOV-2026-07-15-001",
        DocumentId = "ORG-GOV-DS-016",
        EngineVersion = EngineInfo.Version,
        TimestampUtc = "2026-07-15T12:00:00.0000000+00:00",
        Identity = new ManifestIdentity
        {
            ArtifactType = "DS", DocumentId = "ORG-GOV-DS-016", Version = "v1.0",
            Classification = "Client-Facing", ApprovalStatus = "Draft", Layer = "Governance"
        },
        Context = new ManifestContext
        {
            Owner = "Registry Owner", Audience = "Leadership", PreparedBy = "ORG",
            Client = "Talewind CPA", Engagement = "Pilot", ReviewDate = null
        },
        Sections = new[]
        {
            new ManifestSection { Number = 1, Title = "Purpose", BlockCount = 1, ClauseIds = new List<string>() },
            new ManifestSection { Number = 5, Title = "Audit", BlockCount = 2, ClauseIds = new[] { "CL-006" } }
        },
        Dependencies = new[]
        {
            new ManifestDependency { DependencyId = "ORG-MST-SCL", Kind = "clause-library", Blocking = true }
        },
        RegistryReference = null,
        ArtifactSha256 = new string('a', 64)
    };

    [Fact]
    public void Serialized_manifest_uses_camelCase_field_names()
    {
        var node = JsonNode.Parse(ArtifactManifestSerializer.ToJson(Sample()))!.AsObject();

        Assert.Equal("1.0.0", (string?)node["manifestVersion"]);
        Assert.Equal("ORG-GOV-DS-016", (string?)node["documentId"]);
        Assert.Equal("DS", (string?)node["identity"]!["artifactType"]);
        Assert.Equal("Purpose", (string?)node["sections"]!.AsArray()[0]!["title"]);
        Assert.Equal("CL-006", (string?)node["sections"]!.AsArray()[1]!["clauseIds"]!.AsArray()[0]);
        Assert.Equal("clause-library", (string?)node["dependencies"]!.AsArray()[0]!["kind"]);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var restored = ArtifactManifestSerializer.FromJson(ArtifactManifestSerializer.ToJson(Sample()));

        Assert.Equal("GOV-2026-07-15-001", restored.ExecutionId);
        Assert.Equal(2, restored.Sections.Count);
        Assert.Equal("CL-006", restored.Sections[1].ClauseIds[0]);
        Assert.Null(restored.RegistryReference);
    }

    [Fact]
    public void Serialized_manifest_conforms_to_artifact_manifest_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "artifact-manifest.schema.json");
        var schema = IsolatedSchema.FromFile(schemaPath);
        using var document = JsonDocument.Parse(ArtifactManifestSerializer.ToJson(Sample()));

        var result = schema.Evaluate(document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Emitted artifact manifest failed artifact-manifest schema validation.");
    }

    [Fact]
    public void Serialized_manifest_with_populated_registry_reference_conforms_to_schema()
    {
        var manifest = Sample() with
        {
            RegistryReference = new ManifestRegistryReference
            {
                DocumentId = "ORG-GOV-DS-016", FromStatus = "Draft", ToStatus = "In Review", Version = "v1.0"
            },
            Context = Sample().Context with { ReviewDate = "2026-12-01" }
        };
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "artifact-manifest.schema.json");
        var schema = IsolatedSchema.FromFile(schemaPath);
        using var document = JsonDocument.Parse(ArtifactManifestSerializer.ToJson(manifest));

        var result = schema.Evaluate(document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Manifest with a populated registryReference failed schema validation.");
    }
}
