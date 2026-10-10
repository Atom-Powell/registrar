using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class GenerationLogSerializerTests
{
    private static GenerationLog Sample() => new()
    {
        LogVersion = EngineInfo.LogVersion,
        ExecutionId = "GOV-2026-07-15-001",
        DocumentId = "ORG-GOV-DS-016",
        EngineVersion = EngineInfo.Version,
        RequestedMode = "draft",
        Outcome = "pass",
        TimestampUtc = "2026-07-15T12:00:00.0000000+00:00",
        ConfigVersions = new Dictionary<string, string> { ["clause-library.json"] = "abc123" },
        RulesApplied = new[] { "QA-SF-08", "QA-ENG-05" },
        ClauseInsertions = new[]
        {
            new ClauseInsertion
            {
                ClauseId = "CL-001", ExpectedSha = "deadbeef", ActualSha = "deadbeef",
                Verified = true, TokensResolved = 3
            }
        }
    };

    [Fact]
    public void Serialized_log_uses_camelCase_field_names()
    {
        var node = JsonNode.Parse(GenerationLogSerializer.ToJson(Sample()))!.AsObject();

        Assert.Equal("1.0.0", (string?)node["logVersion"]);
        Assert.Equal("ORG-GOV-DS-016", (string?)node["documentId"]);
        Assert.Equal("pass", (string?)node["outcome"]);
        Assert.Equal("QA-SF-08", (string?)node["rulesApplied"]!.AsArray()[0]);
        Assert.Equal("CL-001", (string?)node["clauseInsertions"]!.AsArray()[0]!["clauseId"]);
        Assert.True((bool)node["clauseInsertions"]!.AsArray()[0]!["verified"]!);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var restored = GenerationLogSerializer.FromJson(GenerationLogSerializer.ToJson(Sample()));

        Assert.Equal("GOV-2026-07-15-001", restored.ExecutionId);
        Assert.Single(restored.ClauseInsertions);
        Assert.Equal("CL-001", restored.ClauseInsertions[0].ClauseId);
        Assert.Equal(3, restored.ClauseInsertions[0].TokensResolved);
    }

    [Fact]
    public void Serialized_log_conforms_to_generation_log_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "generation-log.schema.json");
        var schema = IsolatedSchema.FromFile(schemaPath);
        using var document = JsonDocument.Parse(GenerationLogSerializer.ToJson(Sample()));

        var result = schema.Evaluate(document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Emitted generation log failed generation-log schema validation.");
    }
}
