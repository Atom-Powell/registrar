using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;
using Json.Schema;

namespace Registrar.Tests;

public class PromotionPayloadSerializerTests
{
    private static PromotionPayload Sample() => new()
    {
        PayloadVersion = EngineInfo.PayloadVersion,
        ExecutionId = "GOV-2026-07-03-001",
        DocumentId = "ORG-GOV-DS-016",
        ArtifactType = "DS",
        Version = "v1.0",
        GeneratedUtc = "2026-07-03T12:00:00.0000000+00:00",
        RegistryUpdate = new RegistryUpdate
        {
            DocumentId = "ORG-GOV-DS-016", FromStatus = "Draft", ToStatus = "In Review", Version = "v1.0"
        },
        RepositoryTarget = "Controlled Documents/Governance",
        Attestation = new Attestation
        {
            CheckId = "QA-SPEC-18",
            Statement = "Operator must apply this registry update to the PMC Registry sheet and confirm post-update state before final delivery.",
            RequiresOperatorSignoff = true
        }
    };

    [Fact]
    public void Serialized_payload_uses_camelCase_field_names()
    {
        var node = JsonNode.Parse(PromotionPayloadSerializer.ToJson(Sample()))!.AsObject();

        Assert.Equal("1.0.0", (string?)node["payloadVersion"]);
        Assert.Equal("ORG-GOV-DS-016", (string?)node["documentId"]);
        Assert.Equal("In Review", (string?)node["registryUpdate"]!["toStatus"]);
        Assert.Equal("Controlled Documents/Governance", (string?)node["repositoryTarget"]);
        Assert.True((bool)node["attestation"]!["requiresOperatorSignoff"]!);
    }

    [Fact]
    public void Serialized_payload_conforms_to_promotion_payload_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "promotion-payload.schema.json");
        var schema = IsolatedSchema.FromFile(schemaPath);
        using var document = JsonDocument.Parse(PromotionPayloadSerializer.ToJson(Sample()));

        var result = schema.Evaluate(document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Emitted payload failed promotion-payload schema validation.");
    }
}
